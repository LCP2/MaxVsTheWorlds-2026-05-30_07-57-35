using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

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
    public sealed class Replicator : MonoBehaviour, IDamageable
    {
        /// <summary>Same authored HP as <see cref="MowerHutch.factoryHealth"/> (MV-706 change 2) — a
        /// Replicator takes exactly as much focused fire to kill as a shed does.</summary>
        private const float ReplicatorHealth = 474.75f;

        /// <summary>How far a live-capacity Replicator can pull an eligible robot off Max (MV-706).</summary>
        public const float LureRadius = 8f;

        /// <summary>A robot within this of Max is never pulled off him, whatever else is true.</summary>
        public const float MaxMeleeExclusionRadius = 4f;

        /// <summary>How close a lured robot must get to the hatch before it's consumed.</summary>
        public const float ArriveRadius = 1.2f;

        /// <summary>Seconds between consumption and the doubled pair emerging.</summary>
        public const float ConsumeSeconds = 1.2f;

        /// <summary>Seconds a freshly doubled pair refuses the lure (MV-706's "can't immediately walk
        /// back in" rule).</summary>
        public const float TwinNoReplicateSeconds = 8f;

        [SerializeField] private int capacity;

        // Hazard-orange body, same family as MowerHutch.bodyColor — this is also an objective box, not
        // scenery.
        [SerializeField] private Color bodyColor = new Color(0.72f, 0.34f, 0.10f);
        [SerializeField] private Color ledCapacityColor = new Color(0.25f, 0.95f, 1f);   // cyan: capacity left
        [SerializeField] private Color ledSpentColor = new Color(0.9f, 0.15f, 0.1f);     // red: spent, still a target

        private DestructibleHealth _health;
        private EnemySpawner _spawner;
        private Transform _target; // Max
        private Renderer _led;
        private MaterialPropertyBlock _ledMpb;

        private readonly struct PendingEmission
        {
            public readonly EnemyKind Kind;
            public readonly float Timer;
            public PendingEmission(EnemyKind kind, float timer) { Kind = kind; Timer = timer; }
        }

        // Robots currently walking toward this box's hatch.
        private readonly List<RobotEnemy> _seeking = new List<RobotEnemy>(8);
        // Robots that have reached the hatch and are mid-consume, waiting on ConsumeSeconds to emit.
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

        private void BuildBody()
        {
            var rend = GetComponent<Renderer>();
            if (rend != null)
            {
                var mpb = new MaterialPropertyBlock();
                rend.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", bodyColor);
                rend.SetPropertyBlock(mpb);
            }

            // The status LED on the hatch face — cyan while it can still double a robot, red once
            // spent, off once destroyed (OnDestroyed hides it). No collider: same "the Water Blaster
            // must hit the body behind it" reasoning as MowerHutch's own vulnerable core.
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ReplicatorLed";
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            }
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.1f, -0.52f);
            go.transform.localScale = new Vector3(0.3f, 0.3f, 0.08f);
            go.AddComponent<SelfDrivenTint>();
            _led = go.GetComponent<Renderer>();
            Material mat = MaterialLibrary.Character();
            if (mat != null) _led.sharedMaterial = mat;
            _ledMpb = new MaterialPropertyBlock();
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

                r.SeekReplicator(transform.position);
                _seeking.Add(r);
            }
        }

        /// <summary>Watches every robot this box has lured: consumes one the instant it reaches the
        /// hatch, then emits the doubled pair <see cref="ConsumeSeconds"/> later (MV-706 change 4).
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

                if (Vector3.Distance(r.transform.position, transform.position) > ArriveRadius) continue;

                // Consumed: the robot is deactivated right away (no kill, no loot — Despawn, not Die),
                // the pair it becomes emerges ConsumeSeconds later.
                EnemyKind kind = r.Kind;
                r.Despawn();
                _seeking.RemoveAt(i);
                _pending.Add(new PendingEmission(kind, 0f));
            }

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingEmission p = _pending[i];
                float timer = p.Timer + dt;
                if (timer >= ConsumeSeconds)
                {
                    _spawner.SpawnExact(p.Kind, 2, TwinNoReplicateSeconds);
                    capacity = Mathf.Max(0, capacity - 1);
                    _pending.RemoveAt(i);
                }
                else
                {
                    _pending[i] = new PendingEmission(p.Kind, timer);
                }
            }
        }

        private void Update()
        {
            if (!IsAlive) return;
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

            // Exactly the shed drop (MV-706 change 5): PickupDirector.OnFactoryDestroyed is subscribed
            // to this same signal, and drops one Device if any RIG category is locked, otherwise one
            // Supercell + ShedCellCacheAmount PowerCells — never both, never anything else. This is the
            // one call MowerHutch.OnDestroyed itself makes to get that exact drop.
            HudSignals.EmitFactoryDestroyed(transform.position);

            var rend = GetComponent<Renderer>();
            if (rend != null) rend.enabled = false;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            if (_led != null) _led.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_led == null || !IsAlive) return;
            Color c = capacity > 0 ? ledCapacityColor : ledSpentColor;
            _led.GetPropertyBlock(_ledMpb);
            _ledMpb.SetColor("_BaseColor", c);
            _ledMpb.SetColor("_EmissionColor", c * 2f);
            _led.SetPropertyBlock(_ledMpb);
        }
    }
}
