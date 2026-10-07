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
    /// Big Bermuda — the Backyard boss (YT-27, slice version). Stays dormant beyond the gate until the
    /// Mower Hutch dies, then engages: it simply walks at Max and settles into a slow drift at a
    /// standoff (MV-588 removed the ram/charge entirely — no more telegraphed cross-arena hit; MV-720
    /// replaced the resulting dead stop with a circling drift so the gait never stops, and reversed
    /// MV-588's "no contact damage at all" half — standing against its body now hurts). Its real
    /// weapon is still the brood volley, which escalates in composition the longer the fight runs (see
    /// <see cref="BigBermudaBrain.SpawnLevel"/> / <see cref="BroodSpawnLevels"/>): "kill it before its
    /// army outgrows you". At low HP it enrages — faster, and it rains mower blades (the slice's
    /// stand-in for the full M2 phase-2 choreography, spec §4.7). Takes Water-Blaster damage, drives
    /// the HUD boss bar (name card + phase segments + spawn-level bar) via <see cref="HudSignals"/>,
    /// and, only for the world's final boss area (MV-698), payoffs a Weapon Core through
    /// <see cref="MaxWorlds.Bosses.BossVictoryPayoff"/>. Greybox body; VFX are code-driven.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [MaxWorlds.Core.PerfSection("bosses")]
    public sealed class BigBermudaBoss : MonoBehaviour, IDamageable
    {
        private enum Phase { Dormant, Intro, Fight, Dead }

        // Every number this fight is made of lives in BossTuning (YT-94), NOT in a [SerializeField].
        //
        // It used to live in both, and the scene won: Backyard_Slice.unity carries a serialized copy of
        // each one, so the boss the code described was not the boss anyone fought. The last person to
        // change its HP had to RENAME the field to make the value take effect. A const cannot be
        // shadowed, and "the tuning values are easy to adjust" is an acceptance criterion, not a nicety.
        private const string BossName = "BIG BERMUDA";
        private const float Gravity = 20f;


        [Header("Intro")]
        [SerializeField] private float introTime = 1.6f;

        [Header("Tells")]
        [SerializeField] private Color idleColor = new Color(0.35f, 0.45f, 0.30f);
        [SerializeField] private Color bladeColor = new Color(0.8f, 0.8f, 0.85f, 0.8f);

        private Phase _phase = Phase.Dormant;
        private DestructibleHealth _health;
        private BigBermudaBrain _brain;
        private CharacterController _cc;
        private Transform _target;

        /// <summary>MV-1083: Max, specifically — set once in <see cref="AcquireTarget"/> and never
        /// reassigned. <see cref="_target"/> is the boss's CURRENT chase/face/attack target, which
        /// <see cref="RetargetIfNeeded"/> may swing onto a nearby Sentinel; contact damage against Max
        /// always goes through this field instead, so the Sentinel loop in
        /// <see cref="TickContactDamage"/> can never double-hit whichever Sentinel happens to be
        /// <see cref="_target"/> at the time.</summary>
        private Transform _playerTarget;

        /// <summary>MV-1083: the Sentinel <see cref="_target"/> is currently engaged with, or null while
        /// chasing Max — same bookkeeping shape as <see cref="MaxWorlds.Enemies.RobotEnemy"/>'s own
        /// <c>_engagedSentinel</c> (MV-362).</summary>
        private Sentinel _engagedSentinel;

        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;

        // MV-590: the boss was a raw beeline through SafeMove with no wall/prop awareness at all —
        // unlike RobotEnemy (YT-68/MV-447), it never picked up ObstacleSteering/WallLatch when those
        // landed for the robots. Same wiring here: OnControllerColliderHit feeds WallLatch, which
        // slides Approach's desired direction along whatever it's latched onto instead of grinding
        // into it.
        private readonly WallLatch _wallLatch = new WallLatch();
        private float _preferSign;

        // MV-667: WallLatch escapes a flat wall (it has a slide to follow) but not concave geometry —
        // a corner, a planter pocket, a doorway it has overshot — because a slide is a direction, not a
        // route. EnemyNavigation.Waypoint (the same call RobotEnemy.TickChase makes) gives TickApproach
        // an actual route out of the pocket; WallLatch stays underneath it as the local slide that
        // keeps the body off whatever it grazes on the way, exactly as RobotEnemy layers the two. The
        // budget throttles the in-room A* re-solve the same way RobotEnemy's own ZoneRouteBudget does
        // (MV-611) — this boss is one instance, not a swarm, but there is no reason it should re-solve
        // every single frame when nothing has changed since the last one.
        private readonly ZoneRouteBudget _routeBudget = new ZoneRouteBudget();

        // The area whose floor wakes this boss (MV-572) — MapRuntime.BuildBoss hands this in right
        // after AddComponent, from the same WorldArea/MapZone footprint the boss was authored inside.
        // Defaults to an empty Rect, which Contains() never satisfies, so a boss nobody assigns one to
        // (a stray scene-authored instance) simply never wakes rather than waking on any stray position.
        private Rect _wakeArea;

        /// <summary>MV-1122: this boss's own 1-based area (<see cref="ResolveAreaIndex"/>), resolved
        /// once in <see cref="Wake"/> and stamped onto every add it flings (<see cref="LaunchVolley"/>)
        /// — a brood add never carried an <see cref="RobotEnemy.AreaIndex"/> of its own before this,
        /// so <see cref="MaxWorlds.VFX.WorldFinaleGate"/>'s clean-up count/wake/safety-net never saw it
        /// (Lee, device, "FACTORIES 17/17" never falling — the hypothesis this ticket proves).</summary>
        private int _areaIndex;

        private float _verticalVel;
        private float _introTimer;
        private float _bladeTimer;

        /// <summary>MV-720: seconds left before the boss's body can land another contact-damage tick
        /// on Max/a Sentinel — see <see cref="TickContactDamage"/>. Set to the full
        /// <see cref="BossTuning.ContactCooldown"/> in <see cref="Awake"/>, same "no free first hit"
        /// convention as <see cref="MaxWorlds.Enemies.RobotEnemy"/>'s own contact-cooldown timer.</summary>
        private float _contactCooldownTimer;

        // The placeholder greybox tell flashes white on a hit (TakeDamage) and restores itself here —
        // MV-588 removed the old "next phase tick restores it" cycle along with the charge, so the
        // flash now times out on its own instead. Vestigial once BigBermudaRig attaches (it disables
        // this renderer and owns the real tell), but kept correct for the window/tests before it does.
        private float _flashTimer;

        // --- the brood volley: the second attack (YT-157), and now the boss's ONLY attack (MV-588) —
        // its composition escalates with time alive (BroodSpawnLevels), so it is pooled per-kind
        // rather than assuming every add is the same archetype. ---
        private BroodVolley _volley;
        private Transform _addsRoot;                    // world-space, unit scale — NOT under the moving boss
        private readonly List<AddInFlight> _inFlight = new List<AddInFlight>(8);
        private readonly Dictionary<EnemyKind, Stack<RobotEnemy>> _addPools = new Dictionary<EnemyKind, Stack<RobotEnemy>>();
        private Collider[] _playerColliders;

        /// <summary>One robot mid-throw: it is visible but its own logic is switched off, so the boss
        /// drives it along the parabola until it lands and becomes a normal robot.</summary>
        private struct AddInFlight
        {
            public RobotEnemy Robot;
            public Vector3 From;
            public Vector3 To;
            public float T;   // 0..1 along the arc
        }

        public bool IsAlive => _phase == Phase.Fight && _health != null && _health.IsAlive;
        public Team Team => Team.Enemy;

        /// <summary>Re-read the Boss-health slider and retune live (YT-126). Raising it gives
        /// headroom, not a heal; lowering clamps. Pushes the new fraction to the HUD boss bar.</summary>
        public void RefreshMax()
        {
            if (_health == null) return;
            _health.Retune(DevTuning.Or(DevTuning.BossHealth, BossTuning.Health));
            BossCensus.ReportHealth(this, _health.Current, _health.Max);
        }

        // --- read-only fight state, for the art layer (YT-90) ---
        //
        // Big Bermuda is a MACHINE with moving parts, and what those parts are doing has to agree with
        // what the fight is doing: the reel spins up as it winds up, the eyes go hot as it commits, the
        // whole thing goes red when it enrages. None of that is inferable from the outside — a boss
        // standing still is winding up OR recovering, and those are opposite things to a player.
        //
        // So the fight says what it is doing, out loud. These are getters over state this class already
        // holds; nothing here decides anything, and nothing outside this file can write to the fight.
        // Same shape as MowerHutch.Normalized, which is what lets FactoryLife run the factory without
        // reaching into it.

        /// <summary>True below the enrage threshold: phase 2, blade-rain, everything faster.</summary>
        public bool Enraged => _brain != null && _brain.Enraged;

        /// <summary>1..<see cref="BossTuning.MaxSpawnLevel"/> — how far the brood volley's composition
        /// has escalated (MV-588). Drives the HUD's spawn-level bar via <see cref="BossCensus"/>.</summary>
        public int SpawnLevel => _brain != null ? _brain.SpawnLevel : 1;

        /// <summary>True from the moment it wakes until it dies. Dormant beyond the gate before that —
        /// the machine is standing there the whole time, and it should look asleep, not switched off.</summary>
        public bool Engaged => _phase == Phase.Intro || _phase == Phase.Fight;

        /// <summary>True once THIS boss has actually died (MV-625). <see cref="HudSignals.BossDefeated"/>
        /// is a scene-wide signal with no boss identity on it — every bound <see cref="BigBermudaRig"/>
        /// hears it whenever ANY boss anywhere dies — so a rig must check this before starting its own
        /// death sequence, or every other still-living boss on the map (dormant or mid-fight) plays dead
        /// and deactivates the instant the first one falls.</summary>
        public bool IsDead => _phase == Phase.Dead;

        /// <summary>The brood-volley spawn telegraph, 0 shut … 1 flung (YT-157). This is the ONE getter
        /// <see cref="MaxWorlds.VFX.BigBermudaRig"/> reads to open the side hatches — the gameplay says
        /// when the swarm is coming, the rig shows it, and nothing writes back the other way. 0 whenever
        /// the volley is dormant (asleep, intro, between waves, dead).</summary>
        public float SpawnWindup01 => _volley != null ? _volley.SpawnWindup01 : 0f;

        /// <summary>Current HP as a 0..1 fraction (MV-1018) — read-only fight state for a companion
        /// behaviour (<see cref="MaxWorlds.Bosses.AnchorheadBoss"/>) to derive its own phase from,
        /// same shape as <see cref="SpawnWindup01"/>/<see cref="Enraged"/> above.</summary>
        public float HealthFraction01 => _health != null ? _health.Normalized : 1f;

        /// <summary>Extra move-speed multiplier (MV-1018), stacked on top of the enrage scale in
        /// <see cref="TickFight"/>. A skin-specific companion behaviour (today only
        /// <see cref="MaxWorlds.Bosses.AnchorheadBoss"/>) sets this per its own phase without this
        /// class needing to know that boss, or any boss, exists. 1 (no change) unless something sets
        /// it otherwise.</summary>
        public float ExternalSpeedScale { get; set; } = 1f;

        /// <summary>Hands this boss its own authoring area's floor (MV-572) — the rectangle
        /// <see cref="MapRuntime.BuildBoss"/> resolves from the map zone/world area it was built inside.
        /// Called once, right after <c>AddComponent</c>, before this boss ever ticks Dormant.</summary>
        public void SetWakeArea(Rect area) => _wakeArea = area;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _renderer = GetComponent<Renderer>();
            FitColliderToRenderedBody();
            _preferSign = ObstacleSteering.PreferSignFor(GetInstanceID());
            _mpb = new MaterialPropertyBlock();
            _health = new DestructibleHealth(DevTuning.Or(DevTuning.BossHealth, BossTuning.Health));
            _health.Destroyed += OnDeath;
            _brain = new BigBermudaBrain();
            _volley = new BroodVolley();
            _contactCooldownTimer = BossTuning.ContactCooldown; // MV-720: no free first hit
            AcquireTarget();
            SetTell(idleColor);
        }

        /// <summary>Size the CharacterController to the primitive cube <see cref="Stage27BossScaffold"/>
        /// actually renders (MV-542). <c>RequireComponent</c> hands the boss Unity's stock capsule
        /// defaults (height 2, radius 0.5, centred on the pivot), which have no authored relationship
        /// to this transform's scale or to the mesh at all — they only ever matched the render by the
        /// coincidence that a fresh CharacterController's defaults happen to equal a unit cube's own
        /// half-extents. Reading the mesh's own LOCAL bounds and writing them back as the collider's
        /// center/radius/height makes that relationship explicit and scale-independent — this holds at
        /// whatever scale a future second boss (MV-542's multi-boss capability) is placed at, not just
        /// today's authored number.</summary>
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

        /// <summary>
        /// Re-fits the CharacterController to <paramref name="localBounds"/> — the bound
        /// <see cref="BigBermudaRig"/>'s own combined renderer bounds, already expressed in this boss's
        /// local space (see <see cref="BigBermudaRig.Bind"/>). MV-613: the Awake-time fit above sizes the
        /// collider to the hidden placeholder cube, but once a rig attaches IT is what the player sees —
        /// a scaled-down rig left standing next to a stale, unscaled collider is exactly the "looks
        /// unchanged, wedges in doors" bug this replaces. Called once, right after the rig builds itself.
        /// </summary>
        public void FitColliderTo(Bounds localBounds)
        {
            if (_cc == null) return;
            _cc.center = localBounds.center;
            _cc.height = Mathf.Max(0.01f, localBounds.size.y);
            _cc.radius = Mathf.Max(0.01f, Mathf.Max(localBounds.extents.x, localBounds.extents.z));
        }

        private void Start()
        {
            // Tell the HUD a real boss exists so it never engages/drains its stand-in boss.
            HudSignals.EmitBossRegistered();
        }

        // Each boss wakes on its OWN area, not on a world-wide "sources are all gone" signal (MV-572).
        // World 1 v4 authors bosses mid-run, well before every factory in the run is dead, and MV-560
        // opens each boss's own gate on its own prerequisite sheds — so by the time Max can reach a
        // boss, that boss's job is simply to notice he did. A single FactoryCensus.Cleared subscription
        // (the pre-MV-572 wake) is wrong the moment a map has more than one boss area: it would wake
        // every boss on the map at once, and only once the LAST one's factories fall — the exact bug
        // observed live (Lee, 2026-08-26): boss 1's area entered at 9/37 factories, boss standing
        // permanently Dormant because 28 more factories, belonging to areas the player hasn't even
        // reached yet, were still up.
        private void Wake()
        {
            int areaIndex = ResolveAreaIndex();
            _areaIndex = areaIndex; // MV-1122: stamped onto every add this boss flings — see field doc

            // MV-995: a cold-boot RESUME rebuilds every authored boss fresh and Dormant, with no memory
            // of a prior run's fight -- so a boss whose area was already beaten before the checkpoint was
            // saved would otherwise wake up and fight again the first time Max walks back into its own
            // area. BossCensus.IsAreaDefeated is seeded from the checkpoint before this can ever run
            // (SaveSystem.RestoreCheckpoint, well before Max is repositioned into the area at all) --
            // silently remove this instance instead of engaging: no Register, no HudSignals, no HUD bar.
            if (BossCensus.IsAreaDefeated(areaIndex))
            {
                _phase = Phase.Dead;
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }

            _phase = Phase.Intro;
            _introTimer = introTime;
            // 2 phases -> HUD bar shows the 50% segment. MV-542: routed through BossCensus so a 2+
            // boss fight engages the bar once and shows the COMBINED health, not a per-boss re-engage.
            BossCensus.Register(this, BossName, 2, _health.Current, _health.Max, areaIndex);
        }

        /// <summary>Which area this boss stands in (MV-591). Resolved from its own world position, so
        /// a boss knows its area whether or not Max has walked in yet.</summary>
        private int ResolveAreaIndex()
        {
            MapData map = EnemyNavigation.Map;
            MapZone zone = map?.ZoneAt(transform.position.x, transform.position.z);
            if (zone == null) return 0;
            return AreaAccumulationDirector.AreaIndexOf(zone.id);
        }

        /// <summary>Wakes the instant Max's planar position enters this boss's own authored area
        /// (<see cref="_wakeArea"/>, set by <see cref="SetWakeArea"/>) — visible and asleep until then.</summary>
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
                case Phase.Intro: TickIntro(dt); break;
                case Phase.Fight: TickFight(dt); break;
            }
            ApplyGravity(dt);
        }

        private void TickIntro(float dt)
        {
            FaceTarget();
            _introTimer -= dt;
            if (_introTimer <= 0f) _phase = Phase.Fight;
        }

        private void TickFight(float dt)
        {
            if (_target == null) { AcquireTarget(); return; }
            RetargetIfNeeded();
            _brain.Tick(dt, _health.Normalized);
            BossCensus.ReportSpawnLevel(this, _brain.SpawnLevel, _brain.SpawnLevelProgress01);

            float speedScale = (_brain.Enraged ? BossTuning.EnrageMoveScale : 1f) * ExternalSpeedScale;
            Approach(dt, speedScale);
            FaceTarget();
            TickContactDamage(dt);

            if (_flashTimer > 0f)
            {
                _flashTimer -= dt;
                if (_flashTimer <= 0f) SetTell(idleColor);
            }

            // Enrage overlay: rain blades around Max. Untouched by MV-588 — the charge that dropped
            // grass along its own path is gone with it, but this zone never depended on the charge.
            if (_brain.Enraged)
            {
                _bladeTimer -= dt;
                if (_bladeTimer <= 0f) { _bladeTimer = BossTuning.BladeInterval; RainBlades(); }
            }

            // The brood volley (YT-157) is now the boss's ONLY attack (MV-588 removed the ram).
            TickVolley(dt);
            AdvanceAdds(dt);
        }

        /// <summary>Walk toward Max at <see cref="BossTuning.MoveSpeed"/> and settle into a slow
        /// circling drift once within <see cref="BossTuning.Standoff"/> (MV-588 removed the old
        /// charge-cycle circling entirely; MV-720 replaced the dead stop MV-588 left behind with this
        /// drift, see <see cref="DriftAtStandoff"/>).</summary>
        private void Approach(float dt, float speedScale)
        {
            if (_target == null) return;
            TickApproach(dt, _target.position, speedScale);
        }

        /// <summary>The actual approach-and-steer step, target/dt-parameterized like
        /// <see cref="MowerHutch.TickMobility"/> so an EditMode test can drive it directly
        /// against a synthetic target instead of needing a live Update loop. MV-590: routed through
        /// <see cref="WallLatch"/> so a wall/prop in the way is walked around, the same as robots,
        /// instead of ground into. MV-667: the BEARING it walks along is no longer a raw line to
        /// <paramref name="targetPosition"/> — it is <see cref="EnemyNavigation"/>'s own routed
        /// waypoint (in-room A* around this zone's cover, same as a melee robot's Chase), so a boss
        /// standing inside concave geometry has an actual way out instead of only a direction and a
        /// slide. The stop check below still measures the REAL distance to the target, not the route,
        /// so the boss still parks at its authored standoff from Max and not from some intermediate
        /// waypoint. MV-720: reaching standoff no longer returns immediately (a dead stop MV-588 left
        /// behind) — see <see cref="DriftAtStandoff"/>. MV-1083: the stop distance is no longer the
        /// fixed <see cref="BossTuning.Standoff"/> — it is <see cref="ContactReachTo"/>'s own live
        /// number (the exact reach <see cref="TickContactDamage"/> checks) minus
        /// <see cref="BossTuning.StandoffMargin"/>, so a boss can never again park outside the range it
        /// needs to actually land a hit (Lee, device, 2026-10-06: "just goes around Max in a
        /// circle").</summary>
        public void TickApproach(float dt, Vector3 targetPosition, float speedScale = 1f)
        {
            Vector3 to = targetPosition - transform.position;
            to.y = 0f;

            float move = DevTuning.Or(DevTuning.BossMoveSpeed, BossTuning.MoveSpeed);
            float stopDistance = ContactReachTo(_target) - BossTuning.StandoffMargin;

            if (to.magnitude <= stopDistance)
            {
                DriftAtStandoff(dt, to, move * speedScale);
                return;
            }

            Vector3 waypoint = EnemyNavigation.Waypoint(transform.position, targetPosition,
                useZoneRoute: true, budget: _routeBudget, dt: dt);
            Vector3 routeTo = waypoint - transform.position;
            routeTo.y = 0f;
            Vector3 bearing = routeTo.sqrMagnitude > 0.0001f ? routeTo.normalized : to.normalized;

            Vector3 desired = _wallLatch.Tick(bearing, transform.position, dt, _preferSign);
            // MV-386: SafeMove, not cc.Move directly -- same stall-tunneling fix as PlayerController/RobotEnemy.
            CharacterControllerMotion.SafeMove(_cc, desired * move * speedScale * dt);
        }

        /// <summary>MV-720: parked at Standoff no longer means a dead stop. <see cref="BigBermudaRig"/>'s
        /// gait (<c>TickGait</c>) is driven purely by DISTANCE TRAVELLED, so a boss that stops moving
        /// entirely has no leg animation and reads as a statue, not a machine (Lee, device,
        /// 2026-09-04). This circles the target at a slow tangential drift, at the same
        /// <see cref="BossTuning.MoveSpeed"/> pace <see cref="TickApproach"/> already walks at
        /// (untouched by this ticket) — steered through the same <see cref="WallLatch"/> a real
        /// approach uses, so it still slides off anything it grazes while circling instead of grinding
        /// on it, and it never has to know or care which way it's orbiting beyond this robot's own
        /// stable <see cref="_preferSign"/> tie-break.</summary>
        private void DriftAtStandoff(float dt, Vector3 to, float speed)
        {
            if (to.sqrMagnitude < 0.0001f) return; // exactly on top of the target -- no ring to walk
            Vector3 inward = to.normalized;
            Vector3 tangent = new Vector3(-inward.z, 0f, inward.x) * _preferSign;

            Vector3 desired = _wallLatch.Tick(tangent, transform.position, dt, _preferSign);
            CharacterControllerMotion.SafeMove(_cc, desired * speed * dt);
        }

        /// <summary>MV-720 (Lee's 2026-09-04 reversal of MV-588's "no contact damage at all" rule): a
        /// bad idea to stand pressed against the boss now — Max or a Sentinel touching its body takes
        /// <see cref="BossTuning.ContactDamagePerTick"/> on a fixed <see cref="BossTuning.ContactCooldown"/>
        /// cadence, the same rate-limited shape <see cref="MaxWorlds.Enemies.RobotEnemy.TickContactTouch"/>
        /// already uses for its own standing melee — not per physics frame. Distance-based against the
        /// boss's OWN collider radius rather than a collision event: Max can walk up against a
        /// STATIONARY boss (parked at Standoff, no longer approaching, or drifting sideways rather
        /// than into him), and a controller that isn't moving toward whatever leans on it never raises
        /// <see cref="OnControllerColliderHit"/> — only the MOVER's own controller gets that callback.
        /// Goes through the ordinary <see cref="IDamageable.TakeDamage"/> path like any other hit, so
        /// it drains the Force Field first exactly like every other contact source (MV-586) for free —
        /// nothing here needs to know the bubble exists. This is a passive damaging PRESENCE, not an
        /// attack move: it never displaces or interrupts anything else the boss is doing.
        ///
        /// MV-1037: reach is resolved in WORLD units, not compared against <see cref="_cc"/>'s raw
        /// local radius. <see cref="FitColliderToRenderedBody"/>/<see cref="FitColliderTo"/> size the
        /// collider from LOCAL mesh bounds, but <see cref="MapRuntime.BuildBoss"/> spawns the body
        /// scaled to its authored size (3.6-4.5 m in Worlds 1-3) — comparing the unscaled local radius
        /// against a world-space distance made contact unreachable for every boss in every world (Lee,
        /// 2026-09-30: Big Bermuda sat on top of Max and a Sentinel and did no damage).</summary>
        private void TickContactDamage(float dt)
        {
            _contactCooldownTimer -= dt;
            if (_contactCooldownTimer > 0f) return;

            float bossRadius = WorldRadius(_cc, transform);
            // MV-1083: always Max directly, never _target -- _target may currently BE a Sentinel
            // (RetargetIfNeeded), and that Sentinel is already covered by the loop below. Hitting it
            // through both _target and the loop in the same tick would double its damage.
            bool hitSomething = DamageIfTouching(_playerTarget, bossRadius);

            IReadOnlyList<Sentinel> sentinels = Sentinel.Active;
            for (int i = 0; i < sentinels.Count; i++)
            {
                Sentinel s = sentinels[i];
                if (DamageIfTouching(s != null ? s.transform : null, bossRadius)) hitSomething = true;
            }

            // MV-1092: a captured robot standing against the boss hurts exactly like a Sentinel does
            // -- this is passive AoE presence damage, not the single-target engage rule RetargetIfNeeded
            // picks _target from, so every live captured robot in reach is checked, not just one.
            IReadOnlyList<RobotEnemy> captured = RobotEnemy.Converted;
            for (int i = 0; i < captured.Count; i++)
            {
                RobotEnemy r = captured[i];
                if (DamageIfTouching(r != null ? r.transform : null, bossRadius)) hitSomething = true;
            }

            if (hitSomething) _contactCooldownTimer = BossTuning.ContactCooldown;
        }

        /// <summary>Deals <see cref="BossTuning.ContactDamagePerTick"/> to <paramref name="t"/> if it's
        /// within <paramref name="bossRadius"/> (the boss's own WORLD-space contact radius) plus
        /// <paramref name="t"/>'s own WORLD-space radius plus <see cref="BossTuning.ContactSkin"/>, and
        /// carries a live <see cref="IDamageable"/>. Returns whether it actually landed, so
        /// <see cref="TickContactDamage"/> only resets the shared cooldown when something was touching
        /// — a boss standing alone must not silently burn its cadence against nothing.</summary>
        private bool DamageIfTouching(Transform t, float bossRadius)
        {
            if (t == null) return false;
            Vector3 to = t.position - transform.position; to.y = 0f;
            float reach = bossRadius + TargetRadius(t) + BossTuning.ContactSkin;
            if (to.magnitude > reach) return false;

            if (!t.TryGetComponent<IDamageable>(out var damageable) || !damageable.IsAlive) return false;
            Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : Vector3.forward;
            damageable.TakeDamage(new DamageInfo(BossTuning.ContactDamagePerTick, transform.position, dir, Team.Enemy));
            return true;
        }

        /// <summary>MV-1037: <paramref name="t"/>'s own CharacterController world radius (Max's or a
        /// Sentinel's — both carry one on the same GameObject their transform belongs to), falling back
        /// to <see cref="EnemyArchetype.PlayerRadius"/> for anything that doesn't (a bare test fixture,
        /// or any future target this never anticipated).</summary>
        private static float TargetRadius(Transform t) =>
            t.TryGetComponent<CharacterController>(out var cc) ? WorldRadius(cc, t) : EnemyArchetype.PlayerRadius;

        /// <summary>A <see cref="CharacterController"/>'s radius, converted from its own LOCAL space
        /// into world units via <paramref name="t"/>'s lossy scale — same "radius × max(|scale.x|,
        /// |scale.z|)" conversion <c>MV1022MobileShedStandoffTests.WorldCapsuleRadius</c> already uses
        /// for this exact shape of problem (a mobile shed's own scaled-cube collider).</summary>
        private static float WorldRadius(CharacterController cc, Transform t)
        {
            Vector3 scale = t.lossyScale;
            return cc.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        /// <summary>MV-1083: the exact world-space contact reach <see cref="TickContactDamage"/> would
        /// check for <paramref name="target"/> right now — the SAME number <see cref="TickApproach"/>
        /// stops just inside of. Falls back to <see cref="EnemyArchetype.PlayerRadius"/> when
        /// <paramref name="target"/> is null (a bare synthetic Vector3 target with no real Transform —
        /// <c>MV590BossWallSteeringTests</c>/<c>MV667BossConcaveRoutingTests</c> drive <see cref="TickApproach"/>
        /// directly this way, with no Player tagged in their scene for <see cref="AcquireTarget"/> to find).</summary>
        private float ContactReachTo(Transform target)
        {
            float targetRadius = target != null ? TargetRadius(target) : EnemyArchetype.PlayerRadius;
            return WorldRadius(_cc, transform) + targetRadius + BossTuning.ContactSkin;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit) => HandleWallContact(hit.collider, hit.normal);

        /// <summary>Feeds every non-floor, non-character contact into <see cref="_wallLatch"/> (MV-590),
        /// same convention as <see cref="RobotEnemy"/>'s wall handling: a character hit (Max, a robot,
        /// another boss) is not something to route around — physical collision already stops the boss
        /// from overlapping it, and treating it as a "wall" to slide along would fight that. Split out
        /// of <see cref="OnControllerColliderHit"/>, same reasoning as
        /// <see cref="RobotEnemy.HandleWallContact"/> (MV-586): <see cref="ControllerColliderHit"/> has
        /// no public constructor, so a test drives this seam directly. NOT where MV-720's contact
        /// damage lives (see <see cref="TickContactDamage"/> instead): this only fires from the
        /// BOSS's own <see cref="CharacterController.Move"/> sweeping into something, so a stationary
        /// boss standing still while Max walks INTO it — exactly the bug MV-720 fixes — would never
        /// reach here at all.</summary>
        private void HandleWallContact(Collider collider, Vector3 normal)
        {
            if (Mathf.Abs(normal.y) >= 0.5f) return; // floor/ramp, not a wall
            if (collider.TryGetComponent<CharacterController>(out _)) return; // a character
            _wallLatch.NoteHit(normal);
        }

        /// <summary>
        /// The brood volley — the boss's signature side-hatch add-spawner (YT-157), and its only attack
        /// since MV-588 removed the ram. The pure <see cref="BroodVolley"/> owns the cadence and the
        /// telegraph; this executes the fling on its <see cref="BroodVolley.JustFired"/> edge, the same
        /// shape as the blade rain above.
        ///
        /// The volley is vetoed (<c>canVent = false</c>) only while the arena already holds its cap of
        /// adds, which is the whole kiteability guarantee for a fight where no factory is left to bound
        /// the robot count.
        ///
        /// MV-1085: "on field" is counted fresh every time, never carried in a decrement-only field —
        /// see <see cref="CountLandedAddsInArea"/>. A boss standing near a wall used to fling adds clean
        /// over it (<see cref="LaunchVolley"/>'s old unclamped <see cref="BroodArc.Landing"/> call); they
        /// fell forever outside the map (MV-955's repeating FALLS log), could never reach Max, could
        /// never die, and a counter that only ever went down on death held their slot open for good —
        /// six of those and the boss never produced another robot (Lee, device, 2026-10-06). Every add
        /// now lands inside the boss's own area no matter what (<see cref="ResolveLanding"/>), so this
        /// jam should not recur — but counting it fresh, excluding anything outside the area, means even
        /// a stray one from an old save or an edge case can never again wedge the cap shut.
        /// </summary>
        private void TickVolley(float dt)
        {
            bool enraged = _brain.Enraged;
            bool phaseAllows = enraged || BossTuning.VolleyFiresBeforeEnrage;

            int maxAdds = Mathf.Max(0, Mathf.RoundToInt(DevTuning.Or(DevTuning.BossMaxAdds, BossTuning.MaxConcurrentAdds)));
            int onField = CountLandedAddsInArea() + _inFlight.Count;

            bool canVent = phaseAllows && onField < maxAdds;
            _volley.Tick(dt, enraged, canVent);

            if (_volley.JustFired)
            {
                int want = _volley.RobotsThisVolley(enraged);
                LaunchVolley(Mathf.Min(want, Mathf.Max(0, maxAdds - onField)));
            }
        }

        /// <summary>Throw <paramref name="count"/> robots out of the side hatches, alternating flanks so
        /// both hatches disgorge and the wave fans out rather than stacking. Each one draws its own kind
        /// uniformly from the current spawn level's set (MV-588 — <see cref="BroodSpawnLevels"/>), so a
        /// single volley can be a mixed wave once the level has escalated. Each starts at a hatch mouth
        /// and begins its arc; it is not a live robot yet — see <see cref="AdvanceAdds"/>.</summary>
        private void LaunchVolley(int count)
        {
            if (count <= 0) return;

            Vector3 pos = transform.position;
            Quaternion facing = transform.rotation;
            EnemyKind[] kinds = BroodSpawnLevels.KindsFor(_brain.SpawnLevel);

            for (int i = 0; i < count; i++)
            {
                EnemyArchetype archetype = EnemyArchetype.Of(kinds[Random.Range(0, kinds.Length)]);

                float side = (i % 2 == 0) ? -1f : 1f;   // L, R, L, R…
                float spread = (i / 2) * BossTuning.VolleyLandingSpread;

                Vector3 from = BroodArc.Muzzle(pos, facing, side,
                    BossTuning.HatchMuzzleSide, BossTuning.HatchMuzzleHeight);
                Vector3 to = ResolveLanding(from, pos, facing, side, spread,
                    archetype.SpawnHeight, archetype.ColliderRadius);

                RobotEnemy add = TakeAdd(archetype);
                add.TagNoReplicatePermanent(); // MV-706: a boss-flung robot may never be lured into a Replicator
                add.SetAreaIndex(_areaIndex); // MV-1122: see _areaIndex's own doc comment
                add.transform.position = from;

                // MV-1021: this add's CharacterController has sat enabled (just inactive) since
                // CreateAdd/OnAddDied — SetActive(true) on an inactive GameObject re-creates the native
                // PhysX controller for every already-enabled component on it, CharacterController
                // included, independent of RobotEnemy's own OnEnable. One of the three crash logs named
                // exactly this call stack. Refused: this add is not thrown at all — it goes straight
                // back to its own kind's pool (same idiom OnAddDied uses) and the volley is one add short.
                var cc = add.GetComponent<CharacterController>();
                if (!CharacterControllerSafety.CanCreate(add.transform, cc, out string launchReason))
                {
                    CharacterControllerSafety.LogRefusal("BigBermudaBoss.LaunchVolley", add.gameObject.name,
                        launchReason, from, add.transform.lossyScale);
                    if (!_addPools.TryGetValue(add.Kind, out Stack<RobotEnemy> pool))
                        _addPools[add.Kind] = pool = new Stack<RobotEnemy>(4);
                    pool.Push(add);
                    continue;
                }

                add.gameObject.SetActive(true);   // VISIBLE for the throw…
                add.enabled = false;              // …but its own chase/gravity is off while the boss flies it
                _inFlight.Add(new AddInFlight { Robot = add, From = from, To = to, T = 0f });
            }
        }

        /// <summary>How many flanks-then-closer attempts <see cref="ResolveLanding"/> tries before giving
        /// up and landing the add against the boss's own body. 6 gives 3 "rings" (each flank once) at
        /// progressively tighter distances — ample for any wall this boss can stand against; nothing in
        /// this fight's geometry is so tight it would ever need more.</summary>
        private const int LandingAttempts = 6;

        /// <summary>How much <see cref="ResolveLanding"/> shrinks the throw's side/forward/spread
        /// distances every second attempt (once both flanks have been tried at the current distance).</summary>
        private const float LandingShrinkStep = 0.3f;

        /// <summary>MV-1085: where add #<paramref name="preferredSide"/>/<paramref name="spread"/> of
        /// this volley actually lands — never past the boss's own arena wall, however close the boss
        /// stands to it. The raw <see cref="BroodArc.Landing"/> point is only ever a SUGGESTION (a fixed
        /// distance to a flank plus spread, with no awareness of the arena at all); a boss standing
        /// within that distance of a wall used to throw an add clean over it, where it fell forever
        /// outside the map (MV-955's repeating FALLS log), could never reach Max, could never die, and
        /// jammed the volley shut once enough of those had piled up. This tries the preferred flank
        /// first, then the other, then both again progressively closer to the boss, accepting the first
        /// candidate that both lands inside the boss's own authored area (<see cref="ClampLandingToArea"/>,
        /// shrunk by the add's own <paramref name="bodyRadius"/> so its BODY never clips the wall either)
        /// and has a clear line from the muzzle — the same <see cref="LineOfSight"/> cover check every
        /// other ranged read in this game already uses, so an add is never thrown through a wall it
        /// can't see over either. Falls back to a point hugging the boss's own body — always inside the
        /// area it stands in, and nothing can stand between a body and its own skin — if every attempt
        /// is blocked.</summary>
        private Vector3 ResolveLanding(Vector3 muzzle, Vector3 bossPos, Quaternion facing, float preferredSide,
            float spread, float groundHeight, float bodyRadius)
        {
            for (int attempt = 0; attempt < LandingAttempts; attempt++)
            {
                float side = (attempt % 2 == 0) ? preferredSide : -preferredSide;
                float shrink = 1f - (attempt / 2) * LandingShrinkStep;

                Vector3 candidate = BroodArc.Landing(bossPos, facing, side,
                    BossTuning.VolleyLandingSide * shrink, BossTuning.VolleyLandingForward * shrink,
                    groundHeight, spread * shrink);
                Vector3 clamped = ClampLandingToArea(candidate, bodyRadius);

                if (LineOfSight.Clear(muzzle, clamped)) return clamped;
            }

            Vector3 atBoss = bossPos + facing * Vector3.forward * bodyRadius;
            atBoss.y = groundHeight;
            return ClampLandingToArea(atBoss, bodyRadius);
        }

        /// <summary>Pulls <paramref name="point"/>'s XZ back inside the boss's own authored area
        /// (<see cref="_wakeArea"/>) by <paramref name="bodyRadius"/> from every wall — the same
        /// pull-back-from-the-edge shape <see cref="MapZone.Clamp"/> already gives a room — and snaps Y
        /// onto the real walkable surface through <see cref="MapData.SnapToWalkableSurface"/>, the same
        /// path <see cref="MaxWorlds.Enemies.EnemySpawner.ResolveMusterPoint"/> already resolves a
        /// muster point through, so an add can never land floating off a deck edge either. Degrades to
        /// the raw point with no map registered (<see cref="EnemyNavigation.Map"/> is null in a bare test
        /// fixture) or no authored area (<see cref="_wakeArea"/> defaults to an empty Rect for a boss
        /// nobody called <see cref="SetWakeArea"/> on) — nothing to clamp into either way.</summary>
        private Vector3 ClampLandingToArea(Vector3 point, float bodyRadius)
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

        /// <summary>Fly every in-flight add one step along its parabola. On landing it re-enables the
        /// robot — <see cref="RobotEnemy"/>'s OnEnable resets it into Chase and it self-acquires Max — and
        /// lets the player walk through it, so from that instant it is an ordinary robot.</summary>
        private void AdvanceAdds(float dt)
        {
            if (_inFlight.Count == 0) return;
            float arcTime = Mathf.Max(0.05f, BossTuning.VolleyArcTime);

            for (int i = _inFlight.Count - 1; i >= 0; i--)
            {
                AddInFlight a = _inFlight[i];
                if (a.Robot == null) { _inFlight.RemoveAt(i); continue; }

                a.T += dt / arcTime;
                if (a.T >= 1f)
                {
                    a.Robot.transform.position = a.To;
                    Vector3 outward = a.To - a.From; outward.y = 0f;
                    if (outward.sqrMagnitude > 0.0001f)
                        a.Robot.transform.rotation = Quaternion.LookRotation(outward.normalized, Vector3.up);

                    a.Robot.enabled = true;   // OnEnable -> ResetState -> Chase, full health, acquires Max
                    LetThePlayerThrough(a.Robot.gameObject);
                    _inFlight.RemoveAt(i);
                }
                else
                {
                    a.Robot.transform.position = BroodArc.PointAt(a.From, a.To, BossTuning.VolleyArcApex, a.T);
                    _inFlight[i] = a;   // struct — write the advanced T back
                }
            }
        }

        /// <summary>Pooled PER KIND (MV-588): once a volley can throw more than one archetype, a pool
        /// keyed by nothing would hand back a Rusher-sized/rigged body and relabel it a Bruiser without
        /// resizing the collider or rebuilding <see cref="RobotRig"/> — so each kind keeps its own
        /// stack, and a reused instance is always exactly what it was built as.</summary>
        private RobotEnemy TakeAdd(in EnemyArchetype archetype)
        {
            if (_addPools.TryGetValue(archetype.Kind, out Stack<RobotEnemy> pool) && pool.Count > 0)
                return pool.Pop();
            return CreateAdd(archetype);
        }

        /// <summary>Build one add, sized exactly the way <see cref="EnemySpawner"/> builds a factory
        /// robot (YT-74 metre-space collider un-scaling) but parented to the boss's own adds root rather
        /// than a factory. Dressed through the same <see cref="RobotRig"/>/<see cref="CharacterSkin"/>
        /// body pipeline <see cref="EnemySpawner.CreateInstance"/> uses (MV-587) — a flung Rusher now
        /// looks exactly like a shed Rusher, in flight and after landing, instead of the raw primitive
        /// capsule/cube it used to fly out as, which drew Unity's magenta missing-shader colour. Same
        /// ordering rule MV-535 fixed for the factory path: <c>Apply</c> stamps the real
        /// <see cref="RobotEnemy.Kind"/> BEFORE <see cref="RobotRig"/> attaches, because its Awake reads
        /// Kind synchronously to build the right body. Born inactive; a volley activates it on landing.</summary>
        private RobotEnemy CreateAdd(in EnemyArchetype a)
        {
            var go = GameObject.CreatePrimitive(a.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            go.name = $"Brood Add {a.Kind}";
            go.transform.SetParent(AddsRoot(), false);
            go.transform.localScale = a.BodyScale;

            // MV-1021: go is active at this point (CreatePrimitive makes it so), and
            // AddComponent<CharacterController> on an active GameObject creates the native PhysX
            // controller synchronously — the same trap EnemySpawner.CreateInstance carried. Deactivating
            // first defers that native create until LaunchVolley's own guard below reactivates it.
            go.SetActive(false);
            if (!CharacterControllerSafety.CanCreate(go.transform, null, out string createReason))
            {
                CharacterControllerSafety.LogRefusal("BigBermudaBoss.CreateAdd", go.name,
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
            e.gameObject.SetActive(false);
            return e;
        }

        private void OnAddDied(RobotEnemy e)
        {
            if (!_addPools.TryGetValue(e.Kind, out Stack<RobotEnemy> pool))
                _addPools[e.Kind] = pool = new Stack<RobotEnemy>(4);
            pool.Push(e);   // back to its own kind's pool, reused on the next volley — no GC churn
        }

        /// <summary>MV-1085: this boss's own adds that currently hold a cap slot — alive, active, and
        /// standing inside its own authored area. Counted fresh off <see cref="_addsRoot"/> every time
        /// <see cref="TickVolley"/> considers venting, never carried in a decrement-only field: a dead
        /// one deactivates itself (<see cref="RobotEnemy"/>'s own death path), an in-flight one is
        /// excluded by its own disabled <see cref="RobotEnemy.enabled"/> (it is counted separately, in
        /// <see cref="_inFlight"/>), and — the actual jam this replaces — one that somehow ended up
        /// outside the boss's own area (MV-955: flung over a wall, forever falling, never dying) is
        /// excluded too, so it can never again hold a slot the boss can't ever free.</summary>
        private int CountLandedAddsInArea()
        {
            if (_addsRoot == null) return 0;

            int count = 0;
            for (int i = 0; i < _addsRoot.childCount; i++)
            {
                Transform child = _addsRoot.GetChild(i);
                if (child == null || !child.gameObject.activeInHierarchy) continue;
                if (!child.TryGetComponent<RobotEnemy>(out RobotEnemy robot) || !robot.enabled || !robot.IsAlive) continue;

                Vector3 p = child.position;
                if (!_wakeArea.Contains(new Vector2(p.x, p.z))) continue;
                count++;
            }
            return count;
        }

        /// <summary>The container the adds live in. Top-level and unit-scaled ON PURPOSE: the boss MOVES,
        /// and adds parented under it would be dragged across the arena as it charges. It also cancels
        /// nothing (scale 1), so the robots are authored in metres, straight.</summary>
        private Transform AddsRoot()
        {
            if (_addsRoot == null) _addsRoot = new GameObject("Brood Adds").transform;
            return _addsRoot;
        }

        /// <summary>Adds must never body-block Max any more than factory robots do (YT-74). Re-applied on
        /// every landing because Unity drops an ignored pair when the collider is toggled.</summary>
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

        private void OnDestroy()
        {
            // The adds root is ours and outlives nothing — tear it (and every add under it) down with the
            // boss so a torn-down fight leaves no robots pathing after a player who is gone.
            if (_addsRoot != null) Destroy(_addsRoot.gameObject);

            // MV-542: belt-and-braces against a boss outliving its level as a dead reference in
            // BossCensus's static list (same reasoning as FactoryCensus.Forget) -- a proper death
            // already called BossCensus.ReportDefeated, and Forget on an already-removed boss is a
            // no-op, so this only matters for a scene torn down mid-fight.
            BossCensus.Forget(this);
        }

        private void RainBlades()
        {
            if (_target == null) return;
            Vector3 c = _target.position;
            for (int i = 0; i < BossTuning.BladeCount; i++)
            {
                Vector2 off = Random.insideUnitCircle * BossTuning.BladeSpread;
                Vector3 pos = new Vector3(c.x + off.x, 1f, c.z + off.y);

                // The arm delay is the tell. It was 0.55 s on a zone that then bit three times over
                // 1.2 s of life — 36 damage from one blade, three of them every 1.4 s, on top of the
                // charges. Now it warns for the best part of a second and bites twice at most (YT-94).
                DamageZone.Spawn(pos, BossTuning.BladeRadius, BossTuning.BladeDamage, BossTuning.BladeLife,
                                 BossTuning.BladeArm, bladeColor);
            }
        }

        // --- IDamageable ---
        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return; // invulnerable until engaged; nothing after death
            if (!DamageRules.Applies(info.Attacker, Team)) return;
            HudSignals.EmitDamage(transform.position + Vector3.up * 2.5f, info.Amount);
            _health.TakeDamage(info.Amount);
            BossCensus.ReportHealth(this, _health.Current, _health.Max);
            SetTell(Color.white); // brief hit flash; _flashTimer restores it in TickFight (MV-588)
            _flashTimer = 0.15f;
        }

        private void OnDeath()
        {
            _phase = Phase.Dead;
            // MV-542: waits for every living boss, not just this one — see BossCensus.
            // MV-721: ReportDefeated must run BEFORE SetActive(false) below — it reads transform.position
            // synchronously and hands it to HudSignals.BossKilled, which is how BossSpectacle/BossDebris
            // get "what they need" (where to blow up) without ever touching this instance themselves.
            // Reversing this order would hand them a deactivated GameObject with nothing left to read.
            BossCensus.ReportDefeated(this);
            // MV-1122: land every add still mid-throw NOW, before Update stops ticking this boss (the
            // Phase.Dead switch arm below never calls AdvanceAdds again) — see LandInFlightAdds' own doc.
            LandInFlightAdds();
            // MV-698: the "RARE SHARD" toast here was a placeholder — no pickup ever backed it, and the
            // shard concept isn't in the design. The real finale reward (BossVictoryPayoff's Weapon
            // Core, World 1's last boss area only) carries its own toast on collection.
            // The death spectacle hangs off the per-boss BossKilled signal (BossSpectacle/BossDebris,
            // YT-55/MV-721) — raised above, inside ReportDefeated, for every boss's own death, not just
            // the area's last one.
            gameObject.SetActive(false);
        }

        /// <summary>MV-1122: every add still in flight the instant this boss dies is placed at its own
        /// landing point and enabled on this same tick. Before this, only <see cref="AdvanceAdds"/> —
        /// called from this boss's OWN <see cref="Update"/> — ever moved an in-flight add, and
        /// <see cref="Update"/>'s phase switch stops calling it the moment <see cref="_phase"/> flips to
        /// <see cref="Phase.Dead"/> above; an add still mid-arc at that exact instant stayed suspended
        /// in the air forever with <see cref="RobotEnemy.enabled"/> false (brain off) — Lee, 2026-10-07,
        /// "robots end up suspended in mid-air because they're thrown out of the Bermudas in a way that
        /// they can't land". Worse for THIS ticket specifically: a disabled robot is never removed by
        /// clean-up's own unreachable check reading it as alive-but-unreachable forever without this,
        /// since a robot that can never land can never satisfy anything else either — this is the one
        /// layer that actually fixes the cause rather than cleaning up after it.</summary>
        private void LandInFlightAdds()
        {
            foreach (AddInFlight a in _inFlight)
            {
                if (a.Robot == null) continue;
                a.Robot.transform.position = a.To;
                Vector3 outward = a.To - a.From; outward.y = 0f;
                if (outward.sqrMagnitude > 0.0001f)
                    a.Robot.transform.rotation = Quaternion.LookRotation(outward.normalized, Vector3.up);
                a.Robot.enabled = true; // OnEnable already ran; this only flips the brain back on
                LetThePlayerThrough(a.Robot.gameObject);
            }
            _inFlight.Clear();
        }

        private void AcquireTarget()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return;
            _target = p.transform;
            _playerTarget = p.transform;
        }

        /// <summary>MV-1083: re-decide whether to chase Max or the nearest Sentinel — the same
        /// proximity-only rule (never an absolute preference) <see cref="MaxWorlds.Enemies.RobotEnemy"/>
        /// already applies for ordinary robots (MV-362's own <c>RetargetIfNeeded</c>): strictly closer
        /// than Max AND within <see cref="SentinelTargeting.AggroRadius"/> AND on the same combat level,
        /// or a distant/off-level Sentinel never steals the boss away from Max. Checked once per Fight
        /// tick, before <see cref="Approach"/>/<see cref="FaceTarget"/> read <see cref="_target"/>.
        /// <see cref="TickContactDamage"/> is untouched by this — it always hits
        /// <see cref="_playerTarget"/> directly plus every Sentinel in <see cref="Sentinel.Active"/>, so
        /// this only changes who the boss WALKS at and FACES, never who it can hurt.</summary>
        private void RetargetIfNeeded()
        {
            if (_playerTarget == null) return;

            // The Sentinel we were engaging died since the last tick -- fall back to Max before
            // re-evaluating, so a dead Sentinel's Transform is never read below.
            if (_engagedSentinel != null && !_engagedSentinel.IsAlive)
            {
                _engagedSentinel = null;
                _target = _playerTarget;
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
                _target = nearest.transform;
            }
            else if (!engageSentinel && _engagedSentinel != null)
            {
                _engagedSentinel = null;
                _target = _playerTarget;
            }
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

        private void SetTell(Color c)
        {
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_EmissionColor", c * 0.4f);
            _renderer.SetPropertyBlock(_mpb);
        }

    }
}
