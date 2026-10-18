using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// World 5's own LIGHT MISSILE ball (MV-1210, Lee's brief 2026-10-17) — a pooled, homing projectile
    /// fired by <see cref="MaxWorlds.Combat.LightMissile"/>, mirroring <see cref="RicochetDisc"/>'s shape
    /// (pooled, collider stripped, manual hit query, <see cref="RicochetDisc.TryResolveDamageable"/>
    /// reused for the gate fallback) rather than bouncing: a ball flies dead straight for its own launch
    /// leg, then homes onto a target in a smoothly tightening arc (TRACKING's turn rate grows the longer
    /// it stays airborne), retargeting without a snap if that target dies, and fizzling if it never finds
    /// one at all.
    ///
    /// Self-ticks via <see cref="Update"/> but exposes <see cref="Tick"/> publicly with an explicit
    /// <c>dt</c>, the same EditMode seam every other free-flying projectile here carries.
    /// </summary>
    [MaxWorlds.Core.PerfSection("weapons")]
    public sealed class LightMissileBall : MonoBehaviour
    {
        public const int MaxBallsInFlight = 50;

        /// <summary>Rule 2: dead straight for the first 1.5 m before it starts homing.</summary>
        public const float LaunchLegMetres = 1.5f;

        /// <summary>Rule 4: flight height, above every 1.5 m wall/cover piece — balls fly over cover.</summary>
        public const float FlightHeight = 1.7f;

        /// <summary>Rule 3/5: candidates beyond this range (robots, then a fallback cradle) don't qualify.</summary>
        public const float TargetRangeMetres = 12f;

        /// <summary>Rule 7: the 2D (XZ) contact radius against a Team.Enemy IDamageable.</summary>
        public const float ContactRadius = 0.4f;

        /// <summary>Rule 6: no target found at all for this long and the ball fizzles.</summary>
        public const float FizzleSeconds = 4f;

        /// <summary>Rule 6: hard cap — any ball is removed at this age regardless of target state.</summary>
        public const float MaxAirborneSeconds = 6f;

        /// <summary>Rule 4: TurnRate = TrackingBase + this much more per second airborne.</summary>
        public const float TurnRateGrowthPerSecPerSec = 60f;

        public const float VisualDiameter = 0.45f;

        private static readonly Color VioletColor = new Color(0.55f, 0.20f, 0.85f);

        private static readonly List<LightMissileBall> s_pool = new List<LightMissileBall>(MaxBallsInFlight);

        /// <summary>Every pooled ball instance, active or not — test-only visibility into the pool's own
        /// size/reuse, same shape <see cref="RicochetDisc.Pool"/> gives a test.</summary>
        public static IReadOnlyList<LightMissileBall> Pool => s_pool;

        private bool _active;
        private Vector3 _direction;
        private float _speed;
        private float _damage;
        private float _trackingBase;
        private int _areaIndex;
        private float _age;
        private float _distanceTravelled;
        private bool _initialTargetResolved;
        private HashSet<IDamageable> _volleyClaimed;
        private IDamageable _target;
        private Transform _targetTransform;

        private static readonly Collider[] s_hitColliders = new Collider[16];

        /// <summary>Whether this pooled instance is currently a live, flying ball.</summary>
        public bool IsActiveBall => _active;

        /// <summary>Seconds airborne — what TRACKING's growth and the fizzle/hard-cap timers read.</summary>
        public float SecondsAirborne => _age;

        /// <summary>The ball's current heading — what rule 4's own turn-rate cap is measured against.</summary>
        public Vector3 Direction => _direction;

        /// <summary>The ball's own currently tracked target, or null while it has none (rule 6).</summary>
        public IDamageable CurrentTarget => _target;

        /// <summary>TrackingBase + 60 deg/s per second airborne (rule 4) — the max a ball may ever turn
        /// in one tick, growing the whole flight, not only once it starts steering.</summary>
        public float CurrentTurnRateDegPerSec => _trackingBase + TurnRateGrowthPerSecPerSec * _age;

        /// <summary>Fire one ball. Reuses the first inactive pooled instance, or builds a new one while
        /// under <see cref="MaxBallsInFlight"/> — returns null, spawning nothing, once the pool is full.
        /// <paramref name="volleyClaimed"/> (optional) is the firer's own per-volley claimed-target set
        /// (rule 1: "balls in one volley prefer distinct targets") — this ball adds its own pick to it,
        /// once, when it resolves its initial target at the end of its launch leg.</summary>
        public static LightMissileBall Spawn(Vector3 origin, Vector3 direction, float speed, float damage,
            float trackingBase, int areaIndex, HashSet<IDamageable> volleyClaimed = null)
        {
            LightMissileBall ball = null;
            for (int i = 0; i < s_pool.Count; i++)
            {
                if (!s_pool[i]._active) { ball = s_pool[i]; break; }
            }

            if (ball == null)
            {
                if (s_pool.Count >= MaxBallsInFlight) return null;
                var go = new GameObject("LightMissileBall (stand-in)");
                BuildVisual(go.transform);
                ball = go.AddComponent<LightMissileBall>();
                s_pool.Add(ball);
            }

            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f) direction = Vector3.forward;
            direction.Normalize();

            ball.gameObject.SetActive(true);
            ball.transform.position = origin;
            ball.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            ball._direction = direction;
            ball._speed = speed;
            ball._damage = damage;
            ball._trackingBase = trackingBase;
            ball._areaIndex = areaIndex;
            ball._age = 0f;
            ball._distanceTravelled = 0f;
            ball._initialTargetResolved = false;
            ball._volleyClaimed = volleyClaimed;
            ball._target = null;
            ball._targetTransform = null;
            ball._active = true;
            return ball;
        }

        /// <summary>Rule 1: "freeze, death or resume" style clear — deactivates every pooled ball without
        /// destroying the pool, same idiom <see cref="RicochetDisc.ClearAll"/> uses.</summary>
        public static void ClearAll()
        {
            for (int i = 0; i < s_pool.Count; i++)
                if (s_pool[i]._active) s_pool[i].Retire();
        }

        /// <summary>Test-only teardown — destroys every pooled instance and empties the pool.</summary>
        public static void DestroyPoolForTests()
        {
            for (int i = 0; i < s_pool.Count; i++)
                if (s_pool[i] != null) DestroyImmediate(s_pool[i].gameObject);
            s_pool.Clear();
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The real per-frame flight step — public so an EditMode test can drive a fired ball
        /// deterministically.</summary>
        public void Tick(float dt)
        {
            if (!_active) return;
            _age += dt;

            bool launchLegDone = _distanceTravelled >= LaunchLegMetres;

            if (launchLegDone)
            {
                if (!_initialTargetResolved)
                {
                    _initialTargetResolved = true;
                    IDamageable picked = SelectInitialTarget(transform.position, _direction, _areaIndex,
                        TargetRangeMetres, _volleyClaimed);
                    if (picked != null) _volleyClaimed?.Add(picked);
                    SetTarget(picked);
                    _volleyClaimed = null; // one-shot — a later retarget (rule 5) ignores distinctness
                }
                else if (_target == null || !_target.IsAlive)
                {
                    // Rule 5: retarget, measured from the ball's own current position, nearest first.
                    SetTarget(SelectRetarget(transform.position, _areaIndex, TargetRangeMetres));
                }

                if (_target != null && _targetTransform != null)
                {
                    float turnRate = CurrentTurnRateDegPerSec;
                    Quaternion turned = HomingSteering.TurnToward(transform.rotation, transform.position,
                        _targetTransform.position, turnRate, dt);
                    transform.rotation = turned;
                    Vector3 dir = turned * Vector3.forward;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 1e-6f) _direction = dir.normalized;
                }
            }

            Vector3 next = transform.position + _direction * (_speed * dt);
            next.y = FlightHeight;
            _distanceTravelled += _speed * dt;
            transform.position = next;

            if (TryFindContact(transform.position, out IDamageable hit))
            {
                if (hit is RobotEnemy robot && robot.IsDormant) robot.Activate();
                hit.TakeDamage(new DamageInfo(_damage, transform.position, _direction, Team.Player,
                    source: DamageSource.PrimaryWeapon));
                Retire();
                return;
            }

            if (_target == null && _age >= FizzleSeconds) { Retire(); return; }
            if (_age >= MaxAirborneSeconds) { Retire(); return; }
        }

        private void SetTarget(IDamageable target)
        {
            _target = target;
            _targetTransform = (target as Component)?.transform;
        }

        /// <summary>Rule 3: candidates are alive, awake, same combat level, same area as the origin, and
        /// within <paramref name="range"/> — robots preferred over a fallback cradle. Among robot
        /// candidates, picks the smallest angle to <paramref name="heading"/> (ties: nearest), skipping
        /// anything already in <paramref name="claimed"/> while an unclaimed candidate remains.</summary>
        public static IDamageable SelectInitialTarget(Vector3 origin, Vector3 heading, int areaIndex,
            float range, HashSet<IDamageable> claimed)
        {
            RobotEnemy best = null;
            float bestAngle = float.MaxValue;
            float bestDistSq = float.MaxValue;
            float rangeSq = range * range;
            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;

            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy candidate = active[i];
                if (!IsEligibleRobot(candidate, origin, areaIndex)) continue;
                if (claimed != null && claimed.Contains(candidate)) continue;

                Vector3 to = candidate.transform.position - origin;
                to.y = 0f;
                float distSq = to.sqrMagnitude;
                if (distSq > rangeSq) continue;

                float angle = distSq < 1e-6f ? 0f : Vector3.Angle(heading, to.normalized);
                if (angle < bestAngle - 1e-4f || (Mathf.Abs(angle - bestAngle) <= 1e-4f && distSq < bestDistSq))
                {
                    bestAngle = angle;
                    bestDistSq = distSq;
                    best = candidate;
                }
            }

            if (best != null) return best;

            // No unclaimed robot candidate — if every robot in range was claimed by an earlier ball this
            // volley, rule 1 says prefer distinct targets only "while unclaimed candidates remain": fall
            // back to the nearest cradle rather than leaving the ball with nothing at all.
            return NearestCradle(origin, areaIndex, range);
        }

        /// <summary>Rule 5: retarget from the ball's own current position — nearest candidate, no
        /// distinctness preference (that's a volley-fire-time-only rule).</summary>
        public static IDamageable SelectRetarget(Vector3 origin, int areaIndex, float range)
        {
            RobotEnemy best = null;
            float bestDistSq = float.MaxValue;
            float rangeSq = range * range;
            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;

            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy candidate = active[i];
                if (!IsEligibleRobot(candidate, origin, areaIndex)) continue;

                Vector3 to = candidate.transform.position - origin;
                to.y = 0f;
                float distSq = to.sqrMagnitude;
                if (distSq > rangeSq) continue;

                if (distSq < bestDistSq) { bestDistSq = distSq; best = candidate; }
            }

            if (best != null) return best;
            return NearestCradle(origin, areaIndex, range);
        }

        private static bool IsEligibleRobot(RobotEnemy candidate, Vector3 origin, int areaIndex) =>
            candidate != null && candidate.IsAlive && !candidate.IsDormant
            && candidate.AreaIndex == areaIndex
            && CombatLevel.SameLevel(EnemyNavigation.Map, origin, candidate.transform.position);

        /// <summary>The nearest live Assembly Cradle stand-in (rule 3's fallback) — any currently-placed
        /// <see cref="MowerHutch"/>/<see cref="Replicator"/> (both <see cref="IFactoryBody"/>) in
        /// <paramref name="areaIndex"/> within <paramref name="range"/>. The real Assembly Cradle is a
        /// later W5 ticket; until it exists this is what the ticket's own "apply the fallback to
        /// whatever IFactoryBody machines exist so the rule is testable" asks for.</summary>
        private static IDamageable NearestCradle(Vector3 origin, int areaIndex, float range)
        {
            IDamageable best = null;
            float bestDistSq = float.MaxValue;
            float rangeSq = range * range;

            IReadOnlyList<MowerHutch> hutches = FactoryCensus.All;
            for (int i = 0; i < hutches.Count; i++)
            {
                MowerHutch m = hutches[i];
                if (m == null || !m.IsAlive) continue;
                if (!(m is IDamageable d)) continue;
                ConsiderCradle(m.transform.position, d, origin, areaIndex, rangeSq, ref best, ref bestDistSq);
            }

            IReadOnlyList<Replicator> replicators = FactoryCensus.RegisteredReplicators;
            for (int i = 0; i < replicators.Count; i++)
            {
                Replicator r = replicators[i];
                if (r == null || !r.IsAlive) continue;
                if (!(r is IDamageable d)) continue;
                ConsiderCradle(r.transform.position, d, origin, areaIndex, rangeSq, ref best, ref bestDistSq);
            }

            return best;
        }

        private static void ConsiderCradle(Vector3 position, IDamageable d, Vector3 origin, int areaIndex,
            float rangeSq, ref IDamageable best, ref float bestDistSq)
        {
            if (AreaIndexAt(position) != areaIndex) return;
            Vector3 to = position - origin;
            to.y = 0f;
            float distSq = to.sqrMagnitude;
            if (distSq > rangeSq) return;
            if (distSq < bestDistSq) { bestDistSq = distSq; best = d; }
        }

        /// <summary>Rule 3's own area resolver: "RobotEnemy.AreaIndex == Max's current area index" —
        /// the same <c>MapZone.AreaIndex</c> lookup <see cref="RobotEnemy"/>'s own
        /// <c>ComputeIsWellBehindPlayer</c> already uses for the player's side of that comparison, not a
        /// new one invented for this ticket.</summary>
        public static int AreaIndexAt(Vector3 position)
        {
            MapData map = EnemyNavigation.Map;
            if (map == null) return 0;
            MapZone zone = map.ZoneAt(position.x, position.y, position.z);
            return zone != null ? zone.AreaIndex : 0;
        }

        /// <summary>Rule 7: a 2D (XZ) contact radius against any live Team.Enemy IDamageable — an
        /// infinite vertical capsule at the ball's XZ position rather than a 3D sphere, since the ball
        /// flies at <see cref="FlightHeight"/> and must still register a hit on ground-level geometry
        /// (robots, machines, gates). Cover-layer geometry is ignored (rule 7: "balls pass over it").
        /// Reuses <see cref="RicochetDisc.TryResolveDamageable"/> for the AreaGateThresholdLink sibling
        /// fallback rather than a second copy of it.</summary>
        private static bool TryFindContact(Vector3 pos, out IDamageable target)
        {
            target = null;
            Vector3 bottom = new Vector3(pos.x, -1000f, pos.z);
            Vector3 top = new Vector3(pos.x, 1000f, pos.z);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, ContactRadius, s_hitColliders, ~0,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider c = s_hitColliders[i];
                if (CoverLayer.Exists && c.gameObject.layer == CoverLayer.Index) continue;
                if (!RicochetDisc.TryResolveDamageable(c, out IDamageable d)) continue;
                if (!d.IsAlive || d.Team != Team.Enemy) continue;
                target = d;
                return true;
            }

            return false;
        }

        private void Retire()
        {
            _active = false;
            gameObject.SetActive(false);
        }

        /// <summary>Placeholder art only (ticket item: the real look is MV-1211) — a plain violet sphere,
        /// 0.45 m. Collider stripped, same idiom <see cref="RicochetDisc"/> uses: this ball's own hit
        /// test is a manual overlap, never physics collision response.</summary>
        private static void BuildVisual(Transform parent)
        {
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Ball";
            var col = sphere.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            }
            sphere.transform.SetParent(parent, false);
            sphere.transform.localScale = Vector3.one * VisualDiameter;
            Material mat = VfxMaterials.AdditiveTinted(VioletColor);
            if (mat != null) sphere.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
