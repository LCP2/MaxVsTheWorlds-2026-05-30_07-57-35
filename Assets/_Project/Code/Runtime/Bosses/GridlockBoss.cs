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
    /// GRIDLOCK — The City's boss (MV-1299), "The Core": a six-sided drum of rack lights on six
    /// needle legs. Standalone <see cref="IDamageable"/>, modelled on <see cref="SludgequeenBoss"/>
    /// for the walk/standoff/touch shape — one phase, no charge/ram (the ticket's own "no charge or
    /// ram" is absolute, unlike <see cref="BigBermudaBoss"/>'s retired one). MV-1306 adds her three
    /// attacks (SQUAD/JUMP/STRIKE, cadence owned by <see cref="GridlockAttackClock"/>) and her
    /// escalation clock, read by the roof dial's clock ring. The arena, shield and finale are
    /// MV-1309's and still don't exist, so there is deliberately no phase machine here for them to
    /// slot into later beyond the one switch (Dormant -&gt; Fight -&gt; Dead) every boss already has.
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

        // This boss's own authored area (MV-1306 now also reads it — see ClampToOwnArea — same
        // convention as every other boss's _wakeArea, parity with
        // SludgequeenBoss/BigBermudaBoss/HelmsmanBoss's own SetWakeArea).
        private Rect _wakeArea;
        private int _areaIndex;

        // --- attacks (MV-1306): SQUAD/JUMP/STRIKE cadence + escalation, pure-ticked by
        // GridlockAttackClock; this class executes the actual spawn/teleport/damage on each edge. ---
        private readonly GridlockAttackClock _attacks = new GridlockAttackClock();
        private int _lastAnnouncedSpawnLevel = 1;

        private struct PendingBeam { public Vector3 Position; public EnemyKind Kind; public float Timer; }
        private readonly List<PendingBeam> _pendingBeams = new List<PendingBeam>(4);
        private Transform _addsRoot; // top-level, unit-scaled -- same "never dragged by the moving boss" reasoning as BigBermudaBoss.AddsRoot
        private readonly Dictionary<EnemyKind, Stack<RobotEnemy>> _addPools = new Dictionary<EnemyKind, Stack<RobotEnemy>>();
        private Collider[] _playerColliders;

        private bool _jumpWarningWasActive;
        private Vector3 _jumpDestination;

        private struct StrikeCircle { public Vector3 Center; public float Timer; public bool DamageApplied; }
        private readonly List<StrikeCircle> _strikeCircles = new List<StrikeCircle>(GridlockTuning.StrikeCircleCount);

        public int SquadFiredCount { get; private set; }
        public int JumpFiredCount { get; private set; }
        public int StrikeFiredCount { get; private set; }

        /// <summary>How many of her own robots are currently alive — the ticket's own "at most 10 of
        /// her robots alive" cap, enforced in <see cref="FireSquad"/>/<see cref="AdvancePendingBeams"/>.</summary>
        public int AliveRobotCount => CountAliveRobots();

        /// <summary>True on her own dial while SQUAD's 1 s warning is showing — read by
        /// <see cref="MaxWorlds.VFX.GridlockRig"/> to show the three-chevron tell.</summary>
        public bool SquadWarningActive => _attacks.SquadWarningActive;

        /// <summary>True on her own dial while JUMP's 1 s warning (and the landing-spot ring) is
        /// showing — read by <see cref="MaxWorlds.VFX.GridlockRig"/> for the double-arrow tell.</summary>
        public bool JumpWarningActive => _attacks.JumpWarningActive;

        /// <summary>True while any STRIKE circle is still arming (harmless) — read by
        /// <see cref="MaxWorlds.VFX.GridlockRig"/> for the crosshair tell. STRIKE has no separate
        /// pre-telegraph of its own; the circles' own 1.2 s arming window IS the tell.</summary>
        public bool StrikeArming => HasArmingStrikeCircle();

        /// <summary>0..1 toward whichever attack is closest to firing — <see cref="MaxWorlds.VFX.GridlockRig"/>
        /// fills the roof dial's clock ring toward this.</summary>
        public float NextAttackProgress01 => _attacks.NextAttackProgress01;

        /// <summary>The ticket's own "at half health, her lights run twice as fast" — read by
        /// <see cref="MaxWorlds.VFX.GridlockRig"/> to speed up the spinning crystal core.</summary>
        public float LightSpeedScale => IsHalfHealth ? GridlockTuning.HalfHealthLightScale : 1f;

        private bool IsHalfHealth => _health != null && _health.Normalized <= GridlockTuning.HalfHealthThreshold;

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
            // One phase (Dormant -> Fight -> Dead, no Intro/enrage split) -> the HUD name-card bar
            // shows a single segment. Her escalation (MV-1306) drives the SEPARATE spawn-level bar,
            // reported every fight tick via BossCensus.ReportSpawnLevel in TickAttacks below.
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
            TickAttacks(dt);
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

        // ---------------------------------------------------------------- attacks (MV-1306)

        private void TickAttacks(float dt)
        {
            bool halfHealth = IsHalfHealth;
            float timerScale = halfHealth ? GridlockTuning.HalfHealthTimerScale : 1f;
            _attacks.Tick(dt, halfHealth);

            if (_attacks.SquadJustFired) FireSquad();

            // The destination is resolved ONCE, on the warning's rising edge, so the dashed ring and
            // the eventual jump land on the exact same point (MV-1306's own "marks a spot ... for
            // 1 s; she ... reassembles there").
            if (_attacks.JumpWarningActive && !_jumpWarningWasActive) _jumpDestination = ResolveJumpDestination();
            _jumpWarningWasActive = _attacks.JumpWarningActive;
            if (_attacks.JumpJustFired) FireJump();

            if (_attacks.StrikeJustFired) FireStrike();

            AdvancePendingBeams(dt);
            AdvanceStrikeCircles(dt, timerScale);
            TickEscalation();
        }

        private void TickEscalation()
        {
            BossCensus.ReportSpawnLevel(this, _attacks.SpawnLevel, _attacks.SpawnLevelProgress01);
            if (_attacks.SpawnLevel <= _lastAnnouncedSpawnLevel) return;
            _lastAnnouncedSpawnLevel = _attacks.SpawnLevel;

            // The ticket's own singular "L3 adds ... one Jammer" / "L4 adds a Brute" — a one-time add
            // the instant the level is first reached, not an ongoing GridlockSpawnLevels pool member
            // (see that class's own doc comment).
            if (_attacks.SpawnLevel == 3) QueueSpecialAdd(EnemyKind.Jammer);
            else if (_attacks.SpawnLevel == 4) QueueSpecialAdd(EnemyKind.Brute);
        }

        private void QueueSpecialAdd(EnemyKind kind)
        {
            if (CountAliveRobots() + _pendingBeams.Count >= GridlockTuning.MaxGridlockRobots) return;
            Vector3 raw = transform.position + AngleDirFlat(Random.Range(0f, 360f)) * GridlockTuning.SquadLandRadius;
            Vector3 landing = ClampToOwnArea(raw, EnemyArchetype.Of(kind).ColliderRadius);
            _pendingBeams.Add(new PendingBeam { Position = landing, Kind = kind, Timer = 0f });
        }

        // --- SQUAD: a 1 s warning (GridlockAttackClock), then 3 robots beam in round her, 0.2 s apart,
        // through the shared landing clamp (ResolveLanding/ClampToOwnArea below). ---

        private void FireSquad()
        {
            SquadFiredCount++;

            int room = Mathf.Max(0, GridlockTuning.MaxGridlockRobots - CountAliveRobots() - _pendingBeams.Count);
            int spawnCount = Mathf.Min(GridlockTuning.SquadRobotCount, room);
            if (spawnCount <= 0) return;

            EnemyKind[] pool = GridlockSpawnLevels.KindsFor(_attacks.SpawnLevel);
            float startAngle = Random.Range(0f, 360f);

            for (int i = 0; i < spawnCount; i++)
            {
                float angle = startAngle + i * (360f / GridlockTuning.SquadRobotCount);
                EnemyKind kind = pool[Random.Range(0, pool.Length)];
                Vector3 raw = transform.position + AngleDirFlat(angle) * GridlockTuning.SquadLandRadius;
                Vector3 landing = ClampToOwnArea(raw, EnemyArchetype.Of(kind).ColliderRadius);
                _pendingBeams.Add(new PendingBeam { Position = landing, Kind = kind, Timer = i * GridlockTuning.SquadBeamStagger });
            }
        }

        private void AdvancePendingBeams(float dt)
        {
            for (int i = _pendingBeams.Count - 1; i >= 0; i--)
            {
                PendingBeam beam = _pendingBeams[i];
                beam.Timer -= dt;
                if (beam.Timer > 0f) { _pendingBeams[i] = beam; continue; }

                _pendingBeams.RemoveAt(i);
                if (CountAliveRobots() >= GridlockTuning.MaxGridlockRobots) continue; // re-checked at spawn time -- the cap must never be exceeded
                SpawnRobotAt(beam.Position, beam.Kind);
            }
        }

        // --- JUMP: a dashed ring marks the resolved spot for 1 s (GridlockAttackClock's warning),
        // then she teleports there through the same shared landing clamp. ---

        private const int JumpLandingAttempts = 8;

        private Vector3 ResolveJumpDestination()
        {
            Vector3 selfPos = transform.position;
            Vector3 maxPos = _target != null ? _target.position : selfPos + Vector3.forward * GridlockTuning.JumpMinDistanceFromSelf;
            float bodyRadius = WorldRadius(_cc, transform);

            for (int attempt = 0; attempt < JumpLandingAttempts; attempt++)
            {
                float angle = Random.Range(0f, 360f);
                float dist = GridlockTuning.JumpMinDistanceFromSelf + attempt * 1.5f;
                Vector3 candidate = selfPos + AngleDirFlat(angle) * dist;
                Vector3 clamped = ClampToOwnArea(candidate, bodyRadius);

                Vector3 toMax = clamped - maxPos; toMax.y = 0f;
                if (toMax.magnitude >= GridlockTuning.JumpMinDistanceFromMax) return clamped;
            }

            // Fallback: straight away from Max at the minimum self-distance, still through the clamp.
            Vector3 away = selfPos - maxPos; away.y = 0f;
            Vector3 dir = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;
            return ClampToOwnArea(selfPos + dir * GridlockTuning.JumpMinDistanceFromSelf, bodyRadius);
        }

        private void FireJump()
        {
            JumpFiredCount++;
            Vector3 destination = _jumpDestination;
            destination.y = transform.position.y;
            CharacterControllerSafety.SafeReposition(_cc, destination, "GridlockBoss.FireJump");
        }

        // --- STRIKE: four circles near Max arm for 1.2 s (harmless), then go live for 0.4 s, dealing
        // 30 damage once on that transition to Max/Sentinels still standing inside. No separate
        // pre-telegraph of its own -- the circles' own arming window IS the tell (StrikeArming). ---

        private void FireStrike()
        {
            StrikeFiredCount++;
            if (_target == null) return;

            Vector3 center = _target.position;
            for (int i = 0; i < GridlockTuning.StrikeCircleCount; i++)
            {
                Vector2 offset = Random.insideUnitCircle * GridlockTuning.StrikeSpreadRadius;
                Vector3 pos = new Vector3(center.x + offset.x, center.y, center.z + offset.y);
                _strikeCircles.Add(new StrikeCircle { Center = pos, Timer = 0f, DamageApplied = false });
            }
        }

        private void AdvanceStrikeCircles(float dt, float timerScale)
        {
            float armDelay = GridlockTuning.StrikeArmDelay * timerScale;
            float lifetime = armDelay + GridlockTuning.StrikeStayDuration * timerScale;

            for (int i = _strikeCircles.Count - 1; i >= 0; i--)
            {
                StrikeCircle c = _strikeCircles[i];
                float prevTimer = c.Timer;
                c.Timer += dt;

                if (!c.DamageApplied && prevTimer < armDelay && c.Timer >= armDelay)
                {
                    ApplyStrikeDamage(c.Center);
                    c.DamageApplied = true;
                }

                if (c.Timer >= lifetime) { _strikeCircles.RemoveAt(i); continue; }
                _strikeCircles[i] = c;
            }
        }

        private bool HasArmingStrikeCircle()
        {
            for (int i = 0; i < _strikeCircles.Count; i++)
                if (!_strikeCircles[i].DamageApplied) return true;
            return false;
        }

        private void ApplyStrikeDamage(Vector3 center)
        {
            DamageStrikeIfTouching(_target, center);

            IReadOnlyList<Sentinel> sentinels = Sentinel.Active;
            for (int i = 0; i < sentinels.Count; i++)
            {
                Sentinel s = sentinels[i];
                DamageStrikeIfTouching(s != null ? s.transform : null, center);
            }
        }

        private void DamageStrikeIfTouching(Transform t, Vector3 center)
        {
            if (t == null) return;
            Vector3 to = t.position - center; to.y = 0f;
            if (to.magnitude > GridlockTuning.StrikeCircleRadius + TargetRadius(t)) return;

            if (!t.TryGetComponent<IDamageable>(out var damageable) || !damageable.IsAlive) return;
            Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : Vector3.forward;
            damageable.TakeDamage(new DamageInfo(GridlockTuning.StrikeDamage, center, dir, Team.Enemy));
        }

        // --- the shared landing clamp (MV-1306's own phrase): every spot SQUAD/JUMP/the one-time
        // escalation adds resolve to goes through the same snap-to-walkable-surface + clamp-into-her-
        // own-area shape BigBermudaBoss.ClampLandingToArea already established for a flung add. ---

        private Vector3 ClampToOwnArea(Vector3 point, float bodyRadius)
        {
            MapData map = EnemyNavigation.Map;
            Vector3 resolved = map != null ? map.SnapToWalkableSurface(transform.position, point, bodyRadius) : point;

            if (_wakeArea.width <= 0f || _wakeArea.height <= 0f) return resolved;

            float minX = _wakeArea.xMin + bodyRadius, maxX = _wakeArea.xMax - bodyRadius;
            float minZ = _wakeArea.yMin + bodyRadius, maxZ = _wakeArea.yMax - bodyRadius;
            if (minX > maxX) minX = maxX = _wakeArea.center.x;
            if (minZ > maxZ) minZ = maxZ = _wakeArea.center.y;

            return new Vector3(Mathf.Clamp(resolved.x, minX, maxX), resolved.y, Mathf.Clamp(resolved.z, minZ, maxZ));
        }

        private static Vector3 AngleDirFlat(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        // --- robot spawn/pool plumbing -- same shape as BigBermudaBoss.TakeAdd/CreateAdd/OnAddDied,
        // minus the flight arc: a beamed-in SQUAD add already stands at its landing spot the instant
        // it activates, it never flies there. ---

        private RobotEnemy SpawnRobotAt(Vector3 position, EnemyKind kind)
        {
            EnemyArchetype archetype = EnemyArchetype.Of(kind);
            RobotEnemy robot = TakeAdd(archetype);
            robot.TagNoReplicatePermanent(); // MV-706: a boss-flung/beamed robot may never be lured into a Replicator
            robot.SetAreaIndex(_areaIndex);
            robot.transform.position = position;

            Vector3 facing = transform.position - position; facing.y = 0f;
            robot.transform.rotation = facing.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(facing.normalized, Vector3.up)
                : Quaternion.identity;

            var cc = robot.GetComponent<CharacterController>();
            if (!CharacterControllerSafety.CanCreate(robot.transform, cc, out string reason))
            {
                CharacterControllerSafety.LogRefusal("GridlockBoss.SpawnRobotAt", robot.gameObject.name,
                    reason, position, robot.transform.lossyScale);
                ReturnToPool(robot);
                return null;
            }

            robot.gameObject.SetActive(true); // OnEnable -> ResetState -> Chase, full health, acquires Max
            LetThePlayerThrough(robot.gameObject);
            return robot;
        }

        private RobotEnemy TakeAdd(in EnemyArchetype archetype)
        {
            if (_addPools.TryGetValue(archetype.Kind, out Stack<RobotEnemy> pool) && pool.Count > 0)
                return pool.Pop();
            return CreateAdd(archetype);
        }

        private RobotEnemy CreateAdd(in EnemyArchetype a)
        {
            var go = GameObject.CreatePrimitive(a.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            go.name = $"Gridlock Add {a.Kind}";
            go.transform.SetParent(AddsRoot(), false);
            go.transform.localScale = a.BodyScale;

            // MV-1021: deactivate before AddComponent<CharacterController> -- an active GameObject
            // creates the native PhysX controller synchronously (same trap BigBermudaBoss.CreateAdd
            // guards against).
            go.SetActive(false);
            if (!CharacterControllerSafety.CanCreate(go.transform, null, out string createReason))
            {
                CharacterControllerSafety.LogRefusal("GridlockBoss.CreateAdd", go.name,
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
            e.Died += OnAddDied;
            go.SetActive(false);
            return e;
        }

        private void OnAddDied(RobotEnemy e) => ReturnToPool(e);

        private void ReturnToPool(RobotEnemy e)
        {
            if (!_addPools.TryGetValue(e.Kind, out Stack<RobotEnemy> pool))
                _addPools[e.Kind] = pool = new Stack<RobotEnemy>(4);
            pool.Push(e);
        }

        private Transform AddsRoot()
        {
            if (_addsRoot == null) _addsRoot = new GameObject("Gridlock Adds").transform;
            return _addsRoot;
        }

        private void LetThePlayerThrough(GameObject enemy)
        {
            if (_playerColliders == null || _playerColliders.Length == 0)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p == null) return;
                _playerColliders = p.GetComponents<Collider>();
            }

            foreach (var ec in enemy.GetComponents<Collider>())
            {
                if (ec == null) continue;
                foreach (var pc in _playerColliders)
                    if (pc != null) Physics.IgnoreCollision(ec, pc, true);
            }
        }

        private int CountAliveRobots()
        {
            if (_addsRoot == null) return 0;

            int count = 0;
            for (int i = 0; i < _addsRoot.childCount; i++)
            {
                Transform child = _addsRoot.GetChild(i);
                if (child == null || !child.gameObject.activeInHierarchy) continue;
                if (!child.TryGetComponent<RobotEnemy>(out RobotEnemy robot) || !robot.IsAlive) continue;
                count++;
            }
            return count;
        }

        private void OnDestroy()
        {
            if (_addsRoot != null) Destroy(_addsRoot.gameObject);
            BossCensus.Forget(this);
        }

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
