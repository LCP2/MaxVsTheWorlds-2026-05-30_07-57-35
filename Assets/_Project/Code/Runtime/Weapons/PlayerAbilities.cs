using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The two active abilities that need a live component to actually DO something (WV-231): Water
    /// Balloon's throw/landing/splash, and Teleport's blink. Speed and Weapon Cooldown are pure
    /// passive multipliers with no activation and need nothing beyond the read
    /// <see cref="PlayerController.WalkSpeed"/> already does. Water Balloon briefly left the
    /// shed-acquired <see cref="AbilityKind"/> pool under MV-370 (a primary add-on gated only on
    /// cooldown + a per-throw cell cost) and was restored to it by MV-380 after Lee's playtest found
    /// it usable from the very first second with no sense of having earned it — it's acquisition-gated
    /// again now, same as Teleport, with the cell cost and its own three upgrade tracks
    /// (<see cref="WaterBalloonTrackKind"/>) unchanged on top.
    ///
    /// Self-attaches to Max from <see cref="PlayerController.Awake"/> — no scene wiring, the same
    /// code-driven-scenes rule <see cref="MaxWorlds.Combat.WaterBlaster"/> follows for its own
    /// sub-components.
    ///
    /// MV-290: Teleport's activation is gated on cooldown only (spec §6a's cell cost is retired) —
    /// must be acquired, then must be off cooldown. Water Balloon (MV-370) is gated on cooldown plus
    /// one cell per throw. The on-screen controls that call these (WV-240) are out of this ticket's
    /// scope; the public Try* methods are the hand-off point.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(CharacterController))]
    [MaxWorlds.Core.PerfSection("weapons")]
    public sealed class PlayerAbilities : MonoBehaviour
    {
        [Header("Water Balloon")]
        [Tooltip("How fast the balloon actually flies, m/s — the arc mesh (WV-241) is purely " +
                 "visual; this is what times the landing.")]
        [SerializeField] private float waterBalloonFlightSpeed = 9f;

        // MV-847: when the aimed destination itself doesn't fit (the circle is on a crate/wall), how
        // far back toward Max each landing-search probe steps before trying again.
        private const float TeleportLandingSearchStep = 0.25f;

        private CharacterController _cc;
        private WaterBalloonSplashVfx _splashVfx;
        private float _waterBalloonCooldown;
        private float _teleportCooldown;

        // --- Force Field (MV-361) ---
        private float _forceFieldCooldown;     // > 0 while cooling down, only starts once the bubble pops
        private float _forceFieldAbsorbRemaining; // > 0 while the bubble is up
        private float _forceFieldAbsorbCap;    // this activation's full cap, for the HUD/visual fraction
        private ForceFieldBubble _forceFieldBubble;

        // --- TRAP (MV-1035, World 3's PRIMARY ability) ---
        private RobotTrap _activeTrap;
        private float _trapCooldown;    // > 0 while cooling down, only starts once a trap converts

        // --- Sentinel button deploy (MV-1113) ---
        private float _sentinelCooldown;        // > 0 while cooling down, starts on every successful deploy
        private Sentinel _arrivingSentinel;     // the one Sentinel currently mid-arrival, if any (at most one: the cooldown outlasts the 3.0s arrival)
        private int _arrivingAreaIndex;         // Max's own area index at the moment that deploy happened
        private int _arrivingCost;              // what was actually spent, refunded if the arrival is cancelled

        /// <summary>Owned (RIG node <c>p_trp</c> at level &gt;= 1) — a PRIMARY-track node, so this reads
        /// <see cref="RigState"/> directly rather than <see cref="WeaponSystemState.IsAcquired"/>: PRIMARY
        /// tracks have no separate boolean-unlock layer the way <see cref="AbilityKind.Sentinels"/>/
        /// <see cref="AbilityKind.ForceField"/> do (see <see cref="Weapons.RigState"/>'s own doc comment).</summary>
        public bool TrapOwned => RigState.IsOwned("p_trp");

        /// <summary>Seconds left before TRAP can be dropped again, 0 when ready. Only starts counting
        /// down once a trap actually converts something (spec: "the 20s cooldown starts then") — a trap
        /// that despawns having caught nothing never starts it.</summary>
        public float TrapCooldownRemaining => Mathf.Max(0f, _trapCooldown);

        /// <summary>Owned, off cooldown, AND no trap already down — the HUD button's own gate (spec:
        /// "one trap at a time — the button does nothing while one is down").</summary>
        public bool TrapReady => TrapOwned && _activeTrap == null && _trapCooldown <= 0f;

        /// <summary>True while a trap is currently down — what the HUD reads to show the "n / capacity"
        /// hold readout and the post-fill conversion glow.</summary>
        public RobotTrap ActiveTrap => _activeTrap;

        // MV-1028: no longer readonly — grown on overflow by NonRobotOverlapGrowing, same idiom
        // WaterBlaster.OverlapSphereGrowing uses, so a room too cluttered for the original fixed size
        // can no longer silently drop a Replicator or boss out of the query.
        private static Collider[] s_hits = new Collider[32];
        private static readonly System.Collections.Generic.HashSet<int> s_hitGameObjectIds = new System.Collections.Generic.HashSet<int>();

        /// <summary>Seconds left before Water Balloon can be thrown again, 0 when ready.</summary>
        public float WaterBalloonCooldownRemaining => Mathf.Max(0f, _waterBalloonCooldown);

        /// <summary>Owned, off cooldown, AND a Power Cell banked to spend — what an on-screen control
        /// (WV-240) gates its press on. MV-380: restores the acquisition gate MV-370 had silently
        /// dropped — Water Balloon is a shed-acquired <see cref="AbilityKind"/> again, same as
        /// Teleport, on top of the per-throw cell cost MV-370 introduced. MV-673: reads the Power
        /// Cells secondary bank, matching what a throw actually spends now — not Parts.</summary>
        public bool WaterBalloonReady =>
            WeaponSystemState.IsAcquired(AbilityKind.WaterBalloon) &&
            _waterBalloonCooldown <= 0f && PickupWallet.PowerCellsSecondary > 0;

        /// <summary>Seconds left before Teleport can be used again, 0 when ready.</summary>
        public float TeleportCooldownRemaining => Mathf.Max(0f, _teleportCooldown);

        public bool TeleportReady =>
            WeaponSystemState.IsAcquired(AbilityKind.Teleport) && _teleportCooldown <= 0f;

        /// <summary>Seconds left before Force Field can be activated again, 0 when ready. Only starts
        /// counting down once the bubble pops (MV-361) — same "cooldown starts at the END of the
        /// window" shape as the legacy <see cref="MaxWorlds.Upgrades.HydroBurst"/>.</summary>
        public float ForceFieldCooldownRemaining => Mathf.Max(0f, _forceFieldCooldown);

        /// <summary>True while the bubble is up.</summary>
        public bool ForceFieldActive => _forceFieldAbsorbRemaining > 0f;

        /// <summary>Owned, off cooldown, not already active, AND enough cells banked to spend — what
        /// an on-screen control gates its press on (same shape as <see cref="WaterBalloonReady"/>).</summary>
        public bool ForceFieldReady =>
            WeaponSystemState.IsAcquired(AbilityKind.ForceField) && !ForceFieldActive &&
            _forceFieldCooldown <= 0f && PickupWallet.PowerCells >= ForceFieldActivationCost;

        /// <summary>1 (just activated) .. 0 (about to pop) — the bubble's own colour-shift/HUD countdown.</summary>
        public float ForceFieldAbsorbFraction =>
            _forceFieldAbsorbCap > 0f ? Mathf.Clamp01(_forceFieldAbsorbRemaining / _forceFieldAbsorbCap) : 0f;

        /// <summary>Power cells one Force Field activation costs (DECISION #2, MV-361) — fixed, not
        /// leveled.</summary>
        public static int ForceFieldActivationCost => Mathf.Max(0, Mathf.RoundToInt(
            DevTuning.Or(DevTuning.ForceFieldActivationCost, AbilityTuning.DefaultForceFieldActivationCost)));

        /// <summary>The bubble's radius this run, metres — levels with Force Field now (MV-422:
        /// "levels raise absorb AND radius together"), read fresh off <c>e_ff</c>'s current level.
        /// <see cref="DevTuning.ForceFieldRadius"/> still fully overrides it when set, same as before.</summary>
        public static float ForceFieldRadius => DevTuning.Or(DevTuning.ForceFieldRadius,
            AbilityTuning.ForceFieldRadius(WeaponSystemState.AbilityLevel(AbilityKind.ForceField),
                AbilityTuning.DefaultForceFieldRadius, AbilityTuning.DefaultForceFieldRadiusPerLevel));

        /// <summary>The splash radius the current settings would produce — what WV-241's landing
        /// circle and this component's own damage query both size themselves from. MV-370: scales with
        /// the Splash Area track's level, not just a fixed multiple of the large robot's footprint.</summary>
        public static float SplashRadius => AbilityTuning.WaterBalloonSplashRadius(
            EnemyArchetype.Bruiser.ColliderRadius,
            WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.SplashArea),
            DevTuning.Or(DevTuning.WaterBalloonSplashMult, AbilityTuning.DefaultWaterBalloonSplashMult),
            DevTuning.Or(DevTuning.WaterBalloonSplashAreaPerLevel, AbilityTuning.DefaultWaterBalloonSplashAreaPerLevel));

        /// <summary>The lob distance the current Range level actually throws — the same value
        /// <see cref="TryThrowWaterBalloon"/> lands at, so MV-373's auto-aim scan never picks a
        /// candidate landing point the real throw wouldn't reach.</summary>
        public static float ThrowDistance => AbilityTuning.WaterBalloonDistance(
            WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.Range),
            DevTuning.Or(DevTuning.WaterBalloonBaseDistance, AbilityTuning.DefaultWaterBalloonBaseDistance),
            DevTuning.Or(DevTuning.WaterBalloonDistancePerLevel, AbilityTuning.DefaultWaterBalloonDistancePerLevel));

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
        }

        /// <summary>The splash's cosmetic sub-component, built lazily on the first actual throw
        /// (not in Awake): most sessions never acquire Water Balloon, and most that do never throw it
        /// on any given frame, so building its particle systems eagerly would spawn them for every
        /// Max in every test/scene rather than only the ones that use the ability.</summary>
        private WaterBalloonSplashVfx SplashVfx
        {
            get
            {
                if (_splashVfx == null)
                {
                    _splashVfx = GetComponent<WaterBalloonSplashVfx>();
                    if (_splashVfx == null) _splashVfx = gameObject.AddComponent<WaterBalloonSplashVfx>();
                    _splashVfx.Init(SplashRadius);
                }
                return _splashVfx;
            }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Every cooldown this component owns, advanced by an explicit <paramref name="dt"/> —
        /// split out of <see cref="Update"/> (MV-1113) so an EditMode test can drive the SENTINEL
        /// button's own 10s cooldown and arrival-cancellation watch without <see cref="Time.deltaTime"/>,
        /// the same "Tick split out for an explicit dt" shape <see cref="MaxWorlds.Enemies.RobotEnemy.Tick"/>/
        /// <see cref="MaxWorlds.Arena.Sentinel.TickSentinel"/> already use for themselves.</summary>
        public void Tick(float dt)
        {
            _waterBalloonCooldown = Mathf.Max(0f, _waterBalloonCooldown - dt);
            _teleportCooldown = Mathf.Max(0f, _teleportCooldown - dt);
            _forceFieldCooldown = Mathf.Max(0f, _forceFieldCooldown - dt);
            _trapCooldown = Mathf.Max(0f, _trapCooldown - dt);
            _sentinelCooldown = Mathf.Max(0f, _sentinelCooldown - dt);

            if (_forceFieldBubble != null) _forceFieldBubble.SetFraction(ForceFieldAbsorbFraction);

            TickSentinelArrivalWatch();
        }

        /// <summary>Throw a Water Balloon toward <paramref name="aimDirection"/> (WV-240 drives this
        /// from the joystick release). Range track raises throw DISTANCE; Splash Area and Repeat Fire
        /// (MV-370) raise splash radius and fire rate independently. Returns false (no cooldown
        /// started, no cell spent) if on cooldown, aimless, or the bank has no cell to spend.
        ///
        /// MV-992: <paramref name="distance"/> lets a caller land SHORT of the full LOB — auto-fire's
        /// LOB is a RADIUS (<see cref="WaterBalloonAutoAim.TryFindBestLanding"/> picks the actual
        /// candidate distance), not a fixed lob that only ever lands at the full throw distance. Left
        /// null (the manual drag path), behaviour is unchanged: full LOB distance. Clamped to
        /// [<see cref="AbilityTuning.MinThrowDistance"/>, <see cref="ThrowDistance"/>] either way, so a
        /// caller can never request a landing closer than a thrown balloon can credibly land, or
        /// farther than the ability actually reaches.</summary>
        public bool TryThrowWaterBalloon(Vector3 aimDirection, float? distance = null)
        {
            if (!WeaponSystemState.IsAcquired(AbilityKind.WaterBalloon)) return false;
            if (_waterBalloonCooldown > 0f) return false;

            Vector3 dir = new Vector3(aimDirection.x, 0f, aimDirection.z);
            if (dir.sqrMagnitude < 1e-4f) return false;
            dir.Normalize();

            // MV-370: each balloon fired costs one Power Cell (MV-673: the scarce secondary
            // currency, not the everyday Parts balance) — spent only once the throw is actually
            // committing (direction validated), never on a degenerate press.
            if (!PickupWallet.TrySpendPowerCellSecondary()) return false;

            _waterBalloonCooldown = WeaponSystemState.WaterBalloonEffectiveCooldownSeconds();

            float maxDistance = ThrowDistance;
            float landingDistance = Mathf.Clamp(distance ?? maxDistance, AbilityTuning.MinThrowDistance, maxDistance);

            Vector3 landing = transform.position + dir * landingDistance;

            float flightSeconds = waterBalloonFlightSpeed > 0f ? landingDistance / waterBalloonFlightSpeed : 0f;
            if (flightSeconds <= 0f)
            {
                Land(landing);
            }
            else
            {
                // The thrown body (MV-334) — same landing point and timing the coroutine below
                // waits on, so the picture and the splash never drift apart.
                WaterBalloonThrowVfx.Fire(transform.position, landing, flightSeconds);
                StartCoroutine(FlyThenLand(landing, flightSeconds));
            }
            return true;
        }

        private IEnumerator FlyThenLand(Vector3 landing, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            Land(landing);
        }

        /// <summary>The balloon hits the ground: splash damage + halt to every robot in range, plus
        /// the cosmetic burst (WV-241). Damage is a PERCENTAGE of each target's own max health (spec
        /// §9 <c>waterBalloonDamagePct</c>) rather than a flat number, so one fixed splash still
        /// threatens the tougher WV-224 tiers.</summary>
        private void Land(Vector3 point)
        {
            float radius = SplashRadius;
            SplashVfx.Init(radius);
            SplashVfx.Play(point);

            float damagePct = DevTuning.Or(DevTuning.WaterBalloonDamagePct, AbilityTuning.DefaultWaterBalloonDamagePct);
            float stopSeconds = DevTuning.Or(DevTuning.WaterBalloonStopDurationSeconds, AbilityTuning.DefaultWaterBalloonStopDurationSeconds);

            // MV-1028: robots come from RobotEnemy.Active, not the capped all-layers overlap below —
            // the same "never fires" starvation MV-832 fixed for the Sentinel could just as easily
            // leave a splash landing in a cluttered room hitting nothing. Flat XZ distance +
            // CombatLevel.SameLevel, the range convention every other RobotEnemy.Active consumer in
            // Runtime/ uses (ShoulderRack, PlayerRocket, Sentinel).
            MapData map = EnemyNavigation.Map;
            float radiusSq = radius * radius;
            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;
            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy robot = active[i];
                if (robot == null || !robot.IsAlive) continue;
                if (!CombatLevel.SameLevel(map, point, robot.transform.position)) continue;

                Vector3 rp = robot.transform.position;
                float dx = rp.x - point.x, dz = rp.z - point.z;
                if (dx * dx + dz * dz > radiusSq) continue;

                float damage = AbilityTuning.WaterBalloonDamage(robot.MaxHealth, damagePct);
                if (damage > 0f)
                    robot.TakeDamage(new DamageInfo(damage, point, Vector3.up, Team.Player, soak: true));
                robot.ApplyHalt(stopSeconds);
            }

            // Anything else in range that ISN'T a RobotEnemy (a Replicator, a boss IDamageable) still
            // needs the physics query — a greybox robot carries both a CreatePrimitive collider and a
            // CharacterController on the same GameObject (two Colliders OverlapSphereNonAlloc reports
            // separately), and robots are now handled above, so any RobotEnemy hit here is skipped
            // rather than double-halted. The buffer grows on overflow (NonRobotOverlapGrowing) so a
            // cluttered room can't starve a boss/Replicator out of this query either.
            s_hitGameObjectIds.Clear();
            int count = NonRobotOverlapGrowing(point, radius);
            for (int i = 0; i < count; i++)
            {
                if (s_hits[i] == null) continue;
                if (s_hits[i].TryGetComponent<RobotEnemy>(out _)) continue;   // handled above
                if (!s_hitGameObjectIds.Add(s_hits[i].gameObject.GetInstanceID())) continue;
                if (!s_hits[i].TryGetComponent<IDamageable>(out var d) || !d.IsAlive || d.Team == Team.Player) continue;

                // Non-robot IDamageables take no damage from the splash — spec §6a says "robots in
                // the splash", not everything — but still get haltable, same as before MV-1028.
                if (s_hits[i].TryGetComponent<IHaltable>(out var haltable))
                    haltable.ApplyHalt(stopSeconds);
            }

            // MV-426 DELUGE (f_del): the splash leaves a puddle behind once Primary+Secondary is
            // forged — WaterBlaster reads WaterPuddle.Active to arc its stream between wet robots.
            if (RigFusionState.IsForged("f_del"))
            {
                var puddle = new GameObject("Water Puddle").AddComponent<WaterPuddle>();
                puddle.Init(point, radius, AbilityTuning.DefaultPuddleDurationSeconds);
            }
        }

        /// <summary>Blink toward <paramref name="aimDirection"/> (MV-292: an AIMED blink at every
        /// level — a random L1 hop read as "broken"/interchangeable with Dash in playtest). Level only
        /// changes blink DISTANCE (same shape as Water Balloon's level = distance, spec §6a), 8m at L1
        /// up to 12m at the L2 cap. Returns false if unowned or on cooldown.
        ///
        /// MV-393 (DECISION, 15 Aug 2026): a blink that lands in a DIFFERENT area than the one Max is
        /// currently standing in — crossing a wall/area boundary — WARPS there directly (ignores
        /// collision) whenever <see cref="CanWarpAcrossAreas"/> says the destination area is reachable
        /// through gates that are already open, exactly as Lee asked ("teleport over walls... any arena
        /// in range where there's an open gate"). A blink that stays within Max's own current room, or
        /// whose destination area is NOT reachable that way (a still-shut/locked gate in between), goes
        /// through <see cref="ResolveSameRoomLanding"/> instead: MV-847 — a teleport is a dematerialise/
        /// materialise, so nothing between Max and the destination matters, only whether Max's capsule
        /// actually fits AT the destination once he gets there.</summary>
        public bool TryTeleport(Vector3 aimDirection)
        {
            if (!WeaponSystemState.IsAcquired(AbilityKind.Teleport)) return false;
            if (_teleportCooldown > 0f) return false;

            Vector3 aimed = new Vector3(aimDirection.x, 0f, aimDirection.z);
            Vector3 dir = aimed.sqrMagnitude > 1e-4f ? aimed.normalized : transform.forward;

            _teleportCooldown = WeaponSystemState.EffectiveCooldownSeconds(AbilityKind.Teleport);

            int level = WeaponSystemState.AbilityLevel(AbilityKind.Teleport);
            float baseDistance = DevTuning.Or(DevTuning.TeleportBaseDistance, AbilityTuning.DefaultTeleportBaseDistance);
            float perLevel = DevTuning.Or(DevTuning.TeleportDistancePerLevel, AbilityTuning.DefaultTeleportDistancePerLevel);
            float distance = AbilityTuning.TeleportDistance(level, baseDistance, perLevel);

            Vector3 from = transform.position;
            Vector3 target = from + dir * distance;

            // MV-426 SKIRMISH (f_skr): Move+Support forged means the blink snaps to a live Sentinel at
            // any range instead of the normal short aimed hop, once one is deployed.
            if (RigFusionState.IsForged("f_skr"))
            {
                Sentinel nearest = NearestSentinel(from);
                if (nearest != null)
                    target = AbilityTuning.SkirmishSnapPoint(nearest.transform.position, from,
                        AbilityTuning.DefaultSkirmishSnapStandoff);
            }

            Vector3 offset = target - from;

            if (CanWarpAcrossAreas(EnemyNavigation.Map, from, target, EnemyNavigation.IsGateOpen))
            {
                // Bypasses the CharacterController's own collision sweep for this one move — the whole
                // point of a warp into an already-open area is that Max does not have to physically fit
                // through the doorway's exact gap. MV-1021: routed through the guard — a blink is player-
                // triggered and can land anywhere the current area/target maths resolves to, exactly the
                // kind of runtime-computed pose this ticket exists to check before it reaches PhysX.
                if (_cc != null) CharacterControllerSafety.SafeReposition(_cc, target, "PlayerAbilities.TryTeleport(warp)");
                else transform.position = target;
            }
            else if (_cc != null)
            {
                // MV-670: this used to be a plain _cc.Move(offset) — a physics-swept move that stops
                // dead at the first solid collider in its path, including hedges and pots, which are
                // deliberately non-blocking dressing everywhere else (MV-400, MV-613) but still carry
                // real colliders. ResolveSameRoomLanding runs the clearance check and reports where Max
                // actually ends up; landing there directly (the same instant-set pattern the cross-zone
                // warp above already uses) is what lets him pass through a hedge/pot instead of bouncing
                // off it mid-sweep.
                Vector3 landing = ResolveSameRoomLanding(from, target);
                CharacterControllerSafety.SafeReposition(_cc, landing, "PlayerAbilities.TryTeleport(sameRoom)");
            }
            else transform.position += offset;

            // MV-426 BLINKGUARD (f_bgd): Energy+Move forged leaves a stationary Force Field bubble at
            // the departure point — the normal bubble follows Max; this one stays behind and pops on
            // its own once its slice duration runs out.
            if (RigFusionState.IsForged("f_bgd")) SpawnBlinkguardBubble(from);

            // MV-338: HudSignals is the same decoupled hand-off BlinkerTeleported already uses — the
            // VFX beat (CombatVfx) and the brief time-slow (GameFeel) both react to this without
            // PlayerAbilities needing to know either exists.
            HudSignals.EmitMaxTeleported(from, transform.position);
            return true;
        }

        /// <summary>MV-847: where a same-room blink actually lands. A teleport is a dematerialise/
        /// materialise, so whatever sits BETWEEN <paramref name="from"/> and <paramref name="target"/>
        /// (a crate, a wall, the Mower Hutch — anything on <see cref="CoverLayer.Mask"/>) is irrelevant;
        /// only the destination itself is tested. Max lands exactly at <paramref name="target"/> if his
        /// capsule fits there (<see cref="CapsuleFitsAt"/>). If it doesn't — the circle was drawn on top
        /// of solid geometry — this steps back toward <paramref name="from"/> in
        /// <see cref="TeleportLandingSearchStep"/> increments looking for the nearest point that does
        /// fit, falling back to <paramref name="from"/> itself (which must already fit — Max is
        /// standing there) if nothing along the way does either.
        ///
        /// This supersedes MV-670's path <c>CapsuleCast</c> (a genuine wall used to clamp Max just short
        /// of it even when the aimed destination itself was clear well past it) per Lee's direct
        /// instruction — do not re-raise "a wall in the path should still stop the blink".
        ///
        /// MV-945: "fits" also requires landing on the SAME walkable surface Max blinked FROM
        /// (<see cref="LandsOnWalkableSurface"/>) — <see cref="CapsuleFitsAt"/> alone only rejects a
        /// landing that overlaps solid <see cref="CoverLayer"/> geometry, and a deck's parapet colliders
        /// are deliberately kept off that layer (they block <c>CharacterController.Move</c> directly),
        /// so open air just past a deck's own edge used to pass the old check clean and strand Max there
        /// exactly like the Sentinel-deploy bug MV-864 already fixed for the other half of this
        /// symptom.</summary>
        private Vector3 ResolveSameRoomLanding(Vector3 from, Vector3 target)
        {
            if (!CoverLayer.Exists) return target;
            if (LandsOnWalkableSurface(from, target)) return target;

            Vector3 offset = target - from;
            float distance = offset.magnitude;
            if (distance <= 1e-4f) return from;
            Vector3 dir = offset / distance;

            for (float back = TeleportLandingSearchStep; back < distance; back += TeleportLandingSearchStep)
            {
                Vector3 candidate = target - dir * back;
                if (LandsOnWalkableSurface(from, candidate)) return candidate;
            }

            return from;
        }

        /// <summary>True if Max's own <see cref="CharacterController"/> capsule fits at
        /// <paramref name="position"/> (<see cref="CapsuleFitsAt"/>) AND that position is still on the
        /// same walkable surface (deck rect, or room floor) <paramref name="from"/> is standing on
        /// (<see cref="MapData.IsWalkable"/>) — degrading to the capsule check alone with no level
        /// loaded, the same no-level fallback <see cref="TryResolveSentinelSurfacePoint"/> already
        /// uses.</summary>
        private bool LandsOnWalkableSurface(Vector3 from, Vector3 position)
        {
            if (!CapsuleFitsAt(position)) return false;
            MapData map = EnemyNavigation.Map;
            return map == null || map.IsWalkable(from, position);
        }

        /// <summary>True if Max's own <see cref="CharacterController"/> capsule, placed at
        /// <paramref name="position"/>, doesn't overlap anything on <see cref="CoverLayer.Mask"/>.</summary>
        private bool CapsuleFitsAt(Vector3 position)
        {
            Vector3 center = position + _cc.center;
            float halfHeight = Mathf.Max(0f, _cc.height * 0.5f - _cc.radius);
            Vector3 top = center + Vector3.up * halfHeight;
            Vector3 bottom = center - Vector3.up * halfHeight;

            return !Physics.CheckCapsule(bottom, top, _cc.radius, CoverLayer.Mask, QueryTriggerInteraction.Ignore);
        }

        private static Sentinel NearestSentinel(Vector3 from)
        {
            Sentinel best = null;
            float bestSq = float.MaxValue;
            foreach (Sentinel s in Sentinel.Active)
            {
                if (s == null || !s.IsAlive) continue;
                float d = (s.transform.position - from).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = s; }
            }
            return best;
        }

        /// <summary>MV-426 BLINKGUARD's left-behind bubble — a bare <see cref="ForceFieldBubble"/> with
        /// no owner (so it neither follows Max nor is exempted from his own collision) that pops itself
        /// after <see cref="AbilityTuning.DefaultBlinkguardBubbleDurationSeconds"/>.</summary>
        private void SpawnBlinkguardBubble(Vector3 position)
        {
            var go = new GameObject("Blinkguard Force Field Bubble");
            var bubble = go.AddComponent<ForceFieldBubble>();
            bubble.Init(null, null, ForceFieldRadius, WeaponSystemState.AbilityLevel(AbilityKind.ForceField));
            go.transform.position = position;   // Init() re-centres on its (null) owner; set after
            if (Application.isPlaying) Destroy(go, AbilityTuning.DefaultBlinkguardBubbleDurationSeconds);
        }

        /// <summary>True if <paramref name="to"/> lands in a DIFFERENT area than <paramref name="from"/>
        /// AND the level's own room graph — the same BFS <see cref="MapRoutes.Rooms"/> already solves
        /// robot pathing with, fed the same live gate state a real blink asks
        /// <see cref="EnemyNavigation.IsGateOpen"/> for — finds a way through from one to the other right
        /// now. A shut gate (or a locked one, e.g. the boss gate before every shed falls) breaks the
        /// chain, so this returns false and the caller falls back to a normal collision-respecting move —
        /// a genuinely closed-off area can never be blinked into, only an already-open one.
        /// <paramref name="gateOpen"/> is threaded through rather than reading <see cref="EnemyNavigation"/>
        /// directly so a test can assert both outcomes (open and shut) against a bare <see cref="MapData"/>
        /// fixture without building a single live gate GameObject — the same shape
        /// <see cref="MapRoutes.Rooms"/> itself already takes. False with no map loaded (a bare EditMode/
        /// PlayMode fixture) — nothing to warp across.</summary>
        public static bool CanWarpAcrossAreas(MapData map, Vector3 from, Vector3 to, Func<string, bool> gateOpen)
        {
            if (map == null) return false;

            MapZone here = map.ZoneAt(from.x, from.z);
            MapZone there = map.ZoneAt(to.x, to.z);
            if (here == null || there == null || here.id == there.id) return false;

            return MapRoutes.Rooms(map, here, there, gateOpen).Count > 0;
        }

        /// <summary>Raise the bubble (MV-361): spends <see cref="ForceFieldActivationCost"/> cells
        /// (DECISION #2 — the one AbilityKind activation that still costs cells after MV-290 retired
        /// the rest), fills the absorb budget for this run's Force Field level, and spawns the physical
        /// <see cref="ForceFieldBubble"/> that blocks robot bodies. Returns false (nothing spent, no
        /// bubble) if unowned, already up, still cooling down from the last pop, or the bank can't
        /// cover the cost.</summary>
        public bool TryActivateForceField()
        {
            if (!WeaponSystemState.IsAcquired(AbilityKind.ForceField)) return false;
            if (ForceFieldActive) return false;
            if (_forceFieldCooldown > 0f) return false;
            if (!PickupWallet.TrySpendPowerCells(ForceFieldActivationCost)) return false;

            int level = WeaponSystemState.AbilityLevel(AbilityKind.ForceField);
            float baseCap = DevTuning.Or(DevTuning.ForceFieldAbsorbCap, AbilityTuning.DefaultForceFieldAbsorbCap);
            float perLevel = DevTuning.Or(DevTuning.ForceFieldAbsorbCapPerLevel, AbilityTuning.DefaultForceFieldAbsorbCapPerLevel);
            _forceFieldAbsorbCap = AbilityTuning.ForceFieldAbsorbCap(level, baseCap, perLevel);
            _forceFieldAbsorbRemaining = _forceFieldAbsorbCap;

            if (_forceFieldBubble != null) Destroy(_forceFieldBubble.gameObject);
            var go = new GameObject("Force Field Bubble");
            _forceFieldBubble = go.AddComponent<ForceFieldBubble>();
            _forceFieldBubble.Init(transform, _cc, ForceFieldRadius, level);

            // MV-1007: the audio cue for a real activation only — not ForceActivateForceFieldForTuning,
            // which the Settings panel's "Force field hold" slider can retrigger on every drag.
            MaxWorlds.UI.HudSignals.EmitForceFieldRaised(transform.position);

            return true;
        }

        /// <summary>Dev-tuning affordance (MV-455): force the bubble up regardless of acquisition,
        /// cooldown or power-cell cost, so Lee can leave the shield raised while dialling the
        /// shimmer sliders in the Settings panel instead of needing to have actually earned Force
        /// Field this run. Never called from real gameplay — only the panel's "Force field hold"
        /// toggle. No-op if the bubble is already up.</summary>
        public void ForceActivateForceFieldForTuning()
        {
            if (ForceFieldActive) return;

            int level = Mathf.Max(1, WeaponSystemState.AbilityLevel(AbilityKind.ForceField));
            float baseCap = DevTuning.Or(DevTuning.ForceFieldAbsorbCap, AbilityTuning.DefaultForceFieldAbsorbCap);
            float perLevel = DevTuning.Or(DevTuning.ForceFieldAbsorbCapPerLevel, AbilityTuning.DefaultForceFieldAbsorbCapPerLevel);
            _forceFieldAbsorbCap = AbilityTuning.ForceFieldAbsorbCap(level, baseCap, perLevel);
            _forceFieldAbsorbRemaining = _forceFieldAbsorbCap;

            if (_forceFieldBubble != null) Destroy(_forceFieldBubble.gameObject);
            var go = new GameObject("Force Field Bubble (tuning)");
            _forceFieldBubble = go.AddComponent<ForceFieldBubble>();
            _forceFieldBubble.Init(transform, _cc, ForceFieldRadius, level);
        }

        /// <summary>MV-660: pushes the Settings panel's absorb-cap sliders onto every ALREADY-UP bubble
        /// — same "an active bubble updates live rather than needing to be re-triggered" shape
        /// <see cref="MaxWorlds.UI.SettingsPanel"/>'s <c>RefreshForceFieldShimmer</c> uses for the
        /// shader knobs, but here it moves the CAP a moved slider changes headroom by rather than
        /// snapping remaining absorb back to full: the fraction display keeps its ratio and Lee sees
        /// the shield visibly take more (or less) hits before popping, on the shield he's already
        /// raised, with no respawn or re-activation needed. No-op for a bubble that isn't up.</summary>
        public static void RefreshForceFieldAbsorbCap()
        {
            foreach (var abilities in FindObjectsByType<PlayerAbilities>(FindObjectsSortMode.None))
                abilities.ApplyForceFieldAbsorbCapOverride();
        }

        private void ApplyForceFieldAbsorbCapOverride()
        {
            if (!ForceFieldActive) return;

            int level = WeaponSystemState.AbilityLevel(AbilityKind.ForceField);
            float baseCap = DevTuning.Or(DevTuning.ForceFieldAbsorbCap, AbilityTuning.DefaultForceFieldAbsorbCap);
            float perLevel = DevTuning.Or(DevTuning.ForceFieldAbsorbCapPerLevel, AbilityTuning.DefaultForceFieldAbsorbCapPerLevel);
            float newCap = AbilityTuning.ForceFieldAbsorbCap(level, baseCap, perLevel);

            float delta = newCap - _forceFieldAbsorbCap;
            _forceFieldAbsorbCap = newCap;
            _forceFieldAbsorbRemaining = Mathf.Clamp(_forceFieldAbsorbRemaining + delta, 0f, _forceFieldAbsorbCap);
        }

        /// <summary>Eat as much of an incoming hit as the bubble's remaining budget allows — the single
        /// hook <see cref="MaxWorlds.Player.PlayerHealth.TakeDamage"/> calls before touching HP, so
        /// EVERY damage source (contact lunge, beam tick, missile splash) is absorbed the same way
        /// without each needing to know the field exists (DECISION #1: "blocks ALL incoming threats").
        /// Pops the bubble the instant the budget is exhausted. Returns the damage that leaked through
        /// unabsorbed (the full amount if the field isn't up at all).</summary>
        public float AbsorbForceFieldDamage(float incoming)
        {
            if (!ForceFieldActive) return incoming;

            var (absorbed, leaked) = AbilityTuning.ForceFieldAbsorb(incoming, _forceFieldAbsorbRemaining);
            _forceFieldAbsorbRemaining -= absorbed;
            if (_forceFieldAbsorbRemaining <= 0f)
            {
                // MV-455 tuning affordance: refill instead of popping while the panel's "Force
                // field hold" toggle is on, so the shield stays up for the shimmer to be judged
                // without needing to re-trigger it after every hit.
                if (DevTuning.Or(DevTuning.ForceFieldHoldUp, 0f) >= 0.5f) _forceFieldAbsorbRemaining = _forceFieldAbsorbCap;
                else PopForceField();
            }
            return leaked;
        }

        /// <summary>The bubble bursts: cooldown starts NOW (not on activation, MV-361), the physical
        /// collider is torn down, and — only once Force Field is leveled to 3 (DECISION #4) — the pop
        /// deals damage and knocks back everything still touching it, turning the panic button into a
        /// counter-attack.</summary>
        private void PopForceField()
        {
            _forceFieldAbsorbRemaining = 0f;
            _forceFieldCooldown = WeaponSystemState.EffectiveCooldownSeconds(AbilityKind.ForceField);

            MaxWorlds.UI.HudSignals.EmitForceFieldPopped(transform.position);   // MV-1007

            if (_forceFieldBubble != null)
            {
                Destroy(_forceFieldBubble.gameObject);
                _forceFieldBubble = null;
            }

            int level = WeaponSystemState.AbilityLevel(AbilityKind.ForceField);
            if (AbilityTuning.ForceFieldPopDealsDamage(level)) ApplyForceFieldPop();
        }

        /// <summary>Level-3 pop (DECISION #4): every robot still touching the bubble's radius takes
        /// <see cref="AbilityTuning.DefaultForceFieldPopDamage"/> and is knocked outward — same
        /// <c>OverlapSphere</c> + dedupe idiom <see cref="Land"/> uses for the Water Balloon splash.</summary>
        private void ApplyForceFieldPop()
        {
            float damage = DevTuning.Or(DevTuning.ForceFieldPopDamage, AbilityTuning.DefaultForceFieldPopDamage);
            float knockbackSpeed = DevTuning.Or(DevTuning.ForceFieldPopKnockbackSpeed, AbilityTuning.DefaultForceFieldPopKnockbackSpeed);
            Vector3 center = transform.position;
            float radius = ForceFieldRadius;

            // MV-1028: same fix as Land's Water Balloon splash — robots come from RobotEnemy.Active
            // (flat XZ distance + CombatLevel.SameLevel), not the capped all-layers overlap that could
            // starve on a cluttered room the same way MV-832 found for the Sentinel.
            MapData map = EnemyNavigation.Map;
            float radiusSq = radius * radius;
            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;
            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy robot = active[i];
                if (robot == null || !robot.IsAlive) continue;
                // MV-1089: never pop a robot a TRAP is holding or has converted — same two-part
                // exclusion (held is still Team.Enemy, captured flips to Team.Player) every other
                // player-side weapon applies.
                if (robot.Team != Team.Enemy || robot.IsTrapHeld) continue;
                if (!CombatLevel.SameLevel(map, center, robot.transform.position)) continue;

                Vector3 rp = robot.transform.position;
                float dx = rp.x - center.x, dz = rp.z - center.z;
                if (dx * dx + dz * dz > radiusSq) continue;

                Vector3 outward = rp - center; outward.y = 0f;
                Vector3 dir = outward.sqrMagnitude > 1e-4f ? outward.normalized : transform.forward;

                if (damage > 0f)
                    robot.TakeDamage(new DamageInfo(damage, center, dir, Team.Player, source: DamageSource.Ability));
                robot.ApplyKnockback(dir * knockbackSpeed);
            }

            // Anything else in range that ISN'T a RobotEnemy (a Replicator, a boss IDamageable) still
            // needs the physics query, grown on overflow (NonRobotOverlapGrowing) so it can't be
            // starved by a cluttered room either — robots are skipped here since they're handled above.
            s_hitGameObjectIds.Clear();
            int count = NonRobotOverlapGrowing(center, radius);
            for (int i = 0; i < count; i++)
            {
                if (s_hits[i] == null) continue;
                if (s_hits[i].TryGetComponent<RobotEnemy>(out _)) continue;   // handled above
                if (!s_hitGameObjectIds.Add(s_hits[i].gameObject.GetInstanceID())) continue;
                if (!s_hits[i].TryGetComponent<IDamageable>(out var d) || !d.IsAlive || d.Team == Team.Player) continue;

                Vector3 outward = s_hits[i].transform.position - center; outward.y = 0f;
                Vector3 dir = outward.sqrMagnitude > 1e-4f ? outward.normalized : transform.forward;

                if (damage > 0f)
                    d.TakeDamage(new DamageInfo(damage, center, dir, Team.Player, source: DamageSource.Ability));

                if (s_hits[i].TryGetComponent<IKnockbackable>(out var kb))
                    kb.ApplyKnockback(dir * knockbackSpeed);
            }
        }

        /// <summary>Physics.OverlapSphereNonAlloc into <see cref="s_hits"/>, growing the buffer and
        /// re-querying whenever a call comes back exactly <see cref="s_hits"/>-length — the same
        /// growing idiom <c>WaterBlaster.OverlapSphereGrowing</c> uses, so a room too cluttered for the
        /// original fixed size can no longer silently drop a Replicator or boss out of the query. Used
        /// only for the non-robot remainder of <see cref="Land"/> and <see cref="ApplyForceFieldPop"/> —
        /// robots themselves are read straight off <see cref="RobotEnemy.Active"/> in both, never from
        /// this query (MV-1028, the same "never fires" bug MV-832 fixed for the Sentinel).</summary>
        private static int NonRobotOverlapGrowing(Vector3 center, float radius)
        {
            int count;
            while ((count = Physics.OverlapSphereNonAlloc(
                       center, radius, s_hits, ~0, QueryTriggerInteraction.Ignore)) == s_hits.Length)
            {
                s_hits = new Collider[s_hits.Length * 2];
            }
            return count;
        }

        // --- The Sentinel (MV-362, restructured MV-422) ---

        /// <summary>How many sentinels Max may have deployed at once right now, from the Slots
        /// (<c>u_slt</c>) axis's current level.</summary>
        public static int SentinelDeploymentCap => AbilityTuning.SentinelDeploymentSlots(RigState.Level("u_slt"));

        /// <summary>How many sentinels are deployed right now — read live off <see cref="Sentinel.Active"/>,
        /// never a separately-tracked balance, so any sentinel dying (to a robot, a gate crossing, or a
        /// level reset) frees its slot automatically.</summary>
        public static int SentinelDeployedCount => Sentinel.Active.Count;

        /// <summary>Parts deploying the sentinel costs right now, from the Cost (<c>u_cst</c>)
        /// axis's current level.</summary>
        public static int SentinelCost => AbilityTuning.SentinelCost(
            RigState.Level("u_cst"), AbilityTuning.DefaultSentinelCost, AbilityTuning.DefaultSentinelCostReductionPerLevel);

        /// <summary>Owned AND enough Parts banked — ONE of three independent gates the SENTINEL
        /// button's own tap reads (see <see cref="SentinelSlotAvailable"/>, <see cref="SentinelCooldownRemaining"/>).
        /// MV-1113 (SUPERSEDES MV-604's 26 Aug 2026 playtest DECISION): the Slots cap is no longer
        /// bypassed by a redeploy-time recall — the button now goes unavailable ("FULL") at the cap
        /// instead, per Lee's 6 Oct 2026 button-states spec — so this property alone deliberately still
        /// says nothing about the cap; <see cref="SentinelCanDeployNow"/> is the one that folds all three
        /// gates together. MV-1117: reads <see cref="PickupWallet.PowerCells"/> (Parts), not the Power
        /// Cells secondary bank MV-673 had this pinned to — Lee's device playtest found Magneto auto-
        /// draining every secondary cell into the Shoulder Rack/Balloon before a deploy ever got one.</summary>
        public bool SentinelReady =>
            WeaponSystemState.IsAcquired(AbilityKind.Sentinels) &&
            PickupWallet.PowerCells >= SentinelCost;

        /// <summary>MV-1113: a deployment slot is free right now — the button's own "FULL" gate.</summary>
        public bool SentinelSlotAvailable => SentinelDeployedCount < SentinelDeploymentCap;

        /// <summary>MV-1113: seconds left before the SENTINEL button can deploy again, 0 when ready —
        /// starts counting down only after a successful deploy (never after a refused tap or a NO ROOM
        /// outcome), same "cooldown only starts on a real action" shape <see cref="TrapCooldownRemaining"/>
        /// already uses.</summary>
        public float SentinelCooldownRemaining => Mathf.Max(0f, _sentinelCooldown);

        /// <summary>MV-1113: owned, affordable, a slot free, AND off cooldown — what the SENTINEL
        /// button's own tap actually gates on (the three independent "unavailable" reasons: not owned —
        /// the button is hidden entirely — "NO PARTS", "FULL", and the radial cooldown sweep).</summary>
        public bool SentinelCanDeployNow => SentinelReady && SentinelSlotAvailable && _sentinelCooldown <= 0f;

        /// <summary>MV-1113: the SENTINEL button's own cooldown, seconds — fixed, never reduced by any
        /// RIG axis (Lee's own words: "a slow cooldown to prevent me adding sentinels quickly" — a
        /// deliberate brake, not a track to buy down).</summary>
        public const float SentinelCooldownSeconds = 10f;

        /// <summary>How close an aimed placement point must stay to an existing sentinel or a live
        /// robot to count as "occupied" (MV-399's "can't overlap existing structures/robots" AC). MV-1113:
        /// the arrival search below reuses this unchanged for the "at least 1.5 m from other sentinels"
        /// predicate; robots/bosses use their OWN larger, relaxable <see cref="SentinelArrivalEnemyClearanceMin"/>
        /// instead of this constant (see <see cref="IsValidSentinelPlacement"/> vs <see cref="IsArrivalCandidateValid"/>).</summary>
        public const float SentinelPlacementClearance = 1.5f;

        /// <summary>MV-1113: the arrival search's own three distance rules (ticket item 3) — how far from
        /// Max a candidate point must sit, the clearance it must keep from every awake enemy robot/boss
        /// (relaxed in <see cref="SentinelArrivalEnemyClearanceStep"/> steps down to
        /// <see cref="SentinelArrivalEnemyClearanceFloor"/> when nothing qualifies at the full distance).</summary>
        public const float SentinelArrivalMinDistance = 2.5f;
        public const float SentinelArrivalMaxDistance = 4.5f;
        public const float SentinelArrivalEnemyClearanceMin = 3f;
        public const float SentinelArrivalEnemyClearanceFloor = 1.5f;
        public const float SentinelArrivalEnemyClearanceStep = 0.5f;
        private const float ArrivalRadiusStep = 0.25f;
        private const int ArrivalAngleSamples = 24; // 15 degree steps

        /// <summary>How far a deploy point must clear a wall or a gate/doorway span (MV-579 item 4):
        /// the sentinel's own body radius (0.25 m, see <see cref="Sentinel"/>'s CreatePrimitive
        /// Cylinder) plus margin, so it is never dropped straddling a threshold to begin with — the
        /// non-blocking fix (<see cref="Sentinel"/>'s <c>IgnorePlayerCollision</c>) is Max's guarantee
        /// against a sentinel already there; this is about not creating a fresh chokepoint in the
        /// first place.</summary>
        private const float SentinelWallClearance = 0.6f;

        /// <summary>MV-864: how far in from a deck's own actual edge (its authored Deck/Hatch rect —
        /// never the full room footprint a deck overlays, since a parapet/mouth can leave that rect
        /// narrower) a resolved deploy/follow point is pulled back.</summary>
        public const float SentinelDeckEdgeMargin = 0.5f;

        /// <summary>MV-864: the same margin the retired (MV-1113) aimed-placement joystick used to
        /// clamp its reticle into Max's own room (MV-399's <c>ZoneEdgeMargin</c>) — kept identical so
        /// ordinary floor placement is unchanged; the floor was never the bug the MV-864 ticket fixed.</summary>
        private const float SentinelFloorEdgeMargin = 1.5f;

        /// <summary>MV-864: how far the nearest point on a deck's own rect may sit from the raw aim
        /// before "off the walkway" counts as nothing walkable there at all, refusing the deploy —
        /// matches the downward-probe search radius the ticket's spec describes.</summary>
        private const float SentinelDeckSearchDistance = 3f;

        /// <summary>MV-864: resolves an aimed point onto the walkable surface at Max's OWN current
        /// level (<see cref="MapData.ResolveWalkableSurfacePoint"/>) — a deck's own rect (never a ramp)
        /// when he is standing on one, his current room's floor otherwise — so a Sentinel is never
        /// deployed hanging in mid-air off a deck's edge or over/in a wall. Degrades to
        /// <paramref name="aimedPoint"/> unchanged with no level loaded (a bare EditMode test fixture
        /// has none — the same no-level fallback every caller of this already relies on).
        /// <paramref name="resolved"/> is only ever
        /// <paramref name="aimedPoint"/> itself when this returns false, so a caller that ignores the
        /// bool still gets its old raw aim back rather than a stale/default Vector3.</summary>
        public bool TryResolveSentinelSurfacePoint(Vector3 aimedPoint, out Vector3 resolved)
        {
            MapData map = EnemyNavigation.Map;
            if (map == null) { resolved = aimedPoint; return true; }

            Vector3? point = map.ResolveWalkableSurfacePoint(
                transform.position, aimedPoint, SentinelDeckEdgeMargin, SentinelFloorEdgeMargin, SentinelDeckSearchDistance);
            resolved = point ?? aimedPoint;
            return point.HasValue;
        }

        /// <summary>Whether an aimed point is clear of every other deployed sentinel and live robot,
        /// AND clear of any wall/gate/doorway-threshold geometry. Room/wall CONTAINMENT is the
        /// joystick reticle's own job (<see cref="MaxWorlds.Arena.MapZone.Clamp"/>, MV-399 AC1: the
        /// reticle stays "constrained to the current arena" before a point is ever chosen), but a point
        /// well inside the arena can still land ON a wall's own footprint or in a doorway's threshold
        /// span — reusing <see cref="CoverLayer"/> here rather than re-deriving map geometry, since
        /// every one of those (walls, gate leaves, and — per <c>MapRuntime.BuildAreaGate</c> — a
        /// gate's own threshold collider) is already on it.</summary>
        public bool IsValidSentinelPlacement(Vector3 point)
        {
            foreach (Sentinel s in Sentinel.Active)
            {
                if (s == null) continue;
                if (FlatDistance(s.transform.position, point) < SentinelPlacementClearance) return false;
            }

            foreach (RobotEnemy robot in RobotEnemy.Active)
            {
                if (robot == null) continue;
                if (FlatDistance(robot.transform.position, point) < SentinelPlacementClearance) return false;
            }

            if (CoverLayer.Exists &&
                Physics.CheckSphere(point, SentinelWallClearance, CoverLayer.Mask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return true;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Deploy the sentinel at Max's own current position — the convenience shape older
        /// callers/tests still use (no SENTINEL button, no arrival window — see
        /// <see cref="TryDeploySentinelNearMax"/> for the real player-facing path).</summary>
        public bool TryDeploySentinel() => TryDeploySentinel(transform.position);

        /// <summary>Deploy the sentinel at an exact <paramref name="position"/> — the legacy
        /// aimed-placement shape (MV-399's now-retired joystick) that existing tests still exercise
        /// directly. Production has no live caller of this any more (MV-1113 retired the joystick); the
        /// real player-facing deploy is <see cref="TryDeploySentinelNearMax"/>, which picks its own point
        /// via <see cref="TryFindSentinelArrivalPoint"/> and then calls <see cref="TryDeploySentinelCore"/>
        /// (shared by both) to actually spend and spawn. No arrival window, no cooldown — those are
        /// MV-1113's SENTINEL-button-only additions, layered on top only by the new path.</summary>
        public bool TryDeploySentinel(Vector3 position) => TryDeploySentinelCore(position, out _);

        /// <summary>The shared "spend + spawn" core both <see cref="TryDeploySentinel(Vector3)"/> and
        /// <see cref="TryDeploySentinelNearMax"/> call. Returns false (nothing spent, nothing deployed)
        /// if unowned/unaffordable, the Slots cap is already reached (MV-1113 SUPERSEDES MV-604's
        /// redeploy-time recall — the cap now simply refuses, matching the SENTINEL button's own "FULL"
        /// state), the point doesn't resolve onto any walkable surface at Max's own level
        /// (<see cref="TryResolveSentinelSurfacePoint"/>, MV-864), or the resolved point is already
        /// occupied (<see cref="IsValidSentinelPlacement"/>). Reads every RIG axis (Health/Range/Move)
        /// fresh at deploy time (MV-422).</summary>
        private bool TryDeploySentinelCore(Vector3 position, out Sentinel sentinel)
        {
            sentinel = null;
            if (!SentinelReady) return false;
            if (!SentinelSlotAvailable) return false;
            if (!TryResolveSentinelSurfacePoint(position, out Vector3 surfacePoint)) return false;
            if (!IsValidSentinelPlacement(surfacePoint)) return false;
            // MV-1117: Sentinel deploy spends Parts, not the Power Cells secondary bank MV-673 had
            // this on — see SentinelReady's own doc comment for why.
            if (!PickupWallet.TrySpendPowerCells(SentinelCost)) return false;

            float maxHp = AbilityTuning.SentinelMaxHp(
                RigState.Level("u_hp"), AbilityTuning.DefaultSentinelBaseHp, AbilityTuning.DefaultSentinelHpPerLevel);
            float range = AbilityTuning.SentinelRange(
                RigState.Level("u_rng"), AbilityTuning.DefaultSentinelRange, AbilityTuning.DefaultSentinelRangePerLevel);
            float moveSpeed = AbilityTuning.SentinelMoveSpeed(
                RigState.Level("u_mov"), AbilityTuning.DefaultSentinelMoveSpeedPerLevel);

            sentinel = new GameObject("Sentinel").AddComponent<Sentinel>();
            sentinel.Init(surfacePoint, maxHp, range, AbilityTuning.DefaultSentinelFireInterval,
                moveSpeed, AbilityTuning.DefaultSentinelStandoffDistance, transform);
            return true;
        }

        /// <summary>Outcome of a tap on the SENTINEL button (MV-1113).</summary>
        public enum SentinelDeployOutcome
        {
            /// <summary>A sentinel was placed and has begun its 3.0s arrival.</summary>
            Deployed,
            /// <summary>Unowned, on cooldown, unaffordable, or every slot is in use — the button itself
            /// was already showing this as unavailable; nothing was spent, no cooldown started.</summary>
            NotReady,
            /// <summary>Owned, affordable, off cooldown, a slot free — but no point near Max satisfied
            /// enough of item 3's predicates even at the most relaxed enemy clearance. Nothing was
            /// spent, no cooldown started (ticket item 3's own "nothing is spent and no cooldown
            /// starts").</summary>
            NoRoom,
        }

        /// <summary>The real player-facing deploy path (MV-1113): tapping the SENTINEL button. Unlike
        /// <see cref="TryDeploySentinel(Vector3)"/>, Max never aims — the game picks the landing point
        /// itself (<see cref="TryFindSentinelArrivalPoint"/>), charges the cost at the tap, starts the
        /// 10s cooldown, and gives the placed sentinel a 3.0s teleport-style arrival
        /// (<see cref="Sentinel.BeginArrival"/>) during which it is untargetable, undamageable, and
        /// silent. If Max leaves the sentinel's own deploy area before the arrival finishes, the deploy
        /// is cancelled and the cost returned — see <see cref="TickSentinelArrivalWatch"/>.</summary>
        public SentinelDeployOutcome TryDeploySentinelNearMax()
        {
            if (_sentinelCooldown > 0f) return SentinelDeployOutcome.NotReady;
            if (!SentinelReady) return SentinelDeployOutcome.NotReady;
            if (!SentinelSlotAvailable) return SentinelDeployOutcome.NotReady;

            if (!TryFindSentinelArrivalPoint(out Vector3 point)) return SentinelDeployOutcome.NoRoom;

            int cost = SentinelCost; // captured BEFORE TryDeploySentinelCore spends it, for a possible refund
            if (!TryDeploySentinelCore(point, out Sentinel sentinel)) return SentinelDeployOutcome.NoRoom;

            _sentinelCooldown = SentinelCooldownSeconds;
            sentinel.BeginArrival();

            _arrivingSentinel = sentinel;
            _arrivingCost = cost;
            MapData map = EnemyNavigation.Map;
            _arrivingAreaIndex = map != null ? AreaIndexAt(map, transform.position) : -1;

            return SentinelDeployOutcome.Deployed;
        }

        /// <summary>MV-1113 item 5: if Max leaves <see cref="_arrivingAreaIndex"/> before
        /// <see cref="_arrivingSentinel"/> finishes its 3.0s arrival, the deploy is cancelled
        /// (<see cref="Sentinel.CancelArrival"/> — not a death, not a recall) and its cost refunded. At
        /// most one Sentinel is ever mid-arrival from this component at once — the 10s cooldown
        /// comfortably outlasts the 3.0s arrival, so a second deploy can never start while this one is
        /// still watching.</summary>
        private void TickSentinelArrivalWatch()
        {
            if (_arrivingSentinel == null) return;

            if (!_arrivingSentinel.IsArriving) { _arrivingSentinel = null; return; } // finished normally

            MapData map = EnemyNavigation.Map;
            int nowArea = map != null ? AreaIndexAt(map, transform.position) : _arrivingAreaIndex;
            if (nowArea == _arrivingAreaIndex) return;

            _arrivingSentinel.CancelArrival();
            _arrivingSentinel = null;
            PickupWallet.AddPowerCells(_arrivingCost);
        }

        private static int AreaIndexAt(MapData map, Vector3 position)
        {
            MapZone zone = map.ZoneAt(position.x, position.y, position.z);
            return zone?.AreaIndex ?? -1;
        }

        /// <summary>MV-1113 item 3: searches for a sentinel arrival point near Max, trying the full
        /// <see cref="SentinelArrivalEnemyClearanceMin"/> enemy clearance first and relaxing it in
        /// <see cref="SentinelArrivalEnemyClearanceStep"/> steps down to
        /// <see cref="SentinelArrivalEnemyClearanceFloor"/> only when nothing qualifies at a tighter
        /// clearance. Returns the first relaxation step that finds ANY qualifying candidate, choosing
        /// (within that step) whichever candidate sits farthest from its own nearest enemy.</summary>
        private bool TryFindSentinelArrivalPoint(out Vector3 point)
        {
            Vector3 maxPos = transform.position;
            MapData map = EnemyNavigation.Map;

            for (float clearance = SentinelArrivalEnemyClearanceMin;
                 clearance >= SentinelArrivalEnemyClearanceFloor - 1e-3f;
                 clearance -= SentinelArrivalEnemyClearanceStep)
            {
                if (TryBestArrivalCandidate(map, maxPos, clearance, out point)) return true;
            }

            point = maxPos;
            return false;
        }

        private bool TryBestArrivalCandidate(MapData map, Vector3 maxPos, float enemyClearance, out Vector3 best)
        {
            best = maxPos;
            float bestNearestEnemyDist = -1f;
            bool found = false;

            int radiusSteps = Mathf.RoundToInt((SentinelArrivalMaxDistance - SentinelArrivalMinDistance) / ArrivalRadiusStep);
            for (int ri = 0; ri <= radiusSteps; ri++)
            {
                float radius = SentinelArrivalMinDistance + ri * ArrivalRadiusStep;
                for (int ai = 0; ai < ArrivalAngleSamples; ai++)
                {
                    float angle = ai * (360f / ArrivalAngleSamples) * Mathf.Deg2Rad;
                    Vector3 candidate = maxPos + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
                    candidate.y = maxPos.y;

                    if (!IsArrivalCandidateValid(map, maxPos, candidate, enemyClearance, out float nearestEnemyDist)) continue;

                    if (nearestEnemyDist > bestNearestEnemyDist)
                    {
                        bestNearestEnemyDist = nearestEnemyDist;
                        best = candidate;
                        found = true;
                    }
                }
            }
            return found;
        }

        /// <summary>Every predicate from ticket item 3 except the distance-from-Max band, which is
        /// guaranteed by construction (<see cref="TryBestArrivalCandidate"/> only ever samples within
        /// [<see cref="SentinelArrivalMinDistance"/>, <see cref="SentinelArrivalMaxDistance"/>]).</summary>
        private bool IsArrivalCandidateValid(MapData map, Vector3 maxPos, Vector3 candidate, float enemyClearance, out float nearestEnemyDist)
        {
            nearestEnemyDist = float.MaxValue;

            // On Max's own level, on walkable ground inside Max's current area.
            if (map != null && !map.IsWalkable(maxPos, candidate)) return false;

            // At least `enemyClearance` from every awake enemy robot and boss.
            foreach (RobotEnemy robot in RobotEnemy.Active)
            {
                if (robot == null || !robot.IsAwake) continue;
                float d = FlatDistance(robot.transform.position, candidate);
                if (d < nearestEnemyDist) nearestEnemyDist = d;
                if (d < enemyClearance) return false;
            }
            foreach (Vector3 bossPos in BossCensus.LivingPositions())
            {
                float d = FlatDistance(bossPos, candidate);
                if (d < nearestEnemyDist) nearestEnemyDist = d;
                if (d < enemyClearance) return false;
            }

            // At least SentinelPlacementClearance from other sentinels.
            foreach (Sentinel s in Sentinel.Active)
            {
                if (s == null) continue;
                if (FlatDistance(s.transform.position, candidate) < SentinelPlacementClearance) return false;
            }

            // Not in a doorway or gate mouth (the existing wall/threshold refusal rule).
            if (CoverLayer.Exists &&
                Physics.CheckSphere(candidate, SentinelWallClearance, CoverLayer.Mask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            // Not in sludge.
            if (MapSlowZones.Instance.SpeedMultiplierAt(candidate) < 1f) return false;

            // Clear line of sight to Max.
            if (!LineOfSight.Clear(candidate, maxPos)) return false;

            return true;
        }

        // --- TRAP (MV-1035) ---

        /// <summary>Drops a trap at Max's own feet (spec: "drops a trap where Max stands" — no aim, a
        /// plain tap). Reads HOLD (<c>p_tcap</c>) and RADIUS (<c>p_trad</c>) fresh at drop time, same
        /// "every RIG axis read fresh at deploy time" convention <see cref="TryDeploySentinel(Vector3)"/>
        /// already uses. The trap's own catch capacity is capped at the number of ally slots actually
        /// free right now (spec: "a trap never catches more robots than the free ally slots") — with no
        /// other trap ever running concurrently (one at a time), that count can't change out from under
        /// this trap except via its own conversions.</summary>
        public bool TryDropTrap()
        {
            if (!TrapReady) return false;

            int holdLevel = Mathf.Max(1, RigState.Level("p_tcap"));
            int freeSlots = Mathf.Max(0, holdLevel - RobotEnemy.Converted.Count);
            int capacity = Mathf.Min(holdLevel, freeSlots); // 0 if already at the ally cap — the trap will simply never catch
            float radius = 3.0f + 0.75f * RigState.Level("p_trad");

            RobotEnemy.ConversionCap = holdLevel;
            _activeTrap = RobotTrap.Spawn(transform.position, capacity, radius, OnTrapDespawned);
            return true;
        }

        private void OnTrapDespawned(bool converted)
        {
            _activeTrap = null;
            if (converted) _trapCooldown = RobotTrap.ConversionCooldownSeconds;
        }
    }
}
