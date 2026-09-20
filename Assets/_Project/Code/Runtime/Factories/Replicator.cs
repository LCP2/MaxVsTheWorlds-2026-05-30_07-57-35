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

        /// <summary>MV-775 Intake beat: superseded by MV-823's own walk-speed-derived duration (see
        /// <see cref="TickIntake"/> — the walk step is now distance / max(the robot's own MoveSpeed,
        /// <see cref="MinIntakeWalkSpeed"/>), not a flat constant), kept only as the safety margin the
        /// existing tests add into their own total-tick sum so a single big synthetic dt still resolves
        /// the whole walk-in + Cycle + Output beat in one call.</summary>
        public const float IntakeSeconds = 0.35f;

        /// <summary>MV-823 change 1: "the robot walks up the RampIn at its own seeking speed" — floored
        /// at this so a slow archetype's walk-in never reads as a crawl.</summary>
        public const float MinIntakeWalkSpeed = 1.5f;

        /// <summary>MV-828 D2: entry (<see cref="OnAreaEntered"/>) and slot-close (<see cref="TickConsumption"/>'s
        /// own queueClosedUp/Intake branches) are not the only assignment triggers — a robot released
        /// into this box's own area after Max already crossed in (nobody eligible at entry) must still
        /// get taken. While Max is standing in this box's area, it has capacity, and a slot is empty,
        /// assignment is re-tried on this cadence.</summary>
        public const float LureRetryIntervalSeconds = 0.5f;

        /// <summary>MV-823 change 1: seconds the hatch takes to swing fully open, authored as its own
        /// <see cref="AnimSequence"/> step (OutQuad) rather than the pre-Intake MoveTowards creep
        /// <see cref="LateUpdate"/> still uses while a robot is merely queued.</summary>
        public const float HatchOpenSeconds = 0.25f;

        /// <summary>MV-823 change 1: how far beyond the hatch plane, into the hull, a consumed robot
        /// continues before it despawns — the walk no longer stops dead at the hatch face.</summary>
        public const float PassThroughDistance = 0.6f;

        /// <summary>MV-823 change 1: seconds the pass-through-and-shrink-to-0% step (InQuad) takes.</summary>
        public const float PassThroughSeconds = 0.4f;

        /// <summary>MV-823 change 1: the gap between the robot fully passing through and the hatch
        /// beginning to swing shut.</summary>
        public const float HatchCloseDelaySeconds = 0.2f;

        /// <summary>MV-823 change 1: seconds the hatch takes to swing shut (OutQuad, same as open).</summary>
        public const float HatchCloseSeconds = 0.25f;

        /// <summary>MV-823 change 1: the swing angle about the hatch's own top/hinge edge — widened
        /// from MV-775's 70 to Lee's own 80.</summary>
        public const float HatchOpenAngleDeg = 80f;

        /// <summary>MV-823 change 2: how long the replication light lingers after the second twin has
        /// emitted — Lee's own "the whole time it is replicating", read generously rather than cutting
        /// the light the instant the second twin appears.</summary>
        public const float ReplicationLightLingerSeconds = 0.4f;

        /// <summary>MV-823 change 2: the beacon's strobe rate and its emissive range (60%-100%).</summary>
        public const float BeaconStrobeHz = 3f;
        public const float BeaconStrobeMin = 0.6f;
        public const float BeaconStrobeMax = 1.0f;

        /// <summary>MV-834: Lee's own "a big green light on the replicator" — green, shared by the
        /// beacon, the hatch glow, and the status ring while a replication is running. Replaces MV-823's
        /// warm-white (1.00, 0.85, 0.45), which Lee rejected as "illogical" together with the floor pool
        /// it lit.</summary>
        public static readonly Color ReplicationLightColor = new Color(0.20f, 1.00f, 0.35f);

        /// <summary>MV-775 Cycle beat: seconds from a robot being despawned into the box to the FIRST
        /// of its doubled pair emerging. MV-812: cut 3.0 -> 0.9 — one robot's total occupancy (Intake +
        /// Cycle + Output) drops from 4.4 s to 1.45 s. MV-823: 0.9 -> 2.0 — Lee's own "a clear bright
        /// light switch on" tell (see <see cref="ReplicationLightColor"/>) needs long enough on-screen
        /// to actually read; total busy is now roughly 3 s.</summary>
        public const float CycleSeconds = 2.0f;

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
        // MV-834: idle was green (0.30, 0.95, 0.35) — Lee's own "green only ever means replicating" now
        // that the replication beacon/status-ring/hatch-glow are green, so idle drops to a dim amber.
        [SerializeField] private Color ledIdleColor = new Color(0.60f, 0.45f, 0.15f);    // amber: idle, can take a robot
        [SerializeField] private Color ledBusyColor = new Color(1.00f, 0.18f, 0.14f);    // red: consuming/cycling a robot
        [SerializeField] private Color ledSpentColor = new Color(0.9f, 0.15f, 0.1f);     // red: spent, still a target

        private DestructibleHealth _health;
        private EnemySpawner _spawner;
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
        /// <summary>MV-834: the roof beacon dome — Lee's own "a big green light on the replicator",
        /// driven directly by <see cref="TickConsumption"/> (never <see cref="LateUpdate"/>) so an
        /// EditMode test can read its resolved active state back off a synthetic dt. MV-823's floor
        /// pool is gone; this is the only replication tell mounted on the box itself.</summary>
        private Renderer _replicationBeacon;
        private MaterialPropertyBlock _replicationBeaconMpb;
        /// <summary>The generated Body container (MV-693) — hidden whole on death (MV-756 change 4)
        /// instead of the already-hidden root primitive.</summary>
        private Transform _bodyRoot;

        /// <summary>MV-820: found once in <see cref="Start"/> (never by an EditMode test — see that
        /// method's own doc comment) so this box can unsubscribe from
        /// <see cref="AreaAccumulationDirector.PlayerCrossedIntoArea"/> in <see cref="OnDestroy"/>.</summary>
        private AreaAccumulationDirector _areaDirector;

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

        /// <summary>MV-775: how long the pre-Intake "queue non-empty, hatch cracks open" creep
        /// (<see cref="LateUpdate"/>) takes — unrelated to the MV-823 Intake-beat swing, which is
        /// authored on <see cref="HatchOpenSeconds"/>/<see cref="HatchCloseSeconds"/> instead.</summary>
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
        /// <summary>MV-823: the walk-in/pass-through/hatch-swing beat, authored fresh every time a
        /// robot is taken off the queue head (see <see cref="TickConsumption"/>'s "atSlot" branch) —
        /// null once the hatch has finished swinging shut again. Ticked (never null-checked against
        /// <see cref="_intakeRobot"/> alone) so the hatch-close tail keeps running for
        /// <see cref="HatchCloseDelaySeconds"/> + <see cref="HatchCloseSeconds"/> after the robot itself
        /// has already despawned — see <see cref="TickIntake"/>.</summary>
        private AnimSequence _intakeSeq;
        /// <summary>MV-823: the ramp foot the current <see cref="_intakeSeq"/> walk step lerps from —
        /// <see cref="_intakeStartPos"/> flattened to <see cref="GroundY"/>, captured once so the walk
        /// traces a straight line even though <see cref="HatchPosition"/> is read live every tick.</summary>
        private Vector3 _intakeRampFoot;
        // Robots that have been despawned into the box and are mid-Cycle, waiting on CycleSeconds (and
        // then EmitStaggerSeconds) to emit.
        private readonly List<PendingEmission> _pending = new List<PendingEmission>(4);

        /// <summary>MV-823 change 2: true from the instant a robot is fully drawn in (added to
        /// <see cref="_pending"/>) until the second twin has emitted and <see cref="ReplicationLightLingerSeconds"/>
        /// has elapsed since. Read by <see cref="LateUpdate"/> for the status ring's and hatch glow's own
        /// green override (MV-834) and set every tick in <see cref="TickConsumption"/> so it's true
        /// test-drivable with a synthetic dt, same as every other beat in this file.</summary>
        private bool _replicationLightOn;
        private float _replicationLingerTimer;
        private float _beaconStrobeTime;

        /// <summary>MV-828 D2: true from the real area-entry signal for this box's own area until Max
        /// crosses into a different one — what gates the periodic <see cref="LureRetryIntervalSeconds"/>
        /// re-check in <see cref="TickConsumption"/>.</summary>
        private bool _playerInArea;

        /// <summary>MV-828 D2: counts down to the next periodic lure retry — 0 so the very first tick
        /// after a slot opens (or Max enters) tries immediately rather than waiting a full interval.</summary>
        private float _lureRetryTimer;

        public bool IsAlive => _health != null && _health.IsAlive;
        public Team Team => Team.Enemy; // Water Blaster (Team.Player) can damage it; robots can't
        public float Normalized => _health?.Normalized ?? 0f;

        /// <summary>Doublings this box has left. 0 means spent — it lures nothing but remains a
        /// destructible target.</summary>
        public int Capacity => capacity;

        /// <summary>MV-820: which 1-based area (<see cref="AreaAccumulationDirector.AreaIndexOf"/>)
        /// this box was authored into — stamped once by <see cref="MaxWorlds.Arena.WorldRunner"/> right
        /// after it builds this box, the same "known before anything reads it" ordering
        /// <see cref="FactoryCensus.RegisterReplicator"/> already gets. 0 until stamped.</summary>
        public int AreaIndex { get; private set; }

        /// <summary>Public so an EditMode test can drive area membership directly, same reasoning as
        /// every other post-build configure call in this file.</summary>
        public void SetAreaIndex(int area) => AreaIndex = area;

        /// <summary>MV-860: rotates this box in a 90° step so its IN face (the hatch, built at local
        /// -Z) sits on the given compass side ("N"|"S"|"E"|"W", default/unrecognised falls back to "S",
        /// today's unrotated behaviour) — the OUT face (built at local +Z) always lands on the opposite
        /// side. Every reader of orientation here (<see cref="HatchOutwardNormal"/>,
        /// <see cref="OutputOutwardNormal"/>, <see cref="TwinPlacement"/>) already derives its answer
        /// from <c>transform.forward</c>/<c>transform.right</c> at query time rather than baking in the
        /// unrotated default, so setting the rotation is the whole change — nothing downstream needs to
        /// know facing was ever authored. Public, called by <see cref="MaxWorlds.Arena.Map.MapRuntime"/>
        /// right after <c>Configure</c>, same "an EditMode test can drive it directly" convention as
        /// every other post-AddComponent configure call in this file.</summary>
        public void SetFacing(string facing) => transform.rotation = FacingRotation(facing);

        /// <summary>N = world +Z, E = +X, W = -X, S (and anything unrecognised) = -Z — the same compass
        /// <see cref="MaxWorlds.Arena.Map.MapRuntime"/>'s own deck-wall switches already use. The hatch
        /// (<see cref="HatchOutwardNormal"/> = <c>-transform.forward</c>) must sit on that world
        /// direction, so <c>transform.forward</c> is pointed the opposite way.</summary>
        private static Quaternion FacingRotation(string facing)
        {
            Vector3 dir = facing switch
            {
                "N" => Vector3.forward,
                "E" => Vector3.right,
                "W" => Vector3.left,
                _ => Vector3.back, // "S", the default, and any unrecognised value
            };
            return Quaternion.LookRotation(-dir, Vector3.up);
        }

        /// <summary>Stamp this box's authored doubling budget (MV-706), from
        /// <see cref="MaxWorlds.Arena.Map.WorldReplicator.capacity"/> / <see cref="MaxWorlds.Arena.Map.MapEntity.capacity"/>.
        /// Called by <see cref="MaxWorlds.Arena.Map.MapRuntime"/> right after <c>AddComponent&lt;Replicator&gt;</c>,
        /// same ordering as every other post-AddComponent configure call in this codebase
        /// (<see cref="MowerHutch.ConfigureMobility"/>). Public so an EditMode test can drive it directly.</summary>
        public void Configure(int startingCapacity) => capacity = Mathf.Max(0, startingCapacity);

        private void Awake() => Build();

        /// <summary>Same "count it in Start" reasoning as <see cref="MowerHutch.Start"/> — by the time
        /// anything's Start runs (the HUD's), every Replicator built into the level has already run its
        /// own Awake, so <see cref="MaxWorlds.UI.HudModel.RegisterFactory"/>'s count can be trusted.
        ///
        /// MV-820: also where this box wires itself to the area-entry signal — never called by an
        /// EditMode test (which drives <see cref="OnAreaEntered"/> directly, same "public so a test can
        /// call it" convention as every other Tick* method here), so the FindFirstObjectByType lookup
        /// never runs there either.</summary>
        private void Start()
        {
            HudSignals.EmitFactoryRegistered();
            _areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            if (_areaDirector != null) _areaDirector.PlayerCrossedIntoArea += OnAreaEntered;
        }

        private void OnDestroy()
        {
            if (_areaDirector != null) _areaDirector.PlayerCrossedIntoArea -= OnAreaEntered;
        }

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

            // MV-834: the replication beacon, built inactive — TickConsumption switches it on for
            // exactly the "robot fully inside" to "second twin emitted + linger" window.
            _replicationBeacon = parts.ReplicationBeacon;
            _replicationBeaconMpb = new MaterialPropertyBlock();

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
            // MV-828 D3: Apply() reset the twin's AreaIndex to 0 (SpawnKind -> Take -> Apply) — a twin
            // belongs to the box that made it, or it can never be re-assigned once its own
            // TwinNoReplicateSeconds window lapses.
            e.SetAreaIndex(AreaIndex);
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return; // robots can't wreck their own box
            HudSignals.EmitDamage(transform.position + Vector3.up * 1.2f, info.Amount);
            _health.TakeDamage(info.Amount);
        }

        /// <summary>MV-820 R1/R2: assigns up to <see cref="MaxQueueSlots"/> nearest eligible robots in
        /// this box's own area to its queue. Public and un-timer-gated by design, same "an EditMode
        /// test can drive it directly" reasoning as <see cref="MowerHutch.TickMobility"/> — but nothing
        /// calls this on a timer any more: R1 (area entry, see <see cref="OnAreaEntered"/>) and R2
        /// (an instant refill the moment a slot frees, see <see cref="TickConsumption"/>) are the only
        /// two callers in shipped gameplay.
        ///
        /// No radius, no state screen, no field-wide ceiling, no timeout — Lee's rules removed all four
        /// (MV-820): an eligible robot's distance and area membership are the only things that matter.</summary>
        public void TickLure()
        {
            if (!IsAlive || capacity <= 0) return;

            while (_queue.Count < MaxQueueSlots)
            {
                RobotEnemy nearest = NearestEligible();
                if (nearest == null) break; // nobody left in this area to assign
                _queue.Add(nearest);
                nearest.SeekReplicator(QueueSlotPosition(_queue.Count - 1));
            }
        }

        /// <summary>MV-820 Change 1: the nearest-by-straight-line-distance robot that is alive, not a
        /// Lurker/Turret (they cannot walk to a box — kept, MV-688/MV-691), not tagged NoReplicate, not
        /// already assigned to this or any other box (<see cref="RobotEnemy.IsAssignedToReplicator"/>),
        /// and physically in THIS box's own area (<see cref="AreaIndex"/>). Null if nobody qualifies.</summary>
        private RobotEnemy NearestEligible()
        {
            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;
            RobotEnemy nearest = null;
            float nearestDist = float.MaxValue;
            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy r = active[i];
                if (r == null || !r.IsAlive) continue;
                if (r.Kind == EnemyKind.Lurker || r.Kind == EnemyKind.Turret) continue;
                if (r.NoReplicate || r.IsAssignedToReplicator) continue;
                if (r.AreaIndex != AreaIndex) continue;

                float dist = Vector3.Distance(r.transform.position, transform.position);
                if (dist < nearestDist) { nearestDist = dist; nearest = r; }
            }
            return nearest;
        }

        /// <summary>MV-820 R1/R3: the area-entry signal (<see cref="AreaAccumulationDirector.PlayerCrossedIntoArea"/>,
        /// wired in <see cref="Start"/>; public so an EditMode test can drive it directly). Fills this
        /// box's queue the instant Max physically crosses into its own area; releases every current
        /// assignee back to ordinary attack AI the instant he crosses into any OTHER area — the tracker
        /// only ever advances, so "any other" always means "moved on".</summary>
        public void OnAreaEntered(int enteredArea)
        {
            if (!IsAlive) return;
            _playerInArea = enteredArea == AreaIndex;
            if (_playerInArea) TickLure();
            else ReleaseAllAssignees();
        }

        /// <summary>MV-820 Change 2/3: every currently queued assignee resumes ordinary Chase/attack AI
        /// — capacity just hit 0, the box died, or Max moved on to another area. A robot mid-Intake is
        /// handled separately by <see cref="OnDestroyed"/>'s own comment on that case.</summary>
        private void ReleaseAllAssignees()
        {
            for (int i = 0; i < _queue.Count; i++)
            {
                RobotEnemy r = _queue[i];
                if (r != null && r.IsAlive) r.CancelReplicatorSeeking();
            }
            _queue.Clear();
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
            // MV-828 D1: IsAssignedToReplicator (not a bare Current check) is what "still belongs to
            // this queue" means — a robot tagged mid-Telegraph/Lunge (SeekReplicator's own pending
            // hand-off) hasn't reached ReplicatorSeeking yet but is still this box's, and the OLD
            // Current-only check dropped it here on the very next tick, refilling its slot out from
            // under it and leaving it to seek a slot it no longer owned once Recover finally ran.
            bool queueClosedUp = false;
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                RobotEnemy r = _queue[i];
                if (r == null || !r.IsAlive || !r.IsAssignedToReplicator)
                {
                    _queue.RemoveAt(i);
                    queueClosedUp = true;
                }
            }
            if (queueClosedUp)
            {
                RetargetQueue();
                // MV-820 R2: a slot just closed up (a queued robot died or dropped out) — refill it
                // immediately rather than waiting for the next area-entry event. A no-op if capacity is
                // spent or nobody eligible remains in this box's area.
                TickLure();
            }

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
                // MV-820 Change 6: intake is never gated on room any more — consuming this robot and
                // owing back at least the guaranteed first twin (which now ignores the global budget
                // too, see below) is net zero.
                if (atSlot)
                {
                    // At its slot: hand its position over to the Intake beat rather than despawning it
                    // here outright — TickIntake is what actually draws it in and despawns it. Slot 1
                    // (if occupied) is promoted to slot 0 and re-targeted immediately, same tick.
                    _queue.RemoveAt(0);
                    RetargetQueue();
                    _intakeRobot = head;
                    _intakeStartPos = head.transform.position;

                    // MV-823: the walk-in is authored fresh per robot — distance / the robot's own
                    // MoveSpeed (floored at MinIntakeWalkSpeed), so a Brute's walk genuinely takes longer
                    // than a Rusher's rather than both sharing one flat duration.
                    _intakeRampFoot = _intakeStartPos; _intakeRampFoot.y = GroundY;
                    float walkDistance = Vector3.Distance(_intakeRampFoot, HatchPosition);
                    // MV-828 change 5: the robot's own LIVE speed (world overrides included, e.g. the
                    // World 2 Rusher's 2.4) — EnemyArchetype.Of(head.Kind) is the base table, which
                    // never reflects a per-world restat and made every intake walk read at the wrong pace.
                    float walkSpeed = Mathf.Max(head.EffectiveMoveSpeed, MinIntakeWalkSpeed);
                    float walkSeconds = walkDistance / walkSpeed;
                    _intakeSeq = new AnimSequence(new[]
                    {
                        new AnimStep(0f, HatchOpenSeconds, AnimEase.OutQuad),                 // 0: hatch open
                        new AnimStep(0f, walkSeconds, AnimEase.Linear),                        // 1: walk to hatch
                        new AnimStep(walkSeconds, PassThroughSeconds, AnimEase.InQuad),        // 2: pass-through + shrink
                        new AnimStep(walkSeconds + PassThroughSeconds + HatchCloseDelaySeconds,
                            HatchCloseSeconds, AnimEase.OutQuad),                              // 3: hatch close
                    });

                    head.BeginReplicatorIntake();
                    // MV-820 R2: the instant Intake takes the head, the next nearest eligible robot is
                    // assigned to the slot that just freed, same tick.
                    TickLure();
                }
            }

            // MV-823: keeps ticking through the hatch-close tail even after the robot itself has
            // despawned (see TickIntake) — _intakeRobot goes null the instant the robot passes fully
            // through, but the hatch still has HatchCloseDelaySeconds + HatchCloseSeconds left to run.
            if (_intakeRobot != null || _intakeSeq != null) TickIntake(dt);

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingEmission p = _pending[i];
                float timer = p.Timer + dt;
                bool firstEmitted = p.FirstEmitted;

                if (!firstEmitted && timer >= CycleSeconds)
                {
                    // MV-809: release the held reservation — bookkeeping only now (MV-820 Change 6
                    // means this spawn no longer needs the freed slot to look free to itself; it's
                    // guaranteed regardless). MV-817: ignorePerFactoryCap true — a Replicator's own
                    // EnemySpawner authors maxLiveEnemies/startingRobots defaults meant for an ordinary
                    // factory stream, and EffectiveMaxLiveEnemies ramps from 0 early in a run. MV-820:
                    // ignoreGlobalRoom true — consuming the robot that produced this twin already freed
                    // the field-wide budget by exactly one, so this call can never come back short.
                    EnemySpawner.ReleaseReplicatorReservation();
                    List<RobotEnemy> firstSpawn = _spawner.SpawnExact(p.Kind, 1, TwinNoReplicateSeconds,
                        ignorePerFactoryCap: true, ignoreGlobalRoom: true);
                    // MV-808: place the twin at the out-ramp foot itself — the spawner's own door/mouth
                    // placement is for the ordinary emergence walk, not this box's own theatre.
                    PlaceAtOutRamp(firstSpawn, twinIndex: 0);
                    firstEmitted = true;
                }

                if (firstEmitted && timer >= CycleSeconds + EmitStaggerSeconds)
                {
                    // MV-809: the second twin is opportunistic, never guaranteed — only spawns if
                    // genuine room exists beyond what's already reserved elsewhere. Measured via
                    // Emitted (monotonic, this spawner only) rather than assuming success, since
                    // SpawnExact silently emits 0 when GlobalHasRoom is false. MV-817: ignorePerFactoryCap
                    // true, same reasoning as the first twin above.
                    int emittedBefore = _spawner.Emitted;
                    List<RobotEnemy> spawned = _spawner.SpawnExact(p.Kind, 1, TwinNoReplicateSeconds, ignorePerFactoryCap: true);
                    if (_spawner.Emitted > emittedBefore)
                    {
                        PlaceAtOutRamp(spawned, twinIndex: 1); // MV-808
                        // Capacity is spent only when the cycle actually gave back the full pair — a
                        // cycle that could only manage the guaranteed replacement must not burn it.
                        capacity = Mathf.Max(0, capacity - 1);
                        _emitFlashTimer = TwinFlashSeconds; // MV-693 Reads: the twin flash, seeded here
                        // MV-820 Change 2: spent — release whatever's still queued to ordinary attack AI.
                        if (capacity == 0) ReleaseAllAssignees();
                    }
                    _pending.RemoveAt(i);
                    continue;
                }

                _pending[i] = new PendingEmission(p.Kind, timer, firstEmitted);
            }

            // MV-823 change 2: "from the moment the robot is fully inside" (i.e. it's landed in
            // _pending — not merely walking through the hatch) "until the second twin has emitted" (the
            // pending loop above just removed it) "(plus 0.4 s linger)". Driven here, not LateUpdate, so
            // a synthetic-dt test can read the resolved renderer state straight back.
            bool pendingActive = _pending.Count > 0;
            if (pendingActive) _replicationLingerTimer = ReplicationLightLingerSeconds;
            else if (_replicationLingerTimer > 0f) _replicationLingerTimer = Mathf.Max(0f, _replicationLingerTimer - dt);
            _replicationLightOn = pendingActive || _replicationLingerTimer > 0f;

            // MV-828 D2: entry and slot-close are not the only assignment triggers — while Max is
            // standing in this box's own area, has capacity, and a slot sits empty, re-run assignment
            // on LureRetryIntervalSeconds regardless of what triggered the last one. Reset (not merely
            // left to drift) the instant the condition stops holding, so it always fires promptly again
            // the next time a slot actually opens rather than however much of a stale interval happens
            // to be left over.
            if (_playerInArea && capacity > 0 && _queue.Count < MaxQueueSlots)
            {
                _lureRetryTimer -= dt;
                if (_lureRetryTimer <= 0f)
                {
                    TickLure();
                    _lureRetryTimer = LureRetryIntervalSeconds;
                }
            }
            else
            {
                _lureRetryTimer = 0f;
            }

            _beaconStrobeTime += dt;
            UpdateReplicationLight();
        }

        /// <summary>MV-834: paints/toggles <see cref="_replicationBeacon"/> off <see cref="_replicationLightOn"/>
        /// — fully inactive when off ("the difference must be unmistakable", MV-823's own words, still
        /// true here), strobing 60%-100% at <see cref="BeaconStrobeHz"/> when on.</summary>
        private void UpdateReplicationLight()
        {
            if (_replicationBeacon != null)
            {
                _replicationBeacon.gameObject.SetActive(_replicationLightOn);
                if (_replicationLightOn)
                {
                    float phase = Mathf.Sin(_beaconStrobeTime * BeaconStrobeHz * Mathf.PI * 2f) * 0.5f + 0.5f;
                    float mult = Mathf.Lerp(BeaconStrobeMin, BeaconStrobeMax, phase);
                    Color c = ReplicationLightColor * mult;
                    _replicationBeacon.GetPropertyBlock(_replicationBeaconMpb);
                    _replicationBeaconMpb.SetColor("_BaseColor", c);
                    _replicationBeaconMpb.SetColor("_EmissionColor", c * 2f);
                    _replicationBeacon.SetPropertyBlock(_replicationBeaconMpb);
                }
            }
        }

        /// <summary>MV-775 Intake beat, rebuilt on MV-823's own <see cref="AnimSequence"/> (hatch open ->
        /// walk -> pass-through+shrink -> hatch close — see where <see cref="_intakeSeq"/> is authored in
        /// <see cref="TickConsumption"/>). Driving the robot's position directly (rather than its own
        /// SafeMove) is what pins its resolved position to the hatch/hull rather than wherever
        /// <see cref="ArriveTolerance"/> first let it through — see <see cref="RobotEnemy.IsBeingDrawnIn"/>.
        /// Keeps running after the robot itself has despawned (<see cref="_intakeRobot"/> null but
        /// <see cref="_intakeSeq"/> not) purely to finish swinging the hatch shut.</summary>
        private void TickIntake(float dt)
        {
            if (_intakeSeq == null)
            {
                _intakeRobot = null;
                return;
            }

            RobotEnemy r = _intakeRobot;
            if (r != null && !r.IsAlive)
            {
                // MV-775: a robot that died mid-walk (e.g. the Water Blaster caught it through the
                // opening) — abandon the sequence outright rather than continuing to animate a corpse;
                // LateUpdate's own pre-Intake creep takes the hatch back to whatever the queue wants.
                _intakeRobot = null;
                _intakeSeq = null;
                return;
            }

            _intakeSeq.Tick(dt);

            // Hatch: fully open by the time step 0 finishes, fully shut by the time step 3 finishes —
            // the two windows never overlap (step 3's own Delay is well past step 0's end), so reading
            // "closing wins once it has started" is unambiguous.
            float closeProgress = _intakeSeq.Progress(3);
            _hatchOpenAmount = closeProgress > 0f ? 1f - closeProgress : _intakeSeq.Progress(0);
            if (_hatch != null)
                _hatch.localRotation = _hatchClosedLocalRotation * Quaternion.AngleAxis(_hatchOpenAmount * HatchOpenAngleDeg, Vector3.right);

            if (r != null)
            {
                Vector3 hatchPos = HatchPosition;
                Vector3 pos = Vector3.Lerp(_intakeRampFoot, hatchPos, _intakeSeq.Progress(1));

                float passProgress = _intakeSeq.Progress(2);
                if (passProgress > 0f)
                {
                    Vector3 dir = hatchPos - _intakeRampFoot; dir.y = 0f;
                    dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
                    pos = hatchPos + dir * (PassThroughDistance * passProgress);
                    r.transform.localScale = Vector3.one * (1f - passProgress);
                }
                r.transform.position = pos;

                Vector3 face = hatchPos - _intakeRampFoot; face.y = 0f;
                if (face.sqrMagnitude > 0.0001f) r.transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);

                if (passProgress >= 1f)
                {
                    // Fully passed through: gone, and the pair it becomes starts its Cycle beat now.
                    EnemyKind kind = r.Kind;
                    r.Despawn();
                    r.transform.localScale = Vector3.one; // MV-823: reset before the pool hands it back out
                    // MV-809: hold the slot this robot just vacated until the Cycle beat's first
                    // emission spends it — otherwise an ordinary spawn (or another Replicator) could
                    // fill it during the Cycle beat, and this box's own "always return at least what it
                    // consumed" guarantee would have nothing left to spend.
                    EnemySpawner.ReserveReplicatorSlot();
                    _pending.Add(new PendingEmission(kind, 0f, firstEmitted: false));
                    _intakeRobot = null; // the hatch-close tail keeps _intakeSeq running without it
                }
            }

            if (_intakeSeq.IsComplete) _intakeSeq = null;
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

            // MV-820: no periodic lure tick any more — R1 (area entry) and R2 (instant refill) are the
            // only two triggers, both already reached from OnAreaEntered/TickConsumption.
            TickConsumption(Time.deltaTime);
        }

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
            // beelining for a dead wreck's position forever (MV-820 Change 2/3).
            ReleaseAllAssignees();

            // MV-775: a robot mid-Intake (already through the arrive gate, not yet despawned) resumes
            // chasing Max too, same as one still walking in from further out — the box dying mid-draw-in
            // must not leave it permanently handed over to a machine that no longer exists.
            if (_intakeRobot != null)
            {
                if (_intakeRobot.IsAlive) _intakeRobot.CancelReplicatorSeeking();
                _intakeRobot = null;
            }
            _intakeSeq = null;

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
            // MV-834 change 3: overridden to steady green at full strength while a replication is
            // actually running (_replicationLightOn, set by TickConsumption) — Lee's own "the roof
            // status ring is the same green" bullet (was warm white under MV-823).
            if (_statusRing != null)
            {
                Color ringColor;
                if (_replicationLightOn)
                {
                    ringColor = ReplicationLightColor;
                }
                else
                {
                    float multiplier = 1f;
                    if (capacity > 0 && busy)
                    {
                        float phase = Mathf.Sin(_statusRingPulseTime * StatusRingPulseHz * Mathf.PI * 2f) * 0.5f + 0.5f;
                        multiplier = Mathf.Lerp(StatusRingPulseMin, StatusRingPulseMax, phase);
                    }
                    ringColor = c * multiplier;
                }
                _statusRing.GetPropertyBlock(_statusRingMpb);
                _statusRingMpb.SetColor("_BaseColor", ringColor);
                _statusRing.SetPropertyBlock(_statusRingMpb);
            }

            // Hatch-open glow (MV-693 Reads, MV-756 change 2): lit for as long as something is
            // mid-consume, AND pulsed the instant a robot is still walking toward the hatch — a robot
            // seeking the box must read as a thing about to happen, from the box itself, before
            // anything is actually consumed. Every other tell here was downstream of a consume that
            // could never happen (MV-756 Cause 2); this is the one that isn't. MV-775 adds
            // _intakeRobot: a robot mid-Intake has already left _queue but the hatch is still open
            // on it. MV-813 change 3: tinted from the same resolved colour the ring/LED take, rather
            // than a fixed amber, so the box can never show a green ring over a differently-lit hatch.
            // MV-834 change 3: overridden to the same green as the status ring while a replication is
            // actually running — Lee's own "the whole box reads as lit green".
            bool hatchWanted = _pending.Count > 0 || _queue.Count > 0 || _intakeRobot != null;
            if (_hatchGlow != null)
            {
                Color glow = _replicationLightOn ? ReplicationLightColor : (hatchWanted ? c : Color.clear);
                _hatchGlow.GetPropertyBlock(_hatchGlowMpb);
                _hatchGlowMpb.SetColor("_BaseColor", glow);
                _hatchGlow.SetPropertyBlock(_hatchGlowMpb);
            }

            // MV-775: the hatch cracks open while a robot is merely queued, as a "something's coming"
            // tell — but only when MV-823's own Intake-beat AnimSequence isn't already driving the swing
            // (TickIntake fully owns _hatchOpenAmount from the instant a robot leaves the queue until the
            // hatch has finished swinging shut again). MV-823: axis fixed from Vector3.up (which, after
            // the panel's own Euler(90,0,0) build rotation, resolved to the face NORMAL — the door spun
            // in its own plane instead of opening, MV-706's original bug) to Vector3.right — an axis
            // parallel to the hull face, the same hinge-edge swing the Intake beat now uses.
            if (_hatch != null && _intakeSeq == null)
            {
                bool hatchSwingWanted = _queue.Count > 0;
                float target = hatchSwingWanted ? 1f : 0f;
                _hatchOpenAmount = Mathf.MoveTowards(_hatchOpenAmount, target, Time.deltaTime / HatchSwingSeconds);
                _hatch.localRotation = _hatchClosedLocalRotation * Quaternion.AngleAxis(_hatchOpenAmount * HatchOpenAngleDeg, Vector3.right);
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
