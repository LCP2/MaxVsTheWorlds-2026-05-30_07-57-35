using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// UNDERTOW's own firing VFX (MV-1034, replacing the charge/cavitation shot's telegraph; MV-1046
    /// made the beam itself snake and corkscrew rather than just its crackle strands; MV-1064 made it
    /// latch onto a robot and wrap it): a writhing beam from the muzzle rather than a straight laser
    /// line — a white-hot core inside a cyan sheath, both walking one shared, animated centreline
    /// pinned at the muzzle and the exact hit point, wrapped by a handful of orange/red/amber crackle
    /// strands whose phase animates every frame — plus a muzzle flare and a splash flare where the
    /// beam lands, and — while latched (<see cref="SetLatch"/>) — three coloured coils spiralling the
    /// latched robot and a steady spray of sparks off its body.
    ///
    /// Owned entirely by the art stream, same split as <see cref="WaterVfx"/>/<see cref="LppeVfx"/>:
    /// <see cref="MaxWorlds.Combat.Undertow"/> drives it with cosmetic-only calls
    /// (<see cref="Init"/>, <see cref="SetStreaming"/>, <see cref="UpdateStream"/>, <see cref="OnTick"/>,
    /// <see cref="SetLatch"/>) and turning it off changes nothing but the picture.
    ///
    /// Built from <see cref="LineRenderer"/>s updated in place every frame (the core/sheath/strand/coil
    /// positions are overwritten, never reallocated) plus four shared <see cref="VfxBurst"/>s for the
    /// muzzle flare, splash flare, latch sparks and latch impact flare — no per-frame allocation, so
    /// the MV527 allocation guard holds.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("vfx")]
    public sealed class UndertowVfx : MonoBehaviour
    {
        /// <summary>MV-1064 spec: "CoreWidth 0.12 -&gt; 0.22 m" — much more prominent on a 6-inch screen.</summary>
        public const float CoreWidth = 0.22f;

        /// <summary>MV-1064 spec: "SheathWidth 0.35 -&gt; 0.70 m" (keep white-hot core, cyan sheath).</summary>
        public const float SheathWidth = 0.70f;

        /// <summary>MV-1064 spec: "5 strands (was 3)".</summary>
        public const int StrandCount = 5;

        /// <summary>MV-1064 spec: "amplitude 0.3 -&gt; 0.45 m".</summary>
        public const float StrandAmplitude = 0.45f;

        /// <summary>Spec: "Segments ≥ 24 points from muzzle to end point" — shared by the centreline
        /// (core/sheath) and the strand wrap sampling, so both walk the same t values.</summary>
        private const int Segments = 24;

        /// <summary>MV-1064 spec: "width 0.045 -&gt; 0.12 m".</summary>
        private const float StrandWidth = 0.12f;
        private const float WaveCyclesPerBeam = 2.5f;
        private const float PhaseSpeed = 6f;

        // --- MV-1064 latch wrap-around: 3 coils spiralling the latched robot feet-to-top.
        private const int CoilCount = 3;

        /// <summary>Spec: "3 coils ... 0.10 m thick".</summary>
        private const float CoilThickness = 0.10f;

        /// <summary>Spec: "radius = the robot's own radius + 0.15 m".</summary>
        private const float CoilRadiusPad = 0.15f;

        /// <summary>Spec: "~2.2 turns each".</summary>
        private const float CoilTurns = 2.2f;

        /// <summary>Spec: "rotating at 2 revolutions per second".</summary>
        private const float CoilRevsPerSecond = 2f;
        private const int CoilSegments = 28;

        /// <summary>Spec: "Coils appear within 0.1 s of latching".</summary>
        private const float CoilAppearSeconds = 0.1f;

        /// <summary>Spec: "[coils] vanish within 0.15 s of the latch dropping".</summary>
        private const float CoilVanishSeconds = 0.15f;

        /// <summary>Spec: "The last 1 m of the beam curls into the coils rather than stopping dead."</summary>
        private const float CurlLength = 1f;

        // --- MV-1064 latch sparks + impact flare.
        private const float SparksPerSecond = 12f;   // spec: "10-14 ... per second"
        private const float SparkDistanceMin = 0.6f;
        private const float SparkDistanceMax = 1.2f;
        private const float SparkLifeMin = 0.25f;
        private const float SparkLifeMax = 0.4f;

        /// <summary>Spec: "a pulsing impact flare 0.8 m across at the contact point".</summary>
        private const float ImpactFlareSize = 0.8f;
        private const float ImpactPulseHz = 5f;

        /// <summary>Spec: "muzzle flare 0.5 m" (was 0.35m).</summary>
        private const float MuzzleFlareSize = 0.5f;

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

        // MV-1064: "No violet or blue left on the stream" — orange/red/amber, reused (index % Length)
        // across both the 5 crackle strands and the 3 latch coils so the two read as one palette. Kept
        // at the hex values' own natural 0-1 range rather than pushed past 1.0 like CoreColor/MuzzleColor
        // below: both LineRenderer.startColor and VfxBurst's Color32 particles clamp per-channel to
        // [0,1] on write (there is no HDR vertex-colour path for either), so an amber pushed past 1.0 on
        // every channel (as an x2 multiply would) clips R and G to the same 1.0 and loses the R>G>B
        // ordering the spec's own "orange/red family" look depends on.
        private static readonly Color[] EmberColors =
        {
            new Color(1f, 0.478f, 0.102f, 0.85f),   // #FF7A1A (orange)
            new Color(1f, 0.165f, 0.071f, 0.85f),   // #FF2A12 (red)
            new Color(1f, 0.690f, 0.125f, 0.85f),   // #FFB020 (amber)
        };
        private static readonly Color MuzzleColor = new Color(1.9f, 1.95f, 2f, 1f);
        private static readonly Color SplashColor = new Color(1.7f, 1.75f, 1.8f, 1f);
        private static readonly Color SparkColorA = new Color(1f, 0.55f, 0.12f, 1f);
        private static readonly Color SparkColorB = new Color(1f, 0.2f, 0.08f, 1f);
        private static readonly Color ImpactFlareColor = new Color(1f, 0.45f, 0.12f, 1f);

        private LineRenderer _core;
        private LineRenderer _sheath;
        private LineRenderer[] _strands;
        private LineRenderer[] _coils;
        private VfxBurst _muzzleFlare;
        private VfxBurst _splashFlare;
        private VfxBurst _latchSparks;
        private VfxBurst _latchFlare;
        private bool _initialized;
        private float _phase;

        // Centreline buffer shared by the core and sheath renderers (and the strands' wrap anchor) —
        // allocated once in Init, overwritten in place every UpdateStream call, never reallocated.
        private Vector3[] _centerline;
        private bool _centerlineValid;
        private float _beamTime;

        // --- MV-1064 latch wrap-around state.
        private Transform _coilTarget;
        private CharacterController _coilCc;
        private float _coilPhase;

        /// <summary>0..1, eased toward 1 while latched (<see cref="CoilAppearSeconds"/>) and back to 0
        /// once it drops (<see cref="CoilVanishSeconds"/>) — drives both the coils' own alpha and the
        /// beam's end-of-line curl in <see cref="UpdateStream"/>, so the two never visibly desync.</summary>
        private float _latchVisibility;
        private float _sparkAccumulator;

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

            _coils = new LineRenderer[CoilCount];
            for (int i = 0; i < CoilCount; i++)
                _coils[i] = BuildLine($"UndertowCoil{i}", CoilThickness, glow, positionCount: CoilSegments + 1);

            _muzzleFlare = new VfxBurst("UndertowMuzzleFlare", glow, 24, 0f, perFrameCap: 4);
            _splashFlare = new VfxBurst("UndertowSplashFlare", glow, 24, 0f, perFrameCap: 4);
            _latchSparks = new VfxBurst("UndertowLatchSparks", glow, 64, 0f, perFrameCap: 6);
            _latchFlare = new VfxBurst("UndertowLatchFlare", glow, 16, 0f, perFrameCap: 2);
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

                // MV-1064 spec #9: "The last 1m of the beam curls into the coils rather than stopping
                // dead." Only the tail of the centreline (within CurlLength of the end point) bulges
                // outward toward the coil radius; eased by _latchVisibility so the curl appears/vanishes
                // in step with the coils themselves. The weight is a bump (0 at the curl's own start AND
                // at t=1) rather than a ramp all the way to t=1, so the very last point stays pinned
                // exactly on the end point — the invariant MV-1046's own test (and the hit-point math
                // StreamEndPoint feeds) both depend on — while the segments just before it visibly flare
                // into the wrap.
                if (_latchVisibility > 0f && length > 1e-4f)
                {
                    float distFromMuzzle = t * length;
                    float curlStart = Mathf.Max(0f, length - CurlLength);
                    if (distFromMuzzle >= curlStart && t < 1f)
                    {
                        float curlT = Mathf.InverseLerp(curlStart, length, distFromMuzzle);
                        float curlWeight = Mathf.Sin(curlT * Mathf.PI); // 0 at curlStart and at t=1, peak mid-way
                        float curlRadius = CoilRadius() * curlWeight * _latchVisibility;
                        float curlAngle = _coilPhase + curlT * CoilTurns * 2f * Mathf.PI;
                        Vector3 curlOffset = (right * Mathf.Cos(curlAngle) + wrapUp * Mathf.Sin(curlAngle)) * curlRadius;
                        target += curlOffset;
                    }
                }

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

                Color c = EmberColors[s % EmberColors.Length];
                strand.startColor = c;
                strand.endColor = c;
            }
        }

        /// <summary>The latched target's own collider radius plus the spec's pad, or a reasonable
        /// stand-in when there's no <see cref="CharacterController"/> to read (never 0 — a zero-radius
        /// coil/curl would collapse onto the centreline and read as nothing).</summary>
        private float CoilRadius() => (_coilCc != null ? _coilCc.radius : 0.5f) + CoilRadiusPad;

        /// <summary>MV-1064: drives the wrap-around coils and the steady spark/impact-flare spray while
        /// latched. Called every frame <see cref="MaxWorlds.Combat.Undertow.Tick"/> runs (not just on the
        /// fire-tick cadence) so the coils track a moving target and the appear/vanish ease
        /// (<see cref="CoilAppearSeconds"/>/<see cref="CoilVanishSeconds"/>) is smooth. <paramref name="on"/>
        /// false with a null <paramref name="targetTransform"/>/<paramref name="targetCc"/> starts the
        /// vanish while still showing the coils at their last known position/size.</summary>
        public void SetLatch(bool on, Transform targetTransform, CharacterController targetCc, float dt)
        {
            if (!_initialized) return;

            if (on)
            {
                _coilTarget = targetTransform;
                _coilCc = targetCc;
            }

            float rate = on ? 1f / CoilAppearSeconds : -1f / CoilVanishSeconds;
            _latchVisibility = Mathf.Clamp01(_latchVisibility + rate * Mathf.Max(dt, 0f));

            bool show = _latchVisibility > 0f && _coilTarget != null;
            for (int i = 0; i < _coils.Length; i++) _coils[i].enabled = show;

            if (!show)
            {
                if (_latchVisibility <= 0f) { _coilTarget = null; _coilCc = null; }
                return;
            }

            Vector3 centre = _coilCc != null ? _coilCc.bounds.center : _coilTarget.position;
            float radius = CoilRadius();
            float height = _coilCc != null ? _coilCc.height : 1.8f;
            Vector3 feet = centre - Vector3.up * (height * 0.5f);

            _coilPhase += dt * CoilRevsPerSecond * 2f * Mathf.PI;

            for (int c = 0; c < _coils.Length; c++)
            {
                LineRenderer coil = _coils[c];
                float coilPhaseOffset = (2f * Mathf.PI / _coils.Length) * c;
                for (int i = 0; i <= CoilSegments; i++)
                {
                    float t = (float)i / CoilSegments;
                    float angle = _coilPhase + coilPhaseOffset + t * CoilTurns * 2f * Mathf.PI;
                    Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    coil.SetPosition(i, feet + Vector3.up * (height * t) + radial);
                }

                Color col = EmberColors[c % EmberColors.Length];
                col.a *= _latchVisibility;
                coil.startColor = col;
                coil.endColor = col;
            }

            // Spec: "10-14 orange/red sparks per second fly 0.6-1.2m off the robot" — accumulated
            // fractionally so the rate holds true regardless of frame time, same idiom a cooldown
            // timer uses.
            _sparkAccumulator += dt * SparksPerSecond;
            while (_sparkAccumulator >= 1f)
            {
                _sparkAccumulator -= 1f;
                Vector3 dir = Random.onUnitSphere;
                float life = Random.Range(SparkLifeMin, SparkLifeMax);
                float dist = Random.Range(SparkDistanceMin, SparkDistanceMax);
                _latchSparks.Emit(centre, 1, dir, 180f,
                    speedMin: dist / life, speedMax: dist / life,
                    sizeMin: 0.05f, sizeMax: 0.09f,
                    lifeMin: life, lifeMax: life,
                    colorA: SparkColorA, colorB: SparkColorB);
            }

            // Spec: "a pulsing impact flare 0.8m across at the contact point".
            float pulse = ImpactFlareSize * (0.85f + 0.15f * Mathf.Sin(Time.time * ImpactPulseHz * 2f * Mathf.PI));
            _latchFlare.Emit(centre, 1, Vector3.up, 180f,
                speedMin: 0f, speedMax: 0f,
                sizeMin: pulse, sizeMax: pulse,
                lifeMin: 0.1f, lifeMax: 0.1f,
                colorA: ImpactFlareColor, colorB: ImpactFlareColor);
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
                sizeMin: MuzzleFlareSize, sizeMax: MuzzleFlareSize,
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
            _latchSparks.EndFrame();
            _latchFlare.EndFrame();
        }

        private void OnDestroy()
        {
            DestroyLine(_core);
            DestroyLine(_sheath);
            if (_strands != null)
                for (int i = 0; i < _strands.Length; i++) DestroyLine(_strands[i]);
            if (_coils != null)
                for (int i = 0; i < _coils.Length; i++) DestroyLine(_coils[i]);
            Dispose(_muzzleFlare);
            Dispose(_splashFlare);
            Dispose(_latchSparks);
            Dispose(_latchFlare);
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
