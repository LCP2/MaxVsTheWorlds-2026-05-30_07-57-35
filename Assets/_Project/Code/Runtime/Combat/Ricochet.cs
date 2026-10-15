using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Combat
{
    /// <summary>
    /// The City's own primary (MV-1195, design v5 §3.1) — RICOCHET: bouncing discs, granted by World 2's
    /// finale Core in place of UNDERTOW (which now hands over to the Reef instead, via the City's own
    /// bossless-exit interim hand-over — see <see cref="MaxWorlds.VFX.WorldFinaleGate"/>). Same
    /// <see cref="EnergyPool"/>/<see cref="BlasterTuning"/> tank shape and one-thumb "hold to fire"
    /// control as <see cref="Undertow"/>/<see cref="PulseLaser"/>; what comes out is a pooled, bouncing
    /// <see cref="RicochetDisc"/> rather than a continuous stream or a discrete seeking pulse.
    ///
    /// Every PRIMARY-family track on the City's own board (<c>rig_board.city.json</c>) resolves through
    /// an explicit per-level lookup here rather than one of <see cref="WeaponCatalog"/>'s shared linear
    /// formulas — design v5's own numbers are not a straight line (POWER: 12,15,18,22,26,30,35,40), and
    /// the ticket's "build them as written" rule means reproducing that table exactly, not a close
    /// linear approximation. RICOCHETS (<c>p_ric</c>) is the one exception even to that: the board's own
    /// level IS the bounce count (owned from level 2), so <see cref="BouncesPerDisc"/> just reads the
    /// level directly.
    /// </summary>
    [MaxWorlds.Core.PerfSection("combat")]
    public sealed class Ricochet : MonoBehaviour, IPrimaryEnergy
    {
        public const float DefaultSpeed = 20f;
        public const float DefaultRadius = 0.25f;
        public const float DefaultLifetimeSeconds = 2.5f;

        /// <summary>"Each disc costs 2.7 energy from the shared gauge, the same as a Pulse Emitter
        /// shot" (spec) — a flat authored number, not track-scaled, same shape as
        /// <see cref="PulseLaser.EnergyPerPulse"/>.</summary>
        public const float DefaultEnergyPerDisc = PulseLaser.DefaultEnergyPerPulse;

        /// <summary>"Bank bonus: each bounce already taken adds 20% damage" (spec).</summary>
        public const float DefaultBankBonusPerBounce = 0.2f;

        /// <summary>POWER (<c>p_dmg</c>), levels 1..8 — design v5's own table, reproduced exactly
        /// rather than approximated by <see cref="WeaponCatalog.EffectiveDamagePerTick"/>'s linear
        /// curve (the real numbers step by 3,3,4,4,4,5,5, not a single constant).</summary>
        private static readonly float[] PowerDamageByLevel = { 12f, 15f, 18f, 22f, 26f, 30f, 35f, 40f };

        /// <summary>RATE (<c>p_rof</c>), discs/second, levels 1..5.</summary>
        private static readonly float[] RateDiscsPerSecondByLevel = { 3.0f, 3.6f, 4.3f, 5.1f, 6.0f };

        /// <summary>AIMING (<c>p_aim</c>) turn rate, deg/s, levels 0..4 (level 0 = unowned, dead
        /// straight).</summary>
        private static readonly float[] AimTurnRateDegPerSecByLevel = { 0f, 45f, 90f, 140f, 200f };

        /// <summary>AIMING's own lookahead distance, metres, levels 0..4 (level 0 unused — no steering,
        /// so no lookahead to speak of).</summary>
        private static readonly float[] AimLookaheadMetresByLevel = { 0f, 6f, 8f, 10f, 12f };

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

        /// <summary>The energy tank, 0..1 — same tank shape every other primary drains from.</summary>
        public float EnergyNormalized => _tank != null ? _tank.Normalized : 1f;

        /// <summary>RICOCHETS (<c>p_ric</c>): bounces a fresh disc has to spend. The board's own level
        /// IS this number (owned from level 2) — no clamp table, no formula.</summary>
        public int BouncesPerDisc => Mathf.Max(0, WeaponSystemState.RicochetTrackLevel(RicochetTrackKind.Ricochets));

        /// <summary>POWER (<c>p_dmg</c>): a disc's base damage right now, before the bank bonus.</summary>
        public float EffectiveDamagePerDisc => PowerDamageByLevel[
            Mathf.Clamp(WeaponSystemState.TrackLevel(WeaponTrackKind.Damage), 1, PowerDamageByLevel.Length) - 1];

        /// <summary>RATE (<c>p_rof</c>): discs leaving per second right now.</summary>
        public float DiscsPerSecond => RateDiscsPerSecondByLevel[
            Mathf.Clamp(WeaponSystemState.RicochetTrackLevel(RicochetTrackKind.Rate), 1, RateDiscsPerSecondByLevel.Length) - 1];

        public float FireInterval => DiscsPerSecond > 0f ? 1f / DiscsPerSecond : 0f;

        /// <summary>AIMING (<c>p_aim</c>): how fast a disc turns toward the nearest robot ahead right
        /// now — 0 (dead straight) while unowned.</summary>
        public float AimTurnRateDegPerSec => AimTurnRateDegPerSecByLevel[
            Mathf.Clamp(WeaponSystemState.RicochetTrackLevel(RicochetTrackKind.Aiming), 0, AimTurnRateDegPerSecByLevel.Length - 1)];

        /// <summary>AIMING's own lookahead distance right now.</summary>
        public float AimLookaheadMetres => AimLookaheadMetresByLevel[
            Mathf.Clamp(WeaponSystemState.RicochetTrackLevel(RicochetTrackKind.Aiming), 0, AimLookaheadMetresByLevel.Length - 1)];

        public float EnergyPerDisc => DefaultEnergyPerDisc;

        public float BankBonusPerBounce => DefaultBankBonusPerBounce;

        private float _tickTimer;
        private bool _lastEmitting;
        private bool _depleted;
        private EnergyPool _tank;

        private void Awake()
        {
            _tank = new EnergyPool(BlasterTuning.MaxEnergy, BlasterTuning.RegenPerSec, BlasterTuning.RegenDelay);

            // MV-1012-style self-attach: this component is added unconditionally from
            // PlayerController.Awake (code-driven scenes, no scene wiring), so it must resolve its own
            // aim source here rather than relying on one being wired in the editor.
            if (aimSource == null) aimSource = GetComponent<PlayerController>();
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The real per-frame update, pulled out to its own explicit-<paramref name="dt"/>
        /// method — same shape <see cref="Undertow.Tick"/>/<see cref="PulseLaser.Tick"/> already use —
        /// so an EditMode test can drive a controlled fire through the SAME logic <see cref="Update"/>
        /// calls.</summary>
        private void Tick(float dt)
        {
            _tank.Tick(dt);

            if (aimSource != null)
            {
                IsFiring = aimSource.IsAiming;
                Vector3 f = aimSource.Facing;
                if (f.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(f, Vector3.up);
            }

            if (DevMode.IsAutoFiring) IsFiring = true;
            if (DevMode.IsInfiniteEnergy) _tank.Refill();

            float cost = EnergyPerDisc;
            if (_depleted && _tank.Normalized >= BlasterTuning.RechargeFraction) _depleted = false;
            else if (!_depleted && !_tank.CanSpend(cost)) _depleted = true;

            // This component is self-attached unconditionally from PlayerController.Awake (see
            // Undertow's own ActivePrimary gate for the same self-attach-then-no-op shape), so it must
            // gate itself rather than relying on being added/removed.
            bool emitting = WeaponSystemState.ActivePrimary == WeaponCatalog.PrimaryKind.Ricochet
                && ShouldEmit(IsFiring, !_depleted && _tank.CanSpend(cost));
            _lastEmitting = emitting;

            if (!emitting) { _tickTimer = 0f; return; }

            float interval = FireInterval;
            _tickTimer -= dt;
            if (_tickTimer <= 0f)
            {
                _tickTimer += Mathf.Max(interval, 1e-4f);
                if (_tank.TrySpend(cost)) FireOneDisc();
            }
        }

        private void FireOneDisc()
        {
            RicochetDisc.Spawn(transform.position, transform.forward, DefaultSpeed, BouncesPerDisc,
                EffectiveDamagePerDisc, BankBonusPerBounce, AimTurnRateDegPerSec, AimLookaheadMetres,
                DefaultRadius, DefaultLifetimeSeconds);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.35f, 0.65f, 1f, 1f);
            Gizmos.DrawWireSphere(transform.position + transform.forward * 3f, 0.2f);
        }
#endif
    }
}
