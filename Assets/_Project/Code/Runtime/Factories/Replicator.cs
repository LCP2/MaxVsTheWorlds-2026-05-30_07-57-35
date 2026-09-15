using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Feel;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// Replicator — the World 2 factory (MV-706): a 2x2x1.5 m armoured box with a hatch that never
    /// spawns on its own. Any robot that gets INTO it comes out as two. Pressure comes from what you
    /// let reach it; the counter is to kill the box.
    ///
    /// Unlike <see cref="MowerHutch"/> this is deliberately NOT a subclass of it — the hutch's own
    /// cadence/surge/post-destruction machinery does not apply here (this ticket's own "do not
    /// re-raise" list). It still reuses everything that actually is shared: <see cref="DestructibleHealth"/>
    /// for HP, the attached <see cref="EnemySpawner"/> (immediately <see cref="EnemySpawner.Stop"/>'d —
    /// this factory never ticks its own cadence, only ever emits on demand via
    /// <see cref="EnemySpawner.SpawnExact"/>), and <see cref="HudSignals.EmitFactoryDestroyed"/> for
    /// the exact-shed drop (<see cref="MaxWorlds.Pickups.PickupDirector"/> listens for that signal, not
    /// a Replicator-specific one).
    ///
    /// Lee's exact-count rule: the authored composition is the base and this box's authored
    /// <see cref="capacity"/> is the maximum number of doublings it can ever perform — never a live dial.
    /// </summary>
    [RequireComponent(typeof(EnemySpawner))]
    public sealed class Replicator : MonoBehaviour, IDamageable, IFactoryBody
    {
        /// <summary>Same authored HP as <see cref="MowerHutch.factoryHealth"/> (MV-706 change 2) — a
        /// Replicator takes exactly as much focused fire to kill as a shed does.</summary>
        private const float ReplicatorHealth = 474.75f;

        /// <summary>How far a live-capacity Replicator can pull an eligible robot off Max (MV-706).
        /// 16 m (MV-798, up from the original 8 m) — sized against the authored World 2 area
        /// footprints so a box's disc reaches across a whole area rather than a corner of it; see
        /// MV-798's own area-coverage figures for why 8 m left most robots never lured at all.</summary>
        public const float LureRadius = 16f;

        /// <summary>A robot within this of Max is never pulled off him, whatever else is true. 7 m
        /// (MV-798, up from 4 m) — moved with <see cref="LureRadius"/> so the wider lure still can't
        /// pull a robot out of a fight it's already in; 4 m was sized against the old 8 m lure.</summary>
        public const float MaxMeleeExclusionRadius = 7f;

        /// <summary>How close a lured robot's surface must get to the hatch it's walking to before the
        /// Intake beat takes over (MV-756 original fix, MV-775 scoped to the hatch face specifically —
        /// see <see cref="DistanceToHatchFace"/>): the seeking robot's own
        /// <see cref="EnemyArchetype.ColliderRadius"/> subtracted from its distance to
        /// <see cref="HatchPosition"/>, never a flat centre-to-centre radius. A fixed centre-to-centre
        /// test (the box's old ArriveRadius 1.2f) could never be reached: half-extent 1.0 m plus a
        /// robot's own 0.3-0.6 m controller radius put the closest possible centre-to-centre distance
        /// at 1.3-1.6 m, always outside a 1.2 m gate. MV-812: widened 0.35 -> 0.9 m — a robot nudged off
        /// its slot, or stopped short by MV-808's own ramp geometry, could never close the last 35 cm and
        /// was never taken in; the Intake beat already draws an arrived robot the rest of the way, so a
        /// tight gate bought nothing and cost the whole interaction.</summary>
        public const float ArriveTolerance = 0.9f;

        /// <summary>MV-807: the queue a lured robot actually walks into. Two robots pressed against
        /// the same hatch point could never both close on it (see <see cref="QueueSlotPosition"/>'s
        /// own doc), so at most this many are ever steered at once — every other eligible robot goes
        /// back to chasing Max until a slot frees.</summary>
        public const int MaxQueueSlots = 2;

        /// <summary>MV-807: metres between consecutive queue slots along <see cref="HatchOutwardNormal"/>,
        /// and between the hatch itself and slot 0. Lee's own "queue 2 deep" is a line, not a pile —
        /// this is what keeps two queued robots from ever being steered at the same point.</summary>
        public const float QueueSlotSpacing = 1.2f;

        /// <summary>MV-811: longest a lured robot may spend walking to its slot before it gives up —
        /// the release valve for one that can't physically reach it (a jam, a wall, a route dead-end).
        /// Checked by <see cref="RobotEnemy"/>'s own seeking tick, not here — this box only cares that
        /// a robot which times out gets dropped from its queue on the next <see cref="TickConsumption"/>
        /// pass, same as any other robot that's stopped seeking.</summary>
        public const float LureTimeoutSeconds = 6f;

        /// <summary>MV-811: how long a robot that timed out waiting refuses to immediately re-queue at
        /// the same box. Same number as <see cref="TwinNoReplicateSeconds"/>, kept as its own named
        /// constant since the two are conceptually different triggers that just happen to share a
        /// duration.</summary>
        public const float LureTimeoutNoReplicateSeconds = 8f;

        /// <summary>MV-811: at most this fraction of the currently live robots, field-wide, may be in
        /// <see cref="RobotEnemy.State.ReplicatorSeeking"/> at once — see
        /// <see cref="RobotEnemy.ReplicatorSeekingCount"/> and <see cref="TickLure"/>'s own ceiling
        /// check. Without this, MV-798's wider 16 m lure radius plus every box on a world authoring its
        /// own two-deep queue could park most of the field's population out of the fight at once (up to
        /// 22 of 24 on Lee's own build) — Change 1/2 (below) fix the immediate freeze; this is what
        /// stops the same shape re-appearing at a bigger scale.</summary>
        public const float MaxSeekingFraction = 0.25f;

        /// <summary>MV-775 Intake beat: seconds a consumed robot spends being drawn from its arrival
        /// point at the hatch's own arrive gate to the hatch mouth itself, before it is despawned into
        /// the Cycle beat. This is what keeps the robot's resolved position at the moment of removal
        /// pinned to the hatch face rather than wherever <see cref="ArriveTolerance"/> first let it
        /// through — see <see cref="TickIntake"/>. MV-808 lengthened this from 0.5 to 1.0 so the ramp
        /// ascent it added was readable at the play camera; MV-812 cut it back to 0.35 — Lee, on build
        /// 9751529: "they take ages to replicate" — the ramp is short enough that the walk still reads
        /// at 0.35 s.</summary>
        public const float IntakeSeconds = 0.35f;

        /// <summary>MV-775 Cycle beat: seconds from a robot being despawned into the box to the FIRST
        /// of its doubled pair emerging. MV-812: cut 3.0 -> 0.9 — one robot's total occupancy (Intake +
        /// Cycle + Output) drops from 4.4 s to 1.45 s.</summary>
        public const float CycleSeconds = 0.9f;

        /// <summary>MV-775 Output beat: the gap between the first and second emitted robot — the
        /// ticket's own "walk out one after the other, not simultaneously". MV-812: cut 0.4 -> 0.2,
        /// scaled down with the rest of the cycle.</summary>
        public const float EmitStaggerSeconds = 0.2f;

        /// <summary>Seconds a freshly doubled pair refuses the lure (MV-706's "can't immediately walk
        /// back in" rule).</summary>
        public const float TwinNoReplicateSeconds = 8f;

        /// <summary>How long the emit-flash tell stays lit (MV-693 Reads: "a 0.6 s white 'twin'
        /// flash on each emitted robot"), seeded the instant <see cref="TickConsumption"/> spawns
        /// a doubled pair.</summary>
        public const float TwinFlashSeconds = 0.6f;

        /// <summary>MV-813: the status ring's pulse rate while busy — 2 Hz, so a red ring reads as
        /// "working right now" rather than merely "a red thing". Idle and spent never pulse (see
        /// LateUpdate); motion is what a 0.9 m disc needs to actually catch the eye at play scale.</summary>
        public const float StatusRingPulseHz = 2f;

        /// <summary>MV-813: the busy pulse's emissive multiplier range — the ring's own resolved base
        /// colour (1.0x) up to 2.2x.</summary>
        public const float StatusRingPulseMin = 1.0f;
        public const float StatusRingPulseMax = 2.2f;

        [SerializeField] private int capacity;

        // MV-808: the LED now signals BUSY (a robot mid-Intake or mid-Cycle/Output), not just capacity
        // remaining — see LateUpdate. ledSpentColor is unchanged and still wins once capacity hits 0.
        [SerializeField] private Color ledIdleColor = new Color(0.30f, 0.95f, 0.35f);    // green: idle, can take a robot
        [SerializeField] private Color ledBusyColor = new Color(1.00f, 0.18f, 0.14f);    // red: consuming/cycling a robot
        [SerializeField] private Color ledSpentColor = new Color(0.9f, 0.15f, 0.1f);     // red: spent, still a target

        private DestructibleHealth _health;
        private EnemySpawner _spawner;
        private Transform _target; // Max
        private Renderer _led;
        private MaterialPropertyBlock _ledMpb;
        /// <summary>MV-813: the top-face beacon — big enough to read at the play camera, taking the
        /// same resolved colour <see cref="_led"/> does and pulsing while busy. See LateUpdate.</summary>
        private Renderer _statusRing;
        private MaterialPropertyBlock _statusRingMpb;
        /// <summary>MV-813: free-running clock for <see cref="_statusRing"/>'s busy pulse, advanced by
        /// <see cref="TickConsumption"/>'s own dt — never <see cref="Time.time"/>, so a test can drive
        /// the pulse deterministically the same way it already drives every other beat in this file.</summary>
        private float _statusRingPulseTime;
        private Renderer _hatchGlow;
        private MaterialPropertyBlock _hatchGlowMpb;
        private Renderer _emitFlash;
        private MaterialPropertyBlock _emitFlashMpb;
        private float _emitFlashTimer;
        /// <summary>The generated Body container (MV-693) — hidden whole on death (MV-756 change 4)
        /// instead of the already-hidden root primitive.</summary>
        private Transform _bodyRoot;

        /// <summary>MV-775: the hatch this box actually draws a consumed robot into — the Lure/Intake
        /// beats' steering target and arrive gate, and the swing-open transform LateUpdate drives.</summary>
        private Transform _hatch;
        private Quaternion _hatchClosedLocalRotation;
        private float _hatchOpenAmount;

        /// <summary>MV-808: the output face's own reference point (mirrors <see cref="_hatch"/> on the
        /// opposite side) — where a doubled twin's out-ramp foot is measured from.</summary>
        private Transform _outputLip;

        /// <summary>MV-775: the roof fan — "the only moving thing in a quiet room" — spun continuously
        /// in <see cref="Update"/> while this box is alive.</summary>
        private Transform _fan;
        private const float FanIdleSpeedDegPerSec = 40f;
        private const float HatchOpenAngleDeg = 70f;
        private const float HatchSwingSeconds = 0.15f;

        private readonly struct PendingEmission
        {
            public readonly EnemyKind Kind;
            public readonly float Timer;
            public readonly bool FirstEmitted;
            public PendingEmission(EnemyKind kind, float timer, bool firstEmitted)
            {
                Kind = kind; Timer = timer; FirstEmitted = firstEmitted;
            }
        }

        // MV-807: robots currently walking toward this box, ordered slot 0 (nearest the hatch, the
        // only one ever eligible for Intake) to slot MaxQueueSlots-1. Index IS queue position — a
        // robot's own QueueSlotPosition is always its index here, never a separate lookup.
        private readonly List<RobotEnemy> _queue = new List<RobotEnemy>(MaxQueueSlots);
        // The one robot currently being drawn through the Intake beat (MV-775) — the hatch only ever
        // has room for one at a time, so a second arrival waits in _queue until this slot frees.
        private RobotEnemy _intakeRobot;
        private Vector3 _intakeStartPos;
        private float _intakeTimer;
        // Robots that have been despawned into the box and are mid-Cycle, waiting on CycleSeconds (and
        // then EmitStaggerSeconds) to emit.
        private readonly List<PendingEmission> _pending = new List<PendingEmission>(4);

        public bool IsAlive => _health != null && _health.IsAlive;
        public Team Team => Team.Enemy; // Water Blaster (Team.Player) can damage it; robots can't
        public float Normalized => _health?.Normalized ?? 0f;

        /// <summary>Doublings this box has left. 0 means spent — it lures nothing but remains a
        /// destructible target.</summary>
        public int Capacity => capacity;

        /// <summary>Stamp this box's authored doubling budget (MV-706), from
        /// <see cref="MaxWorlds.Arena.Map.WorldReplicator.capacity"/> / <see cref="MaxWorlds.Arena.Map.MapEntity.capacity"/>.
        /// Called by <see cref="MaxWorlds.Arena.Map.MapRuntime"/> right after <c>AddComponent&lt;Replicator&gt;</c>,
        /// same ordering as every other post-AddComponent configure call in this codebase
        /// (<see cref="MowerHutch.ConfigureMobility"/>). Public so an EditMode test can drive it directly.</summary>
        public void Configure(int startingCapacity) => capacity = Mathf.Max(0, startingCapacity);

        private void Awake() => Build();

        /// <summary>Same "count it in Start" reasoning as <see cref="MowerHutch.Start"/> — by the time
        /// anything's Start runs (the HUD's), every Replicator built into the level has already run its
        /// own Awake, so <see cref="MaxWorlds.UI.HudModel.RegisterFactory"/>'s count can be trusted.</summary>
        private void Start() => HudSignals.EmitFactoryRegistered();

        /// <summary>Construct the health model, stop the attached spawner's own cadence, and build the
        /// greybox body. Exposed publicly, same reasoning as <see cref="MowerHutch.Build"/> — Awake never
        /// runs as a side effect of AddComponent outside Play mode, so an EditMode test calls this
        /// directly.</summary>
        public void Build()
        {
            _health = new DestructibleHealth(ReplicatorHealth);
            _health.Destroyed += OnDestroyed;

            _spawner = GetComponent<EnemySpawner>();
            // This factory never spawns on its own (MV-706 change 2) — SpawnExact bypasses the _running
            // latch entirely, so stopping it here permanently silences the ordinary cadence Update()
            // would otherwise try to run.
            _spawner.Stop();

            // Same reasoning as MowerHutch: a 2x2 armoured box breaks a sight-line exactly as a shed
            // does, and putting it on the cover layer doesn't stop the Water Blaster hitting the
            // collider it's actually aimed at.
            CoverLayer.Assign(gameObject);

            BuildBody();
        }

        /// <summary>MV-693: replaces MV-706's primitive-cube visual with a generated mesh (see
        /// <see cref="FactoryBodies"/>). The primitive cube <see cref="Replicator"/> is itself
        /// added to stays — it is still the collider the Water Blaster has to hit — but its own
        /// renderer is switched off (never destroyed: <c>Destroy</c> on a component is EditMode-
        /// illegal, the same trap MowerHutch.BuildCore's own doc comment names) rather than the
        /// body it draws.</summary>
        private void BuildBody()
        {
            var rend = GetComponent<Renderer>();
            if (rend != null) rend.enabled = false;

            Transform bodyRoot = ParentScale.MakeMetreSpace(new GameObject("Body").transform, transform);
            FactoryBodies.ReplicatorParts parts = FactoryBodies.BuildReplicator(bodyRoot, transform.lossyScale);
            _bodyRoot = bodyRoot;

            _hatchGlow = parts.HatchGlow;
            _hatchGlowMpb = new MaterialPropertyBlock();
            _emitFlash = parts.EmitFlash;
            _emitFlashMpb = new MaterialPropertyBlock();

            // The status LED — green idle / red busy while it can still double a robot (MV-808), red
            // (spent) once capacity hits 0, off once destroyed (OnDestroyed hides it).
            _led = parts.Led;
            _ledMpb = new MaterialPropertyBlock();

            // MV-813: the big top-face beacon — same three-state colour as _led, just legible at
            // the play camera's scale.
            _statusRing = parts.StatusRing;
            _statusRingMpb = new MaterialPropertyBlock();

            // MV-775: the hatch a lured robot actually walks to and is drawn into, and the fan this
            // box spins continuously to read as powered before anything ever reaches it.
            _hatch = parts.Hatch;
            _hatchClosedLocalRotation = _hatch != null ? _hatch.localRotation : Quaternion.identity;
            _fan = parts.Fan;
            _outputLip = parts.OutputLip;
        }

        /// <summary>The hatch's own world position (MV-775) — where <see cref="TickLure"/> steers a
        /// lured robot, where the arrive gate in <see cref="TickConsumption"/> measures against, and
        /// where <see cref="TickIntake"/> draws a consumed robot to. Public so a test can read it back
        /// without re-deriving <see cref="FactoryBodies.BuildReplicator"/>'s own hatch-offset formula.</summary>
        public Vector3 HatchPosition => _hatch != null ? _hatch.position : transform.position;

        /// <summary>MV-807: the direction a queued robot lines up along, away from the hatch — the
        /// same -Z box-local face <see cref="FactoryBodies.BuildReplicator"/> put the hatch on
        /// (<c>hatchAt</c>'s own -Z offset), flattened to the ground plane since a queue is a walking
        /// line, not a ramp. World-space so it stays correct under whatever rotation the level authors
        /// this box at.</summary>
        private Vector3 HatchOutwardNormal
        {
            get
            {
                Vector3 n = -transform.forward;
                n.y = 0f;
                return n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.back;
            }
        }

        /// <summary>MV-807: where the robot at queue index <paramref name="slot"/> steers to — a line
        /// leading away from the hatch along <see cref="HatchOutwardNormal"/>, slot 0 closest
        /// (<see cref="QueueSlotSpacing"/> out) and each further slot one more spacing beyond it. Public
        /// so a test can read a slot back without re-deriving this formula.</summary>
        public Vector3 QueueSlotPosition(int slot) => HatchPosition + HatchOutwardNormal * (QueueSlotSpacing * (slot + 1));

        /// <summary>MV-808: the output face's own world position — the same hatch-mirroring reasoning
        /// as <see cref="HatchPosition"/>, read off <see cref="_outputLip"/> instead of <see cref="_hatch"/>.</summary>
        public Vector3 OutputPosition => _outputLip != null ? _outputLip.position : transform.position;

        /// <summary>MV-808: the direction a twin walks away from the box on the output face — the
        /// opposite face to <see cref="HatchOutwardNormal"/> (+Z box-local, flattened), same reasoning.</summary>
        private Vector3 OutputOutwardNormal
        {
            get
            {
                Vector3 n = transform.forward;
                n.y = 0f;
                return n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.forward;
            }
        }

        /// <summary>MV-808: true ground level — the box's own pivot sits at its vertical centre
        /// (<see cref="MaxWorlds.Arena.Map.MapData.GroundedCenter"/>), so this is always just the
        /// half-height below it. Used as the ramp foot's Y for both the Intake walk-up and the out-ramp
        /// foot, so the rise to <see cref="HatchPosition"/>'s elevated Y is always real, not merely
        /// however close a given robot's own resting height happens to land.</summary>
        private float GroundY => transform.position.y - transform.lossyScale.y * 0.5f;

        /// <summary>MV-808: the out-ramp's own foot, at true ground level — the single reference point
        /// both twins are placed near (each staggered slightly off it, see <see cref="TwinPlacement"/>).
        /// Public so a test can read it back without re-deriving this formula, the same reasoning
        /// <see cref="QueueSlotPosition"/> already documents for the in-ramp foot.</summary>
        public Vector3 OutRampFootPosition
        {
            get
            {
                Vector3 foot = OutputPosition + OutputOutwardNormal * QueueSlotSpacing;
                foot.y = GroundY;
                return foot;
            }
        }

        /// <summary>MV-808: where twin <paramref name="twinIndex"/> (0 or 1) actually lands — the
        /// out-ramp foot, staggered sideways so the pair doesn't overlap (still well within the 1.2 m
        /// the ticket's own test tolerates).</summary>
        private Vector3 TwinPlacement(int twinIndex)
        {
            Vector3 side = transform.right; side.y = 0f;
            side = side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.right;
            return OutRampFootPosition + side * (twinIndex == 0 ? -0.4f : 0.4f);
        }

        /// <summary>MV-808: hands a just-spawned twin its out-ramp position and an outward facing,
        /// overriding wherever <see cref="EnemySpawner.SpawnKind"/> put it — <paramref name="spawned"/>
        /// is <see cref="EnemySpawner.SpawnExact"/>'s own return, empty when a population cap ate the
        /// spawn (nothing to place in that case).</summary>
        private void PlaceAtOutRamp(List<RobotEnemy> spawned, int twinIndex)
        {
            if (spawned.Count == 0) return;
            RobotEnemy e = spawned[0];
            e.transform.position = TwinPlacement(twinIndex);
            Vector3 face = OutputOutwardNormal;
            if (face.sqrMagnitude > 0.0001f) e.transform.rotation = Quaternion.LookRotation(face, Vector3.up);
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return; // robots can't wreck their own box
            HudSignals.EmitDamage(transform.position + Vector3.up * 1.2f, info.Amount);
            _health.TakeDamage(info.Amount);
        }

        /// <summary>Every 0.5 s (MV-706 change 3): pull an eligible awake robot off Max toward this
        /// box's hatch. Public and un-timer-gated by design, same "an EditMode test can drive it
        /// directly" reasoning as <see cref="MowerHutch.TickMobility"/> — a caller (<see cref="Update"/>
        /// in the shipped game, a test directly) decides the cadence; this call is one evaluation.</summary>
        public void TickLure()
        {
            if (!IsAlive || capacity <= 0) return;
            if (_queue.Count >= MaxQueueSlots) return; // MV-807: a full queue lures nobody

            // MV-816 change 3: a box that couldn't actually take a robot right now lures nobody — the
            // old behaviour lured one anyway, walked it to slot 0, and left it standing there until
            // LureTimeoutSeconds gave up, then repeated with the next robot. Room is re-checked on
            // every tick, same cadence as everything else here, so lure resumes the instant it frees.
            if (!EnemySpawner.HasRoomForReplicatorIntake()) return;

            // MV-811 change 5: a field-wide ceiling — at most MaxSeekingFraction of the currently live
            // robots may be seeking at once. Max(1, ...) rather than a bare fraction: with only a
            // handful of robots alive, a strict 25% floors to 0 and would forbid luring anyone at all —
            // this ceiling exists to stop a large field from being parked wholesale, not to block the
            // ordinary single-robot case a small population is.
            int seekingCeiling = Mathf.Max(1, Mathf.FloorToInt(RobotEnemy.ActiveCount * MaxSeekingFraction));
            if (RobotEnemy.ReplicatorSeekingCount >= seekingCeiling) return; // would cross the ceiling — lure nobody this tick

            // MV-811 change 4: an empty box lures one robot, not two — slot 1 only opens once slot 0 is
            // occupied by a robot that has actually arrived there, so a box never commits to serving a
            // second robot it can't get to soon.
            int queueCap = MaxQueueSlots;
            if (_queue.Count == 0)
            {
                queueCap = 1;
            }
            else if (_queue.Count == 1)
            {
                bool slot0Arrived = HorizontalDistance(_queue[0].transform.position, QueueSlotPosition(0)) <= ArriveTolerance;
                queueCap = slot0Arrived ? MaxQueueSlots : _queue.Count;
            }

            if (_target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) _target = p.transform;
            }

            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;
            for (int i = 0; i < active.Count && _queue.Count < queueCap; i++)
            {
                RobotEnemy r = active[i];
                if (r == null || !r.IsAlive || r.IsDormant) continue;
                if (r.NoReplicate) continue;
                // MV-688: a Grate Lurker is never Dormant (it lives in its own State.Submerged cycle),
                // so the IsDormant screen above lets it through — luring one off its grate would freeze
                // LurkerCycle mid-cycle with no way back. Excluded outright, same as an already-seeking
                // robot below.
                if (r.Kind == EnemyKind.Lurker) continue;
                // MV-691: a Pipe Turret is wall-mounted and never moves (MoveSpeed 0) — luring one
                // toward a hatch would either do nothing (correct, but pointless bookkeeping) or, if a
                // dev-tuning override ever forces a global move speed onto every robot, visibly slide a
                // "static" turret across the yard. Excluded outright, same reasoning as Lurker above.
                if (r.Kind == EnemyKind.Turret) continue;
                // MV-816 change 2: only Chase or Search may be lured — this single gate covers
                // Telegraph, Lunge, Recover, Emerging, Teleport, Alert, Submerged AND ReplicatorSeeking
                // (already lured, by this box or another) in one place, rather than naming each one.
                if (r.Current != RobotEnemy.State.Chase && r.Current != RobotEnemy.State.Search) continue;

                float distToMe = Vector3.Distance(r.transform.position, transform.position);
                if (distToMe > LureRadius) continue;

                // MV-816 change 1: the same IsEngagingTarget predicate TickReplicatorSeeking's own
                // per-tick cancel now uses — a robot already fighting Max (within the 7 m exclusion, OR
                // in sight and within its own lungeRange) is never pulled off. MV-811: this is still
                // only the SELECTION screen — RobotEnemy's own seeking tick re-checks the identical
                // predicate every tick afterward, so a robot that starts fighting Max mid-walk-to-the-
                // hatch is pulled back too, not just one that was already engaging here.
                if (r.IsEngagingTarget(_target)) continue;

                // MV-807: the steering target is this robot's own queue slot, never the hatch itself —
                // two robots must never be steered at the same point (that was the jam Lee reported).
                _queue.Add(r);
                r.SeekReplicator(QueueSlotPosition(_queue.Count - 1));
            }
        }

        /// <summary>Watches every robot this box has lured, draws the one at the hatch through the
        /// Intake beat, then ticks the Cycle/Output timers on whatever's already inside (MV-775).
        /// Public and explicitly dt-parameterized, same <see cref="MowerHutch.TickMobility"/> reasoning
        /// as <see cref="TickLure"/> above — a test drives this directly with a synthetic dt.</summary>
        public void TickConsumption(float dt)
        {
            if (!IsAlive) return;

            // MV-813: free-running, independent of every other beat here — the status ring pulses off
            // this alone, whether or not anything is actually happening.
            _statusRingPulseTime += dt;

            // MV-807: a queued robot that died, got converted, or is otherwise no longer eligible is
            // dropped and every remaining slot behind it closes up — re-targeted onto its new slot.
            bool queueClosedUp = false;
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                RobotEnemy r = _queue[i];
                if (r == null || !r.IsAlive || r.Current != RobotEnemy.State.ReplicatorSeeking)
                {
                    _queue.RemoveAt(i);
                    queueClosedUp = true;
                }
            }
            if (queueClosedUp) RetargetQueue();

            // MV-807: only the robot at slot 0 — nearest the hatch — is ever eligible for Intake. The
            // rest of the queue is still walking toward its own slot further back.
            if (_intakeRobot == null && _queue.Count > 0)
            {
                RobotEnemy head = _queue[0];
                // MV-808: horizontal-only — QueueSlotPosition's own Y sits at the hatch's elevation
                // (see HatchPosition), while a robot arriving to walk the ramp is at true ground level
                // (GroundY) until TickIntake lifts it. A 3D distance here would gate arrival on a
                // vertical gap ArriveTolerance was never sized to cover; TickReplicatorSeeking's own
                // steering already ignores Y for the same reason.
                bool atSlot = HorizontalDistance(head.transform.position, QueueSlotPosition(0)) <= ArriveTolerance;
                // MV-809: never consume a robot this box can't at least give back — see
                // EnemySpawner.HasRoomForReplicatorIntake's reservation contract. The robot stays right
                // where it arrived (still queued at slot 0) until room frees up.
                if (atSlot && EnemySpawner.HasRoomForReplicatorIntake())
                {
                    // At its slot: hand its position over to the Intake beat rather than despawning it
                    // here outright — TickIntake is what actually draws it in and despawns it. Slot 1
                    // (if occupied) is promoted to slot 0 and re-targeted immediately, same tick.
                    _queue.RemoveAt(0);
                    RetargetQueue();
                    _intakeRobot = head;
                    _intakeStartPos = head.transform.position;
                    _intakeTimer = 0f;
                    head.BeginReplicatorIntake();
                }
            }

            if (_intakeRobot != null) TickIntake(dt);

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingEmission p = _pending[i];
                float timer = p.Timer + dt;
                bool firstEmitted = p.FirstEmitted;

                if (!firstEmitted && timer >= CycleSeconds)
                {
                    // MV-809: spend the held reservation FIRST so this guaranteed replacement's own
                    // room check (inside SpawnExact) sees the slot as free rather than double-counting
                    // it against itself. HasRoomForReplicatorIntake already proved this always fits.
                    EnemySpawner.ReleaseReplicatorReservation();
                    // MV-808: place the twin at the out-ramp foot itself — the spawner's own door/mouth
                    // placement is for the ordinary emergence walk, not this box's own theatre.
                    PlaceAtOutRamp(_spawner.SpawnExact(p.Kind, 1, TwinNoReplicateSeconds), twinIndex: 0);
                    firstEmitted = true;
                }

                if (firstEmitted && timer >= CycleSeconds + EmitStaggerSeconds)
                {
                    // MV-809: the second twin is opportunistic, never guaranteed — only spawns if
                    // genuine room exists beyond what's already reserved elsewhere. Measured via
                    // Emitted (monotonic, this spawner only) rather than assuming success, since
                    // SpawnExact silently emits 0 when GlobalHasRoom is false.
                    int emittedBefore = _spawner.Emitted;
                    List<RobotEnemy> spawned = _spawner.SpawnExact(p.Kind, 1, TwinNoReplicateSeconds);
                    if (_spawner.Emitted > emittedBefore)
                    {
                        PlaceAtOutRamp(spawned, twinIndex: 1); // MV-808
                        // Capacity is spent only when the cycle actually gave back the full pair — a
                        // cycle that could only manage the guaranteed replacement must not burn it.
                        capacity = Mathf.Max(0, capacity - 1);
                        _emitFlashTimer = TwinFlashSeconds; // MV-693 Reads: the twin flash, seeded here
                    }
                    _pending.RemoveAt(i);
                    continue;
                }

                _pending[i] = new PendingEmission(p.Kind, timer, firstEmitted);
            }
        }

        /// <summary>MV-775 Intake beat: draws <see cref="_intakeRobot"/> from wherever it crossed the
        /// arrive gate to the hatch mouth itself over <see cref="IntakeSeconds"/>, then despawns it
        /// into the Cycle beat. Driving its position directly (rather than its own SafeMove) is what
        /// pins the robot's resolved position at the moment of removal to the hatch face regardless of
        /// its own collider radius — see <see cref="RobotEnemy.IsBeingDrawnIn"/>. MV-808: the path now
        /// starts at the ramp foot's true ground level (<see cref="GroundY"/>), not wherever the
        /// robot's own resting height happened to be, so the walk up the ramp to the hatch lip is
        /// always a real, monotonic rise, not merely however close those two Y values already were.</summary>
        private void TickIntake(float dt)
        {
            RobotEnemy r = _intakeRobot;
            if (r == null || !r.IsAlive)
            {
                _intakeRobot = null;
                return;
            }

            _intakeTimer += dt;
            float u = Mathf.Clamp01(_intakeTimer / IntakeSeconds);
            Vector3 hatchPos = HatchPosition;
            Vector3 rampFoot = _intakeStartPos; rampFoot.y = GroundY;
            r.transform.position = Vector3.Lerp(rampFoot, hatchPos, AnimSequence.OutQuad(u));

            Vector3 face = hatchPos - rampFoot; face.y = 0f;
            if (face.sqrMagnitude > 0.0001f) r.transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);

            if (_intakeTimer < IntakeSeconds) return;

            // Fully drawn in: gone, and the pair it becomes starts its Cycle beat now.
            EnemyKind kind = r.Kind;
            r.Despawn();
            // MV-809: hold the slot this robot just vacated until the Cycle beat's first emission
            // spends it (see TickConsumption's pending loop below) — otherwise an ordinary spawn (or
            // another Replicator) could fill it during the 3 s Cycle beat, and this box's own
            // "always return at least what it consumed" guarantee would have nothing left to spend.
            EnemySpawner.ReserveReplicatorSlot();
            _intakeRobot = null;
            _pending.Add(new PendingEmission(kind, 0f, firstEmitted: false));
        }

        /// <summary>MV-807: re-stamps every queued robot's steering target onto its CURRENT index —
        /// called whenever the queue's membership changes (a slot vacates, an entry drops out), so a
        /// robot promoted from slot 1 to slot 0 is re-targeted the same tick, never left walking toward
        /// a slot that's no longer its own.</summary>
        private void RetargetQueue()
        {
            for (int i = 0; i < _queue.Count; i++)
                _queue[i].SeekReplicator(QueueSlotPosition(i));
        }

        /// <summary>MV-808: distance ignoring Y — see the "atSlot" arrival check's own doc comment for
        /// why a queued robot's arrival must never be gated on the vertical gap to a target elevation.</summary>
        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            Vector3 d = a - b; d.y = 0f;
            return d.magnitude;
        }

        private void Update()
        {
            if (!IsAlive) return;

            // MV-775: "the only moving thing in a quiet room" — spins whether or not anything has
            // ever reached this box, so it reads as powered before the player touches it.
            if (_fan != null) _fan.Rotate(Vector3.up, FanIdleSpeedDegPerSec * Time.deltaTime, Space.Self);

            _lureTimer += Time.deltaTime;
            if (_lureTimer >= LureIntervalSeconds)
            {
                _lureTimer = 0f;
                TickLure();
            }
            TickConsumption(Time.deltaTime);
        }

        private const float LureIntervalSeconds = 0.5f;
        private float _lureTimer;

        private void OnDestroyed()
        {
            // A robot mid-consume when the box dies is destroyed WITH it — no emission (MV-706 change 5).
            // MV-809: any not-yet-first-emitted entry is still holding its reservation — release it
            // here or that slot leaks out of the global budget forever, since nothing else ever spends
            // a reservation whose owning box no longer exists to reach the Cycle beat's emission.
            for (int i = 0; i < _pending.Count; i++)
                if (!_pending[i].FirstEmitted) EnemySpawner.ReleaseReplicatorReservation();
            _pending.Clear();

            // A robot still walking toward a box that no longer exists resumes chasing Max instead of
            // beelining for a dead wreck's position forever.
            for (int i = 0; i < _queue.Count; i++)
            {
                RobotEnemy r = _queue[i];
                if (r != null && r.IsAlive) r.CancelReplicatorSeeking();
            }
            _queue.Clear();

            // MV-775: a robot mid-Intake (already through the arrive gate, not yet despawned) resumes
            // chasing Max too, same as one still walking in from further out — the box dying mid-draw-in
            // must not leave it permanently handed over to a machine that no longer exists.
            if (_intakeRobot != null)
            {
                if (_intakeRobot.IsAlive) _intakeRobot.CancelReplicatorSeeking();
                _intakeRobot = null;
            }

            // Exactly the shed drop (MV-706 change 5): PickupDirector.OnFactoryDestroyed is subscribed
            // to this same signal, and drops one Device if any RIG category is locked, otherwise one
            // Supercell + ShedCellCacheAmount PowerCells — never both, never anything else. This is the
            // one call MowerHutch.OnDestroyed itself makes to get that exact drop.
            HudSignals.EmitFactoryDestroyed(transform.position);

            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;

            // MV-756 change 4: hide the GENERATED Body child (hull/band/hatch/LED/valve wheel), not
            // the root's own renderer — that one was already switched off in BuildBody, the instant
            // FactoryBodies replaced it, so re-hiding it here was a no-op and the real geometry stayed
            // standing forever. FactoryHusk (generalised off MowerHutch's own, MV-756) now stands a
            // proper shudder/collapse/sink wreck in this exact spot, so the live body has to disappear
            // completely for that to read — the same "hide it, let the husk take over" contract
            // MowerHutch already gives FactoryHusk.
            if (_bodyRoot != null) _bodyRoot.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!IsAlive) return;

            // MV-808: busy — a robot is mid-Intake or a doubled pair is mid-Cycle/Output — from the
            // instant Intake begins until the second twin emits (removing the pending entry below).
            // A merely-queued robot (still walking to slot 0) does NOT count; only Intake onward.
            bool busy = _intakeRobot != null || _pending.Count > 0;
            Color c = capacity > 0 ? (busy ? ledBusyColor : ledIdleColor) : ledSpentColor;

            if (_led != null)
            {
                _led.GetPropertyBlock(_ledMpb);
                _ledMpb.SetColor("_BaseColor", c);
                _ledMpb.SetColor("_EmissionColor", c * 2f);
                _led.SetPropertyBlock(_ledMpb);
            }

            // MV-813: the top-face beacon takes the exact same resolved colour as _led — Change 3's
            // own "never disagree" rule — and additionally pulses its emissive strength 1.0x-2.2x at
            // StatusRingPulseHz while busy; steady (1x) idle or spent, per the ticket's own Change 4.
            if (_statusRing != null)
            {
                float multiplier = 1f;
                if (capacity > 0 && busy)
                {
                    float phase = Mathf.Sin(_statusRingPulseTime * StatusRingPulseHz * Mathf.PI * 2f) * 0.5f + 0.5f;
                    multiplier = Mathf.Lerp(StatusRingPulseMin, StatusRingPulseMax, phase);
                }
                _statusRing.GetPropertyBlock(_statusRingMpb);
                _statusRingMpb.SetColor("_BaseColor", c * multiplier);
                _statusRing.SetPropertyBlock(_statusRingMpb);
            }

            // Hatch-open glow (MV-693 Reads, MV-756 change 2): lit for as long as something is
            // mid-consume, AND pulsed the instant a robot is still walking toward the hatch — a robot
            // seeking the box must read as a thing about to happen, from the box itself, before
            // anything is actually consumed. Every other tell here was downstream of a consume that
            // could never happen (MV-756 Cause 2); this is the one that isn't. MV-775 adds
            // _intakeRobot: a robot mid-Intake has already left _queue but the hatch is still open
            // on it. MV-813 change 3: tinted from the same resolved colour the ring/LED take, rather
            // than a fixed amber, so the box can never show a green ring over a red-lit hatch.
            bool hatchWanted = _pending.Count > 0 || _queue.Count > 0 || _intakeRobot != null;
            if (_hatchGlow != null)
            {
                Color glow = hatchWanted ? c : Color.clear;
                _hatchGlow.GetPropertyBlock(_hatchGlowMpb);
                _hatchGlowMpb.SetColor("_BaseColor", glow);
                _hatchGlow.SetPropertyBlock(_hatchGlowMpb);
            }

            // MV-775: the hatch itself swings open through Lure and Intake only — it closes again the
            // instant a robot is drawn fully in, rather than sitting open through the whole Cycle/Output
            // beat the way the glow (above) does.
            if (_hatch != null)
            {
                bool hatchSwingWanted = _queue.Count > 0 || _intakeRobot != null;
                float target = hatchSwingWanted ? 1f : 0f;
                _hatchOpenAmount = Mathf.MoveTowards(_hatchOpenAmount, target, Time.deltaTime / HatchSwingSeconds);
                _hatch.localRotation = _hatchClosedLocalRotation * Quaternion.AngleAxis(_hatchOpenAmount * HatchOpenAngleDeg, Vector3.up);
            }

            // The 0.6 s white "twin" flash (MV-693 Reads), decaying from the timer TickConsumption
            // seeds the instant a doubled pair emerges.
            if (_emitFlash != null)
            {
                _emitFlashTimer = Mathf.Max(0f, _emitFlashTimer - Time.deltaTime);
                Color flash = _emitFlashTimer > 0f
                    ? Color.white * (_emitFlashTimer / TwinFlashSeconds) * 2.5f
                    : Color.clear;
                _emitFlash.GetPropertyBlock(_emitFlashMpb);
                _emitFlashMpb.SetColor("_BaseColor", flash);
                _emitFlash.SetPropertyBlock(_emitFlashMpb);
            }
        }
    }
}
