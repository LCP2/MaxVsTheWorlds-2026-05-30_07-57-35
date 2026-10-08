using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// UNDERTOW's own firing VFX (MV-1034, replacing the charge/cavitation shot's telegraph; MV-1046
    /// made the beam itself snake and corkscrew rather than just its crackle strands; MV-1064 made it
    /// latch onto a robot and wrap it; MV-1070 made the tip itself seek, then MV-1121 removed the
    /// deliberate-finish prongs MV-1070 added — no jaw, nothing at all on the end): a writhing beam from
    /// the muzzle rather than a straight laser line — a white-hot core inside a cyan sheath, both walking
    /// one shared, animated centreline pinned at the muzzle and bent along a curve toward the tip
    /// (<see cref="UpdateStream"/>, MV-1121 — the beam always leaves the gun on the aim line and bends to
    /// wherever the tip is), wrapped by a handful of orange/red/amber crackle strands whose phase
    /// animates every frame — a muzzle flare, and — while latched (<see cref="SetLatch"/>) — three
    /// coloured coils and an orange/white lock ring sized from the latched robot's own visible renderer
    /// bounds (MV-1121, never <see cref="CharacterController.radius"/>) spiralling/encircling it, and a
    /// steady spray of sparks off its body.
    ///
    /// MV-1128 ("SPLIT"): beams beyond the first (<see cref="MaxWorlds.Combat.Undertow.BeamCount"/>, 1-3)
    /// get their own full copy of this same core/sheath/strand/coil/lock-ring set, built once in
    /// <see cref="Init"/> alongside beam 0's own — every line's width scaled by
    /// <see cref="SecondaryWidthScale"/> (spec #8) — and driven by <see cref="UpdateSecondaryStream"/>/
    /// <see cref="SetSecondaryLatch"/>. Beam 0 keeps its ORIGINAL method names/GameObject names
    /// (<c>UndertowCore</c>, <c>UndertowCoil0</c>, ...) byte-for-byte, so every pre-MV-1128 EditMode test
    /// (MV-1046/1064/1070/1121) that finds them by name keeps working unchanged; beams 1/2 carry a
    /// <c>"Beam1"</c>/<c>"Beam2"</c> suffix instead. The real per-beam math (<see cref="LayBeamGeometry"/>/
    /// <see cref="DriveLatch"/>) is written once and shared by both beam 0's own calls and the secondary
    /// ones, so there is exactly one copy of this logic to ever get wrong.
    ///
    /// Owned entirely by the art stream, same split as <see cref="WaterVfx"/>/<see cref="LppeVfx"/>:
    /// <see cref="MaxWorlds.Combat.Undertow"/> drives it with cosmetic-only calls
    /// (<see cref="Init"/>, <see cref="SetStreaming"/>, <see cref="UpdateStream"/>, <see cref="OnTick"/>,
    /// <see cref="SetLatch"/>) and turning it off changes nothing but the picture.
    ///
    /// Built from <see cref="LineRenderer"/>s updated in place every frame (the core/sheath/strand/coil/
    /// lock-ring positions are overwritten, never reallocated) plus three shared <see cref="VfxBurst"/>s
    /// for the muzzle flare, latch sparks and latch impact flare — no per-frame allocation, so the MV527
    /// allocation guard holds.
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

        /// <summary>MV-1121: "The strands wrap at a constant 0.24 m from the centre-line (so the bundle
        /// stays inside the 0.70 m sheath)" — down from MV-1064's 0.45m, which peaked only at mid-beam;
        /// this is now the CONSTANT wrap distance once <see cref="StrandRampFraction"/> has ramped up.</summary>
        public const float StrandAmplitude = 0.24f;

        /// <summary>MV-1121: "ramping up from zero only over the first 6% of the beam at the gun" — the
        /// strand wrap no longer tapers back down toward the tip (MV-1064's shape), so the bundle reads
        /// full width right up to the plain cut end.</summary>
        private const float StrandRampFraction = 0.06f;

        /// <summary>Spec: "Segments ≥ 24 points from muzzle to end point" — shared by the centreline
        /// (core/sheath) and the strand wrap sampling, so both walk the same t values.</summary>
        private const int Segments = 24;

        /// <summary>MV-1064 spec: "width 0.045 -&gt; 0.12 m".</summary>
        private const float StrandWidth = 0.12f;
        private const float WaveCyclesPerBeam = 2.5f;
        private const float PhaseSpeed = 6f;

        /// <summary>MV-1121: "a quadratic curve from the muzzle to the tip whose control point is on the
        /// aim line, 55% of the muzzle-to-tip distance out from the muzzle" — the beam therefore always
        /// leaves the gun along the aim direction and bends to wherever the tip actually is, rather than
        /// swinging the whole chord (MV-1046's straight muzzle-&gt;tip line).</summary>
        private const float CurveControlFraction = 0.55f;

        // --- MV-1064 latch wrap-around: 3 coils spiralling the latched robot feet-to-top.
        private const int CoilCount = 3;

        /// <summary>Spec: "~2.2 turns each".</summary>
        private const float CoilTurns = 2.2f;

        /// <summary>Spec: "rotating at 2 revolutions per second".</summary>
        private const float CoilRevsPerSecond = 2f;
        private const int CoilSegments = 28;

        /// <summary>MV-1121: "rising from 0.15 m to the top of the robot's renderer bounds" — measured up
        /// from the renderer bounds' own floor, not from a CharacterController's feet.</summary>
        private const float CoilBottomHeight = 0.15f;

        /// <summary>Spec: "Coils appear within 0.1 s of latching".</summary>
        private const float CoilAppearSeconds = 0.1f;

        /// <summary>Spec: "[coils] vanish within 0.15 s of the latch dropping".</summary>
        private const float CoilVanishSeconds = 0.15f;

        /// <summary>Spec: "The last 1 m of the beam curls into the coils rather than stopping dead."</summary>
        private const float CurlLength = 1f;

        // --- MV-1121 lock sizing: "Lock radius R = half the widest horizontal extent of the robot's
        // combined renderer bounds, plus 0.25 m. Never the controller radius."
        private const float LockRadiusPad = 0.25f;

        /// <summary>Legacy fallback pad for the CharacterController-radius shape MV-1064 originally
        /// shipped — used ONLY when <see cref="ResolveLockGeometry"/> finds no renderer to measure (a
        /// test double built without a <see cref="RobotRig"/>; every real in-game robot always carries
        /// one). Keeps every existing EditMode test's coil/curl geometry numerically unchanged.</summary>
        private const float LegacyRadiusPad = 0.15f;

        /// <summary>MV-1121 spec: "Line thickness for the ring and coils = 0.14 m x (R / 0.75), clamped
        /// to 0.14 to 0.30 m."</summary>
        private const float LockThicknessBase = 0.14f;
        private const float LockThicknessRefRadius = 0.75f;
        private const float LockThicknessMin = 0.14f;
        private const float LockThicknessMax = 0.30f;

        /// <summary>MV-1121 spec: "The ring is drawn 1.25 times that [thickness]."</summary>
        private const float LockRingThicknessMultiplier = 1.25f;

        /// <summary>MV-1121 spec: "It appears at 1.6 R and closes to R over 0.12 s (the "snap shut")".</summary>
        private const float LockRingOvershootMultiplier = 1.6f;
        private const float LockRingSnapSeconds = 0.12f;

        /// <summary>MV-1121 spec: "a closed circle ... (0.12 m above the floor)".</summary>
        private const float LockRingHeightAboveFloor = 0.12f;
        private const int LockRingSegments = 48;

        // --- MV-1064 latch sparks + impact flare.
        private const float SparksPerSecond = 12f;   // spec: "10-14 ... per second"
        private const float SparkDistanceMin = 0.6f;
        private const float SparkDistanceMax = 1.2f;
        private const float SparkLifeMin = 0.25f;
        private const float SparkLifeMax = 0.4f;

        /// <summary>Spec: "a pulsing impact flare ... at the contact point". MV-1070 AC3: "no glow or
        /// flare at the tip may be wider than the sheath itself (0.70m)" — clamped down from MV-1064's
        /// original 0.8m, which would now fail that hard cap.</summary>
        private const float ImpactFlareSize = 0.65f;
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

        /// <summary>MV-1128 spec #8: "beams 1 and 2 are drawn with every line width at 0.72 of beam 0's."</summary>
        public const float SecondaryWidthScale = 0.72f;

        /// <summary>MV-1128: how many beams this VFX ever builds a full renderer set for — kept in sync
        /// BY HAND with <see cref="MaxWorlds.Combat.Undertow.MaxBeamCount"/> (a direct reference would
        /// need a cross-namespace dependency this VFX-layer class doesn't otherwise carry).</summary>
        private const int MaxBeams = 3;

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
        private static readonly Color SparkColorA = new Color(1f, 0.55f, 0.12f, 1f);
        private static readonly Color SparkColorB = new Color(1f, 0.2f, 0.08f, 1f);
        private static readonly Color ImpactFlareColor = new Color(1f, 0.45f, 0.12f, 1f);

        /// <summary>MV-1121 spec: "orange #FF7A1A with a white line inside it" — the lock ring's own
        /// outer/inner pair, the same core/sheath (white-inside-colour) idiom the beam itself already uses.</summary>
        private static readonly Color LockRingOuterColor = new Color(1f, 0.478f, 0.102f, 1f);
        private static readonly Color LockRingInnerColor = new Color(1.6f, 1.65f, 1.7f, 1f);

        /// <summary>MV-1128: one beam's full renderer set and animation/latch state — beam 0's own
        /// instance keeps the exact field roles the old single-beam fields had, just addressed through
        /// <c>_beams[0]</c> instead of bare fields, so its rendered geometry is bit-identical to before
        /// this ticket.</summary>
        private sealed class BeamVisual
        {
            public LineRenderer Core;
            public LineRenderer Sheath;
            public LineRenderer[] Strands;
            public LineRenderer[] Coils;
            public LineRenderer LockRingOuter;
            public LineRenderer LockRingInner;

            public Vector3[] Centerline;
            public bool CenterlineValid;
            public float BeamTime;
            public float Phase;
            public bool Streaming;

            public Transform CoilTarget;
            public CharacterController CoilCc;
            public float CoilPhase;
            public float LatchVisibility;
            public float SparkAccumulator;
            public float LockSnapElapsed;
            public bool WasLatchedLastFrame;
            public float LockRadius;
        }

        private BeamVisual[] _beams;
        private VfxBurst _muzzleFlare;
        private VfxBurst _latchSparks;
        private VfxBurst _latchFlare;
        private bool _initialized;

        /// <summary>Whether beam 0's own renderers are currently enabled — what
        /// <see cref="MaxWorlds.Combat.Undertow.IsStreamVisible"/> reads.</summary>
        public bool IsStreaming => _initialized && _beams[0].Streaming;

        /// <summary>Builds every renderer once, for every beam (<see cref="MaxBeams"/>) up front — a
        /// secondary beam simply starts disabled, same as beam 0 always has, until
        /// <see cref="SetSecondaryStreaming"/> turns it on. Idempotent, and called explicitly by the
        /// owner (see <see cref="WaterVfx.Init"/>'s equivalent shape) rather than from Awake — neither
        /// Awake nor OnEnable reliably run for AddComponent outside Play mode.</summary>
        public void Init()
        {
            if (_initialized) return;
            _initialized = true;

            // MV-1121: every Undertow LINE uses a texture soft ACROSS its width and constant ALONG its
            // length (VfxMaterials.LineGlow), not the radial Glow blob a LineRenderer stretches end to
            // end — that stretch is exactly what read as "thick in the middle, wispy at the gun and the
            // tip". Particle bursts (muzzle flare / latch sparks / impact flare) are untouched by this —
            // they keep the radial Glow blob, which is correct for a one-shot point flash.
            Material lineGlow = VfxMaterials.Additive(VfxMaterials.LineGlow());
            Material burstGlow = VfxMaterials.Additive(VfxMaterials.Glow());

            _beams = new BeamVisual[MaxBeams];
            for (int bi = 0; bi < MaxBeams; bi++)
            {
                // MV-1128 spec #8: beams 1/2 draw every line at SecondaryWidthScale of beam 0's.
                float scale = bi == 0 ? 1f : SecondaryWidthScale;
                string suffix = bi == 0 ? string.Empty : $"Beam{bi}";
                var beam = new BeamVisual();

                beam.Sheath = BuildLine($"UndertowSheath{suffix}", SheathWidth * scale, lineGlow, positionCount: Segments + 1);
                beam.Core = BuildLine($"UndertowCore{suffix}", CoreWidth * scale, lineGlow, positionCount: Segments + 1);
                beam.Centerline = new Vector3[Segments + 1];

                beam.Strands = new LineRenderer[StrandCount];
                for (int i = 0; i < StrandCount; i++)
                    beam.Strands[i] = BuildLine($"UndertowStrand{suffix}{i}", StrandWidth * scale, lineGlow, positionCount: Segments + 1);

                beam.Coils = new LineRenderer[CoilCount];
                for (int i = 0; i < CoilCount; i++)
                    beam.Coils[i] = BuildLine($"UndertowCoil{suffix}{i}", LockThicknessMin * scale, lineGlow, positionCount: CoilSegments + 1);

                // MV-1121: the lock ring — a closed circle round the latched robot, sized from its own
                // renderer bounds. Built as an outer/inner pair (orange with a white line inside it), the
                // same core/sheath idiom the beam itself already uses.
                beam.LockRingOuter = BuildLine($"UndertowLockRing{suffix}", LockThicknessMin * LockRingThicknessMultiplier * scale, lineGlow, positionCount: LockRingSegments + 1);
                beam.LockRingInner = BuildLine($"UndertowLockRingCore{suffix}", LockThicknessMin * 0.4f * scale, lineGlow, positionCount: LockRingSegments + 1);

                _beams[bi] = beam;
            }

            _muzzleFlare = new VfxBurst("UndertowMuzzleFlare", burstGlow, 24, 0f, perFrameCap: 4);
            _latchSparks = new VfxBurst("UndertowLatchSparks", burstGlow, 64, 0f, perFrameCap: 6);
            _latchFlare = new VfxBurst("UndertowLatchFlare", burstGlow, 16, 0f, perFrameCap: 2);
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

        /// <summary>Start/stop beam 0's stream. Only acts on change, so it is free to call every frame.</summary>
        public void SetStreaming(bool on) => SetBeamStreaming(0, on);

        /// <summary>MV-1128: start/stop a secondary beam (<paramref name="slot"/> 0 -&gt; beam index 1, 1
        /// -&gt; beam index 2) — same on-change-only shape as <see cref="SetStreaming"/>.</summary>
        public void SetSecondaryStreaming(int slot, bool on) => SetBeamStreaming(slot + 1, on);

        private void SetBeamStreaming(int beamIndex, bool on)
        {
            if (!_initialized) return;
            BeamVisual beam = _beams[beamIndex];
            if (beam.Streaming == on) return;
            beam.Streaming = on;
            // Force a hard snap (no whip lag) on the first frame back on, so the centreline doesn't
            // ease in from wherever it was left sitting the last time the stream was up.
            if (on) beam.CenterlineValid = false;

            beam.Sheath.enabled = on;
            beam.Core.enabled = on;
            for (int i = 0; i < beam.Strands.Length; i++) beam.Strands[i].enabled = on;
        }

        /// <summary>Re-lay beam 0's core/sheath/strand geometry between <paramref name="muzzle"/> and
        /// <paramref name="endPoint"/>, advancing the crackle strands' animation phase by
        /// <paramref name="dt"/>. Called every frame the stream is up — cheap, no allocation — so the
        /// beam tracks Max moving/aiming even between the fire-tick cadence that moves
        /// <paramref name="endPoint"/> itself.</summary>
        public void UpdateStream(Vector3 muzzle, Vector3 endPoint, Vector3 aimDirection, float dt) =>
            LayBeamGeometry(0, muzzle, endPoint, aimDirection, dt);

        /// <summary>MV-1128: re-lay a secondary beam's own geometry — identical algorithm to
        /// <see cref="UpdateStream"/>, just addressed at <paramref name="slot"/>+1's own renderer/
        /// animation state.</summary>
        public void UpdateSecondaryStream(int slot, Vector3 muzzle, Vector3 endPoint, Vector3 aimDirection, float dt) =>
            LayBeamGeometry(slot + 1, muzzle, endPoint, aimDirection, dt);

        private void LayBeamGeometry(int beamIndex, Vector3 muzzle, Vector3 endPoint, Vector3 aimDirection, float dt)
        {
            if (!_initialized) return;
            BeamVisual beam = _beams[beamIndex];
            if (!beam.Streaming) return;
            beam.Phase += dt * PhaseSpeed;
            beam.BeamTime += dt;

            Vector3 axis = endPoint - muzzle;
            float length = axis.magnitude;

            // MV-1121: the wrap/snake frame is anchored to the AIM direction, not the muzzle->tip chord —
            // the beam always leaves the gun along the aim line (Change 3), so the plane it snakes/wraps
            // in has to be the one that direction defines, not one that rotates with wherever the tip
            // happens to be.
            Vector3 aimDir = aimDirection.sqrMagnitude > 1e-6f
                ? aimDirection.normalized
                : (length > 1e-4f ? axis / length : Vector3.forward);
            Vector3 right = Vector3.Cross(Vector3.up, aimDir);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();
            Vector3 wrapUp = Vector3.Cross(aimDir, right);

            // MV-1121 Change 3: a quadratic curve from the muzzle to the tip, control point on the aim
            // line CurveControlFraction of the way out — replaces MV-1046's straight muzzle->tip chord
            // as the base the snake wave rides on.
            Vector3 control = muzzle + aimDir * (length * CurveControlFraction);

            // --- The whole beam snakes along that curve: one centreline, walked by both the core and
            // the sheath.
            for (int i = 0; i <= Segments; i++)
            {
                float t = (float)i / Segments;
                float oneMinusT = 1f - t;
                Vector3 basePoint = oneMinusT * oneMinusT * muzzle
                    + 2f * oneMinusT * t * control
                    + t * t * endPoint;

                // Both ends pinned (muzzle, exact hit point): taper is 0 at t=0 and t=1. Clamped to
                // >=0 first — float rounding can make sin(t*pi) a tiny NEGATIVE epsilon right at t=1,
                // and Mathf.Pow(negative, non-integer) is NaN, not a small negative number.
                float taper = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), TaperExponent);

                float theta1 = 2f * Mathf.PI * LateralFreq1 * t - LateralK1 * beam.BeamTime;
                float theta2 = 2f * Mathf.PI * LateralFreq2 * t - LateralK2 * beam.BeamTime + LateralPhase2;
                float lateral = LateralAmplitude * taper * (Mathf.Sin(theta1) + LateralTermWeight2 * Mathf.Sin(theta2));
                // Quadrature (+90°) with the primary lateral term traces a corkscrew rather than a
                // second, uncorrelated wobble.
                float vertical = VerticalAmplitude * taper * Mathf.Cos(theta1);

                Vector3 target = basePoint + right * lateral + Vector3.up * vertical;

                // MV-1064 spec #9: "The last 1m of the beam curls into the coils rather than stopping
                // dead." Only the tail of the centreline (within CurlLength of the end point) bulges
                // outward toward the lock radius; eased by LatchVisibility so the curl appears/vanishes
                // in step with the coils themselves. The weight is a bump (0 at the curl's own start AND
                // at t=1) rather than a ramp all the way to t=1, so the very last point stays pinned
                // exactly on the end point — the invariant MV-1046's own test (and the hit-point math
                // StreamEndPoint feeds) both depend on — while the segments just before it visibly flare
                // into the wrap.
                if (beam.LatchVisibility > 0f && length > 1e-4f)
                {
                    float distFromMuzzle = t * length;
                    float curlStart = Mathf.Max(0f, length - CurlLength);
                    if (distFromMuzzle >= curlStart && t < 1f)
                    {
                        float curlT = Mathf.InverseLerp(curlStart, length, distFromMuzzle);
                        float curlWeight = Mathf.Sin(curlT * Mathf.PI); // 0 at curlStart and at t=1, peak mid-way
                        float curlRadius = beam.LockRadius * curlWeight * beam.LatchVisibility;
                        float curlAngle = beam.CoilPhase + curlT * CoilTurns * 2f * Mathf.PI;
                        Vector3 curlOffset = (right * Mathf.Cos(curlAngle) + wrapUp * Mathf.Sin(curlAngle)) * curlRadius;
                        target += curlOffset;
                    }
                }

                if (!beam.CenterlineValid)
                {
                    beam.Centerline[i] = target;
                }
                else
                {
                    // Whip on aim changes: lag grows along the beam (muzzle 0, end WhipLagAtEnd).
                    float lag = WhipLagAtEnd * t;
                    float easeFactor = lag > 1e-5f ? 1f - Mathf.Exp(-dt / lag) : 1f;
                    beam.Centerline[i] = Vector3.Lerp(beam.Centerline[i], target, easeFactor);
                }
            }
            beam.CenterlineValid = true;

            beam.Core.SetPositions(beam.Centerline);
            beam.Core.startColor = CoreColor;
            beam.Core.endColor = CoreColor;

            beam.Sheath.SetPositions(beam.Centerline);
            beam.Sheath.startColor = SheathColor;
            beam.Sheath.endColor = SheathColor;

            // --- The crackle strands wrap the snaking centreline (offset from it, not the straight line).
            // MV-1121: constant 0.24m wrap, ramped up from zero only over the first 6% of the beam —
            // replaces MV-1064's sin(t*pi) taper, which peaked at mid-beam and fell back to zero at the
            // tip (the beam must now end at a PLAIN CUT, full width).
            for (int s = 0; s < beam.Strands.Length; s++)
            {
                LineRenderer strand = beam.Strands[s];
                float wrapAngle = (360f / beam.Strands.Length) * s * Mathf.Deg2Rad;
                Vector3 wrapDir = right * Mathf.Cos(wrapAngle) + wrapUp * Mathf.Sin(wrapAngle);

                for (int i = 0; i <= Segments; i++)
                {
                    float t = (float)i / Segments;
                    float strandTaper = t < StrandRampFraction ? (t / StrandRampFraction) : 1f;
                    float wave = Mathf.Sin(t * WaveCyclesPerBeam * Mathf.PI * 2f + beam.Phase + s * 2.1f);
                    Vector3 point = beam.Centerline[i] + wrapDir * (StrandAmplitude * strandTaper * wave);
                    strand.SetPosition(i, point);
                }

                Color c = EmberColors[s % EmberColors.Length];
                strand.startColor = c;
                strand.endColor = c;
            }
        }

        /// <summary>The combined world-space renderer bounds of every renderer under
        /// <paramref name="target"/>, plus a legacy CharacterController-radius fallback for a test double
        /// built with no renderer at all — every real in-game robot carries a <see cref="RobotRig"/> and
        /// always takes the renderer-bounds branch. MV-1121 spec: lock radius is half the widest
        /// HORIZONTAL extent of that combined bounds, plus <see cref="LockRadiusPad"/> — "never the
        /// controller radius".</summary>
        private static void ResolveLockGeometry(Transform target, CharacterController cc,
            out float radius, out Vector3 centre, out float floorY, out float topY)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(includeInactive: false);
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                radius = 0.5f * Mathf.Max(b.size.x, b.size.z) + LockRadiusPad;
                centre = b.center;
                floorY = b.min.y;
                topY = b.max.y;
                return;
            }

            radius = (cc != null ? cc.radius : 0.5f) + LegacyRadiusPad;
            centre = cc != null ? cc.bounds.center : target.position;
            float height = cc != null ? cc.height : 1.8f;
            floorY = centre.y - height * 0.5f;
            topY = centre.y + height * 0.5f;
        }

        /// <summary>MV-1064: drives beam 0's wrap-around coils/lock ring and the steady spark/impact-flare
        /// spray while latched. Called every frame <see cref="MaxWorlds.Combat.Undertow.Tick"/> runs (not
        /// just on the fire-tick cadence) so they track a moving target and the appear/vanish ease
        /// (<see cref="CoilAppearSeconds"/>/<see cref="CoilVanishSeconds"/>) is smooth. <paramref name="on"/>
        /// false with a null <paramref name="targetTransform"/>/<paramref name="targetCc"/> starts the
        /// vanish while still showing the coils/ring at their last known position/size.</summary>
        public void SetLatch(bool on, Transform targetTransform, CharacterController targetCc, float dt) =>
            DriveLatch(0, on, targetTransform, targetCc, dt);

        /// <summary>MV-1128: the same latch-drive as <see cref="SetLatch"/>, for a secondary beam
        /// (<paramref name="slot"/> 0 -&gt; beam index 1, 1 -&gt; beam index 2) — "the same lock ring and
        /// coils as beam 0" (spec #8), just its own independent target/state.</summary>
        public void SetSecondaryLatch(int slot, bool on, Transform targetTransform, CharacterController targetCc, float dt) =>
            DriveLatch(slot + 1, on, targetTransform, targetCc, dt);

        private void DriveLatch(int beamIndex, bool on, Transform targetTransform, CharacterController targetCc, float dt)
        {
            if (!_initialized) return;
            BeamVisual beam = _beams[beamIndex];

            bool freshLatch = on && !beam.WasLatchedLastFrame;
            beam.WasLatchedLastFrame = on;
            if (on) beam.LockSnapElapsed = freshLatch ? 0f : beam.LockSnapElapsed + Mathf.Max(dt, 0f);

            if (on)
            {
                beam.CoilTarget = targetTransform;
                beam.CoilCc = targetCc;
            }

            float rate = on ? 1f / CoilAppearSeconds : -1f / CoilVanishSeconds;
            beam.LatchVisibility = Mathf.Clamp01(beam.LatchVisibility + rate * Mathf.Max(dt, 0f));

            bool show = beam.LatchVisibility > 0f && beam.CoilTarget != null;
            for (int i = 0; i < beam.Coils.Length; i++) beam.Coils[i].enabled = show;
            beam.LockRingOuter.enabled = show;
            beam.LockRingInner.enabled = show;

            if (!show)
            {
                if (beam.LatchVisibility <= 0f) { beam.CoilTarget = null; beam.CoilCc = null; beam.LockRadius = 0f; }
                return;
            }

            ResolveLockGeometry(beam.CoilTarget, beam.CoilCc, out float radius, out Vector3 centre, out float floorY, out float topY);
            beam.LockRadius = radius;

            float thickness = Mathf.Clamp(LockThicknessBase * (radius / LockThicknessRefRadius), LockThicknessMin, LockThicknessMax);
            float ringThickness = thickness * LockRingThicknessMultiplier;

            float snapT = Mathf.Clamp01(beam.LockSnapElapsed / LockRingSnapSeconds);
            float ringRadius = Mathf.Lerp(radius * LockRingOvershootMultiplier, radius, snapT);
            float ringY = floorY + LockRingHeightAboveFloor;

            beam.LockRingOuter.widthMultiplier = ringThickness;
            beam.LockRingInner.widthMultiplier = Mathf.Min(thickness * 0.4f, ringThickness * 0.4f);

            Color ringOuter = LockRingOuterColor; ringOuter.a *= beam.LatchVisibility;
            Color ringInner = LockRingInnerColor; ringInner.a *= beam.LatchVisibility;
            beam.LockRingOuter.startColor = beam.LockRingOuter.endColor = ringOuter;
            beam.LockRingInner.startColor = beam.LockRingInner.endColor = ringInner;

            for (int i = 0; i <= LockRingSegments; i++)
            {
                float angle = (float)i / LockRingSegments * 2f * Mathf.PI;
                Vector3 p = new Vector3(
                    centre.x + Mathf.Cos(angle) * ringRadius,
                    ringY,
                    centre.z + Mathf.Sin(angle) * ringRadius);
                beam.LockRingOuter.SetPosition(i, p);
                beam.LockRingInner.SetPosition(i, p);
            }

            beam.CoilPhase += dt * CoilRevsPerSecond * 2f * Mathf.PI;
            float coilBottom = floorY + CoilBottomHeight;
            float coilTop = Mathf.Max(coilBottom + 0.05f, topY);

            for (int c = 0; c < beam.Coils.Length; c++)
            {
                LineRenderer coil = beam.Coils[c];
                coil.widthMultiplier = thickness;
                float coilPhaseOffset = (2f * Mathf.PI / beam.Coils.Length) * c;
                for (int i = 0; i <= CoilSegments; i++)
                {
                    float t = (float)i / CoilSegments;
                    float angle = beam.CoilPhase + coilPhaseOffset + t * CoilTurns * 2f * Mathf.PI;
                    Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    Vector3 basePos = new Vector3(centre.x, Mathf.Lerp(coilBottom, coilTop, t), centre.z);
                    coil.SetPosition(i, basePos + radial);
                }

                Color col = EmberColors[c % EmberColors.Length];
                col.a *= beam.LatchVisibility;
                coil.startColor = col;
                coil.endColor = col;
            }

            // Spec: "10-14 orange/red sparks per second fly 0.6-1.2m off the robot" — accumulated
            // fractionally so the rate holds true regardless of frame time, same idiom a cooldown
            // timer uses.
            beam.SparkAccumulator += dt * SparksPerSecond;
            while (beam.SparkAccumulator >= 1f)
            {
                beam.SparkAccumulator -= 1f;
                Vector3 dir = Random.onUnitSphere;
                float life = Random.Range(SparkLifeMin, SparkLifeMax);
                float dist = Random.Range(SparkDistanceMin, SparkDistanceMax);
                _latchSparks.Emit(centre, 1, dir, 180f,
                    speedMin: dist / life, speedMax: dist / life,
                    sizeMin: 0.05f, sizeMax: 0.09f,
                    lifeMin: life, lifeMax: life,
                    colorA: SparkColorA, colorB: SparkColorB);
            }

            // Spec: "a pulsing impact flare ... at the contact point" -- clamped to ImpactFlareSize,
            // itself capped at the sheath's own 0.70m by MV-1070 AC3 ("no renderer at the tip wider
            // than the sheath -- no ball").
            float pulse = ImpactFlareSize * (0.85f + 0.15f * Mathf.Sin(Time.time * ImpactPulseHz * 2f * Mathf.PI));
            _latchFlare.Emit(centre, 1, Vector3.up, 180f,
                speedMin: 0f, speedMax: 0f,
                sizeMin: pulse, sizeMax: pulse,
                lifeMin: 0.1f, lifeMax: 0.1f,
                colorA: ImpactFlareColor, colorB: ImpactFlareColor);
        }

        /// <summary>The per-tick punctuation (spec): a bright muzzle flare at Max. Called once per fire
        /// tick (regardless of how many beams fired that tick), not per frame.</summary>
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
        }

        private void LateUpdate()
        {
            if (!_initialized) return;
            _muzzleFlare.EndFrame();
            _latchSparks.EndFrame();
            _latchFlare.EndFrame();
        }

        private void OnDestroy()
        {
            if (_beams != null)
            {
                foreach (BeamVisual beam in _beams)
                {
                    if (beam == null) continue;
                    DestroyLine(beam.Core);
                    DestroyLine(beam.Sheath);
                    if (beam.Strands != null)
                        for (int i = 0; i < beam.Strands.Length; i++) DestroyLine(beam.Strands[i]);
                    if (beam.Coils != null)
                        for (int i = 0; i < beam.Coils.Length; i++) DestroyLine(beam.Coils[i]);
                    DestroyLine(beam.LockRingOuter);
                    DestroyLine(beam.LockRingInner);
                }
            }
            Dispose(_muzzleFlare);
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
