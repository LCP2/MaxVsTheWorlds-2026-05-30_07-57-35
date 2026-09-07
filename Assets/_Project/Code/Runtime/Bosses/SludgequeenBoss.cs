using System;
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
    /// Sludgequeen — the Wet Well boss (MV-696): a colossal sewage-pumping rig standing in a 44x44 arena
    /// whose floor she floods. Modelled on <see cref="BigBermudaBoss"/> — slow walker, standoff, zero
    /// contact damage, every attack a volley — but her signature is the FLOOR, not an add swarm: at
    /// 100-50% HP the south half of the well is ankle-deep in ooze (slow + damage); below 50%, after a
    /// 3 s tell, the whole floor floods and only the map-authored deck islands (and the centre block)
    /// stay dry. <see cref="FloodRect"/>/<see cref="IsDry"/> are resolved values — an EditMode test
    /// drives the phase transition directly (no scene, no Update loop) and reads them straight off, the
    /// same "public getters, private state machine" shape <see cref="BigBermudaBoss"/> already uses for
    /// its own fight-reading surface.
    ///
    /// Out of this ticket's slice (BUILD MODE: INDICATIVE) and left for a follow-up once the arena
    /// itself is authored (world2_config.json's a23 is still MV-700's stub, explicitly "not a design
    /// pass" — see its own note): actually placing this boss in <c>bosses[]</c> (a <c>WorldBoss.kind</c>
    /// dispatch in <see cref="MapRuntime"/>), and a <see cref="BossVictoryPayoff"/>-equivalent finale
    /// beat (today's is hard-wired to <c>BackyardPath</c>'s own Backyard geometry). This class is
    /// fully playable dropped into a scene by hand in the meantime, and shares the HUD boss bar and
    /// <see cref="BossCensus"/> death bookkeeping with Big Bermuda today (MV-696 generalized
    /// <see cref="BossCensus"/> off the concrete <c>BigBermudaBoss</c> type to make that possible).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class SludgequeenBoss : MonoBehaviour, IDamageable
    {
        private enum Phase { Dormant, Fight, PhaseTwoTell, PhaseTwo, Dead }

        private const string BossName = "SLUDGEQUEEN";
        private const float Gravity = 20f;

        private Phase _phase = Phase.Dormant;
        private DestructibleHealth _health;
        private CharacterController _cc;
        private Transform _target;
        private IDamageable _targetDamageable;

        // Same wall/route handling as BigBermudaBoss (MV-590/MV-667) — a boss this size still has to
        // navigate the arena's cover, not beeline through it.
        private readonly WallLatch _wallLatch = new WallLatch();
        private float _preferSign;
        private readonly ZoneRouteBudget _routeBudget = new ZoneRouteBudget();

        private Rect _wakeArea;
        private Rect _arenaBounds;
        private Rect[] _dryZones = Array.Empty<Rect>();

        private float _verticalVel;
        private float _tellTimer;

        private float _globTimer;
        private float _broodTimer;
        private float _broodTellTimer;
        private bool _broodTelling;
        private Transform _broodRoot;

        // Registered while actively flooding, so MapSlowZones can slow a mover standing in the flood
        // without needing a live reference to this specific boss (same shape as SludgePuddle._active).
        private static readonly List<SludgequeenBoss> _active = new List<SludgequeenBoss>(2);

        public bool IsAlive => _phase != Phase.Dead && _health != null && _health.IsAlive;
        public Team Team => Team.Enemy;

        /// <summary>True from wake until death — same meaning as <see cref="BigBermudaBoss.Engaged"/>.</summary>
        public bool Engaged => _phase == Phase.Fight || _phase == Phase.PhaseTwoTell || _phase == Phase.PhaseTwo;

        public bool IsDead => _phase == Phase.Dead;

        /// <summary>True once the full-floor flood has actually landed (post-tell).</summary>
        public bool IsPhaseTwo => _phase == Phase.PhaseTwo;

        /// <summary>True during the brood's 1 s hatch-open tell — the ONE getter
        /// <see cref="MaxWorlds.VFX.SludgequeenRig"/> reads to spin the valve wheel up for the attack
        /// tell, same read-gameplay-write-nothing seam <see cref="BigBermudaBoss.SpawnWindup01"/> uses.</summary>
        public bool IsVenting => _broodTelling;

        /// <summary>Hands this boss its own authoring area's floor, same convention as
        /// <see cref="BigBermudaBoss.SetWakeArea"/>.</summary>
        public void SetWakeArea(Rect area) => _wakeArea = area;

        /// <summary>The arena's own world-space X/Z extent — what <see cref="FloodRect"/> is computed
        /// against. <c>bounds.y</c>/<c>bounds.height</c> map to world Z, same "Rect over the XZ plane"
        /// convention <see cref="_wakeArea"/> already uses.</summary>
        public void SetArenaBounds(Rect bounds) => _arenaBounds = bounds;

        /// <summary>The map-authored deck islands + centre block (MV-692) — ground that stays dry no
        /// matter how much of the floor has flooded.</summary>
        public void SetDryZones(Rect[] dryZones) => _dryZones = dryZones ?? Array.Empty<Rect>();

        /// <summary>
        /// The flood's current outer extent (MV-696 §3-4): the south half of <see cref="_arenaBounds"/>
        /// while above the phase-2 threshold (or still ticking down the tell), the whole floor once
        /// phase 2 has actually landed. This is the OUTER extent only — <see cref="IsDry"/> is what
        /// decides whether a given point is actually wet, since the deck islands stay dry throughout.
        /// </summary>
        public Rect FloodRect => _phase == Phase.PhaseTwo || _phase == Phase.Dead
            ? _arenaBounds
            : SouthHalf(_arenaBounds);

        private static Rect SouthHalf(Rect bounds)
        {
            float half = bounds.height * 0.5f;
            return new Rect(bounds.xMin, bounds.yMin, bounds.width, half);
        }

        /// <summary>True if <paramref name="point"/> (world X/Z) is dry ground: inside an authored dry
        /// zone regardless of the flood, or simply outside <see cref="FloodRect"/> (the ticket's own
        /// "dry ground: the north half + all islands" in phase 1).</summary>
        public bool IsDry(Vector3 point)
        {
            var p = new Vector2(point.x, point.z);
            for (int i = 0; i < _dryZones.Length; i++)
                if (_dryZones[i].Contains(p)) return true;
            return !FloodRect.Contains(p);
        }

        /// <summary>Apply the flood's damage-over-time to <paramref name="receiver"/> standing at
        /// <paramref name="position"/>, scaled by <paramref name="dt"/> so the total over any span sums
        /// exactly to <see cref="SludgequeenTuning.FloodDamagePerSecond"/> × that span (MV-696 AC1: a 1 s
        /// probe on the floor records exactly 4 damage). A no-op on dry ground. Never called against a
        /// robot — the ticket's own "robots unaffected".</summary>
        public void TickFloodDamage(float dt, Vector3 position, IDamageable receiver)
        {
            if (dt <= 0f || receiver == null || !receiver.IsAlive) return;
            if (IsDry(position)) return;
            receiver.TakeDamage(new DamageInfo(SludgequeenTuning.FloodDamagePerSecond * dt, position, Vector3.up, Team.Enemy));
        }

        /// <summary>The slowest flood multiplier among every living, flooding Sludgequeen at
        /// <paramref name="worldPosition"/> (1 = unaffected) — consulted by
        /// <see cref="MaxWorlds.Arena.MapSlowZones.SpeedMultiplierAt"/> alongside the map's own zones and
        /// <see cref="MaxWorlds.Enemies.SludgePuddle"/>, the same shared-hook shape MV-705 already used.</summary>
        public static float FloodSpeedMultiplierAt(Vector3 worldPosition)
        {
            float best = 1f;
            for (int i = 0; i < _active.Count; i++)
            {
                SludgequeenBoss b = _active[i];
                if (b == null || !b.IsAlive) continue;
                if (!b.IsDry(worldPosition)) best = Mathf.Min(best, SludgequeenTuning.FloodSlowMultiplier);
            }
            return best;
        }

        /// <summary>Test/level-reset hygiene, same idiom as <see cref="MaxWorlds.Enemies.SludgePuddle.ResetRegistry"/>.</summary>
        public static void ResetRegistry() => _active.Clear();

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            FitColliderToRenderedBody();
            _preferSign = ObstacleSteering.PreferSignFor(GetInstanceID());
            _health = new DestructibleHealth(SludgequeenTuning.Health);
            _health.Destroyed += OnDeath;
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
            _phase = Phase.Fight;
            if (!_active.Contains(this)) _active.Add(this);
            // 2 phases -> HUD bar shows the 50% segment, same as BigBermudaBoss.
            BossCensus.Register(this, BossName, 2, _health.Current, _health.Max, ResolveAreaIndex());
        }

        private int ResolveAreaIndex()
        {
            MapData map = EnemyNavigation.Map;
            MapZone zone = map?.ZoneAt(transform.position.x, transform.position.z);
            if (zone == null) return 0;
            return AreaAccumulationDirector.AreaIndexOf(zone.id);
        }

        private void TickDormant()
        {
            if (_target == null) { AcquireTarget(); return; }
            if (_wakeArea.Contains(new Vector2(_target.position.x, _target.position.z))) Wake();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            switch (_phase)
            {
                case Phase.Dormant: TickDormant(); break;
                case Phase.PhaseTwoTell: TickPhaseTwoTell(dt); TickFight(dt); break;
                case Phase.Fight:
                case Phase.PhaseTwo: TickFight(dt); break;
            }
            ApplyGravity(dt);
        }

        /// <summary>Counts down the crown-spin tell (MV-696 §4); once it elapses the flood is Phase 2's
        /// full-floor extent from the very next <see cref="FloodRect"/> read.</summary>
        private void TickPhaseTwoTell(float dt)
        {
            _tellTimer -= dt;
            if (_tellTimer <= 0f) _phase = Phase.PhaseTwo;
        }

        private void TickFight(float dt)
        {
            if (_target == null) { AcquireTarget(); return; }
            Approach(dt);
            FaceTarget();
            TickGlobVolley(dt);
            TickBrood(dt);

            if (_targetDamageable == null) _targetDamageable = _target.GetComponent<IDamageable>();
            TickFloodDamage(dt, _target.position, _targetDamageable);
        }

        private void Approach(float dt)
        {
            if (_target == null) return;
            TickApproach(dt, _target.position);
        }

        /// <summary>The actual approach-and-steer step, dt/target-parameterized so a test can drive it
        /// directly — same shape and same reasoning as <see cref="BigBermudaBoss.TickApproach"/>.</summary>
        public void TickApproach(float dt, Vector3 targetPosition)
        {
            Vector3 to = targetPosition - transform.position;
            to.y = 0f;
            if (to.magnitude <= SludgequeenTuning.Standoff) return;

            Vector3 waypoint = EnemyNavigation.Waypoint(transform.position, targetPosition,
                useZoneRoute: true, budget: _routeBudget, dt: dt);
            Vector3 routeTo = waypoint - transform.position;
            routeTo.y = 0f;
            Vector3 bearing = routeTo.sqrMagnitude > 0.0001f ? routeTo.normalized : to.normalized;

            Vector3 desired = _wallLatch.Tick(bearing, transform.position, dt, _preferSign);
            CharacterControllerMotion.SafeMove(_cc, desired * SludgequeenTuning.MoveSpeed * dt);
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

        // ---------------------------------------------------------------- glob volley (MV-696 §2)

        private void TickGlobVolley(float dt)
        {
            _globTimer += dt;
            float interval = _phase == Phase.PhaseTwo ? SludgequeenTuning.Phase2GlobInterval : SludgequeenTuning.Phase1GlobInterval;
            if (_globTimer < interval) return;
            _globTimer = 0f;

            int count = _phase == Phase.PhaseTwo ? SludgequeenTuning.Phase2GlobCount : SludgequeenTuning.Phase1GlobCount;
            FireGlobVolley(count);
        }

        /// <summary>Reuses the Pipe Turret's own corrosive glob (MV-691) — same projectile, same puddle,
        /// just fired from the boss instead of a static emplacement.</summary>
        private void FireGlobVolley(int count)
        {
            if (_target == null) return;
            Vector3 origin = transform.position + Vector3.up * 1.5f;
            for (int i = 0; i < count; i++)
            {
                CorrosiveGlob.Fire(origin, _target, SludgequeenTuning.GlobSpeed, SludgequeenTuning.GlobDamage,
                    SludgequeenTuning.GlobSplashRadius, SludgequeenTuning.GlobPuddleRadius, SludgequeenTuning.GlobPuddleDuration);
            }
        }

        // ---------------------------------------------------------------- brood (MV-696 §2)

        private void TickBrood(float dt)
        {
            if (_broodTelling)
            {
                _broodTellTimer -= dt;
                if (_broodTellTimer <= 0f) { _broodTelling = false; SpawnBrood(); }
                return;
            }

            _broodTimer += dt;
            float interval = _phase == Phase.PhaseTwo ? SludgequeenTuning.Phase2BroodInterval : SludgequeenTuning.Phase1BroodInterval;
            if (_broodTimer < interval) return;

            _broodTimer = 0f;
            _broodTelling = true;
            _broodTellTimer = SludgequeenTuning.BroodTellTime;
        }

        /// <summary>Fling <see cref="SludgequeenTuning.BroodCount"/> Sludge Drones off the hatches
        /// (MV-696 §2) — landing maths reused straight from <see cref="BroodArc"/>, same as
        /// <see cref="BigBermudaBoss.LaunchVolley"/>'s own hatch throw.</summary>
        private void SpawnBrood()
        {
            Vector3 pos = transform.position;
            Quaternion facing = transform.rotation;
            EnemyArchetype archetype = EnemyArchetype.Sludger;

            for (int i = 0; i < SludgequeenTuning.BroodCount; i++)
            {
                float side = (i % 2 == 0) ? -1f : 1f;
                Vector3 landing = BroodArc.Landing(pos, facing, side,
                    SludgequeenTuning.HatchSide, SludgequeenTuning.HatchLandingForward, archetype.SpawnHeight, 0f);

                RobotEnemy add = CreateSludger(archetype);
                add.transform.position = landing;
                add.transform.rotation = facing;
                add.TagNoReplicatePermanent(); // MV-706: a boss-flung robot may never be lured into a Replicator
                add.gameObject.SetActive(true);
            }
        }

        private RobotEnemy CreateSludger(in EnemyArchetype a)
        {
            var go = GameObject.CreatePrimitive(a.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            go.name = "Brood Sludger";
            go.transform.SetParent(BroodRoot(), false);
            go.transform.localScale = a.BodyScale;

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
            _active.Remove(this);
            BossCensus.Forget(this);
        }

        // ---------------------------------------------------------------- IDamageable

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return;
            HudSignals.EmitDamage(transform.position + Vector3.up * 2.5f, info.Amount);
            _health.TakeDamage(info.Amount);
            BossCensus.ReportHealth(this, _health.Current, _health.Max);

            if (_phase == Phase.Fight && _health.Normalized <= SludgequeenTuning.PhaseTwoThreshold)
            {
                _phase = Phase.PhaseTwoTell;
                _tellTimer = SludgequeenTuning.PhaseTwoTellTime;
            }
        }

        /// <summary>The flood stops affecting Max the instant she dies — <see cref="_active"/> drops her
        /// so <see cref="FloodSpeedMultiplierAt"/>/a live boss's own <see cref="TickFloodDamage"/> never
        /// runs again for this instance (MV-696 §5's "the flood drains"). The 3 s drain-out itself is a
        /// rig-only visual, left to the art pass — nothing in this ticket's ACs exercises it.</summary>
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
            if (p != null) _target = p.transform;
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
