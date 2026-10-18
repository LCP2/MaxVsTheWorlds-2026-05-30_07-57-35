using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.UI;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// The Helmsman (MV-1239) — World 5's final boss, on the Bridge (area 30). Seated on its throne in
    /// Phase 1: a lens sweep (<see cref="LensSweepTelegraph"/>) plus a pod volley from the side hatches,
    /// both while stationary. Below half health it tears free and walks (Phase 2): a faster lens sweep,
    /// plus the shared passive contact damage every boss gives (<see cref="BossTuning"/>'s own MV-720
    /// rule). The look (seated-then-torn-free body) is MV-1242's own ticket — this class is behaviour and
    /// placement only, and rides the plain greybox cube <see cref="BossKinds"/> spawns every boss on
    /// until a rig binds over it.
    ///
    /// Modelled on <see cref="SludgequeenBoss"/> (standalone boss, not a <see cref="BigBermudaBoss"/>
    /// companion like <see cref="AnchorheadBoss"/>) because Phase 1 does not move or approach at all —
    /// the companion pattern assumes the base class is already walking in both phases, which the
    /// Helmsman's seated opening breaks. Pod volleys reuse Big Bermuda's own brood-volley shape wholesale
    /// (<see cref="BigBermudaBrain"/> for the shared time-based L1-L4 ramp, <see cref="BroodSpawnLevels"/>
    /// for composition, <see cref="BroodArc"/> for the landing maths) — "as today" (the ticket's own
    /// words), not a reimplementation.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [PerfSection("bosses")]
    public sealed class HelmsmanBoss : MonoBehaviour, IDamageable
    {
        private enum Phase { Dormant, Seated, Walking, Dead }

        private const string BossName = "HELMSMAN";
        private const float Gravity = 20f;

        private Phase _phase = Phase.Dormant;
        private DestructibleHealth _health;
        private CharacterController _cc;
        private Transform _target;

        private float _contactCooldownTimer;
        private float _verticalVel;

        private readonly WallLatch _wallLatch = new WallLatch();
        private float _preferSign;
        private readonly ZoneRouteBudget _routeBudget = new ZoneRouteBudget();

        // This boss's own authored area, same convention as every other boss's _wakeArea — unused
        // beyond storage today (the shared wake rule consults range + line of sight only, not the
        // area), kept for parity with SludgequeenBoss/BigBermudaBoss's own SetWakeArea.
        private Rect _wakeArea;
        private int _areaIndex;

        private readonly BigBermudaBrain _brain = new BigBermudaBrain(HelmsmanTuning.PhaseTwoThreshold);
        private readonly LensSweepTelegraph _sweep = new LensSweepTelegraph(HelmsmanTuning.SweepCooldownPhase1);
        private float _podTimer;
        private Transform _podRoot;

        public bool IsAlive => _phase != Phase.Dead && _health != null && _health.IsAlive;
        public Team Team => Team.Enemy;

        /// <summary>True from wake until death — same meaning as <see cref="BigBermudaBoss.Engaged"/>.</summary>
        public bool Engaged => _phase == Phase.Seated || _phase == Phase.Walking;

        public bool IsDead => _phase == Phase.Dead;

        /// <summary>True once Phase 2 has begun (torn free of the throne, walking) — what an EditMode
        /// test reads instead of the private phase enum.</summary>
        public bool IsWalking => _phase == Phase.Walking;

        /// <summary>Hands this boss its own authoring area's floor, same convention as
        /// <see cref="SludgequeenBoss.SetWakeArea"/>.</summary>
        public void SetWakeArea(Rect area) => _wakeArea = area;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            FitColliderToRenderedBody();
            _preferSign = ObstacleSteering.PreferSignFor(GetInstanceID());
            _health = new DestructibleHealth(DevTuning.Or(DevTuning.BossHealth, HelmsmanTuning.Health));
            _health.Destroyed += OnDeath;
            _contactCooldownTimer = HelmsmanTuning.ContactCooldown; // no free first hit
            AcquireTarget();
        }

        /// <summary>Same MV-542 collider-fit reasoning as <see cref="SludgequeenBoss.FitColliderToRenderedBody"/>.</summary>
        private void FitColliderToRenderedBody()
        {
            if (_cc == null) return;
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            Bounds local = mf.sharedMesh.bounds;
            _cc.center = local.center;
            _cc.height = local.size.y;
            _cc.radius = Mathf.Max(local.extents.x, local.extents.z);
        }

        private void Start() => HudSignals.EmitBossRegistered();

        private void Wake()
        {
            int areaIndex = ResolveAreaIndex();
            _areaIndex = areaIndex;

            // MV-995: same reasoning as every other boss's Wake -- a cold-boot RESUME rebuilds every
            // authored boss fresh and Dormant, so an area already beaten before the checkpoint was saved
            // must not fight again.
            if (BossCensus.IsAreaDefeated(areaIndex))
            {
                _phase = Phase.Dead;
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }

            _phase = Phase.Seated;
            // 2 phases -> HUD bar shows the half-health segment, same as every other boss.
            BossCensus.Register(this, BossName, 2, _health.Current, _health.Max, areaIndex);
        }

        private int ResolveAreaIndex()
        {
            MapData map = EnemyNavigation.Map;
            MapZone zone = map?.ZoneAt(transform.position.x, transform.position.z);
            if (zone == null) return 0;
            return AreaAccumulationDirector.AreaIndexOf(zone.id);
        }

        /// <summary>Same shared wake rule as every other boss: within <see cref="BossTuning.WakeRadius"/>
        /// (16 m) of its own post, with clear line of sight.</summary>
        private void TickDormant()
        {
            if (_target == null) { AcquireTarget(); return; }
            if (IsWithinWakeRange(_target.position)) Wake();
        }

        private bool IsWithinWakeRange(Vector3 targetPosition)
        {
            Vector3 to = targetPosition - transform.position;
            to.y = 0f;
            if (to.magnitude > BossTuning.WakeRadius) return false;
            return LineOfSight.Between(transform, _target);
        }

        /// <summary>The full per-frame fight tick, dt-parameterized so an EditMode test can drive the
        /// whole fight deterministically without a live Update loop — same contract every other boss's
        /// own <c>Tick(dt)</c> gives.</summary>
        public void Tick(float dt)
        {
            switch (_phase)
            {
                case Phase.Dormant: TickDormant(); break;
                case Phase.Seated: TickSeated(dt); break;
                case Phase.Walking: TickWalking(dt); break;
            }
            ApplyGravity(dt);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Phase 1: stationary on the throne. Faces Max, sweeps its lens on a 5 s cadence, and
        /// flings a pod volley from the side hatches every 10 s.</summary>
        private void TickSeated(float dt)
        {
            if (_target == null) { AcquireTarget(); return; }
            FaceTarget();

            _sweep.SetCooldownSeconds(HelmsmanTuning.SweepCooldownPhase1);
            _sweep.Tick(dt);
            if (_sweep.JustActivated) ApplyLensSweepDamage();

            _podTimer += dt;
            if (_podTimer >= HelmsmanTuning.PodVolleyInterval)
            {
                _podTimer = 0f;
                SpawnPods();
            }

            _brain.Tick(dt, _health.Normalized);
            BossCensus.ReportSpawnLevel(this, _brain.SpawnLevel, _brain.SpawnLevelProgress01);
        }

        /// <summary>Phase 2: torn free of the throne. Walks Max down to a 3 m stand-off, hurts on
        /// contact, and keeps sweeping its lens on a slower 6 s cadence.</summary>
        private void TickWalking(float dt)
        {
            if (_target == null) { AcquireTarget(); return; }
            Approach(dt);
            FaceTarget();
            TickContactDamage(dt);

            _sweep.SetCooldownSeconds(HelmsmanTuning.SweepCooldownPhase2);
            _sweep.Tick(dt);
            if (_sweep.JustActivated) ApplyLensSweepDamage();
        }

        private void Approach(float dt)
        {
            if (_target == null) return;
            TickApproach(dt, _target.position);
        }

        /// <summary>The actual approach-and-steer step, dt/target-parameterized so a test can drive it
        /// directly — same shape as <see cref="SludgequeenBoss.TickApproach"/>, but stopping at the
        /// ticket's own fixed 3 m stand-off rather than a collider-reach-derived one.</summary>
        public void TickApproach(float dt, Vector3 targetPosition)
        {
            Vector3 to = targetPosition - transform.position;
            to.y = 0f;

            if (to.magnitude <= HelmsmanTuning.WalkStandoff)
            {
                DriftAtStandoff(dt, to, HelmsmanTuning.MoveSpeed);
                return;
            }

            Vector3 waypoint = EnemyNavigation.Waypoint(transform.position, targetPosition,
                useZoneRoute: true, budget: _routeBudget, dt: dt);
            Vector3 routeTo = waypoint - transform.position;
            routeTo.y = 0f;
            Vector3 bearing = routeTo.sqrMagnitude > 0.0001f ? routeTo.normalized : to.normalized;

            Vector3 desired = _wallLatch.Tick(bearing, transform.position, dt, _preferSign);
            CharacterControllerMotion.SafeMove(_cc, desired * HelmsmanTuning.MoveSpeed * dt);
        }

        /// <summary>Same circling drift as <see cref="SludgequeenBoss.DriftAtStandoff"/>, so a walking
        /// Helmsman never stops dead at its stand-off either.</summary>
        private void DriftAtStandoff(float dt, Vector3 to, float speed)
        {
            if (to.sqrMagnitude < 0.0001f) return; // exactly on top of the target -- no ring to walk
            Vector3 inward = to.normalized;
            Vector3 tangent = new Vector3(-inward.z, 0f, inward.x) * _preferSign;

            Vector3 desired = _wallLatch.Tick(tangent, transform.position, dt, _preferSign);
            CharacterControllerMotion.SafeMove(_cc, desired * speed * dt);
        }

        private void OnControllerColliderHit(ControllerColliderHit hit) => HandleWallContact(hit.collider, hit.normal);

        private void HandleWallContact(Collider collider, Vector3 normal)
        {
            if (Mathf.Abs(normal.y) >= 0.5f) return;
            if (collider.TryGetComponent<CharacterController>(out _)) return;
            _wallLatch.NoteHit(normal);
        }

        // ---------------------------------------------------------------- contact damage (phase 2 only)

        /// <summary>Phase 2's passive touch damage — same "Bosses must do damage to Max and Sentinels in
        /// every world" shape as <see cref="SludgequeenBoss.TickContactDamage"/>: rate-limited, distance-
        /// based against the boss's own world-space collider radius.</summary>
        private void TickContactDamage(float dt)
        {
            _contactCooldownTimer -= dt;
            if (_contactCooldownTimer > 0f) return;

            float bossRadius = WorldRadius(_cc, transform);
            bool hitSomething = DamageIfTouching(_target, bossRadius);

            IReadOnlyList<Sentinel> sentinels = Sentinel.Active;
            for (int i = 0; i < sentinels.Count; i++)
            {
                Sentinel s = sentinels[i];
                if (DamageIfTouching(s != null ? s.transform : null, bossRadius)) hitSomething = true;
            }

            if (hitSomething) _contactCooldownTimer = HelmsmanTuning.ContactCooldown;
        }

        private bool DamageIfTouching(Transform t, float bossRadius)
        {
            if (t == null) return false;
            Vector3 to = t.position - transform.position; to.y = 0f;
            float reach = bossRadius + TargetRadius(t) + HelmsmanTuning.ContactSkin;
            if (to.magnitude > reach) return false;

            if (!t.TryGetComponent<IDamageable>(out var damageable) || !damageable.IsAlive) return false;
            Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : Vector3.forward;
            damageable.TakeDamage(new DamageInfo(HelmsmanTuning.ContactDamagePerTick, transform.position, dir, Team.Enemy));
            return true;
        }

        private static float TargetRadius(Transform t) =>
            t.TryGetComponent<CharacterController>(out var cc) ? WorldRadius(cc, t) : EnemyArchetype.PlayerRadius;

        private static float WorldRadius(CharacterController cc, Transform t)
        {
            Vector3 scale = t.lossyScale;
            return cc.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        // ---------------------------------------------------------------- lens sweep (both phases)

        /// <summary>The arc going live: 25 damage once to Max or any Sentinel inside the 90° frontal arc
        /// (centred on this boss's current facing — set by <see cref="FaceTarget"/> every tick) with a
        /// clear line of sight. Approximates the ticket's "only the 1.6 m bulkheads block it" with the
        /// shared <see cref="LineOfSight"/> check every boss already uses for its own wake rule — the
        /// Bridge's bulkheads (mothership_config.json's own 1.6 m "ship_bulkhead_stub" cover) are solid
        /// colliders LineOfSight already raycasts against, while the shorter console-bank cover is not
        /// tall enough to occlude it.</summary>
        private void ApplyLensSweepDamage()
        {
            TryHitLensSweep(_target);

            IReadOnlyList<Sentinel> sentinels = Sentinel.Active;
            for (int i = 0; i < sentinels.Count; i++)
                TryHitLensSweep(sentinels[i] != null ? sentinels[i].transform : null);
        }

        private void TryHitLensSweep(Transform t)
        {
            if (t == null) return;
            Vector3 to = t.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return;

            float angle = Vector3.Angle(transform.forward, to);
            if (angle > LensSweepTelegraph.ArcDegrees * 0.5f) return;
            if (!LineOfSight.Between(transform, t)) return;

            if (!t.TryGetComponent<IDamageable>(out var damageable) || !damageable.IsAlive) return;
            Vector3 dir = to.normalized;
            damageable.TakeDamage(new DamageInfo(LensSweepTelegraph.Damage, transform.position, dir, Team.Enemy));
        }

        // ---------------------------------------------------------------- pod volley (phase 1 only)

        /// <summary>Releases a pod volley down the side hatches, never taking the Helmsman's own live pod
        /// count past <see cref="HelmsmanTuning.MaxConcurrentPods"/> — counted fresh every wave, never a
        /// decrement-only field, same rule MV-1085/MV-1127 give Big Bermuda's and Sludgequeen's own
        /// volleys. Composition draws from the shared <see cref="BroodSpawnLevels"/> table at this boss's
        /// current <see cref="BigBermudaBrain.SpawnLevel"/> ("as today" — the ticket's own words).</summary>
        private void SpawnPods()
        {
            int alive = CountLivePodsInArea();
            int want = Mathf.Min(HelmsmanTuning.PodsPerVolley, Mathf.Max(0, HelmsmanTuning.MaxConcurrentPods - alive));
            if (want <= 0) return;

            Vector3 pos = transform.position;
            Quaternion facing = transform.rotation;
            EnemyKind[] kinds = BroodSpawnLevels.KindsFor(_brain.SpawnLevel);

            for (int i = 0; i < want; i++)
            {
                float side = (i % 2 == 0) ? -1f : 1f;
                EnemyKind kind = kinds[UnityEngine.Random.Range(0, kinds.Length)];
                EnemyArchetype archetype = EnemyArchetype.Of(kind);

                Vector3 landing = BroodArc.Landing(pos, facing, side, HelmsmanTuning.HatchSide,
                    HelmsmanTuning.HatchLandingForward, archetype.SpawnHeight, 0f);

                RobotEnemy add = CreatePod(archetype);
                add.transform.position = landing;
                add.transform.rotation = facing;
                add.TagNoReplicatePermanent(); // a boss-flung robot may never be lured into a Replicator
                add.SetAreaIndex(_areaIndex);

                var cc = add.GetComponent<CharacterController>();
                if (!CharacterControllerSafety.CanCreate(add.transform, cc, out string spawnReason))
                {
                    CharacterControllerSafety.LogRefusal("HelmsmanBoss.SpawnPods", add.gameObject.name,
                        spawnReason, landing, add.transform.lossyScale);
                    Destroy(add.gameObject);
                    continue;
                }

                add.gameObject.SetActive(true);
            }
        }

        /// <summary>This boss's own pods currently alive — counted fresh off <see cref="_podRoot"/> every
        /// wave, never carried in a decrement-only field, so one lost by any route frees its place the
        /// instant it stops being alive.</summary>
        private int CountLivePodsInArea()
        {
            if (_podRoot == null) return 0;

            int count = 0;
            for (int i = 0; i < _podRoot.childCount; i++)
            {
                Transform child = _podRoot.GetChild(i);
                if (child == null || !child.gameObject.activeInHierarchy) continue;
                if (!child.TryGetComponent<RobotEnemy>(out RobotEnemy robot) || !robot.enabled || !robot.IsAlive) continue;
                count++;
            }
            return count;
        }

        private RobotEnemy CreatePod(in EnemyArchetype a)
        {
            var go = GameObject.CreatePrimitive(a.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            go.name = $"Helmsman Pod {a.Kind}";
            go.transform.SetParent(PodRoot(), false);
            go.transform.localScale = a.BodyScale;

            go.SetActive(false);
            if (!CharacterControllerSafety.CanCreate(go.transform, null, out string createReason))
            {
                CharacterControllerSafety.LogRefusal("HelmsmanBoss.CreatePod", go.name,
                    createReason, go.transform.position, go.transform.lossyScale);
            }

            var cc = go.AddComponent<CharacterController>();
            float lateral = Mathf.Max(a.BodyScale.x, a.BodyScale.z);
            cc.height = a.ColliderHeight / Mathf.Max(a.BodyScale.y, 1e-4f);
            cc.radius = a.ColliderRadius / Mathf.Max(lateral, 1e-4f);
            cc.center = Vector3.zero;

            var e = go.AddComponent<RobotEnemy>();
            e.Apply(a);
            go.AddComponent<MaxWorlds.VFX.RobotRig>();
            e.gameObject.SetActive(false);
            return e;
        }

        /// <summary>Top-level and unit-scaled, same reasoning as <see cref="SludgequeenBoss.BroodRoot"/>
        /// — the boss never moves while pods are active (Phase 1 is seated), but matching the pattern
        /// keeps this consistent with every other boss's own brood root.</summary>
        private Transform PodRoot()
        {
            if (_podRoot == null) _podRoot = new GameObject("Helmsman Pods").transform;
            return _podRoot;
        }

        private void OnDestroy()
        {
            if (_podRoot != null) Destroy(_podRoot.gameObject);
            BossCensus.Forget(this);
        }

        // ---------------------------------------------------------------- IDamageable

        public void TakeDamage(in DamageInfo info)
        {
            // Same "a hit landing is itself a wake trigger" rule as every other boss's TakeDamage —
            // consumed as the wake, not as damage.
            if (_phase == Phase.Dormant)
            {
                if (!DamageRules.Applies(info.Attacker, Team)) return;
                Wake();
                return;
            }

            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return;
            HudSignals.EmitDamage(transform.position + Vector3.up * 2.5f, info.Amount);
            _health.TakeDamage(info.Amount);
            BossCensus.ReportHealth(this, _health.Current, _health.Max);

            // No tell: the ticket's own "below half health: tears free of the throne, walks" is an
            // immediate switch, unlike Sludgequeen's 3 s enrage tell.
            if (_phase == Phase.Seated && _health.Normalized <= HelmsmanTuning.PhaseTwoThreshold)
                _phase = Phase.Walking;
        }

        private void OnDeath()
        {
            _phase = Phase.Dead;
            BossCensus.ReportDefeated(this);
            // MV-1245 owns what collecting the Helm Key actually DOES (the viewport exit) — this only
            // drops it where the Helmsman dies.
            PickupDirector.EnsureInstalled().PlacePickupAt(PickupKind.HelmKey, transform.position);
            gameObject.SetActive(false);
        }

        private void AcquireTarget()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return;
            _target = p.transform;
        }

        private void FaceTarget()
        {
            if (_target == null) return;
            Vector3 to = _target.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        }

        private void ApplyGravity(float dt)
        {
            if (_cc == null || !_cc.enabled) return;
            if (_cc.isGrounded && _verticalVel < 0f) _verticalVel = -2f;
            _verticalVel -= Gravity * dt;
            _cc.Move(Vector3.up * _verticalVel * dt);
        }
    }
}
