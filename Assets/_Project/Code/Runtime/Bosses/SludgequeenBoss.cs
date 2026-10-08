using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Sludgequeen — World 2's final boss (MV-1127, switching her on in place of the Big Bermuda
    /// <c>MapRuntime.BuildBoss</c> used to build for every authored id). Modelled on
    /// <see cref="BigBermudaBoss"/> — slow walker, standoff, passive contact damage, a time-based spawn
    /// escalation shared with Big Bermuda's own clock (<see cref="BigBermudaBrain"/>) — but her signature
    /// attack is marked sludge LOBS, not a floor flood: MV-696's flood/dry-zone mechanic is gone
    /// entirely (Lee, 2026-09-30: "no rising flood in any world"). Five lobs fly out of her two cannon
    /// mouths every volley, one aimed at the nearest player-side body and the rest scattered 2-5 m
    /// around it, each marked by a yellow <see cref="GroundRing"/> for its whole flight — reusing the
    /// Pipe Turret's own <see cref="MaxWorlds.Enemies.CorrosiveGlob"/>/<see cref="MaxWorlds.Enemies.CorrosionPuddle"/>
    /// (MV-691), fired at a fixed landing point rather than a tracked target so the splash lands on
    /// whoever is actually standing there. Her chutes release sludgers in waves, capped at
    /// <see cref="SludgequeenTuning.MaxConcurrentBrood"/> alive at once (MV-1127 §5, the same "counted
    /// fresh, never decrement-only" rule MV-1085 gave Big Bermuda's own volley).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [MaxWorlds.Core.PerfSection("bosses")]
    public sealed class SludgequeenBoss : MonoBehaviour, IDamageable
    {
        private enum Phase { Dormant, Fight, EnrageTell, Enrage, Dead }

        private const string BossName = "SLUDGEQUEEN";
        private const float Gravity = 20f;

        private static readonly Color LobRingColor = new Color(0.98f, 0.80f, 0.14f);

        private Phase _phase = Phase.Dormant;
        private DestructibleHealth _health;
        private CharacterController _cc;
        private Transform _target;
        private IDamageable _targetDamageable;

        /// <summary>MV-1083: Max, specifically — set once in <see cref="AcquireTarget"/> and never
        /// reassigned. Same split as <see cref="MaxWorlds.Bosses.BigBermudaBoss"/>'s own
        /// <c>_playerTarget</c>/<c>_target</c>: <see cref="_target"/> is the CURRENT chase/face/attack
        /// target, which <see cref="RetargetIfNeeded"/> may swing onto a nearby Sentinel; contact
        /// damage against Max always goes through this field instead, so the Sentinel loop in
        /// <see cref="TickContactDamage"/> can never double-hit whichever Sentinel happens to be
        /// <see cref="_target"/> at the time.</summary>
        private Transform _playerTarget;

        /// <summary>MV-1083: the Sentinel <see cref="_target"/> is currently engaged with, or null
        /// while chasing Max — same bookkeeping as <see cref="MaxWorlds.Bosses.BigBermudaBoss"/>'s own
        /// <c>_engagedSentinel</c>.</summary>
        private Sentinel _engagedSentinel;

        /// <summary>MV-1083: seconds left before another contact-damage tick can land — same
        /// "no free first hit" convention as <see cref="MaxWorlds.Bosses.BigBermudaBoss"/>'s own
        /// <c>_contactCooldownTimer</c> (MV-720).</summary>
        private float _contactCooldownTimer;

        // Same wall/route handling as BigBermudaBoss (MV-590/MV-667) — a boss this size still has to
        // navigate the arena's cover, not beeline through it.
        private readonly WallLatch _wallLatch = new WallLatch();
        private float _preferSign;
        private readonly ZoneRouteBudget _routeBudget = new ZoneRouteBudget();

        // This boss's own authored area (MV-572), same convention as BigBermudaBoss's own _wakeArea.
        private Rect _wakeArea;

        /// <summary>The arena's own world-space X/Z extent (MV-696) — now consulted only to keep a lob's
        /// landing spot (and the sludger cap's own spawn point) inside the arena, never for a flood.</summary>
        private Rect _arenaBounds;

        /// <summary>MV-1122-style: this boss's own 1-based area, resolved once in <see cref="Wake"/> and
        /// stamped onto every sludger it releases — same reasoning as <see cref="BigBermudaBoss"/>'s own
        /// <c>_areaIndex</c>.</summary>
        private int _areaIndex;

        private float _verticalVel;
        private float _tellTimer;

        // ---------------------------------------------------------------- sludge lobs (MV-1127)

        private struct PendingLob
        {
            public float Delay;
            public int Side;
            public Vector3 LandingPoint;
        }

        private struct ActiveRing
        {
            public GroundRing Ring;
            public float Remaining;
        }

        private readonly List<PendingLob> _pendingLobs = new List<PendingLob>(8);
        private readonly List<ActiveRing> _activeRings = new List<ActiveRing>(8);
        private readonly Stack<GroundRing> _ringPool = new Stack<GroundRing>(8);
        private readonly List<ActiveRing> _dribbleFlashes = new List<ActiveRing>(4);
        private float _globTimer;
        private float _dribbleTimer;
        private const float DribbleFlashRadius = 0.25f;
        private const float DribbleFlashLife = 0.15f;

        /// <summary>The rig's own two cannon-mouth transforms (MV-1127) — set once by
        /// <see cref="MaxWorlds.VFX.SludgequeenRig.Bind"/> right after it builds them, same "rig hands the
        /// boss what it built" flow <see cref="FitColliderTo"/> already uses. A lob fires from the EXACT
        /// transform (not a procedural approximation) so "first seen within 0.3 m of a cannon mouth" is
        /// trivially true. Falls back to this boss's own transform when unset (a bare fixture with no
        /// rig bound).</summary>
        private readonly Transform[] _cannonMouths = new Transform[2];

        /// <summary>The rig's own two chute-foot transforms (MV-1127) — where a sludger actually steps
        /// off the chute onto the floor. Same fallback-to-procedural-point reasoning as
        /// <see cref="_cannonMouths"/>.</summary>
        private readonly Transform[] _chuteFeet = new Transform[2];

        // ---------------------------------------------------------------- sludgers (MV-1127, brood)

        private float _broodTimer;
        private float _broodTellTimer;
        private bool _broodTelling;
        private Transform _broodRoot;

        /// <summary>Purely the shared time-based spawn-level clock (MV-1127 §6: "one level every 30 s,
        /// four levels") — Big Bermuda's own <see cref="BigBermudaBrain"/>, reused rather than
        /// duplicated. Its own <c>Enraged</c> output is not read here; this boss's half-health tell is
        /// its own phase machine below (<see cref="Phase.EnrageTell"/>/<see cref="Phase.Enrage"/>), since
        /// the ticket's 3 s tell has no equivalent in Big Bermuda's immediate enrage.</summary>
        private readonly BigBermudaBrain _brain = new BigBermudaBrain(SludgequeenTuning.PhaseTwoThreshold);

        public bool IsAlive => _phase != Phase.Dead && _health != null && _health.IsAlive;
        public Team Team => Team.Enemy;

        /// <summary>True from wake until death — same meaning as <see cref="BigBermudaBoss.Engaged"/>.</summary>
        public bool Engaged => _phase == Phase.Fight || _phase == Phase.EnrageTell || _phase == Phase.Enrage;

        public bool IsDead => _phase == Phase.Dead;

        /// <summary>True once the half-health tell has actually elapsed and the faster timings have
        /// landed (MV-1127 §9).</summary>
        public bool IsEnraged => _phase == Phase.Enrage;

        /// <summary>True during the brood's 1 s hatch-open tell — the ONE getter
        /// <see cref="MaxWorlds.VFX.SludgequeenRig"/> reads to spin the hatch strip up for the attack
        /// tell, same read-gameplay-write-nothing seam <see cref="BigBermudaBoss.SpawnWindup01"/> uses.</summary>
        public bool IsVenting => _broodTelling;

        /// <summary>1..<see cref="BossTuning.MaxSpawnLevel"/> — how far her sludger composition has
        /// escalated (MV-1127 §6). Drives the HUD's spawn-level bar via <see cref="BossCensus"/>.</summary>
        public int SpawnLevel => _brain.SpawnLevel;

        /// <summary>Hands this boss its own authoring area's floor, same convention as
        /// <see cref="BigBermudaBoss.SetWakeArea"/>.</summary>
        public void SetWakeArea(Rect area) => _wakeArea = area;

        /// <summary>The arena's own world-space X/Z extent — what a lob's landing spot is clamped
        /// inside (MV-1127; MV-696's original flood consumer of this is gone).</summary>
        public void SetArenaBounds(Rect bounds) => _arenaBounds = bounds;

        /// <summary>MV-1127: the rig's own two cannon-mouth transforms, bound once right after the rig
        /// builds them — see <see cref="_cannonMouths"/>'s own doc comment.</summary>
        public void SetCannonMouths(Transform left, Transform right)
        {
            _cannonMouths[0] = left;
            _cannonMouths[1] = right;
        }

        /// <summary>MV-1127: the rig's own two chute-foot transforms — see <see cref="_chuteFeet"/>'s
        /// own doc comment.</summary>
        public void SetChuteFeet(Transform left, Transform right)
        {
            _chuteFeet[0] = left;
            _chuteFeet[1] = right;
        }

        // Registered while alive, same bookkeeping idiom as SludgePuddle._active -- nothing in this
        // ticket reads this static list, but ResetRegistry is a hygiene reset several tests already call.
        private static readonly List<SludgequeenBoss> _active = new List<SludgequeenBoss>(2);

        /// <summary>Test/level-reset hygiene, same idiom as <see cref="MaxWorlds.Enemies.SludgePuddle.ResetRegistry"/>.</summary>
        public static void ResetRegistry() => _active.Clear();

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            FitColliderToRenderedBody();
            _preferSign = ObstacleSteering.PreferSignFor(GetInstanceID());
            _health = new DestructibleHealth(DevTuning.Or(DevTuning.BossHealth, SludgequeenTuning.Health));
            _health.Destroyed += OnDeath;
            _contactCooldownTimer = SludgequeenTuning.ContactCooldown; // MV-1083: no free first hit
            AcquireTarget();
        }

        /// <summary>Same MV-542 collider-fit reasoning as <see cref="BigBermudaBoss.FitColliderToRenderedBody"/>.</summary>
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

        /// <summary>Same MV-613 reasoning as <see cref="BigBermudaBoss.FitColliderTo"/> — called once the
        /// rig's own combined bounds are known.</summary>
        public void FitColliderTo(Bounds localBounds)
        {
            if (_cc == null) return;
            _cc.center = localBounds.center;
            _cc.height = Mathf.Max(0.01f, localBounds.size.y);
            _cc.radius = Mathf.Max(0.01f, Mathf.Max(localBounds.extents.x, localBounds.extents.z));
        }

        private void Start() => HudSignals.EmitBossRegistered();

        private void Wake()
        {
            int areaIndex = ResolveAreaIndex();
            _areaIndex = areaIndex;

            // MV-995: same reasoning as BigBermudaBoss.Wake -- a cold-boot RESUME rebuilds every
            // authored boss fresh and Dormant, so an area already beaten before the checkpoint was saved
            // must not fight again. BossCensus.IsAreaDefeated is seeded from the checkpoint well before
            // this can run.
            if (BossCensus.IsAreaDefeated(areaIndex))
            {
                _phase = Phase.Dead;
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }

            _phase = Phase.Fight;
            if (!_active.Contains(this)) _active.Add(this);
            // 2 phases -> HUD bar shows the half-health segment, same as BigBermudaBoss.
            BossCensus.Register(this, BossName, 2, _health.Current, _health.Max, areaIndex);
        }

        private int ResolveAreaIndex()
        {
            MapData map = EnemyNavigation.Map;
            MapZone zone = map?.ZoneAt(transform.position.x, transform.position.z);
            if (zone == null) return 0;
            return AreaAccumulationDirector.AreaIndexOf(zone.id);
        }

        /// <summary>MV-1110: same shared wake rule as <see cref="BigBermudaBoss.TickDormant"/> — within
        /// <see cref="BossTuning.WakeRadius"/> of her own post, with clear line of sight, rather than
        /// simply entering her authored area.</summary>
        private void TickDormant()
        {
            if (_target == null) { AcquireTarget(); return; }
            if (IsWithinWakeRange(_target.position)) Wake();
        }

        /// <summary>Same test as <see cref="BigBermudaBoss.IsWithinWakeRange"/> — range AND sight
        /// together, not either alone.</summary>
        private bool IsWithinWakeRange(Vector3 targetPosition)
        {
            Vector3 to = targetPosition - transform.position;
            to.y = 0f;
            if (to.magnitude > BossTuning.WakeRadius) return false;
            return LineOfSight.Between(transform, _target);
        }

        /// <summary>The full per-frame fight tick, dt-parameterized so an EditMode test can drive the
        /// whole fight deterministically without a live Update loop — same "Tick(dt) the test can drive"
        /// contract <see cref="MaxWorlds.Enemies.RobotEnemy.Tick"/>/<see cref="MaxWorlds.Enemies.CorrosiveGlob.Tick"/>
        /// already give their own per-frame logic.</summary>
        public void Tick(float dt)
        {
            switch (_phase)
            {
                case Phase.Dormant: TickDormant(); break;
                case Phase.EnrageTell: TickEnrageTell(dt); TickFight(dt); break;
                case Phase.Fight:
                case Phase.Enrage: TickFight(dt); break;
            }
            ApplyGravity(dt);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Counts down the half-health tell (MV-1127 §9); once it elapses the faster lob/brood
        /// timings take over from the very next tick.</summary>
        private void TickEnrageTell(float dt)
        {
            _tellTimer -= dt;
            if (_tellTimer <= 0f) _phase = Phase.Enrage;
        }

        private void TickFight(float dt)
        {
            if (_target == null) { AcquireTarget(); return; }
            RetargetIfNeeded();
            Approach(dt);
            FaceTarget();
            TickContactDamage(dt);
            _brain.Tick(dt, _health.Normalized);
            BossCensus.ReportSpawnLevel(this, _brain.SpawnLevel, _brain.SpawnLevelProgress01);
            TickGlobVolley(dt);
            TickLobs(dt);
            TickBrood(dt);
        }

        private void Approach(float dt)
        {
            if (_target == null) return;
            TickApproach(dt, _target.position);
        }

        /// <summary>The actual approach-and-steer step, dt/target-parameterized so a test can drive it
        /// directly — same shape and same reasoning as <see cref="BigBermudaBoss.TickApproach"/>.
        /// MV-1083: the stop distance is no longer the fixed <see cref="SludgequeenTuning.Standoff"/>
        /// — it is <see cref="ContactReachTo"/>'s own live number (the exact reach
        /// <see cref="TickContactDamage"/> checks) minus <see cref="SludgequeenTuning.StandoffMargin"/>,
        /// same reasoning as <see cref="BigBermudaBoss.TickApproach"/>. Reaching it no longer means a
        /// dead stop either — see <see cref="DriftAtStandoff"/>, same MV-720 "a parked boss reads as a
        /// statue" rule.</summary>
        public void TickApproach(float dt, Vector3 targetPosition)
        {
            Vector3 to = targetPosition - transform.position;
            to.y = 0f;
            float stopDistance = ContactReachTo(_target) - SludgequeenTuning.StandoffMargin;

            if (to.magnitude <= stopDistance)
            {
                DriftAtStandoff(dt, to, SludgequeenTuning.MoveSpeed);
                return;
            }

            Vector3 waypoint = EnemyNavigation.Waypoint(transform.position, targetPosition,
                useZoneRoute: true, budget: _routeBudget, dt: dt);
            Vector3 routeTo = waypoint - transform.position;
            routeTo.y = 0f;
            Vector3 bearing = routeTo.sqrMagnitude > 0.0001f ? routeTo.normalized : to.normalized;

            Vector3 desired = _wallLatch.Tick(bearing, transform.position, dt, _preferSign);
            CharacterControllerMotion.SafeMove(_cc, desired * SludgequeenTuning.MoveSpeed * dt);
        }

        /// <summary>MV-1083: same circling drift as <see cref="BigBermudaBoss.DriftAtStandoff"/>, so
        /// Sludgequeen never stops dead at standoff either.</summary>
        private void DriftAtStandoff(float dt, Vector3 to, float speed)
        {
            if (to.sqrMagnitude < 0.0001f) return; // exactly on top of the target -- no ring to walk
            Vector3 inward = to.normalized;
            Vector3 tangent = new Vector3(-inward.z, 0f, inward.x) * _preferSign;

            Vector3 desired = _wallLatch.Tick(tangent, transform.position, dt, _preferSign);
            CharacterControllerMotion.SafeMove(_cc, desired * speed * dt);
        }

        private void OnControllerColliderHit(ControllerColliderHit hit) => HandleWallContact(hit.collider, hit.normal);

        /// <summary>Same wall-vs-character split as <see cref="BigBermudaBoss.HandleWallContact"/> — a
        /// character hit is not something to route around.</summary>
        private void HandleWallContact(Collider collider, Vector3 normal)
        {
            if (Mathf.Abs(normal.y) >= 0.5f) return;
            if (collider.TryGetComponent<CharacterController>(out _)) return;
            _wallLatch.NoteHit(normal);
        }

        /// <summary>MV-1083: Sludgequeen hurts on contact too — "Bosses must do damage to Max and
        /// Sentinels in every world" (Lee, 2026-09-30) is not a BigBermudaBoss-only rule. Same shape as
        /// <see cref="BigBermudaBoss.TickContactDamage"/>: rate-limited, distance-based against the
        /// boss's own WORLD-space collider radius.</summary>
        private void TickContactDamage(float dt)
        {
            _contactCooldownTimer -= dt;
            if (_contactCooldownTimer > 0f) return;

            float bossRadius = WorldRadius(_cc, transform);
            // Always Max directly, never _target -- _target may currently BE a Sentinel
            // (RetargetIfNeeded), and that Sentinel is already covered by the loop below.
            bool hitSomething = DamageIfTouching(_playerTarget, bossRadius);

            IReadOnlyList<Sentinel> sentinels = Sentinel.Active;
            for (int i = 0; i < sentinels.Count; i++)
            {
                Sentinel s = sentinels[i];
                if (DamageIfTouching(s != null ? s.transform : null, bossRadius)) hitSomething = true;
            }

            // MV-1092: a captured robot standing against the boss hurts exactly like a Sentinel does.
            IReadOnlyList<RobotEnemy> captured = RobotEnemy.Converted;
            for (int i = 0; i < captured.Count; i++)
            {
                RobotEnemy r = captured[i];
                if (DamageIfTouching(r != null ? r.transform : null, bossRadius)) hitSomething = true;
            }

            if (hitSomething) _contactCooldownTimer = SludgequeenTuning.ContactCooldown;
        }

        private bool DamageIfTouching(Transform t, float bossRadius)
        {
            if (t == null) return false;
            Vector3 to = t.position - transform.position; to.y = 0f;
            float reach = bossRadius + TargetRadius(t) + SludgequeenTuning.ContactSkin;
            if (to.magnitude > reach) return false;

            if (!t.TryGetComponent<IDamageable>(out var damageable) || !damageable.IsAlive) return false;
            Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : Vector3.forward;
            damageable.TakeDamage(new DamageInfo(SludgequeenTuning.ContactDamagePerTick, transform.position, dir, Team.Enemy));
            return true;
        }

        private static float TargetRadius(Transform t) =>
            t.TryGetComponent<CharacterController>(out var cc) ? WorldRadius(cc, t) : EnemyArchetype.PlayerRadius;

        private static float WorldRadius(CharacterController cc, Transform t)
        {
            Vector3 scale = t.lossyScale;
            return cc.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        /// <summary>MV-1083: the exact world-space contact reach <see cref="TickContactDamage"/> would
        /// check for <paramref name="target"/> right now — the SAME number <see cref="TickApproach"/>
        /// stops just inside of. Falls back to <see cref="EnemyArchetype.PlayerRadius"/> when
        /// <paramref name="target"/> is null.</summary>
        private float ContactReachTo(Transform target)
        {
            float targetRadius = target != null ? TargetRadius(target) : EnemyArchetype.PlayerRadius;
            return WorldRadius(_cc, transform) + targetRadius + SludgequeenTuning.ContactSkin;
        }

        /// <summary>MV-1083: re-decide whether to chase Max or the nearest Sentinel — same
        /// proximity-only rule as <see cref="BigBermudaBoss.RetargetIfNeeded"/> (itself mirroring
        /// <see cref="MaxWorlds.Enemies.RobotEnemy"/>'s MV-362 rule). <see cref="TickContactDamage"/> is
        /// untouched by this — it always hits <see cref="_playerTarget"/> directly plus every Sentinel
        /// in <see cref="Sentinel.Active"/>, so this only changes who the boss WALKS at, FACES and
        /// AIMS her first lob at, never who it can hurt.</summary>
        private void RetargetIfNeeded()
        {
            if (_playerTarget == null) return;

            if (_engagedSentinel != null && !_engagedSentinel.IsAlive)
            {
                _engagedSentinel = null;
                SetTarget(_playerTarget);
            }

            MapData map = EnemyNavigation.Map;
            float distToPlayer = Vector3.Distance(transform.position, _playerTarget.position);
            Sentinel nearest = SentinelTargeting.Nearest(transform.position);
            bool nearestSameLevel = nearest != null
                && CombatLevel.SameLevel(map, transform.position, nearest.transform.position);
            float distToSentinel = nearestSameLevel
                ? Vector3.Distance(transform.position, nearest.transform.position)
                : float.MaxValue;

            bool engageSentinel = nearestSameLevel &&
                SentinelTargeting.ShouldEngageSentinel(distToPlayer, distToSentinel, SentinelTargeting.AggroRadius);

            if (engageSentinel && nearest != _engagedSentinel)
            {
                _engagedSentinel = nearest;
                SetTarget(nearest.transform);
            }
            else if (!engageSentinel && _engagedSentinel != null)
            {
                _engagedSentinel = null;
                SetTarget(_playerTarget);
            }
        }

        private void SetTarget(Transform t)
        {
            _target = t;
            _targetDamageable = t != null ? t.GetComponent<IDamageable>() : null;
        }

        // ---------------------------------------------------------------- sludge lob volley (MV-1127 §4)

        private void TickGlobVolley(float dt)
        {
            _globTimer += dt;
            float interval = IsEnraged ? SludgequeenTuning.Phase2GlobInterval : SludgequeenTuning.Phase1GlobInterval;
            if (_globTimer < interval) return;
            _globTimer = 0f;

            int count = IsEnraged ? SludgequeenTuning.Phase2GlobCount : SludgequeenTuning.Phase1GlobCount;
            FireLobVolley(count);
        }

        /// <summary>Schedules <paramref name="count"/> marked lobs: one aimed at the nearest player-side
        /// body (<see cref="_target"/>), the rest scattered <see cref="SludgequeenTuning.LobScatterMin"/>-
        /// <see cref="SludgequeenTuning.LobScatterMax"/> m around it on a fixed angular spread (so they
        /// are pairwise at least that scatter's own chord apart by construction, never by retrying a
        /// random pick), each clamped onto walkable ground inside the arena. Actual launch is staggered
        /// <see cref="SludgequeenTuning.GlobLaunchStagger"/> s apart, alternating cannon mouths — see
        /// <see cref="TickLobs"/>.</summary>
        private void FireLobVolley(int count)
        {
            if (_target == null || count <= 0) return;

            Vector3 anchor = _target.position;
            Vector3[] landings = ComputeLandingSpots(anchor, count);

            for (int i = 0; i < landings.Length; i++)
            {
                _pendingLobs.Add(new PendingLob
                {
                    Delay = i * SludgequeenTuning.GlobLaunchStagger,
                    Side = i % 2,
                    LandingPoint = landings[i],
                });
            }
        }

        /// <summary><paramref name="anchor"/> itself, plus <paramref name="count"/>-1 more points evenly
        /// spread in angle around it (a random overall rotation, so the pattern isn't always the same
        /// four compass points) at a random distance in [<see cref="SludgequeenTuning.LobScatterMin"/>,
        /// <see cref="SludgequeenTuning.LobScatterMax"/>] each. Fixed angular spacing guarantees every
        /// pair of scattered points is at least <c>2 * LobScatterMin * sin(halfSpacing)</c> apart, which
        /// for 4 points 90 degrees apart and a 2 m minimum radius is ~2.83 m — comfortably clear of the
        /// ticket's own "at least 2 m apart" without ever needing a reject-and-retry loop (which an
        /// EditMode test could not then assert on deterministically).</summary>
        private Vector3[] ComputeLandingSpots(Vector3 anchor, int count)
        {
            var spots = new Vector3[count];
            spots[0] = ClampToArena(anchor);

            int scatterCount = count - 1;
            if (scatterCount <= 0) return spots;

            float baseAngle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float step = (360f / scatterCount) * Mathf.Deg2Rad;

            for (int i = 0; i < scatterCount; i++)
            {
                float angle = baseAngle + i * step;
                float dist = UnityEngine.Random.Range(SludgequeenTuning.LobScatterMin, SludgequeenTuning.LobScatterMax);
                Vector3 candidate = anchor + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                spots[i + 1] = ClampToArena(candidate);
            }

            return spots;
        }

        /// <summary>Pulls <paramref name="point"/>'s XZ back inside <see cref="_arenaBounds"/> by the
        /// splash radius from every wall, and snaps Y onto the real walkable surface — same
        /// pull-back-from-the-edge shape <see cref="BigBermudaBoss"/>'s own <c>ClampLandingToArea</c>
        /// uses for its brood volley.</summary>
        private Vector3 ClampToArena(Vector3 point)
        {
            float margin = SludgequeenTuning.GlobSplashRadius;
            MapData map = EnemyNavigation.Map;
            Vector3 resolved = map != null ? map.SnapToWalkableSurface(transform.position, point, margin) : point;

            if (_arenaBounds.width <= 0f || _arenaBounds.height <= 0f) return resolved;

            float minX = _arenaBounds.xMin + margin, maxX = _arenaBounds.xMax - margin;
            float minZ = _arenaBounds.yMin + margin, maxZ = _arenaBounds.yMax - margin;
            if (minX > maxX) minX = maxX = _arenaBounds.center.x;
            if (minZ > maxZ) minZ = maxZ = _arenaBounds.center.y;

            return new Vector3(Mathf.Clamp(resolved.x, minX, maxX), resolved.y, Mathf.Clamp(resolved.z, minZ, maxZ));
        }

        /// <summary>Advances every scheduled lob launch and every live landing ring, plus the between-
        /// volleys cannon dribble (MV-1127 §4's own dressing beat).</summary>
        private void TickLobs(float dt)
        {
            for (int i = _pendingLobs.Count - 1; i >= 0; i--)
            {
                PendingLob p = _pendingLobs[i];
                p.Delay -= dt;
                if (p.Delay <= 0f)
                {
                    LaunchLob(p.Side, p.LandingPoint);
                    _pendingLobs.RemoveAt(i);
                }
                else
                {
                    _pendingLobs[i] = p;
                }
            }

            for (int i = _activeRings.Count - 1; i >= 0; i--)
            {
                ActiveRing r = _activeRings[i];
                r.Remaining -= dt;
                if (r.Remaining <= 0f)
                {
                    r.Ring.Hide();
                    _ringPool.Push(r.Ring);
                    _activeRings.RemoveAt(i);
                }
                else
                {
                    _activeRings[i] = r;
                }
            }

            // Dressing only, no damage: one droplet falling from each mouth between volleys (never
            // while lobs are actively mid-flight -- see the ticket's own "between volleys").
            if (_pendingLobs.Count == 0 && _activeRings.Count == 0)
            {
                _dribbleTimer += dt;
                if (_dribbleTimer >= SludgequeenTuning.DribbleInterval)
                {
                    _dribbleTimer = 0f;
                    DribbleCannons();
                }
            }

            for (int i = _dribbleFlashes.Count - 1; i >= 0; i--)
            {
                ActiveRing r = _dribbleFlashes[i];
                r.Remaining -= dt;
                if (r.Remaining <= 0f)
                {
                    r.Ring.Hide();
                    _ringPool.Push(r.Ring);
                    _dribbleFlashes.RemoveAt(i);
                }
                else
                {
                    _dribbleFlashes[i] = r;
                }
            }
        }

        /// <summary>Fires one glob from cannon <paramref name="side"/>'s own mouth transform at
        /// <paramref name="landingPoint"/>, and shows the yellow landing ring there for the lob's whole
        /// flight (MV-1127 §4). Speed is derived from distance/<see cref="SludgequeenTuning.GlobFlightTime"/>
        /// rather than a fixed speed, so every lob's own flight time is the ticket's authored minimum
        /// regardless of how far it has to travel.</summary>
        private void LaunchLob(int side, Vector3 landingPoint)
        {
            Transform mouth = _cannonMouths[side] != null ? _cannonMouths[side] : transform;
            Vector3 origin = mouth.position;

            GroundRing ring = _ringPool.Count > 0 ? _ringPool.Pop() : GroundRing.Create("Sludgequeen Lob Ring");
            ring.Show(new Vector3(landingPoint.x, 0f, landingPoint.z), SludgequeenTuning.GlobSplashRadius, LobRingColor);
            _activeRings.Add(new ActiveRing { Ring = ring, Remaining = SludgequeenTuning.GlobFlightTime });

            float distance = Mathf.Max(0.01f, Vector3.Distance(origin, landingPoint));
            float speed = distance / SludgequeenTuning.GlobFlightTime;

            CorrosiveGlob.FireAt(origin, landingPoint, speed, SludgequeenTuning.GlobDamage,
                SludgequeenTuning.GlobSplashRadius, SludgequeenTuning.GlobPuddleRadius, SludgequeenTuning.GlobPuddleDuration,
                affectsRobots: false, blobDiameter: SludgequeenTuning.GlobVisualDiameter);
        }

        /// <summary>Dressing-only droplets falling from each cannon mouth between volleys (MV-1127 §4) —
        /// a brief ring flash at the floor under each mouth, no damage, no puddle.</summary>
        private void DribbleCannons()
        {
            for (int side = 0; side < 2; side++)
            {
                Transform mouth = _cannonMouths[side];
                if (mouth == null) continue;

                GroundRing ring = _ringPool.Count > 0 ? _ringPool.Pop() : GroundRing.Create("Sludgequeen Dribble");
                ring.Show(new Vector3(mouth.position.x, 0f, mouth.position.z), DribbleFlashRadius, LobRingColor);
                _dribbleFlashes.Add(new ActiveRing { Ring = ring, Remaining = DribbleFlashLife });
            }
        }

        // ---------------------------------------------------------------- sludgers (MV-1127 §5)

        private void TickBrood(float dt)
        {
            if (_broodTelling)
            {
                _broodTellTimer -= dt;
                if (_broodTellTimer <= 0f) { _broodTelling = false; SpawnBrood(); }
                return;
            }

            _broodTimer += dt;
            float interval = IsEnraged ? SludgequeenTuning.Phase2BroodInterval : SludgequeenTuning.Phase1BroodInterval;
            if (_broodTimer < interval) return;

            _broodTimer = 0f;
            _broodTelling = true;
            _broodTellTimer = SludgequeenTuning.BroodTellTime;
        }

        /// <summary>Releases up to <see cref="SludgequeenTuning.BroodCount"/> sludgers down the chutes,
        /// alternating sides, never taking her own live count past
        /// <see cref="SludgequeenTuning.MaxConcurrentBrood"/> (MV-1127 §5 — counted fresh every wave,
        /// never a decrement-only field, same rule MV-1085 gave Big Bermuda's volley). Composition draws
        /// from <see cref="SludgequeenBroodLevels"/> at her current <see cref="SpawnLevel"/>.</summary>
        private void SpawnBrood()
        {
            int alive = CountLiveBroodInArea();
            int want = Mathf.Min(SludgequeenTuning.BroodCount, Mathf.Max(0, SludgequeenTuning.MaxConcurrentBrood - alive));
            if (want <= 0) return;

            Vector3 pos = transform.position;
            Quaternion facing = transform.rotation;
            EnemyKind[] kinds = SludgequeenBroodLevels.KindsFor(_brain.SpawnLevel);
            // MV-1127 §6: "level 4 adds one brute per group" -- guaranteed, not a 1-in-N draw, so it is
            // handled here rather than by adding Brute to SludgequeenBroodLevels' own pool.
            bool guaranteeBrute = _brain.SpawnLevel >= BossTuning.MaxSpawnLevel;

            for (int i = 0; i < want; i++)
            {
                float side = (i % 2 == 0) ? -1f : 1f;
                EnemyKind kind = (guaranteeBrute && i == 0) ? EnemyKind.Brute : kinds[UnityEngine.Random.Range(0, kinds.Length)];
                EnemyArchetype archetype = EnemyArchetype.Of(kind);

                Transform chuteFoot = _chuteFeet[i % 2];
                Vector3 landing = chuteFoot != null
                    ? chuteFoot.position
                    : BroodArc.Landing(pos, facing, side, SludgequeenTuning.HatchSide,
                        SludgequeenTuning.HatchLandingForward, archetype.SpawnHeight, 0f);

                RobotEnemy add = CreateSludger(archetype);
                add.transform.position = landing;
                add.transform.rotation = facing;
                add.TagNoReplicatePermanent(); // MV-706: a boss-flung robot may never be lured into a Replicator
                add.SetAreaIndex(_areaIndex);  // same MV-1122 reasoning as BigBermudaBoss.LaunchVolley

                var cc = add.GetComponent<CharacterController>();
                if (!CharacterControllerSafety.CanCreate(add.transform, cc, out string spawnReason))
                {
                    CharacterControllerSafety.LogRefusal("SludgequeenBoss.SpawnBrood", add.gameObject.name,
                        spawnReason, landing, add.transform.lossyScale);
                    Destroy(add.gameObject);
                    continue;
                }

                add.gameObject.SetActive(true);
            }
        }

        /// <summary>MV-1127 §5: this boss's own sludgers currently alive — counted fresh off
        /// <see cref="_broodRoot"/> every wave, never carried in a decrement-only field, so one lost by
        /// any route (killed, captured, whatever) frees its place the instant it stops being alive.</summary>
        private int CountLiveBroodInArea()
        {
            if (_broodRoot == null) return 0;

            int count = 0;
            for (int i = 0; i < _broodRoot.childCount; i++)
            {
                Transform child = _broodRoot.GetChild(i);
                if (child == null || !child.gameObject.activeInHierarchy) continue;
                if (!child.TryGetComponent<RobotEnemy>(out RobotEnemy robot) || !robot.enabled || !robot.IsAlive) continue;
                count++;
            }
            return count;
        }

        private RobotEnemy CreateSludger(in EnemyArchetype a)
        {
            var go = GameObject.CreatePrimitive(a.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            go.name = $"Brood Sludger {a.Kind}";
            go.transform.SetParent(BroodRoot(), false);
            go.transform.localScale = a.BodyScale;

            go.SetActive(false);
            if (!CharacterControllerSafety.CanCreate(go.transform, null, out string createReason))
            {
                CharacterControllerSafety.LogRefusal("SludgequeenBoss.CreateSludger", go.name,
                    createReason, go.transform.position, go.transform.lossyScale);
            }

            var cc = go.AddComponent<CharacterController>();
            float lateral = Mathf.Max(a.BodyScale.x, a.BodyScale.z);
            cc.height = a.ColliderHeight / Mathf.Max(a.BodyScale.y, 1e-4f);
            cc.radius = a.ColliderRadius / Mathf.Max(lateral, 1e-4f);
            cc.center = Vector3.zero;

            var e = go.AddComponent<RobotEnemy>();
            e.Apply(a);
            go.AddComponent<RobotRig>();
            e.gameObject.SetActive(false);
            return e;
        }

        /// <summary>Top-level and unit-scaled, same reasoning as <see cref="BigBermudaBoss.AddsRoot"/> —
        /// the boss moves, and adds parented under it would be dragged across the arena.</summary>
        private Transform BroodRoot()
        {
            if (_broodRoot == null) _broodRoot = new GameObject("Sludgequeen Brood").transform;
            return _broodRoot;
        }

        private void OnDestroy()
        {
            if (_broodRoot != null) Destroy(_broodRoot.gameObject);
            foreach (ActiveRing r in _activeRings) if (r.Ring != null) Destroy(r.Ring.gameObject);
            foreach (ActiveRing r in _dribbleFlashes) if (r.Ring != null) Destroy(r.Ring.gameObject);
            foreach (GroundRing r in _ringPool) if (r != null) Destroy(r.gameObject);
            _active.Remove(this);
            BossCensus.Forget(this);
        }

        // ---------------------------------------------------------------- IDamageable

        public void TakeDamage(in DamageInfo info)
        {
            // MV-1110: same "a hit landing is itself a wake trigger" rule as BigBermudaBoss.TakeDamage —
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

            if (_phase == Phase.Fight && _health.Normalized <= SludgequeenTuning.PhaseTwoThreshold)
            {
                _phase = Phase.EnrageTell;
                _tellTimer = SludgequeenTuning.PhaseTwoTellTime;
            }
        }

        private void OnDeath()
        {
            _phase = Phase.Dead;
            _active.Remove(this);
            BossCensus.ReportDefeated(this);
            gameObject.SetActive(false);
        }

        private void AcquireTarget()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return;
            _playerTarget = p.transform;
            SetTarget(p.transform);
        }

        private Vector3 PlanarToTarget()
        {
            if (_target == null) return Vector3.zero;
            Vector3 to = _target.position - transform.position;
            to.y = 0f;
            return to;
        }

        private void FaceTarget()
        {
            Vector3 to = PlanarToTarget();
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
