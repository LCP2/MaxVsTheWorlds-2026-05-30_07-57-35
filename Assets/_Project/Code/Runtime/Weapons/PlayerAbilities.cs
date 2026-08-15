using System.Collections;
using UnityEngine;
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
    public sealed class PlayerAbilities : MonoBehaviour
    {
        [Header("Water Balloon")]
        [Tooltip("How fast the balloon actually flies, m/s — the arc mesh (WV-241) is purely " +
                 "visual; this is what times the landing.")]
        [SerializeField] private float waterBalloonFlightSpeed = 9f;

        private CharacterController _cc;
        private WaterBalloonSplashVfx _splashVfx;
        private float _waterBalloonCooldown;
        private float _teleportCooldown;

        // --- Force Field (MV-361) ---
        private float _forceFieldCooldown;     // > 0 while cooling down, only starts once the bubble pops
        private float _forceFieldAbsorbRemaining; // > 0 while the bubble is up
        private float _forceFieldAbsorbCap;    // this activation's full cap, for the HUD/visual fraction
        private ForceFieldBubble _forceFieldBubble;

        private static readonly Collider[] s_hits = new Collider[32];
        private static readonly System.Collections.Generic.HashSet<int> s_hitGameObjectIds = new System.Collections.Generic.HashSet<int>();

        /// <summary>Seconds left before Water Balloon can be thrown again, 0 when ready.</summary>
        public float WaterBalloonCooldownRemaining => Mathf.Max(0f, _waterBalloonCooldown);

        /// <summary>Owned, off cooldown, AND a cell banked to spend — what an on-screen control
        /// (WV-240) gates its press on. MV-380: restores the acquisition gate MV-370 had silently
        /// dropped — Water Balloon is a shed-acquired <see cref="AbilityKind"/> again, same as
        /// Teleport, on top of the per-throw cell cost MV-370 introduced.</summary>
        public bool WaterBalloonReady =>
            WeaponSystemState.IsAcquired(AbilityKind.WaterBalloon) &&
            _waterBalloonCooldown <= 0f && PickupWallet.PowerCells > 0;

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

        /// <summary>The bubble's radius this run, metres (DECISION #3, MV-361) — fixed, not leveled.</summary>
        public static float ForceFieldRadius =>
            DevTuning.Or(DevTuning.ForceFieldRadius, AbilityTuning.DefaultForceFieldRadius);

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

        private void Update()
        {
            float dt = Time.deltaTime;
            _waterBalloonCooldown = Mathf.Max(0f, _waterBalloonCooldown - dt);
            _teleportCooldown = Mathf.Max(0f, _teleportCooldown - dt);
            _forceFieldCooldown = Mathf.Max(0f, _forceFieldCooldown - dt);

            if (_forceFieldBubble != null) _forceFieldBubble.SetFraction(ForceFieldAbsorbFraction);
        }

        /// <summary>Throw a Water Balloon toward <paramref name="aimDirection"/> (WV-240 drives this
        /// from the joystick release). Range track raises throw DISTANCE; Splash Area and Repeat Fire
        /// (MV-370) raise splash radius and fire rate independently. Returns false (no cooldown
        /// started, no cell spent) if on cooldown, aimless, or the bank has no cell to spend.</summary>
        public bool TryThrowWaterBalloon(Vector3 aimDirection)
        {
            if (!WeaponSystemState.IsAcquired(AbilityKind.WaterBalloon)) return false;
            if (_waterBalloonCooldown > 0f) return false;

            Vector3 dir = new Vector3(aimDirection.x, 0f, aimDirection.z);
            if (dir.sqrMagnitude < 1e-4f) return false;
            dir.Normalize();

            // MV-370: each balloon fired costs one cell — spent only once the throw is actually
            // committing (direction validated), never on a degenerate press.
            if (!PickupWallet.TrySpendPowerCell()) return false;

            _waterBalloonCooldown = WeaponSystemState.WaterBalloonEffectiveCooldownSeconds();

            int level = WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.Range);
            float baseDistance = DevTuning.Or(DevTuning.WaterBalloonBaseDistance, AbilityTuning.DefaultWaterBalloonBaseDistance);
            float perLevel = DevTuning.Or(DevTuning.WaterBalloonDistancePerLevel, AbilityTuning.DefaultWaterBalloonDistancePerLevel);
            float distance = AbilityTuning.WaterBalloonDistance(level, baseDistance, perLevel);

            Vector3 landing = transform.position + dir * distance;

            float flightSeconds = waterBalloonFlightSpeed > 0f ? distance / waterBalloonFlightSpeed : 0f;
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

            float pct = 0.01f * DevTuning.Or(DevTuning.WaterBalloonDamagePct, AbilityTuning.DefaultWaterBalloonDamagePct);
            float stopSeconds = DevTuning.Or(DevTuning.WaterBalloonStopDurationSeconds, AbilityTuning.DefaultWaterBalloonStopDurationSeconds);

            // A greybox robot (EnemySpawner's stand-in path, which is what ships today) carries BOTH
            // its CreatePrimitive collider AND a CharacterController on the same GameObject — two
            // Colliders OverlapSphereNonAlloc reports separately. Without this dedupe every robot in
            // range gets hit twice: double splash damage, double halt.
            s_hitGameObjectIds.Clear();
            int count = Physics.OverlapSphereNonAlloc(point, radius, s_hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (s_hits[i] == null) continue;
                if (!s_hitGameObjectIds.Add(s_hits[i].gameObject.GetInstanceID())) continue;
                if (!s_hits[i].TryGetComponent<IDamageable>(out var d) || !d.IsAlive || d.Team == Team.Player) continue;

                // Robots are the only source of truth for their own max health (IDamageable
                // deliberately doesn't carry it). Anything hit that isn't a RobotEnemy takes no
                // damage from the splash — spec §6a says "robots in the splash", not everything.
                if (s_hits[i].TryGetComponent<RobotEnemy>(out var robot))
                {
                    float damage = robot.MaxHealth * pct;
                    if (damage > 0f)
                        d.TakeDamage(new DamageInfo(damage, point, Vector3.up, Team.Player, soak: true));
                }

                if (s_hits[i].TryGetComponent<IHaltable>(out var haltable))
                    haltable.ApplyHalt(stopSeconds);
            }
        }

        /// <summary>Blink toward <paramref name="aimDirection"/> (MV-292: an AIMED blink at every
        /// level — a random L1 hop read as "broken"/interchangeable with Dash in playtest). Level only
        /// changes blink DISTANCE (same shape as Water Balloon's level = distance, spec §6a), 8m at L1
        /// up to 12m at the L2 cap. Moved via the CharacterController so a blink stops at a wall rather
        /// than clipping through it. Returns false if unowned or on cooldown.</summary>
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

            Vector3 offset = dir * distance;
            Vector3 from = transform.position;
            if (_cc != null) _cc.Move(offset);
            else transform.position += offset;

            // MV-338: HudSignals is the same decoupled hand-off BlinkerTeleported already uses — the
            // VFX beat (CombatVfx) and the brief time-slow (GameFeel) both react to this without
            // PlayerAbilities needing to know either exists.
            HudSignals.EmitMaxTeleported(from, transform.position);
            return true;
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
            _forceFieldBubble.Init(transform, _cc, ForceFieldRadius);

            return true;
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
            if (_forceFieldAbsorbRemaining <= 0f) PopForceField();
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

            s_hitGameObjectIds.Clear();
            int count = Physics.OverlapSphereNonAlloc(center, radius, s_hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (s_hits[i] == null) continue;
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
    }
}
