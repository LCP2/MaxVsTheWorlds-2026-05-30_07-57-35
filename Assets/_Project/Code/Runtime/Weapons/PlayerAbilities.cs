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
    /// <see cref="PlayerController.WalkSpeed"/> already does. Water Balloon left the shed-acquired
    /// <see cref="AbilityKind"/> pool by MV-370 — it's a primary add-on now, owned from run start and
    /// gated on cooldown + a per-throw cell cost rather than acquisition; Teleport is still a
    /// shed-acquired <see cref="AbilityKind"/>.
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

        private static readonly Collider[] s_hits = new Collider[32];
        private static readonly System.Collections.Generic.HashSet<int> s_hitGameObjectIds = new System.Collections.Generic.HashSet<int>();

        /// <summary>Seconds left before Water Balloon can be thrown again, 0 when ready.</summary>
        public float WaterBalloonCooldownRemaining => Mathf.Max(0f, _waterBalloonCooldown);

        /// <summary>Off cooldown AND a cell banked to spend — what an on-screen control (WV-240) gates
        /// its press on. MV-370: Water Balloon is a primary add-on now, always available from run
        /// start, so unlike before there's no "owned" gate here — only cooldown and the per-throw cell
        /// cost.</summary>
        public bool WaterBalloonReady => _waterBalloonCooldown <= 0f && PickupWallet.PowerCells > 0;

        /// <summary>Seconds left before Teleport can be used again, 0 when ready.</summary>
        public float TeleportCooldownRemaining => Mathf.Max(0f, _teleportCooldown);

        public bool TeleportReady =>
            WeaponSystemState.IsAcquired(AbilityKind.Teleport) && _teleportCooldown <= 0f;

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
        }

        /// <summary>Throw a Water Balloon toward <paramref name="aimDirection"/> (WV-240 drives this
        /// from the joystick release). Range track raises throw DISTANCE; Splash Area and Repeat Fire
        /// (MV-370) raise splash radius and fire rate independently. Returns false (no cooldown
        /// started, no cell spent) if on cooldown, aimless, or the bank has no cell to spend.</summary>
        public bool TryThrowWaterBalloon(Vector3 aimDirection)
        {
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
    }
}
