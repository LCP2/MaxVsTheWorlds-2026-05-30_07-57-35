using System.Collections;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The two shed-acquired abilities that need a live component to actually DO something (WV-231):
    /// Water Balloon's throw/landing/splash, and Teleport's blink. Dash stays inside
    /// <see cref="PlayerController"/> — it was already the base movement tech there, gated on
    /// ownership in place (WV-231) rather than duplicated here. Speed, Power Efficiency and Weapon
    /// Cooldown are pure passive multipliers with no activation and need nothing beyond the reads
    /// <see cref="PlayerController.WalkSpeed"/> and <see cref="AbilityCellSpend"/> already do.
    ///
    /// Self-attaches to Max from <see cref="PlayerController.Awake"/> — no scene wiring, the same
    /// code-driven-scenes rule <see cref="MaxWorlds.Combat.WaterBlaster"/> follows for its own
    /// sub-components.
    ///
    /// Every activation is gated the same order (spec §6a): must be acquired, must be off cooldown,
    /// then must afford its cell cost — cheapest checks first, so an inactive or on-cooldown press
    /// never touches the wallet. The on-screen controls that call these (WV-240) are out of this
    /// ticket's scope; the public Try* methods are the hand-off point.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerAbilities : MonoBehaviour
    {
        [Header("Water Balloon")]
        [Tooltip("How fast the balloon actually flies, m/s — the arc mesh (WV-241) is purely " +
                 "visual; this is what times the landing.")]
        [SerializeField] private float waterBalloonFlightSpeed = 9f;

        [Header("Teleport")]
        [Tooltip("How far a blink moves Max, metres.")]
        [SerializeField] private float teleportDistance = 5f;

        private CharacterController _cc;
        private WaterBalloonSplashVfx _splashVfx;
        private float _waterBalloonCooldown;
        private float _teleportCooldown;

        private static readonly Collider[] s_hits = new Collider[32];
        private static readonly System.Collections.Generic.HashSet<int> s_hitGameObjectIds = new System.Collections.Generic.HashSet<int>();

        /// <summary>Seconds left before Water Balloon can be thrown again, 0 when ready.</summary>
        public float WaterBalloonCooldownRemaining => Mathf.Max(0f, _waterBalloonCooldown);

        /// <summary>Owned AND off cooldown — what an on-screen control (WV-240) gates its press on.</summary>
        public bool WaterBalloonReady =>
            WeaponSystemState.IsAcquired(AbilityKind.WaterBalloon) && _waterBalloonCooldown <= 0f;

        /// <summary>Seconds left before Teleport can be used again, 0 when ready.</summary>
        public float TeleportCooldownRemaining => Mathf.Max(0f, _teleportCooldown);

        public bool TeleportReady =>
            WeaponSystemState.IsAcquired(AbilityKind.Teleport) && _teleportCooldown <= 0f;

        /// <summary>The splash radius the current settings would produce — what WV-241's landing
        /// circle and this component's own damage query both size themselves from.</summary>
        public static float SplashRadius => AbilityTuning.WaterBalloonSplashRadius(
            EnemyArchetype.Bruiser.ColliderRadius,
            DevTuning.Or(DevTuning.WaterBalloonSplashMult, AbilityTuning.DefaultWaterBalloonSplashMult));

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
        /// from the joystick release). Level only changes throw DISTANCE (spec §6a) — never damage or
        /// splash size. Returns false (nothing spent, no cooldown started) if unowned, on cooldown, or
        /// unaffordable.</summary>
        public bool TryThrowWaterBalloon(Vector3 aimDirection)
        {
            if (!WeaponSystemState.IsAcquired(AbilityKind.WaterBalloon)) return false;
            if (_waterBalloonCooldown > 0f) return false;

            Vector3 dir = new Vector3(aimDirection.x, 0f, aimDirection.z);
            if (dir.sqrMagnitude < 1e-4f) return false;
            dir.Normalize();

            if (!AbilityCellSpend.TrySpendSecondary()) return false;

            _waterBalloonCooldown = WeaponSystemState.EffectiveCooldownSeconds(AbilityKind.WaterBalloon);

            int level = WeaponSystemState.AbilityLevel(AbilityKind.WaterBalloon);
            float baseDistance = DevTuning.Or(DevTuning.WaterBalloonBaseDistance, AbilityTuning.DefaultWaterBalloonBaseDistance);
            float perLevel = DevTuning.Or(DevTuning.WaterBalloonDistancePerLevel, AbilityTuning.DefaultWaterBalloonDistancePerLevel);
            float distance = AbilityTuning.WaterBalloonDistance(level, baseDistance, perLevel);

            Vector3 landing = transform.position + dir * distance;

            float flightSeconds = waterBalloonFlightSpeed > 0f ? distance / waterBalloonFlightSpeed : 0f;
            if (flightSeconds <= 0f) Land(landing);
            else StartCoroutine(FlyThenLand(landing, flightSeconds));
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

        /// <summary>Blink (spec §6a): L1 is a random hop, L2 blinks toward <paramref name="aimDirection"/>.
        /// Moved via the CharacterController so a blink stops at a wall rather than clipping through
        /// it. Returns false if unowned, on cooldown, or unaffordable.</summary>
        public bool TryTeleport(Vector3 aimDirection)
        {
            if (!WeaponSystemState.IsAcquired(AbilityKind.Teleport)) return false;
            if (_teleportCooldown > 0f) return false;
            if (!AbilityCellSpend.TrySpendSpecial()) return false;

            _teleportCooldown = WeaponSystemState.EffectiveCooldownSeconds(AbilityKind.Teleport);

            int level = WeaponSystemState.AbilityLevel(AbilityKind.Teleport);
            Vector3 aimed = new Vector3(aimDirection.x, 0f, aimDirection.z);
            Vector3 dir;
            if (level >= 2 && aimed.sqrMagnitude > 1e-4f)
            {
                dir = aimed.normalized;
            }
            else
            {
                Vector2 rand = Random.insideUnitCircle;
                if (rand.sqrMagnitude < 1e-4f) rand = Vector2.up;
                dir = new Vector3(rand.x, 0f, rand.y).normalized;
            }

            Vector3 offset = dir * teleportDistance;
            if (_cc != null) _cc.Move(offset);
            else transform.position += offset;
            return true;
        }
    }
}
