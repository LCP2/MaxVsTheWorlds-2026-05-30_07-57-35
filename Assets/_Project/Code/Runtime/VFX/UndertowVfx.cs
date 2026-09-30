using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// UNDERTOW's own firing VFX (MV-1034, replacing the charge/cavitation shot's telegraph; MV-1046
    /// made the beam itself snake and corkscrew rather than just its crackle strands): a writhing beam
    /// from the muzzle rather than a straight laser line — a white-hot core inside a cyan sheath, both
    /// walking one shared, animated centreline pinned at the muzzle and the exact hit point, wrapped by
    /// a handful of violet/blue crackle strands whose phase animates every frame — plus a muzzle flare
    /// and a splash flare where the beam lands.
    ///
    /// Owned entirely by the art stream, same split as <see cref="WaterVfx"/>/<see cref="LppeVfx"/>:
    /// <see cref="MaxWorlds.Combat.Undertow"/> drives it with cosmetic-only calls
    /// (<see cref="Init"/>, <see cref="SetStreaming"/>, <see cref="UpdateStream"/>, <see cref="OnTick"/>)
    /// and turning it off changes nothing but the picture.
    ///
    /// Built from <see cref="LineRenderer"/>s updated in place every frame (the core/sheath/strand
    /// positions are overwritten, never reallocated) plus two shared <see cref="VfxBurst"/>s for the
    /// muzzle and splash flares — no per-frame allocation, so the MV527 allocation guard holds.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("vfx")]
    public sealed class UndertowVfx : MonoBehaviour
    {
        /// <summary>Spec: "a white-hot core (~0.12 m wide)".</summary>
        public const float CoreWidth = 0.12f;

        /// <summary>Spec: "a cyan sheath (#3FE0FF, ~0.35 m wide, additive, soft edge)".</summary>
        public const float SheathWidth = 0.35f;

        /// <summary>Spec: "2-3 thin violet/blue crackle strands".</summary>
        public const int StrandCount = 3;

        /// <summary>Spec: "amplitude up to ~0.3 m, tapering to 0 at both ends".</summary>
        public const float StrandAmplitude = 0.3f;

        /// <summary>Spec: "Segments ≥ 24 points from muzzle to end point" — shared by the centreline
        /// (core/sheath) and the strand wrap sampling, so both walk the same t values.</summary>
        private const int Segments = 24;
        private const float StrandWidth = 0.045f;
        private const float WaveCyclesPerBeam = 2.5f;
        private const float PhaseSpeed = 6f;

        // MV-1046 centreline snake/corkscrew — every constant below is the ticket's own authored
        // number, not a tuned guess.
        private const float LateralAmplitude = 0.55f;
        private const float LateralFreq1 = 1.6f;
        private const float LateralK1 = 9f;
        private const float LateralFreq2 = 3.1f;
        private const float LateralK2 = 14f;
        private const float LateralPhase2 = 1.3f;
        private const float LateralTermWeight2 = 0.45f;
        private const float TaperExponent = 0.7f;

        /// <summary>Vertical offset amplitude (spec: "a second, smaller vertical offset (0.25 m,
        /// different phase) makes it corkscrew"). Driven in quadrature with the primary lateral term
        /// (<see cref="LateralFreq1"/>/<see cref="LateralK1"/>, phase +90°) rather than a second
        /// independent frequency, so the two trace an ellipse rather than an uncorrelated wobble — a
        /// proper corkscrew instead of two unrelated waves that happen to share a beam.</summary>
        private const float VerticalAmplitude = 0.25f;

        /// <summary>Spec: "each centreline point eases toward its target position with a lag that
        /// grows along the beam (muzzle 0, end 0.12 s)".</summary>
        private const float WhipLagAtEnd = 0.12f;

        // Pushed past 1.0 for bloom headroom — the same "over-1.0 authored colour" trick LppeVfx's own
        // muzzle/impact colours use against URP's bloom threshold.
        private static readonly Color CoreColor = new Color(1.7f, 1.75f, 1.8f, 1f);
        private static readonly Color SheathColor = new Color(0.247f, 0.878f, 1f, 0.6f);   // #3FE0FF
        private static readonly Color[] StrandColors =
        {
            new Color(0.706f, 0.420f, 1f, 0.85f),   // #B46BFF
            new Color(0.373f, 0.482f, 1f, 0.85f),   // #5F7BFF
            new Color(0.706f, 0.420f, 1f, 0.85f),
        };
        private static readonly Color MuzzleColor = new Color(1.9f, 1.95f, 2f, 1f);
        private static readonly Color SplashColor = new Color(1.7f, 1.75f, 1.8f, 1f);

        private LineRenderer _core;
        private LineRenderer _sheath;
        private LineRenderer[] _strands;
        private VfxBurst _muzzleFlare;
        private VfxBurst _splashFlare;
        private bool _initialized;
        private float _phase;

        // Centreline buffer shared by the core and sheath renderers (and the strands' wrap anchor) —
        // allocated once in Init, overwritten in place every UpdateStream call, never reallocated.
        private Vector3[] _centerline;
        private bool _centerlineValid;
        private float _beamTime;

        /// <summary>Whether the stream's renderers are currently enabled — what
        /// <see cref="MaxWorlds.Combat.Undertow.IsStreamVisible"/> reads.</summary>
        public bool IsStreaming { get; private set; }

        /// <summary>Builds every renderer once. Idempotent, and called explicitly by the owner (see
        /// <see cref="WaterVfx.Init"/>'s equivalent shape) rather than from Awake — neither Awake nor
        /// OnEnable reliably run for AddComponent outside Play mode.</summary>
        public void Init()
        {
            if (_initialized) return;
            _initialized = true;

            Material glow = VfxMaterials.Additive(VfxMaterials.Glow());

            _sheath = BuildLine("UndertowSheath", SheathWidth, glow, positionCount: Segments + 1);
            _core = BuildLine("UndertowCore", CoreWidth, glow, positionCount: Segments + 1);
            _centerline = new Vector3[Segments + 1];

            _strands = new LineRenderer[StrandCount];
            for (int i = 0; i < StrandCount; i++)
                _strands[i] = BuildLine($"UndertowStrand{i}", StrandWidth, glow, positionCount: Segments + 1);

            _muzzleFlare = new VfxBurst("UndertowMuzzleFlare", glow, 24, 0f, perFrameCap: 4);
            _splashFlare = new VfxBurst("UndertowSplashFlare", glow, 24, 0f, perFrameCap: 4);
        }

        private LineRenderer BuildLine(string name, float width, Material material, int positionCount)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: true);

            var line = go.AddComponent<LineRenderer>();
            line.positionCount = positionCount;
            line.widthMultiplier = width;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;
            line.enabled = false;
            return line;
        }

        /// <summary>Start/stop the stream. Only acts on change, so it is free to call every frame.</summary>
        public void SetStreaming(bool on)
        {
            if (!_initialized || IsStreaming == on) return;
            IsStreaming = on;
            // Force a hard snap (no whip lag) on the first frame back on, so the centreline doesn't
            // ease in from wherever it was left sitting the last time the stream was up.
            if (on) _centerlineValid = false;

            _sheath.enabled = on;
            _core.enabled = on;
            for (int i = 0; i < _strands.Length; i++) _strands[i].enabled = on;
        }

        /// <summary>Re-lay the core/sheath/strand geometry between <paramref name="muzzle"/> and
        /// <paramref name="endPoint"/>, advancing the crackle strands' animation phase by
        /// <paramref name="dt"/>. Called every frame the stream is up — cheap, no allocation — so the
        /// beam tracks Max moving/aiming even between the fire-tick cadence that moves
        /// <paramref name="endPoint"/> itself.</summary>
        public void UpdateStream(Vector3 muzzle, Vector3 endPoint, float dt)
        {
            if (!_initialized || !IsStreaming) return;
            _phase += dt * PhaseSpeed;
            _beamTime += dt;

            Vector3 axis = endPoint - muzzle;
            float length = axis.magnitude;
            Vector3 dir = length > 1e-4f ? axis / length : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();
            Vector3 wrapUp = Vector3.Cross(dir, right);

            // --- The whole beam snakes: one centreline, walked by both the core and the sheath.
            for (int i = 0; i <= Segments; i++)
            {
                float t = (float)i / Segments;
                // Both ends pinned (muzzle, exact hit point): taper is 0 at t=0 and t=1. Clamped to
                // >=0 first — float rounding can make sin(t*pi) a tiny NEGATIVE epsilon right at t=1,
                // and Mathf.Pow(negative, non-integer) is NaN, not a small negative number.
                float taper = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), TaperExponent);

                float theta1 = 2f * Mathf.PI * LateralFreq1 * t - LateralK1 * _beamTime;
                float theta2 = 2f * Mathf.PI * LateralFreq2 * t - LateralK2 * _beamTime + LateralPhase2;
                float lateral = LateralAmplitude * taper * (Mathf.Sin(theta1) + LateralTermWeight2 * Mathf.Sin(theta2));
                // Quadrature (+90°) with the primary lateral term traces a corkscrew rather than a
                // second, uncorrelated wobble.
                float vertical = VerticalAmplitude * taper * Mathf.Cos(theta1);

                Vector3 target = Vector3.Lerp(muzzle, endPoint, t) + right * lateral + Vector3.up * vertical;

                if (!_centerlineValid)
                {
                    _centerline[i] = target;
                }
                else
                {
                    // Whip on aim changes: lag grows along the beam (muzzle 0, end WhipLagAtEnd).
                    float lag = WhipLagAtEnd * t;
                    float easeFactor = lag > 1e-5f ? 1f - Mathf.Exp(-dt / lag) : 1f;
                    _centerline[i] = Vector3.Lerp(_centerline[i], target, easeFactor);
                }
            }
            _centerlineValid = true;

            _core.SetPositions(_centerline);
            _core.startColor = CoreColor;
            _core.endColor = CoreColor;

            _sheath.SetPositions(_centerline);
            _sheath.startColor = SheathColor;
            _sheath.endColor = SheathColor;

            // --- The crackle strands wrap the snaking centreline (offset from it, not the straight line).
            for (int s = 0; s < _strands.Length; s++)
            {
                LineRenderer strand = _strands[s];
                float wrapAngle = (360f / _strands.Length) * s * Mathf.Deg2Rad;
                Vector3 wrapDir = right * Mathf.Cos(wrapAngle) + wrapUp * Mathf.Sin(wrapAngle);

                for (int i = 0; i <= Segments; i++)
                {
                    float t = (float)i / Segments;
                    float strandTaper = Mathf.Sin(t * Mathf.PI);   // 0 at both ends, peak at the middle
                    float wave = Mathf.Sin(t * WaveCyclesPerBeam * Mathf.PI * 2f + _phase + s * 2.1f);
                    Vector3 point = _centerline[i] + wrapDir * (StrandAmplitude * strandTaper * wave);
                    strand.SetPosition(i, point);
                }

                Color c = StrandColors[s % StrandColors.Length];
                strand.startColor = c;
                strand.endColor = c;
            }
        }

        /// <summary>The per-tick punctuation (spec): a bright muzzle flare at Max, and a splash flare
        /// with 3 short white sparks where the beam lands. Called once per fire tick, not per frame.</summary>
        public void OnTick(Vector3 muzzle, Vector3 endPoint, Vector3 forward)
        {
            if (!_initialized) return;
            Vector3 axis = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;

            _muzzleFlare.Emit(muzzle + axis * 0.2f, 1,
                axis: axis, spreadDegrees: 10f,
                speedMin: 0f, speedMax: 0f,
                sizeMin: 0.35f, sizeMax: 0.35f,
                lifeMin: 0.1f, lifeMax: 0.1f,
                colorA: MuzzleColor, colorB: MuzzleColor);

            _splashFlare.Emit(endPoint, 3,
                axis: Vector3.up, spreadDegrees: 60f,
                speedMin: 1.5f, speedMax: 3f,
                sizeMin: 0.08f, sizeMax: 0.14f,
                lifeMin: 0.12f, lifeMax: 0.18f,
                colorA: SplashColor, colorB: SplashColor);
        }

        private void LateUpdate()
        {
            if (!_initialized) return;
            _muzzleFlare.EndFrame();
            _splashFlare.EndFrame();
        }

        private void OnDestroy()
        {
            DestroyLine(_core);
            DestroyLine(_sheath);
            if (_strands != null)
                for (int i = 0; i < _strands.Length; i++) DestroyLine(_strands[i]);
            Dispose(_muzzleFlare);
            Dispose(_splashFlare);
        }

        private static void DestroyLine(LineRenderer line)
        {
            if (line == null) return;
            if (Application.isPlaying) Destroy(line.gameObject); else DestroyImmediate(line.gameObject);
        }

        private static void Dispose(VfxBurst b)
        {
            var go = b?.GameObject;
            if (go == null) return;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }
}
