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

        /// <summary>How far a live-capacity Replicator can pull an eligible robot off Max (MV-706).</summary>
        public const float LureRadius = 8f;

        /// <summary>A robot within this of Max is never pulled off him, whatever else is true.</summary>
        public const float MaxMeleeExclusionRadius = 4f;

        /// <summary>How close a lured robot's surface must get to the hatch it's walking to before the
        /// Intake beat takes over (MV-756 original fix, MV-775 scoped to the hatch face specifically —
        /// see <see cref="DistanceToHatchFace"/>): the seeking robot's own
        /// <see cref="EnemyArchetype.ColliderRadius"/> subtracted from its distance to
        /// <see cref="HatchPosition"/>, never a flat centre-to-centre radius. A fixed centre-to-centre
        /// test (the box's old ArriveRadius 1.2f) could never be reached: half-extent 1.0 m plus a
        /// robot's own 0.3-0.6 m controller radius put the closest possible centre-to-centre distance
        /// at 1.3-1.6 m, always outside a 1.2 m gate.</summary>
        public const float ArriveTolerance = 0.35f;

        /// <summary>MV-775 Intake beat: seconds a consumed robot spends being drawn from its arrival
        /// point at the hatch's own arrive gate to the hatch mouth itself, before it is despawned into
        /// the Cycle beat. This is what keeps the robot's resolved position at the moment of removal
        /// pinned to the hatch face rather than wherever <see cref="ArriveTolerance"/> first let it
        /// through — see <see cref="TickIntake"/>.</summary>
        public const float IntakeSeconds = 0.5f;

        /// <summary>MV-775 Cycle beat: seconds from a robot being despawned into the box to the FIRST
        /// of its doubled pair emerging.</summary>
        public const float CycleSeconds = 3.0f;

        /// <summary>MV-775 Output beat: the gap between the first and second emitted robot — the
        /// ticket's own "walk out one after the other, not simultaneously".</summary>
        public const float EmitStaggerSeconds = 0.4f;

        /// <summary>Seconds a freshly doubled pair refuses the lure (MV-706's "can't immediately walk
        /// back in" rule).</summary>
        public const float TwinNoReplicateSeconds = 8f;

        /// <summary>How long the emit-flash tell stays lit (MV-693 Reads: "a 0.6 s white 'twin'
        /// flash on each emitted robot"), seeded the instant <see cref="TickConsumption"/> spawns
        /// a doubled pair.</summary>
        public const float TwinFlashSeconds = 0.6f;

        [SerializeField] private int capacity;

        [SerializeField] private Color ledCapacityColor = new Color(0.25f, 0.95f, 1f);   // cyan: capacity left
        [SerializeField] private Color ledSpentColor = new Color(0.9f, 0.15f, 0.1f);     // red: spent, still a target
        [SerializeField] private Color hatchGlowColor = new Color(1f, 0.82f, 0.45f);     // warm amber: mid-consume

        private DestructibleHealth _health;
        private EnemySpawner _spawner;
        private Transform _target; // Max
        private Renderer _led;
        private MaterialPropertyBlock _ledMpb;
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

        // Robots currently walking toward this box's hatch.
        private readonly List<RobotEnemy> _seeking = new List<RobotEnemy>(8);
        // The one robot currently being drawn through the Intake beat (MV-775) — the hatch only ever
        // has room for one at a time, so a second arrival waits in _seeking until this slot frees.
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

            // The status LED — cyan while it can still double a robot, red once spent, off once
            // destroyed (OnDestroyed hides it).
            _led = parts.Led;
            _ledMpb = new MaterialPropertyBlock();

            // MV-775: the hatch a lured robot actually walks to and is drawn into, and the fan this
            // box spins continuously to read as powered before anything ever reaches it.
            _hatch = parts.Hatch;
            _hatchClosedLocalRotation = _hatch != null ? _hatch.localRotation : Quaternion.identity;
            _fan = parts.Fan;
        }

        /// <summary>The hatch's own world position (MV-775) — where <see cref="TickLure"/> steers a
        /// lured robot, where the arrive gate in <see cref="TickConsumption"/> measures against, and
        /// where <see cref="TickIntake"/> draws a consumed robot to. Public so a test can read it back
        /// without re-deriving <see cref="FactoryBodies.BuildReplicator"/>'s own hatch-offset formula.</summary>
        public Vector3 HatchPosition => _hatch != null ? _hatch.position : transform.position;

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

            if (_target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) _target = p.transform;
            }

            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;
            for (int i = 0; i < active.Count; i++)
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
                if (r.Current == RobotEnemy.State.ReplicatorSeeking) continue; // already lured (by this box or another)

                float distToMe = Vector3.Distance(r.transform.position, transform.position);
                if (distToMe > LureRadius) continue;

                // The 4 m rule: a robot already close enough to Max to be fighting him is never pulled
                // off — checked at selection time only, never re-checked once seeking has started.
                if (_target != null &&
                    Vector3.Distance(r.transform.position, _target.position) <= MaxMeleeExclusionRadius)
                    continue;

                // MV-775: the steering target is the hatch's own face, not the box's centre — a robot
                // must walk to the mouth it's actually consumed at, never "through the box" to whichever
                // face happened to be nearest.
                r.SeekReplicator(HatchPosition);
                _seeking.Add(r);
            }
        }

        /// <summary>Watches every robot this box has lured, draws the one at the hatch through the
        /// Intake beat, then ticks the Cycle/Output timers on whatever's already inside (MV-775).
        /// Public and explicitly dt-parameterized, same <see cref="MowerHutch.TickMobility"/> reasoning
        /// as <see cref="TickLure"/> above — a test drives this directly with a synthetic dt.</summary>
        public void TickConsumption(float dt)
        {
            if (!IsAlive) return;

            for (int i = _seeking.Count - 1; i >= 0; i--)
            {
                RobotEnemy r = _seeking[i];
                if (r == null || !r.IsAlive || r.Current != RobotEnemy.State.ReplicatorSeeking)
                {
                    _seeking.RemoveAt(i);
                    continue;
                }

                if (_intakeRobot != null) continue; // MV-775: the hatch only fits one robot at a time
                if (DistanceToHatchFace(r) > ArriveTolerance) continue;

                // At the hatch: hand its position over to the Intake beat rather than despawning it
                // here outright — TickIntake is what actually draws it in and despawns it.
                _seeking.RemoveAt(i);
                _intakeRobot = r;
                _intakeStartPos = r.transform.position;
                _intakeTimer = 0f;
                r.BeginReplicatorIntake();
            }

            if (_intakeRobot != null) TickIntake(dt);

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingEmission p = _pending[i];
                float timer = p.Timer + dt;
                bool firstEmitted = p.FirstEmitted;

                if (!firstEmitted && timer >= CycleSeconds)
                {
                    _spawner.SpawnExact(p.Kind, 1, TwinNoReplicateSeconds);
                    firstEmitted = true;
                }

                if (firstEmitted && timer >= CycleSeconds + EmitStaggerSeconds)
                {
                    _spawner.SpawnExact(p.Kind, 1, TwinNoReplicateSeconds);
                    capacity = Mathf.Max(0, capacity - 1);
                    _emitFlashTimer = TwinFlashSeconds; // MV-693 Reads: the twin flash, seeded here
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
        /// its own collider radius — see <see cref="RobotEnemy.IsBeingDrawnIn"/>.</summary>
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
            r.transform.position = Vector3.Lerp(_intakeStartPos, hatchPos, AnimSequence.OutQuad(u));

            Vector3 face = hatchPos - _intakeStartPos; face.y = 0f;
            if (face.sqrMagnitude > 0.0001f) r.transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);

            if (_intakeTimer < IntakeSeconds) return;

            // Fully drawn in: gone, and the pair it becomes starts its Cycle beat now.
            EnemyKind kind = r.Kind;
            r.Despawn();
            _intakeRobot = null;
            _pending.Add(new PendingEmission(kind, 0f, firstEmitted: false));
        }

        /// <summary>How far a lured robot still is from the hatch it's actually being consumed at
        /// (MV-775) — a point on the hatch itself, never the box's nearest face (MV-756's original
        /// collider-surface fix, but scoped to the one face a robot is meant to walk to). The robot's
        /// own <see cref="EnemyArchetype.ColliderRadius"/> is still subtracted so a real
        /// <see cref="CharacterController"/>-driven approach can actually satisfy this gate — the
        /// Intake beat that follows is what then draws it the rest of the way to the exact hatch point.</summary>
        private float DistanceToHatchFace(RobotEnemy r)
        {
            float robotRadius = EnemyArchetype.Of(r.Kind).ColliderRadius;
            return Vector3.Distance(r.transform.position, HatchPosition) - robotRadius;
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
            _pending.Clear();

            // A robot still walking toward a box that no longer exists resumes chasing Max instead of
            // beelining for a dead wreck's position forever.
            for (int i = 0; i < _seeking.Count; i++)
            {
                RobotEnemy r = _seeking[i];
                if (r != null && r.IsAlive) r.CancelReplicatorSeeking();
            }
            _seeking.Clear();

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

            if (_led != null)
            {
                Color c = capacity > 0 ? ledCapacityColor : ledSpentColor;
                _led.GetPropertyBlock(_ledMpb);
                _ledMpb.SetColor("_BaseColor", c);
                _ledMpb.SetColor("_EmissionColor", c * 2f);
                _led.SetPropertyBlock(_ledMpb);
            }

            // Hatch-open glow (MV-693 Reads, MV-756 change 2): lit for as long as something is
            // mid-consume, AND pulsed the instant a robot is still walking toward the hatch — a robot
            // seeking the box must read as a thing about to happen, from the box itself, before
            // anything is actually consumed. Every other tell here was downstream of a consume that
            // could never happen (MV-756 Cause 2); this is the one that isn't. MV-775 adds
            // _intakeRobot: a robot mid-Intake has already left _seeking but the hatch is still open
            // on it.
            bool hatchWanted = _pending.Count > 0 || _seeking.Count > 0 || _intakeRobot != null;
            if (_hatchGlow != null)
            {
                Color glow = hatchWanted ? hatchGlowColor : Color.clear;
                _hatchGlow.GetPropertyBlock(_hatchGlowMpb);
                _hatchGlowMpb.SetColor("_BaseColor", glow);
                _hatchGlow.SetPropertyBlock(_hatchGlowMpb);
            }

            // MV-775: the hatch itself swings open through Lure and Intake only — it closes again the
            // instant a robot is drawn fully in, rather than sitting open through the whole Cycle/Output
            // beat the way the glow (above) does.
            if (_hatch != null)
            {
                bool hatchSwingWanted = _seeking.Count > 0 || _intakeRobot != null;
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
