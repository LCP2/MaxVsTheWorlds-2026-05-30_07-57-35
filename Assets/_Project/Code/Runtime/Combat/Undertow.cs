using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Combat
{
    /// <summary>
    /// World 3's primary (MV-714, re-shaped by MV-1034) — UNDERTOW: same shell as
    /// <see cref="WaterBlaster"/>/<see cref="PulseLaser"/> (an <see cref="EnergyPool"/> tank built from
    /// <see cref="BlasterTuning"/>, an optional <see cref="PlayerController"/> aim source driving
    /// <see cref="IsFiring"/>/facing).
    ///
    /// Holding fire streams the <b>pressure lance</b> continuously for as long as the trigger is held —
    /// a narrow, long-ranged tick that pierces up to <see cref="MaxPierceCount"/> robots in a line — at
    /// the same per-tick cadence and (base) damage as the RCDA, so its DPS tracks the RCDA's within the
    /// ticket's 10% band by construction rather than by a coincidentally-matched authored number.
    ///
    /// MV-1034 removed the charge-and-release cavitation shot entirely (Lee: "the blue ball does not
    /// work as a design — the Shoulder Rack rockets already kill robots before it can convert them").
    /// There is no charge phase any more: releasing fire simply stops the stream, the same "trigger
    /// held = stream on" shape <see cref="WaterBlaster"/> already uses. Robot conversion
    /// (<see cref="MaxWorlds.Enemies.RobotEnemy.TryConvert"/>) is unchanged and now belongs to a
    /// separate trap ability (MV-1035).
    ///
    /// MV-1070 replaced MV-1064's "latch only acquires dead-on, inside the 3-degree fire cone" shape
    /// with a genuinely SEEKING tip: <see cref="Branch.TipPosition"/> is a simulated point, spring-pulled
    /// toward the nearest eligible robot/boss within <see cref="AcquireConeDegrees"/> of Max's aim (or
    /// swaying around the aim point at <see cref="Range"/> when nothing qualifies), and only counts as
    /// <see cref="IsLatched"/> once it is physically within <see cref="LatchDistance"/> of that target's
    /// own centre. The beam is always drawn from the muzzle to the tip (<see cref="StreamEndPoint"/>),
    /// whether seeking or latched — see <see cref="UpdateTip"/>. Damage still only ever lands while
    /// latched (<see cref="FireLatchedTick"/>); with no seek candidate at all, a gate on the aim line is
    /// still hit exactly as before MV-1070 (<see cref="FireAimedGateOnlyTick"/>) — robots can no longer
    /// take a hit any other way.
    /// </summary>
    [MaxWorlds.Core.PerfSection("combat")]
    public sealed class Undertow : MonoBehaviour, IPrimaryEnergy
    {
        /// <summary>MV-1106 (Lee, 2026-10-06): "the primary weapon much too weak" — UNDERTOW gets its
        /// own base damage constant, 8 per tick (80/s at Power 0), twice <see cref="WaterBlaster"/>'s 4
        /// rather than inheriting it. World 1/2's RCDA/LPPE are untouched — this is World 3 only.</summary>
        public const float DefaultDamagePerTick = 8f;

        public const float DefaultFireInterval = 0.1f;

        /// <summary>Longer-ranged than the RCDA's 5m base (spec).</summary>
        public const float DefaultRange = 9f;

        /// <summary>Narrower than the RCDA's 8° base (spec) — approximates a line rather than a fan,
        /// the same "narrow cone as a line" idiom the codebase already uses for hit-testing instead of a
        /// raycast (see <see cref="SprayHit"/>). MV-1070: this is now ONLY the gate-hit cone
        /// (<see cref="FireAimedGateOnlyTick"/>) — robot targeting uses <see cref="AcquireConeDegrees"/>
        /// instead.</summary>
        public const float DefaultConeHalfAngle = 3f;

        /// <summary>"Pierces up to two robots" (spec) — a hard count cap on the closest in-cone,
        /// in-range, in-sight targets, not a distance/falloff cutoff.</summary>
        public const int MaxPierceCount = 2;

        /// <summary>MV-1128 ("SPLIT"): SPLIT's level is a plain beam count — never fewer than this, so a
        /// save predating this ticket (or a <c>p_spr</c> level of 0 some other way) still draws the one
        /// beam SPLIT 1 guarantees rather than none.</summary>
        public const int MinBeamCount = 1;

        /// <summary>MV-1128: the hard ceiling on simultaneous beams <see cref="_branches"/> actually
        /// allocates — matches World 3's own board cap on <c>p_spr</c> (<c>rig_board.world3.json</c>'s
        /// <c>maxLevel</c>), duplicated here so a stale level above the board's current cap can never ask
        /// for more branches than this class holds.</summary>
        public const int MaxBeamCount = 3;

        /// <summary>MV-1128 spec #6: each beam beyond the first deals this fraction of a full tick's
        /// damage — a named constant so the figure (Lee's to retune later, per the ticket) lives in one
        /// place rather than a bare literal inside <see cref="FireLatchedTick"/>.</summary>
        public const float SplitSecondaryDamageFraction = 0.6f;

        /// <summary>MV-1064, kept by MV-1070: once latched, the target stays latched out to slightly
        /// beyond the lance's own <see cref="Range"/> — a plain `&lt;= Range` would drop the latch the
        /// instant a damaged robot staggers back half a step, which reads as flickery rather than a
        /// solid grip.</summary>
        public const float LatchRangeMultiplier = 1.15f;

        /// <summary>MV-1064 spec, unchanged by MV-1070: "the latched pierce target is the nearest OTHER
        /// robot within 1m of the line from Max through the latched robot, not a cone — the latch has
        /// already bent the beam away from Max's own aim axis, so re-using the cone here would pierce
        /// along the wrong line.</summary>
        public const float LatchPierceLineRadius = 1.0f;

        /// <summary>MV-1070 spec #2: the tip seeks the nearest eligible robot/boss within this angle of
        /// Max's aim — much wider than the lance's own <see cref="ConeHalfAngle"/>, since Max only needs
        /// to point in the GENERAL direction and the tip does the rest.</summary>
        public const float AcquireConeDegrees = 35f;

        /// <summary>MV-1070 spec #5: once latched, the hold widens to this angle before it breaks (was
        /// MV-1064's 30 degrees) — wider than <see cref="AcquireConeDegrees"/> on purpose, so the latch
        /// doesn't flicker right at the edge of its own acquire cone.</summary>
        public const float LatchBreakAngleDegrees = 45f;

        /// <summary>MV-1070 spec #4: "latched... when the tip is within 0.4m of the robot's centre".</summary>
        public const float LatchDistance = 0.4f;

        /// <summary>MV-1070 spec #4: natural frequency of the tip's seek spring, ~3.5Hz.</summary>
        public const float TipSpringHz = 3.5f;

        /// <summary>MV-1070 spec #4: damping ratio of the tip's seek spring, under-damped so it visibly
        /// overshoots before settling.</summary>
        public const float TipDampingRatio = 0.45f;

        /// <summary>MV-1070 spec #4: "tip speed capped at 18 m/s".</summary>
        public const float TipMaxSpeed = 18f;

        /// <summary>MV-1121 Change 3: "the tip sweeps side to side across the aim line, 1.25 m each way
        /// at 0.8 Hz (today 0.35 m)" — up from MV-1070's 0.35m-across figure-of-eight (each-way amplitude
        /// ~0.175m), so the free end reads as visibly searching. This is now the each-way amplitude
        /// itself (<see cref="SwayOffset"/> no longer halves it), not a peak-to-peak "width".</summary>
        public const float SwayWidth = 2.5f;

        /// <summary>MV-1070 spec #3: "~0.8 Hz".</summary>
        public const float SwayHz = 0.8f;

        /// <summary>The spring is integrated in fixed sub-steps no matter how large <c>dt</c> is handed
        /// to <see cref="Tick"/> — an EditMode test drives this with one large explicit <c>dt</c> rather
        /// than many small per-frame ones (see <c>MV1036UndertowGatedOutsideWorld3Tests</c>'s own 1.2s
        /// single tick), and a single large semi-implicit-Euler step at ~22 rad/s would be numerically
        /// unstable. Sub-stepping at a fixed, small interval keeps every call — real per-frame or one
        /// big EditMode tick — physically identical.</summary>
        private const float TipSubstep = 1f / 120f;

        private const int MaxTipSubsteps = 4096;

        [Header("Lance")]
        [SerializeField] private float range = DefaultRange;
        [SerializeField] private float coneHalfAngle = DefaultConeHalfAngle;
        [SerializeField] private float damagePerTick = DefaultDamagePerTick;
        [SerializeField] private float fireInterval = DefaultFireInterval;
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Aim source")]
        [Tooltip("Optional. If set, fires while the player aims and orients to their facing. " +
                 "If null, IsFiring drives it directly (useful for isolated testing).")]
        [SerializeField] private PlayerController aimSource;

        /// <summary>Whether the trigger is currently held — same contract as <see cref="WaterBlaster.IsFiring"/>.</summary>
        public bool IsFiring { get; private set; }

        /// <summary>Pure fire-gate decision (unit-testable), same shape as <see cref="WaterBlaster.ShouldEmit"/>.</summary>
        public static bool ShouldEmit(bool firingHeld, bool hasEnergy) => firingHeld && hasEnergy;

        public void SetFiring(bool firing) => IsFiring = firing;
        public bool IsEmitting => _lastEmitting;

        /// <summary>The energy tank, 0..1 — same tank shape the RCDA/LPPE drain from.</summary>
        public float EnergyNormalized => _tank != null ? _tank.Normalized : 1f;

        /// <summary>How far the lance reaches — the RCDA Range track's own bonus layered on top (spec:
        /// the weapon is picked up by Max's existing RCDA tracks, not a new set).</summary>
        public float Range => WeaponCatalog.EffectiveRange(
            range, WeaponSystemState.TrackLevel(WeaponTrackKind.Range), WeaponCatalog.DefaultRcdaRangePerLevel);

        /// <summary>MV-1070: only the GATE acquire cone (<see cref="FireAimedGateOnlyTick"/>) — robot
        /// targeting always uses <see cref="AcquireConeDegrees"/>/<see cref="LatchBreakAngleDegrees"/>.
        /// MV-1128: no longer reads SPLIT (<c>p_spr</c>) at all — a device playtest found the track
        /// widened this cone and nothing else, which robots never notice, so SPLIT now drives
        /// <see cref="BeamCount"/> instead and this is just the lance's own authored base angle.</summary>
        public float ConeHalfAngle => coneHalfAngle;

        /// <summary>MV-1128 ("SPLIT"): the number of simultaneous beams — 1, 2 or 3, directly SPLIT's own
        /// resolved level (<c>p_spr</c> on World 3's board), clamped to <see cref="MinBeamCount"/>..
        /// <see cref="MaxBeamCount"/> so a stale level (a pre-ticket save holding 0, or one holding a
        /// level above the board's current cap) always resolves to a playable beam count rather than
        /// needing a separate floor/ceiling fix-up at save/load time.</summary>
        public int BeamCount => Mathf.Clamp(
            WeaponSystemState.TrackLevel(WeaponTrackKind.Spread), MinBeamCount, MaxBeamCount);

        /// <summary>Damage one lance tick deals right now — the RCDA Damage track's own bonus layered on
        /// top, the same formula <see cref="WaterBlaster.EffectiveDamagePerTick"/> uses.</summary>
        public float EffectiveDamagePerTick => WeaponCatalog.EffectiveDamagePerTick(
            damagePerTick, WeaponSystemState.TrackLevel(WeaponTrackKind.Damage), WeaponCatalog.DefaultRcdaDamagePerLevel);

        public float FireInterval => fireInterval;

        /// <summary>What the lance actually outputs per second, per pierced target — AC5's comparison
        /// point against the RCDA's own <see cref="WaterBlaster.DamagePerSecond"/>.</summary>
        public float DamagePerSecond => fireInterval > 0f ? EffectiveDamagePerTick / fireInterval : 0f;

        /// <summary>Flat per-tick energy cost — the RCDA's own base drain rate, not track-scaled the way
        /// the RCDA's is (same "flat authored number" shape <see cref="PulseLaser.EnergyPerPulse"/> uses).</summary>
        public float EnergyPerTick => DevTuning.Or(DevTuning.PrimaryDepletionRate, BlasterTuning.EnergyPerSecond) * fireInterval;

        /// <summary>MV-1070: where the beam's SEEKING TIP currently sits, world space — the muzzle end
        /// draws to here every frame regardless of whether anything is latched (spec #1). Set every
        /// <see cref="Tick"/> by <see cref="UpdateTip"/>.</summary>
        public Vector3 StreamEndPoint { get; private set; }

        /// <summary>Whether the stream's VFX is actually showing right now — what an EditMode test
        /// reads instead of inspecting the VFX component's own renderers directly.</summary>
        public bool IsStreamVisible => _vfx != null && _vfx.IsStreaming;

        /// <summary>MV-1064, re-shaped by MV-1070: whether the lance's tip is currently latched onto a
        /// robot/boss — true once <see cref="Branch.TipPosition"/> has physically closed to within
        /// <see cref="LatchDistance"/> of it, not merely "a candidate is being sought".</summary>
        public bool IsLatched => _branch.LatchedTarget != null;

        /// <summary>The latched target's transform, or null when nothing is latched — what the VFX
        /// (and an EditMode test) reads to find the robot the coils must wrap.</summary>
        public Transform LatchedTransform => _branch.LatchedTarget != null ? _branch.SeekTransform : null;

        /// <summary>MV-1128: beam <paramref name="index"/> (0-based)'s own latch state — index 0 is
        /// <see cref="IsLatched"/> itself; indices 1/2 only matter once <see cref="BeamCount"/> is high
        /// enough to have claimed them a target.</summary>
        public bool IsBeamLatched(int index) =>
            index < _branches.Count && _branches[index].LatchedTarget != null;

        /// <summary>MV-1128: beam <paramref name="index"/>'s own latched target's transform, or null —
        /// the per-beam twin of <see cref="LatchedTransform"/>.</summary>
        public Transform LatchedTransformAt(int index) =>
            index < _branches.Count && _branches[index].LatchedTarget != null ? _branches[index].SeekTransform : null;

        /// <summary>MV-1128 spec #5: "A beam with no candidate is not drawn." Beam 0 always draws while
        /// emitting (it sways at the rest point when nothing qualifies — unchanged SPLIT-1 behaviour); a
        /// secondary beam (<paramref name="index"/> &gt;= 1) draws only once it has its own candidate.
        /// What a test reads instead of inspecting <see cref="MaxWorlds.VFX.UndertowVfx"/>'s own
        /// LineRenderers directly.</summary>
        public bool IsBeamDrawn(int index)
        {
            if (!_lastEmitting) return false;
            if (index >= BeamCount || index >= _branches.Count) return false;
            return index == 0 || _branches[index].SeekTarget != null;
        }

        private float _tickTimer;
        private bool _lastEmitting;
        private bool _depleted;
        private EnergyPool _tank;
        private UndertowVfx _vfx;
        private AimReticle _reticle;

        /// <summary>MV-1070 spec #7: "hold the tip state ... in one small Branch object inside a list of
        /// length 1, so a future upgrade can run 2-3 branches. Do not build multiple branches now." Only
        /// <see cref="_branch"/> (<c>_branches[0]</c>) is ever used today.</summary>
        private sealed class Branch
        {
            public Vector3 TipPosition;
            public Vector3 TipVelocity;
            public bool TipValid;
            public float SwayTime;

            /// <summary>The robot/boss the tip is currently pursuing (seeking OR already latched onto) —
            /// null when nothing qualifies and the tip is just swaying around the rest point.</summary>
            public IDamageable SeekTarget;
            public Transform SeekTransform;
            public CharacterController SeekCc;

            /// <summary>Sticky: set once <see cref="SeekTarget"/>'s centre is within
            /// <see cref="LatchDistance"/> of the tip, cleared only by an explicit break (see
            /// <see cref="LatchHolds"/>) or the stream stopping — never merely because the tip is
            /// momentarily more than <see cref="LatchDistance"/> from a target that is itself moving.</summary>
            public IDamageable LatchedTarget;
        }

        /// <summary>MV-1128: now MaxBeamCount branches, pre-allocated — the "future upgrade" the MV-1070
        /// doc comment above anticipated. Only <c>_branches[0..BeamCount-1]</c> are ever actively ticked
        /// at once; the rest sit dropped (see <see cref="Tick"/>).</summary>
        private readonly List<Branch> _branches =
            new List<Branch>(MaxBeamCount) { new Branch(), new Branch(), new Branch() };
        private Branch _branch => _branches[0];

        /// <summary>MV-1128 spec #4: targets an earlier (lower-index) beam has already claimed this
        /// frame — reused every tick like <see cref="s_buffer"/>; Undertow only ever ticks on the main
        /// thread, so a single shared buffer is safe.</summary>
        private static readonly List<IDamageable> s_claimedTargets = new List<IDamageable>(MaxBeamCount);

        private const int InitialHitBufferSize = 16;
        private Collider[] _hits = new Collider[InitialHitBufferSize];
        private static readonly List<IDamageable> s_buffer = new List<IDamageable>(4);
        private static readonly List<float> s_dist = new List<float>(4);

        private void Awake()
        {
            _tank = new EnergyPool(BlasterTuning.MaxEnergy, BlasterTuning.RegenPerSec, BlasterTuning.RegenDelay);

            // VFX attaches itself — no scene wiring, no prefab (code-driven scenes rule), same idiom
            // WaterVfx/LppeVfx use for their own weapons.
            _vfx = GetComponent<UndertowVfx>();
            if (_vfx == null) _vfx = gameObject.AddComponent<UndertowVfx>();
            _vfx.Init();

            // MV-1128: the floor wedge (YT-84) is the SAME shared, [DisallowMultipleComponent]
            // AimReticle component WaterBlaster also self-attaches to this same player GameObject —
            // whichever weapon is actually ActivePrimary is the only one allowed to feed it its own
            // numbers (see RefreshReticle/WaterBlaster.RefreshUpgrades' own matching gate), or the two
            // fight over the one shared mesh every time WeaponSystemState.Changed fires.
            _reticle = GetComponent<AimReticle>();
            if (_reticle == null) _reticle = gameObject.AddComponent<AimReticle>();
            RefreshReticle();

            // A safe resting endpoint before the first tick ever lands, so the very first emitting
            // frame doesn't draw a stream collapsed onto the origin.
            StreamEndPoint = transform.position + transform.forward * range;

            // MV-1012: self-attached from PlayerController.Awake (code-driven scenes, no scene wiring),
            // same "resolve-or-fall-back" shape PulseLaser.Awake uses for its own aimSource.
            if (aimSource == null) aimSource = GetComponent<PlayerController>();
        }

        private void OnEnable() => WeaponSystemState.Changed += RefreshReticle;
        private void OnDisable() => WeaponSystemState.Changed -= RefreshReticle;

        /// <summary>MV-1128: re-fits the shared <see cref="AimReticle"/> to UNDERTOW's own real numbers —
        /// only while UNDERTOW is actually the active primary (see <see cref="Awake"/>'s own doc for
        /// why). Spec #10: length <see cref="Range"/>, half-angle <see cref="AcquireConeDegrees"/> (the
        /// robot-acquire cone a device player actually feels — not the narrow gate cone,
        /// <see cref="ConeHalfAngle"/>, which would draw an almost invisible sliver).</summary>
        private void RefreshReticle()
        {
            if (_reticle != null && WeaponSystemState.ActivePrimary == WeaponCatalog.PrimaryKind.Undertow)
                _reticle.Init(transform, Range, AcquireConeDegrees);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The real per-frame update, pulled out to its own explicit-<paramref name="dt"/>
        /// method (the same shape <see cref="MaxWorlds.Enemies.RobotEnemy.TickConversion"/> already
        /// uses) so an EditMode test can drive exactly 0.1s ticks through the SAME logic
        /// <see cref="Update"/> calls, instead of reading Unity's own near-zero-in-EditMode
        /// <see cref="Time.deltaTime"/> (MV-1034's stream test).</summary>
        private void Tick(float dt)
        {
            _tank.Tick(dt);

            if (aimSource != null)
            {
                IsFiring = aimSource.IsAiming;
                Vector3 f = aimSource.Facing;
                if (f.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(f, Vector3.up);
            }

            if (DevMode.IsAutoFiring) IsFiring = true;
            if (DevMode.IsInfiniteEnergy) _tank.Refill();

            float cost = EnergyPerTick;
            if (_depleted && _tank.Normalized >= BlasterTuning.RechargeFraction) _depleted = false;
            else if (!_depleted && !_tank.CanSpend(cost)) _depleted = true;

            // MV-1012: this component is self-attached unconditionally from PlayerController.Awake (see
            // PulseLaser's own ActivePrimary gate for the same self-attach-then-no-op shape), so it must
            // gate itself rather than relying on being added/removed.
            bool emitting = WeaponSystemState.ActivePrimary == WeaponCatalog.PrimaryKind.Undertow
                && ShouldEmit(IsFiring, !_depleted && _tank.CanSpend(cost));
            _lastEmitting = emitting;

            if (_vfx != null) _vfx.SetStreaming(emitting);
            if (!emitting)
            {
                _tickTimer = 0f;
                for (int i = 0; i < _branches.Count; i++) DropAll(_branches[i]);
                if (_vfx != null)
                {
                    _vfx.SetLatch(false, null, null, dt);
                    for (int i = 1; i < MaxBeamCount; i++)
                    {
                        _vfx.SetSecondaryStreaming(i - 1, false);
                        _vfx.SetSecondaryLatch(i - 1, false, null, null, dt);
                    }
                }
                return;
            }

            Vector3 origin = transform.position;
            Vector3 dir = transform.forward;
            float reach = Range;
            int beamCount = BeamCount;

            // MV-1128: branches beyond the currently-active beam count (SPLIT bought down mid-run via a
            // stale resume, or simply beyond MaxBeamCount) are fully dropped every frame so none of them
            // ever holds a stale target.
            for (int i = beamCount; i < _branches.Count; i++) DropAll(_branches[i]);

            // MV-1070: the tip moves every frame the stream is up, whether it's seeking, latched or just
            // swaying — the beam always draws to it (spec #1). MV-1128 spec #4: beam 0 chooses first with
            // no exclusion; each later beam excludes whatever an earlier beam already holds/seeks THIS
            // frame.
            s_claimedTargets.Clear();
            for (int i = 0; i < beamCount; i++)
            {
                UpdateTip(_branches[i], dt, origin, dir, reach, i == 0 ? null : s_claimedTargets);
                if (_branches[i].SeekTarget != null) s_claimedTargets.Add(_branches[i].SeekTarget);
            }
            StreamEndPoint = _branch.TipPosition;

            _tickTimer -= dt;
            if (_tickTimer <= 0f)
            {
                _tickTimer = fireInterval;
                if (_tank.TrySpend(cost))
                {
                    // MV-1128 spec #6: beam 0 full damage, the existing line-pierce rule beam 0 only;
                    // beams 1/2 at SplitSecondaryDamageFraction each, no line-pierce of their own.
                    bool anyFired = false;
                    for (int i = 0; i < beamCount; i++)
                    {
                        if (_branches[i].LatchedTarget == null) continue;
                        float fraction = i == 0 ? 1f : SplitSecondaryDamageFraction;
                        FireLatchedTick(_branches[i], origin, dir, reach, fraction, allowLinePierce: i == 0);
                        anyFired = true;
                    }
                    if (_branch.LatchedTarget == null && FireAimedGateOnlyTick(origin, dir, reach)) anyFired = true;

                    if (anyFired && _vfx != null) _vfx.OnTick(origin, origin + dir * reach, dir);
                }
            }

            if (_vfx != null)
            {
                // SetLatch before UpdateStream: the beam's own last-1m curl (UpdateStream) reads the
                // coil geometry this call just resolved, so the two must never visibly lag a frame
                // apart.
                _vfx.SetLatch(_branch.LatchedTarget != null, _branch.SeekTransform, _branch.SeekCc, dt);
                // Animate the crackle strands/prongs every frame the stream is up, not just on the tick
                // cadence — the fire tick above (if it ran this frame) already refreshed StreamEndPoint
                // indirectly via the tip, but UpdateStream also needs to run every frame regardless.
                _vfx.UpdateStream(transform.position, StreamEndPoint, dir, dt);

                for (int i = 1; i < MaxBeamCount; i++)
                {
                    int slot = i - 1;
                    bool drawn = IsBeamDrawn(i);
                    _vfx.SetSecondaryStreaming(slot, drawn);
                    if (drawn)
                    {
                        _vfx.SetSecondaryLatch(slot, _branches[i].LatchedTarget != null,
                            _branches[i].SeekTransform, _branches[i].SeekCc, dt);
                        _vfx.UpdateSecondaryStream(slot, transform.position, _branches[i].TipPosition, dir, dt);
                    }
                    else
                    {
                        _vfx.SetSecondaryLatch(slot, false, null, null, dt);
                    }
                }
            }
        }

        /// <summary>Same growing-buffer idiom <see cref="WaterBlaster.OverlapSphereGrowing"/> uses
        /// (MV-666): a fixed-size buffer risks silently dropping robots in a crowded room the moment a
        /// query saturates it, and the lance's 9m base range only makes that more likely than the RCDA's
        /// own 5m. Grows only on saturation, so the steady-state per-tick path stays allocation-free.</summary>
        private int OverlapSphereGrowing(Vector3 origin, float radius)
        {
            int count;
            while ((count = Physics.OverlapSphereNonAlloc(
                       origin, radius, _hits, hitMask, QueryTriggerInteraction.Ignore)) == _hits.Length)
            {
                _hits = new Collider[_hits.Length * 2];
            }
            return count;
        }

        /// <summary>MV-1070: moves <paramref name="b"/>'s tip toward whatever it should be pulled at this
        /// frame — the robot/boss it's seeking or already latched onto (spec #2/#4), or a slow
        /// figure-of-eight sway around the rest point when nothing qualifies (spec #3) — by integrating
        /// the under-damped spring (<see cref="IntegrateSpring"/>) toward that point, then promotes the
        /// seek to a latch the instant the tip closes within <see cref="LatchDistance"/> (spec #4).</summary>
        private void UpdateTip(Branch b, float dt, Vector3 origin, Vector3 dir, float reach, List<IDamageable> excluded)
        {
            // MV-1128 spec #4: beam 0 always re-picks with no exclusion, so it can "steal" a target a
            // later beam is currently holding — if that just happened, drop the stale latch immediately
            // rather than waiting for LatchHolds' own unrelated break conditions to eventually catch it.
            bool stolenByEarlierBeam = excluded != null && b.LatchedTarget != null && excluded.Contains(b.LatchedTarget);

            // --- Candidate resolution: keep an already-latched target as long as it still holds
            // (spec #5, "do not hop between robots while one is held"); otherwise look for a new one.
            if (!stolenByEarlierBeam && b.LatchedTarget != null && LatchHolds(b, origin, dir, reach))
            {
                // Keep b.SeekTarget == b.LatchedTarget; nothing to resolve.
            }
            else
            {
                if (b.LatchedTarget != null) b.LatchedTarget = null;
                IDamageable candidate = FindSeekCandidate(origin, dir, reach, excluded);
                if (!ReferenceEquals(candidate, b.SeekTarget)) AcquireSeek(b, candidate);
            }

            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(dir, right);

            Vector3 target = b.SeekTarget != null
                ? SeekCentre(b)
                : RestPoint(origin, dir, reach) + SwayOffset(b, right, up, dt);

            if (!b.TipValid)
            {
                // A fresh activation always starts the spring from the muzzle, never snapped straight
                // onto an already-resolved candidate — otherwise a robot dead-ahead on frame one would
                // never show the overshoot spec #4 requires.
                b.TipPosition = origin;
                b.TipVelocity = Vector3.zero;
                b.TipValid = true;
            }

            IntegrateSpring(ref b.TipPosition, ref b.TipVelocity, target, dt, TipSpringHz, TipDampingRatio, TipMaxSpeed);

            if (b.SeekTarget != null)
            {
                if (b.LatchedTarget == null && Vector3.Distance(b.TipPosition, SeekCentre(b)) <= LatchDistance)
                    b.LatchedTarget = b.SeekTarget;
            }
            else
            {
                b.LatchedTarget = null;
            }
        }

        /// <summary>Integrates an under-damped spring-damper (natural frequency <paramref name="hz"/>,
        /// damping ratio <paramref name="damping"/>) pulling <paramref name="pos"/> toward
        /// <paramref name="target"/>, speed-capped at <paramref name="maxSpeed"/>. Sub-steps at a fixed,
        /// small interval (<see cref="TipSubstep"/>) regardless of how large <paramref name="dt"/> is, so
        /// a single big explicit-dt call (an EditMode test) and many small per-frame calls (the real
        /// game) integrate to the same physical result — semi-implicit Euler at this spring's ~22 rad/s
        /// natural frequency would be unstable taken in one large step.</summary>
        private static void IntegrateSpring(ref Vector3 pos, ref Vector3 vel, Vector3 target, float dt,
            float hz, float damping, float maxSpeed)
        {
            if (dt <= 0f) return;
            float w = 2f * Mathf.PI * hz;
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / TipSubstep), 1, MaxTipSubsteps);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                Vector3 accel = -(w * w) * (pos - target) - 2f * damping * w * vel;
                vel += accel * h;
                float speed = vel.magnitude;
                if (speed > maxSpeed) vel *= maxSpeed / speed;
                pos += vel * h;
            }
        }

        /// <summary>Two angles within this of each other are treated as tied (two-decimal float jitter
        /// from the angle math, or genuinely collinear robots) — distance breaks the tie instead.</summary>
        private const float AngleTieEpsilonDegrees = 0.1f;

        /// <summary>MV-1070 spec #2: the candidate is the living enemy robot/boss with the smallest angle
        /// off Max's aim that is within <see cref="AcquireConeDegrees"/>, in <see cref="Range"/>, on the
        /// same combat level, with clear line of sight, and not trap-held. Gates never qualify — they
        /// keep MV-1044's own separate hit test (<see cref="FireAimedGateOnlyTick"/>). Ties on angle (two
        /// or more robots dead-on in the same line) break toward the CLOSEST one — the spec's own "pierces
        /// up to two robots... in a line" framing only makes sense if the nearest is sought/latched first.</summary>
        private IDamageable FindSeekCandidate(Vector3 origin, Vector3 dir, float reach, List<IDamageable> excluded)
        {
            int count = OverlapSphereGrowing(origin, reach);
            IDamageable best = null;
            float bestAngle = float.MaxValue;
            float bestDist = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                if (_hits[i] == null) continue;
                if (!_hits[i].TryGetComponent<IDamageable>(out var d) || !d.IsAlive || d.Team != Team.Enemy) continue;
                if (d is AreaGate) continue;
                if (d is RobotEnemy robot && robot.IsTrapHeld) continue;
                if (s_buffer.Contains(d)) continue; // same collider set can report a multi-collider body twice
                // MV-1128 spec #4: "not already held by an earlier beam".
                if (excluded != null && excluded.Contains(d)) continue;

                Transform t = _hits[i].transform;
                Vector3 centre = CentreOf(t);

                Vector3 to = centre - origin; to.y = 0f;
                float dist = to.magnitude;
                if (dist > reach) continue;

                float angle = SprayHit.AngleDeg(origin, dir, centre);
                if (angle > AcquireConeDegrees) continue;

                if (!LineOfSight.Clear(origin, centre, t)) continue;
                if (!CombatLevel.SameLevel(EnemyNavigation.Map, origin, centre)) continue;

                s_buffer.Add(d);
                bool better = angle < bestAngle - AngleTieEpsilonDegrees
                    || (angle < bestAngle + AngleTieEpsilonDegrees && dist < bestDist);
                if (better) { bestAngle = angle; bestDist = dist; best = d; }
            }
            s_buffer.Clear();
            return best;
        }

        /// <summary>A target's own centre (chest height) — the same <c>CharacterController.bounds.center</c>
        /// idiom <see cref="WaterBlaster"/>'s own gate-damage diagnostic already uses, falling back to the
        /// plain transform position when there's no CharacterController to read.</summary>
        private static Vector3 CentreOf(Transform t)
        {
            var cc = t.GetComponent<CharacterController>();
            return cc != null ? cc.bounds.center : t.position;
        }

        private static Vector3 SeekCentre(Branch b)
        {
            if (b.SeekCc != null) return b.SeekCc.bounds.center;
            return b.SeekTransform != null ? b.SeekTransform.position : b.TipPosition;
        }

        private static void AcquireSeek(Branch b, IDamageable target)
        {
            b.SeekTarget = target;
            b.SeekTransform = (target as Component)?.transform;
            b.SeekCc = b.SeekTransform != null ? b.SeekTransform.GetComponent<CharacterController>() : null;
        }

        private static void DropAll(Branch b)
        {
            b.SeekTarget = null;
            b.SeekTransform = null;
            b.SeekCc = null;
            b.LatchedTarget = null;
            b.TipValid = false;
        }

        /// <summary>Where the tip rests when nothing is being sought: the aim point at full
        /// <paramref name="reach"/>, or the first wall/cover/gate contact point on the way there (a
        /// gate's own <see cref="AreaGate.ThresholdObject"/> sits ON the cover layer, so this single
        /// raycast already stops the rest point at a closed gate exactly as it did before MV-1070).</summary>
        private static Vector3 RestPoint(Vector3 origin, Vector3 dir, float reach)
        {
            float dist = reach;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, reach, CoverLayer.Mask, QueryTriggerInteraction.Ignore))
                dist = Mathf.Min(dist, hit.distance);
            return origin + dir * dist;
        }

        /// <summary>MV-1121 Change 3: "sweeps side to side across the aim line, 1.25 m each way at 0.8 Hz"
        /// — a lemniscate traced in the plane perpendicular to the aim direction, same shape MV-1070
        /// shipped, just a bigger, more visibly-searching sway.</summary>
        private static Vector3 SwayOffset(Branch b, Vector3 right, Vector3 up, float dt)
        {
            b.SwayTime += dt;
            float w = 2f * Mathf.PI * SwayHz * b.SwayTime;
            float a = SwayWidth * 0.5f;
            float x = a * Mathf.Sin(w);
            float y = a * 0.5f * Mathf.Sin(2f * w);
            return right * x + up * y;
        }

        /// <summary>MV-1070 spec #5: latched holds while ALL of — target alive, still an enemy (a
        /// converted robot flips to <see cref="Team.Player"/>), not trapped, within <see cref="Range"/>
        /// &#215; <see cref="LatchRangeMultiplier"/>, in sight, within <see cref="LatchBreakAngleDegrees"/>
        /// of Max's aim, and on the same combat level.</summary>
        private bool LatchHolds(Branch b, Vector3 origin, Vector3 aimDir, float reach)
        {
            if (b.LatchedTarget == null) return false;
            if (!b.LatchedTarget.IsAlive) return false;
            if (b.LatchedTarget.Team != Team.Enemy) return false;
            if (b.LatchedTarget is RobotEnemy robot && robot.IsTrapHeld) return false;

            Vector3 centre = SeekCentre(b);
            Vector3 to = centre - origin;
            float dist = to.magnitude;
            if (dist > reach * LatchRangeMultiplier) return false;

            if (!LineOfSight.Clear(origin, centre, b.SeekTransform)) return false;

            Vector3 toDir = dist > 1e-4f ? to / dist : aimDir;
            if (Vector3.Angle(aimDir, toDir) > LatchBreakAngleDegrees) return false;

            if (!CombatLevel.SameLevel(EnemyNavigation.Map, origin, centre)) return false;

            return true;
        }

        /// <summary>MV-1070's own per-tick latch damage, extended by MV-1128 spec #6: a secondary beam
        /// (<paramref name="allowLinePierce"/> false) only ever damages its own latched target, at
        /// <paramref name="damageFraction"/> of a full tick — the "second robot within 1m of the line"
        /// pierce rule stays beam 0's own, since that rule's line is drawn from Max through beam 0's own
        /// latched robot, which would be the wrong line for any other beam's own latch.</summary>
        private void FireLatchedTick(Branch b, Vector3 origin, Vector3 dir, float reach, float damageFraction, bool allowLinePierce)
        {
            float tickDamage = EffectiveDamagePerTick * damageFraction;
            Vector3 latchedCentre = SeekCentre(b);

            b.LatchedTarget.TakeDamage(new DamageInfo(tickDamage, origin, dir, Team.Player, soak: true,
                source: DamageSource.PrimaryWeapon));

            if (!allowLinePierce) return;

            Vector3 toLatched = latchedCentre - origin;
            float latchedDist = toLatched.magnitude;
            Vector3 lineDir = latchedDist > 1e-4f ? toLatched / latchedDist : dir;

            int count = OverlapSphereGrowing(origin, reach);
            IDamageable second = null;
            float secondDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (_hits[i] == null) continue;
                if (!_hits[i].TryGetComponent<IDamageable>(out var d) || !d.IsAlive || d.Team == Team.Player) continue;
                if (ReferenceEquals(d, b.LatchedTarget) || d is AreaGate) continue;

                Vector3 testPoint = _hits[i].transform.position;
                Vector3 to = testPoint - origin; to.y = 0f;
                float dist = to.magnitude;
                if (dist > reach) continue;
                if (PerpendicularDistanceToLine(testPoint, origin, lineDir) > LatchPierceLineRadius) continue;
                if (!LineOfSight.Clear(origin, testPoint, _hits[i].transform)) continue;
                if (!CombatLevel.SameLevel(EnemyNavigation.Map, origin, testPoint)) continue;

                if (dist < secondDist) { secondDist = dist; second = d; }
            }
            if (second != null)
            {
                second.TakeDamage(new DamageInfo(tickDamage, origin, dir, Team.Player, soak: true,
                    source: DamageSource.PrimaryWeapon));
            }
        }

        /// <summary>MV-1070 spec #6: "Gates keep MV-1044's handling: with no robot candidate, a gate on
        /// the aim line is hit as today." Robots can no longer be damaged here at all — only while
        /// latched (<see cref="FireLatchedTick"/>) — so this filters to <see cref="AreaGate"/> only,
        /// otherwise unchanged from the pre-MV-1070 aimed hit test.</summary>
        private bool FireAimedGateOnlyTick(Vector3 origin, Vector3 dir, float reach)
        {
            float cone = ConeHalfAngle;
            float tickDamage = EffectiveDamagePerTick;

            int count = OverlapSphereGrowing(origin, reach);

            s_buffer.Clear();
            s_dist.Clear();
            for (int i = 0; i < count; i++)
            {
                if (_hits[i] == null) continue;
                if (!(_hits[i].TryGetComponent<IDamageable>(out var d) && d is AreaGate) || !d.IsAlive) continue;
                if (s_buffer.Contains(d)) continue;

                // MV-1044: an AreaGate needs the same gate-aware hit test WaterBlaster.FireTick has
                // carried since MV-302/MV-386 — its own leaf collider's testPoint/sight target reject a
                // shot that isn't dead-centre, and the leaf sits off the Cover layer entirely, so a
                // Cover-masked line of sight always found the gate's threshold first and read every shot
                // as blocked. Shared via GateHitResolver rather than copied a second time.
                GateHitResolver.Resolve(d, origin, dir, _hits[i], out Vector3 pos, out Transform sightTarget);
                if (!GateHitResolver.Passes(origin, dir, pos, sightTarget, reach, cone)) continue;

                Vector3 to = pos - origin; to.y = 0f;
                s_buffer.Add(d);
                s_dist.Add(to.magnitude);
            }

            // Small N (a handful of overlapping colliders at most) — a plain insertion sort by distance
            // is allocation-free and simpler than pulling in a general sort for two list slots.
            for (int i = 1; i < s_buffer.Count; i++)
            {
                float key = s_dist[i];
                IDamageable keyD = s_buffer[i];
                int j = i - 1;
                while (j >= 0 && s_dist[j] > key)
                {
                    s_dist[j + 1] = s_dist[j];
                    s_buffer[j + 1] = s_buffer[j];
                    j--;
                }
                s_dist[j + 1] = key;
                s_buffer[j + 1] = keyD;
            }

            int pierced = Mathf.Min(MaxPierceCount, s_buffer.Count);
            if (pierced == 0) return false;

            for (int i = 0; i < pierced; i++)
            {
                s_buffer[i].TakeDamage(new DamageInfo(tickDamage, origin, dir, Team.Player, soak: true,
                    source: DamageSource.PrimaryWeapon));
            }

            return true;
        }

        /// <summary>Shortest distance from <paramref name="point"/> to the infinite line through
        /// <paramref name="lineOrigin"/> along <paramref name="lineDir"/> (assumed normalised) — what
        /// "within 1m of the line from Max through the latched robot" (spec #4) means for a 3D point.</summary>
        private static float PerpendicularDistanceToLine(Vector3 point, Vector3 lineOrigin, Vector3 lineDir)
        {
            Vector3 toPoint = point - lineOrigin;
            float along = Vector3.Dot(toPoint, lineDir);
            Vector3 closest = lineOrigin + lineDir * along;
            return Vector3.Distance(point, closest);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.95f, 1f, 1f);
            Gizmos.DrawWireSphere(transform.position + transform.forward * range, 0.3f);
        }
#endif
    }
}
