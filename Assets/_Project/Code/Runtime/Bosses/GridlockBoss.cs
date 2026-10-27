using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.UI;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// GRIDLOCK — The City's boss (MV-1299), "The Core": a six-sided drum of rack lights on six
    /// needle legs. Standalone <see cref="IDamageable"/>, modelled on <see cref="SludgequeenBoss"/>
    /// for the walk/standoff/touch shape, but intentionally simpler: this ticket is body, wake, walk
    /// and passive touch only — one phase, no escalation, no spawns, no charge/ram (the ticket's own
    /// "no charge or ram" is absolute, unlike <see cref="BigBermudaBoss"/>'s retired one). Her attacks
    /// (SQUAD/JUMP/STRIKE, the escalation clock the roof dial's clock ring reads) are MV-1306's; the
    /// arena, shield and finale are MV-1309's. Neither exists yet, so there is deliberately no phase
    /// machine here for them to slot into later beyond the one switch (Dormant -&gt; Fight -&gt; Dead)
    /// every boss already has.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [PerfSection("bosses")]
    public sealed class GridlockBoss : MonoBehaviour, IDamageable
    {
        private enum Phase { Dormant, Fight, Dead }

        private const string BossName = "GRIDLOCK";
        private const float Gravity = 20f;

        private Phase _phase = Phase.Dormant;
        private DestructibleHealth _health;
        private CharacterController _cc;
        private Transform _target;

        private float _contactCooldownTimer;
        private float _verticalVel;

        // Same wall/route handling as every other walking boss (MV-590/MV-667) — a boss this size
        // still has to navigate the arena's cover, not beeline through it.
        private readonly WallLatch _wallLatch = new WallLatch();
        private float _preferSign;
        private readonly ZoneRouteBudget _routeBudget = new ZoneRouteBudget();

        // This boss's own authored area, same convention as every other boss's _wakeArea — unused
        // beyond storage today (the shared wake rule consults range + line of sight only), kept for
        // parity with SludgequeenBoss/BigBermudaBoss/HelmsmanBoss's own SetWakeArea.
        private Rect _wakeArea;
        private int _areaIndex;

        public bool IsAlive => _phase != Phase.Dead && _health != null && _health.IsAlive;
        public Team Team => Team.Enemy;

        /// <summary>True from wake until death — same meaning as <see cref="BigBermudaBoss.Engaged"/>.</summary>
        public bool Engaged => _phase == Phase.Fight;

        public bool IsDead => _phase == Phase.Dead;

        /// <summary>Hands this boss its own authoring area's floor, same convention as
        /// <see cref="SludgequeenBoss.SetWakeArea"/>.</summary>
        public void SetWakeArea(Rect area) => _wakeArea = area;

        /// <summary>Same MV-613 reasoning as <see cref="SludgequeenBoss.FitColliderTo"/> — called once
        /// <see cref="MaxWorlds.VFX.GridlockRig"/>'s own drum bounds are known. The ticket's own
        /// "collider fitted to the drum, not the legs": the rig only ever passes the drum's own
        /// bounds here, never the full rendered body including legs.</summary>
        public void FitColliderTo(Bounds localBounds)
        {
            if (_cc == null) return;
            _cc.center = localBounds.center;
            _cc.height = Mathf.Max(0.01f, localBounds.size.y);
            _cc.radius = Mathf.Max(0.01f, Mathf.Max(localBounds.extents.x, localBounds.extents.z));
        }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            FitColliderToRenderedBody();
            _preferSign = ObstacleSteering.PreferSignFor(GetInstanceID());
            _health = new DestructibleHealth(DevTuning.Or(DevTuning.BossHealth, GridlockTuning.Health));
            _health.Destroyed += OnDeath;
            _contactCooldownTimer = GridlockTuning.ContactCooldown; // no free first hit
            AcquireTarget();
        }

        /// <summary>Same MV-542 collider-fit reasoning as <see cref="SludgequeenBoss.FitColliderToRenderedBody"/>
        /// — a fallback for the bare greybox cube before <see cref="MaxWorlds.VFX.GridlockRig"/> binds
        /// over it and calls <see cref="FitColliderTo"/> for real.</summary>
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
            // authored boss fresh and Dormant, so an area already beaten before the checkpoint was
            // saved must not fight again.
            if (BossCensus.IsAreaDefeated(areaIndex))
            {
                _phase = Phase.Dead;
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }

            _phase = Phase.Fight;
            // One phase today (no escalation in this ticket) -> the HUD bar shows a single segment.
            BossCensus.Register(this, BossName, 1, _health.Current, _health.Max, areaIndex);
        }

        private int ResolveAreaIndex()
        {
            MapData map = EnemyNavigation.Map;
            MapZone zone = map?.ZoneAt(transform.position.x, transform.position.z);
            if (zone == null) return 0;
            return AreaAccumulationDirector.AreaIndexOf(zone.id);
        }

        /// <summary>Same shared wake rule as every other boss: within <see cref="BossTuning.WakeRadius"/>
        /// (16 m) of its own post, with clear line of sight (the ticket's own "universal wake").</summary>
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
                case Phase.Fight: TickFight(dt); break;
            }
            ApplyGravity(dt);
        }

        private void Update() => Tick(Time.deltaTime);

        private void TickFight(float dt)
        {
            if (_target == null) { AcquireTarget(); return; }
            Approach(dt);
            FaceTarget();
            TickContactDamage(dt);
        }

        private void Approach(float dt)
        {
            if (_target == null) return;
            TickApproach(dt, _target.position);
        }

        /// <summary>The actual approach-and-steer step, dt/target-parameterized so a test can drive it
        /// directly — same shape as <see cref="SludgequeenBoss.TickApproach"/>. The ticket's own
        /// "stopping at the edge of her touch reach and repositioning so she never stands still as a
        /// statue": the stop distance is <see cref="ContactReachTo"/>'s own live number minus
        /// <see cref="GridlockTuning.StandoffMargin"/>, so "stopped" and "within touch reach" can never
        /// drift apart, and reaching it starts the circling drift below rather than a dead stop.</summary>
        public void TickApproach(float dt, Vector3 targetPosition)
        {
            Vector3 to = targetPosition - transform.position;
            to.y = 0f;
            float stopDistance = ContactReachTo(_target) - GridlockTuning.StandoffMargin;

            if (to.magnitude <= stopDistance)
            {
                DriftAtStandoff(dt, to, GridlockTuning.MoveSpeed);
                return;
            }

            Vector3 waypoint = EnemyNavigation.Waypoint(transform.position, targetPosition,
                useZoneRoute: true, budget: _routeBudget, dt: dt);
            Vector3 routeTo = waypoint - transform.position;
            routeTo.y = 0f;
            Vector3 bearing = routeTo.sqrMagnitude > 0.0001f ? routeTo.normalized : to.normalized;

            Vector3 desired = _wallLatch.Tick(bearing, transform.position, dt, _preferSign);
            CharacterControllerMotion.SafeMove(_cc, desired * GridlockTuning.MoveSpeed * dt);
        }

        /// <summary>Same circling drift as <see cref="SludgequeenBoss.DriftAtStandoff"/>, so Gridlock
        /// never stops dead at standoff either — "she never stands still as a statue" (the ticket's
        /// own words).</summary>
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

        // ---------------------------------------------------------------- touch (MV-1299)

        /// <summary>The ticket's own "15 damage/s to Max and Sentinels while in contact ... rate-
        /// limited like RobotEnemy contact damage, drains the force field" — same rate-limited,
        /// distance-based shape every other boss's passive touch already uses.</summary>
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

            if (hitSomething) _contactCooldownTimer = GridlockTuning.ContactCooldown;
        }

        private bool DamageIfTouching(Transform t, float bossRadius)
        {
            if (t == null) return false;
            Vector3 to = t.position - transform.position; to.y = 0f;
            float reach = bossRadius + TargetRadius(t) + GridlockTuning.ContactSkin;
            if (to.magnitude > reach) return false;

            if (!t.TryGetComponent<IDamageable>(out var damageable) || !damageable.IsAlive) return false;
            Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : Vector3.forward;
            damageable.TakeDamage(new DamageInfo(GridlockTuning.ContactDamagePerTick, transform.position, dir, Team.Enemy));
            return true;
        }

        private static float TargetRadius(Transform t) =>
            t.TryGetComponent<CharacterController>(out var cc) ? WorldRadius(cc, t) : EnemyArchetype.PlayerRadius;

        private static float WorldRadius(CharacterController cc, Transform t)
        {
            Vector3 scale = t.lossyScale;
            return cc.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        /// <summary>The exact world-space contact reach <see cref="TickContactDamage"/> would check for
        /// <paramref name="target"/> right now — the SAME number <see cref="TickApproach"/> stops just
        /// inside of (MV-1083's reasoning, carried into this boss). Falls back to
        /// <see cref="EnemyArchetype.PlayerRadius"/> when <paramref name="target"/> is null.</summary>
        private float ContactReachTo(Transform target)
        {
            float targetRadius = target != null ? TargetRadius(target) : EnemyArchetype.PlayerRadius;
            return WorldRadius(_cc, transform) + targetRadius + GridlockTuning.ContactSkin;
        }

        private void OnDestroy() => BossCensus.Forget(this);

        // ---------------------------------------------------------------- IDamageable

        public void TakeDamage(in DamageInfo info)
        {
            // Same "a hit landing is itself a wake trigger" rule as every other boss's TakeDamage --
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
        }

        private void OnDeath()
        {
            _phase = Phase.Dead;
            BossCensus.ReportDefeated(this);
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
