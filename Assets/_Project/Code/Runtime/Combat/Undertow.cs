using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Combat
{
    /// <summary>
    /// World 3's primary (MV-714) — UNDERTOW: one button, two behaviours, same shell as
    /// <see cref="WaterBlaster"/>/<see cref="PulseLaser"/> (an <see cref="EnergyPool"/> tank built from
    /// <see cref="BlasterTuning"/>, an optional <see cref="PlayerController"/> aim source driving
    /// <see cref="IsFiring"/>/facing).
    ///
    /// Holding fire streams the <b>pressure lance</b> — a narrow, long-ranged tick that pierces up to
    /// <see cref="MaxPierceCount"/> robots in a line — at the same per-tick cadence and (base) damage as
    /// the RCDA, so its DPS tracks the RCDA's within the ticket's 10% band by construction rather than by
    /// a coincidentally-matched authored number. Once a continuous hold reaches <see cref="ChargeSeconds"/>
    /// the lance stops ticking and charges instead (telegraphed on the weapon itself); releasing while
    /// charged fires the <b>cavitation shot</b> (<see cref="CavitationBubble"/>) instead of a final tick.
    /// An early release (never charged) fires one ordinary lance tick — "never a wasted shot", per spec.
    /// </summary>
    public sealed class Undertow : MonoBehaviour
    {
        // -------------------------------------------------------------------------------- lance

        /// <summary>Authored base per-tick damage — deliberately the SAME number as
        /// <see cref="WaterBlaster.DefaultDamagePerTick"/> (and the same <see cref="DefaultFireInterval"/>
        /// as <see cref="WaterBlaster"/>'s own fire interval), so the lance's DPS matches the RCDA's
        /// exactly at the shared base, then continues to track it through every future retune of either
        /// constant, without this ticket's own "within 10%" band ever needing separate upkeep.</summary>
        public const float DefaultDamagePerTick = WaterBlaster.DefaultDamagePerTick;

        public const float DefaultFireInterval = 0.1f;

        /// <summary>Longer-ranged than the RCDA's 5m base (spec).</summary>
        public const float DefaultRange = 9f;

        /// <summary>Narrower than the RCDA's 8° base (spec) — approximates a line rather than a fan,
        /// the same "narrow cone as a line" idiom the codebase already uses for hit-testing instead of a
        /// raycast (see <see cref="SprayHit"/>).</summary>
        public const float DefaultConeHalfAngle = 3f;

        /// <summary>"Pierces up to two robots" (spec) — a hard count cap on the closest in-cone,
        /// in-range, in-sight targets, not a distance/falloff cutoff.</summary>
        public const int MaxPierceCount = 2;

        // -------------------------------------------------------------------------------- cavitation

        /// <summary>Seconds of unbroken hold before the lance stops ticking and charges (spec: "hold the
        /// fire button 0.9s").</summary>
        public const float DefaultChargeSeconds = 0.9f;

        /// <summary>Spec: "Cooldown 4s."</summary>
        public const float DefaultCavitationCooldownSeconds = 4f;

        /// <summary>Spec: "robots within 4m are pulled".</summary>
        public const float DefaultPullRadius = 4f;

        /// <summary>Spec: "pulled 2.5m toward the impact point" — a flat displacement, not clamped to
        /// the robot's own remaining distance to the point (see <see cref="RobotEnemy.ApplyPull"/>'s own
        /// doc for why: the ticket's AC wants a robot seeded well inside the pull distance to still move
        /// the full 2.5m, ending up pulled through and past the impact point).</summary>
        public const float DefaultPullDistance = 2.5f;

        /// <summary>Spec: "staggered for 0.6s" — reuses <see cref="RobotEnemy.Stun"/>, the same freeze
        /// mechanic the LPPE's Shock already uses (MV-708); a second stagger/stun mechanic would be a
        /// distinction with no behavioural difference.</summary>
        public const float DefaultStaggerSeconds = 0.6f;

        /// <summary>Spec: "cavitation direct damage 25-40% of one second of lance DPS" — 30%, the
        /// midpoint of that band, expressed as a fraction of <see cref="DamagePerSecond"/> so it keeps
        /// tracking the lance's own upgrades rather than needing a second authored number retuned in
        /// lockstep.</summary>
        public const float CavitationDamageFraction = 0.3f;

        /// <summary>A slow spinning bubble (spec) — much slower than <see cref="PlayerRocket"/>'s 14 m/s.</summary>
        public const float DefaultCavitationSpeed = 6f;

        [Header("Lance")]
        [SerializeField] private float range = DefaultRange;
        [SerializeField] private float coneHalfAngle = DefaultConeHalfAngle;
        [SerializeField] private float damagePerTick = DefaultDamagePerTick;
        [SerializeField] private float fireInterval = DefaultFireInterval;
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Cavitation")]
        [SerializeField] private float chargeSeconds = DefaultChargeSeconds;
        [SerializeField] private float cavitationCooldownSeconds = DefaultCavitationCooldownSeconds;
        [SerializeField] private float pullRadius = DefaultPullRadius;
        [SerializeField] private float pullDistance = DefaultPullDistance;
        [SerializeField] private float staggerSeconds = DefaultStaggerSeconds;
        [SerializeField] private float cavitationSpeed = DefaultCavitationSpeed;

        [Header("Aim source")]
        [Tooltip("Optional. If set, fires while the player aims and orients to their facing. " +
                 "If null, IsFiring drives it directly (useful for isolated testing).")]
        [SerializeField] private PlayerController aimSource;

        /// <summary>Whether the trigger is currently held — same contract as <see cref="WaterBlaster.IsFiring"/>.</summary>
        public bool IsFiring { get; private set; }

        /// <summary>Pure fire-gate decision (unit-testable), same shape as <see cref="WaterBlaster.ShouldEmit"/>.</summary>
        public static bool ShouldEmit(bool firingHeld, bool hasEnergy) => firingHeld && hasEnergy;

        public void SetFiring(bool firing) => IsFiring = firing;
        public bool IsEmitting => _lastEmitting;

        /// <summary>The energy tank, 0..1 — same tank shape the RCDA/LPPE drain from.</summary>
        public float EnergyNormalized => _tank != null ? _tank.Normalized : 1f;

        /// <summary>How far the lance reaches — the RCDA Range track's own bonus layered on top (spec:
        /// the weapon is picked up by Max's existing RCDA tracks, not a new set).</summary>
        public float Range => WeaponCatalog.EffectiveRange(
            range, WeaponSystemState.TrackLevel(WeaponTrackKind.Range), WeaponCatalog.DefaultRcdaRangePerLevel);

        public float ConeHalfAngle => WeaponCatalog.EffectiveConeHalfAngle(
            coneHalfAngle, WeaponSystemState.TrackLevel(WeaponTrackKind.Spread), WeaponCatalog.DefaultRcdaSpreadPerLevel);

        /// <summary>Damage one lance tick deals right now — the RCDA Damage track's own bonus layered on
        /// top, the same formula <see cref="WaterBlaster.EffectiveDamagePerTick"/> uses.</summary>
        public float EffectiveDamagePerTick => WeaponCatalog.EffectiveDamagePerTick(
            damagePerTick, WeaponSystemState.TrackLevel(WeaponTrackKind.Damage), WeaponCatalog.DefaultRcdaDamagePerLevel);

        public float FireInterval => fireInterval;

        /// <summary>What the lance actually outputs per second, per pierced target — AC5's comparison
        /// point against the RCDA's own <see cref="WaterBlaster.DamagePerSecond"/>.</summary>
        public float DamagePerSecond => fireInterval > 0f ? EffectiveDamagePerTick / fireInterval : 0f;

        /// <summary>Flat per-tick energy cost — the RCDA's own base drain rate, not track-scaled the way
        /// the RCDA's is (same "flat authored number" shape <see cref="PulseLaser.EnergyPerPulse"/> uses).</summary>
        public float EnergyPerTick => DevTuning.Or(DevTuning.PrimaryDepletionRate, BlasterTuning.EnergyPerSecond) * fireInterval;

        public float ChargeSeconds => chargeSeconds;
        public float CavitationCooldownSeconds => cavitationCooldownSeconds;

        /// <summary>Whether a cavitation shot could fire right now (spec: "a second cavitation shot is
        /// refused within 4s of the first").</summary>
        public bool CavitationReady => _cooldownRemaining <= 0f;

        /// <summary>The cavitation shot the most recent charge-and-release spawned, or null before the
        /// first one — the same "public accessor for a test" idiom as <see cref="PulseLaser.LastSpawnedPulseForTests"/>.</summary>
        public CavitationBubble LastSpawnedCavitationForTests { get; private set; }

        private float _tickTimer;
        private float _holdTimer;
        private float _cooldownRemaining;
        private bool _charged;
        private bool _wasFiring;
        private bool _lastEmitting;
        private bool _depleted;
        private EnergyPool _tank;

        private const int InitialHitBufferSize = 16;
        private Collider[] _hits = new Collider[InitialHitBufferSize];
        private static readonly List<IDamageable> s_buffer = new List<IDamageable>(4);
        private static readonly List<float> s_dist = new List<float>(4);

        private void Awake()
        {
            _tank = new EnergyPool(BlasterTuning.MaxEnergy, BlasterTuning.RegenPerSec, BlasterTuning.RegenDelay);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _tank.Tick(dt);
            if (_cooldownRemaining > 0f) _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - dt);

            if (aimSource != null)
            {
                IsFiring = aimSource.IsAiming;
                Vector3 f = aimSource.Facing;
                if (f.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(f, Vector3.up);
            }

            if (DevMode.IsAutoFiring) IsFiring = true;
            if (DevMode.IsInfiniteEnergy) _tank.Refill();

            if (IsFiring)
            {
                _holdTimer += dt;
                if (!_charged && _holdTimer >= chargeSeconds) _charged = true;
            }
            else
            {
                if (_wasFiring) Release(_holdTimer);
                _holdTimer = 0f;
                _charged = false;
            }
            _wasFiring = IsFiring;

            // Charging pauses the stream (spec: "the lance barrel fills with a bright core") — the
            // charge itself is the tell, not an interruption on top of an still-streaming lance.
            float cost = EnergyPerTick;
            bool wantsToStream = IsFiring && !_charged;
            if (_depleted && _tank.Normalized >= BlasterTuning.RechargeFraction) _depleted = false;
            else if (!_depleted && !_tank.CanSpend(cost)) _depleted = true;

            bool emitting = ShouldEmit(wantsToStream, !_depleted && _tank.CanSpend(cost));
            _lastEmitting = emitting;
            if (!emitting) { _tickTimer = 0f; return; }

            _tickTimer -= dt;
            if (_tickTimer > 0f) return;
            _tickTimer = fireInterval;

            if (!_tank.TrySpend(cost)) return;
            FireLanceTick();
        }

        /// <summary>Fires on release (button up). <paramref name="heldSeconds"/> is the length of the
        /// hold that just ended — charged (&gt;= <see cref="chargeSeconds"/>) and off cooldown fires the
        /// cavitation shot; anything else (an early release, or a charged release still on cooldown)
        /// fires one ordinary lance tick instead, so a charge that can't be spent is never a wasted shot
        /// (spec).</summary>
        private bool Release(float heldSeconds)
        {
            if (heldSeconds >= chargeSeconds && CavitationReady)
            {
                FireCavitation();
                return true;
            }
            FireLanceTick();
            return false;
        }

        /// <summary>Same growing-buffer idiom <see cref="WaterBlaster.OverlapSphereGrowing"/> uses
        /// (MV-666): a fixed-size buffer risks silently dropping robots in a crowded room the moment a
        /// query saturates it, and the lance's 9m base range only makes that more likely than the RCDA's
        /// own 5m. Grows only on saturation, so the steady-state per-tick path stays allocation-free.</summary>
        private int OverlapSphereGrowing(Vector3 origin, float radius)
        {
            int count;
            while ((count = Physics.OverlapSphereNonAlloc(
                       origin, radius, _hits, hitMask, QueryTriggerInteraction.Ignore)) == _hits.Length)
            {
                _hits = new Collider[_hits.Length * 2];
            }
            return count;
        }

        /// <summary>
        /// Gathers everything in range, keeps only what's inside the narrow lance cone (the codebase's
        /// established line-hit idiom — no raycast, see <see cref="SprayHit"/>) and in sight, sorts by
        /// distance along the aim axis, then damages only the closest <see cref="MaxPierceCount"/> — a
        /// third robot standing further back in the same line is never hit (AC1).
        /// </summary>
        private void FireLanceTick()
        {
            Vector3 origin = transform.position;
            Vector3 dir = transform.forward;
            float reach = Range;
            float cone = ConeHalfAngle;
            float tickDamage = EffectiveDamagePerTick;

            int count = OverlapSphereGrowing(origin, reach);

            s_buffer.Clear();
            s_dist.Clear();
            for (int i = 0; i < count; i++)
            {
                if (_hits[i] == null) continue;
                if (!_hits[i].TryGetComponent<IDamageable>(out var d) || !d.IsAlive || d.Team == Team.Player) continue;
                if (s_buffer.Contains(d)) continue;

                Vector3 pos = _hits[i].transform.position;
                if (!SprayHit.InCone(origin, dir, pos, reach, cone)) continue;
                if (!LineOfSight.Clear(origin, pos, _hits[i].transform)) continue;

                Vector3 to = pos - origin; to.y = 0f;
                s_buffer.Add(d);
                s_dist.Add(to.magnitude);
            }

            // Small N (a handful of overlapping colliders at most) — a plain insertion sort by distance
            // is allocation-free and simpler than pulling in a general sort for two list slots.
            for (int i = 1; i < s_buffer.Count; i++)
            {
                float key = s_dist[i];
                IDamageable keyD = s_buffer[i];
                int j = i - 1;
                while (j >= 0 && s_dist[j] > key)
                {
                    s_dist[j + 1] = s_dist[j];
                    s_buffer[j + 1] = s_buffer[j];
                    j--;
                }
                s_dist[j + 1] = key;
                s_buffer[j + 1] = keyD;
            }

            int pierced = Mathf.Min(MaxPierceCount, s_buffer.Count);
            for (int i = 0; i < pierced; i++)
            {
                s_buffer[i].TakeDamage(new DamageInfo(tickDamage, origin, dir, Team.Player, soak: true,
                    source: DamageSource.PrimaryWeapon));
            }
        }

        private void FireCavitation()
        {
            _cooldownRemaining = cavitationCooldownSeconds;
            Vector3 origin = transform.position;
            Vector3 dir = transform.forward;
            float damage = DamagePerSecond * CavitationDamageFraction;

            LastSpawnedCavitationForTests = CavitationBubble.Fire(
                origin, dir, cavitationSpeed, Range, damage, pullRadius, pullDistance, staggerSeconds);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.95f, 1f, 1f);
            Gizmos.DrawWireSphere(transform.position + transform.forward * range, 0.3f);
        }
#endif
    }
}
