using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Combat
{
    /// <summary>
    /// World 5's own primary (MV-1210, design v5 §3.1 equivalent — Lee's brief, 2026-10-17): LIGHT
    /// MISSILE — balls that launch straight out of Max, then home onto a target in a smooth, tightening
    /// arc ("I expect to see it circle around a lot"), re-targeting if that target dies, and firing more
    /// at once as COUNT is upgraded. Mirrors <see cref="Ricochet"/>'s own shape exactly: self-attached
    /// from <see cref="MaxWorlds.Player.PlayerController.Awake"/>, gated on
    /// <see cref="WeaponSystemState.ActivePrimary"/>, <see cref="IPrimaryEnergy"/>, the same
    /// <see cref="EnergyPool"/>/<see cref="BlasterTuning"/> tank shape and one-thumb "hold to fire"
    /// control, explicit-<c>dt</c> <see cref="Tick"/> for an EditMode test.
    ///
    /// Every PRIMARY-family track on this world's own board (<c>rig_board.ship.json</c>) resolves
    /// through an explicit per-level lookup here, same reasoning <see cref="Ricochet"/>'s own class doc
    /// gives: design's own numbers are not a straight line, and the ticket's "build them as written"
    /// rule means reproducing each table exactly. COUNT (<c>p_cnt</c>) is the one exception even to that
    /// — the board's own level IS the ball count directly, owned from level 1.
    /// </summary>
    [MaxWorlds.Core.PerfSection("combat")]
    public sealed class LightMissile : MonoBehaviour, IPrimaryEnergy
    {
        /// <summary>Rule 4: constant flight speed, m/s.</summary>
        public const float DefaultSpeed = 9f;

        /// <summary>Rule 1: "each ball costs 2.7 energy from the shared gauge, the same as a disc" —
        /// a flat authored number, not track-scaled, same shape as <see cref="Ricochet.DefaultEnergyPerDisc"/>.</summary>
        public const float DefaultEnergyPerBall = Ricochet.DefaultEnergyPerDisc;

        /// <summary>Rule 1: a volley's balls fan out 15 degrees apart, centred on the aim.</summary>
        public const float DefaultFanDegrees = 15f;

        /// <summary>POWER (<c>p_dmg</c>), levels 1..8 — the ticket's own table, built as written.</summary>
        private static readonly float[] PowerDamageByLevel = { 30f, 34f, 38f, 42f, 46f, 50f, 55f, 60f };

        /// <summary>TRACKING (<c>p_trk</c>) base turn rate, deg/s, levels 0..4.</summary>
        private static readonly float[] TrackingBaseDegPerSecByLevel = { 170f, 200f, 240f, 290f, 350f };

        /// <summary>RATE (<c>p_rof</c>) volley interval, seconds, levels 1..5.</summary>
        private static readonly float[] RateIntervalSecondsByLevel = { 1.0f, 0.9f, 0.8f, 0.7f, 0.6f };

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

        /// <summary>COUNT (<c>p_cnt</c>): balls per volley right now. The board's own level IS this
        /// number directly (owned from level 1) — no clamp table, no formula.</summary>
        public int BallsPerVolley => Mathf.Max(1, WeaponSystemState.LightMissileTrackLevel(LightMissileTrackKind.Count));

        /// <summary>POWER (<c>p_dmg</c>): a ball's damage right now.</summary>
        public float EffectiveDamagePerBall => PowerDamageByLevel[
            Mathf.Clamp(WeaponSystemState.TrackLevel(WeaponTrackKind.Damage), 1, PowerDamageByLevel.Length) - 1];

        /// <summary>RATE (<c>p_rof</c>): seconds between volleys right now.</summary>
        public float FireInterval => RateIntervalSecondsByLevel[
            Mathf.Clamp(WeaponSystemState.LightMissileTrackLevel(LightMissileTrackKind.Rate), 1, RateIntervalSecondsByLevel.Length) - 1];

        /// <summary>TRACKING (<c>p_trk</c>): a fresh ball's base turn rate right now, before the growth
        /// it picks up the longer it stays airborne (<see cref="LightMissileBall.CurrentTurnRateDegPerSec"/>).</summary>
        public float TrackingBaseDegPerSec => TrackingBaseDegPerSecByLevel[
            Mathf.Clamp(WeaponSystemState.LightMissileTrackLevel(LightMissileTrackKind.Tracking), 0, TrackingBaseDegPerSecByLevel.Length - 1)];

        public float EnergyPerBall => DefaultEnergyPerBall;

        /// <summary>MV-1210 CAPACITY (<c>p_cap</c>): the tank's own max right now — 140 at level 0,
        /// rising to 280 at this board's own level-5 cap. Same formula and shape as
        /// <see cref="Ricochet.EffectiveMaxEnergy"/>, read through <see cref="LightMissileTrackKind.Capacity"/>.</summary>
        public float EffectiveMaxEnergy => WeaponCatalog.EffectiveMaxEnergy(
            BlasterTuning.MaxEnergy, WeaponSystemState.LightMissileTrackLevel(LightMissileTrackKind.Capacity),
            WeaponCatalog.DefaultLppeCapacityPerLevel);

        private float _tickTimer;
        private bool _lastEmitting;
        private bool _depleted;
        private EnergyPool _tank;

        private void Awake()
        {
            _tank = new EnergyPool(EffectiveMaxEnergy, BlasterTuning.RegenPerSec, BlasterTuning.RegenDelay);

            // Self-attach shape (MV-1012-style): this component is added unconditionally from
            // PlayerController.Awake (code-driven scenes, no scene wiring), so it must resolve its own
            // aim source here rather than relying on one being wired in the editor.
            if (aimSource == null) aimSource = GetComponent<PlayerController>();
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The real per-frame update, pulled out to its own explicit-<paramref name="dt"/>
        /// method — same shape <see cref="Ricochet.Tick"/> uses — so an EditMode test can drive a
        /// controlled fire through the SAME logic <see cref="Update"/> calls.</summary>
        private void Tick(float dt)
        {
            // Buying a CAPACITY level must raise the tank's max immediately and hand the difference
            // straight to the current charge — same EnergyPool.Retune rule Ricochet.Tick already uses.
            _tank.Retune(EffectiveMaxEnergy);
            _tank.Tick(dt);

            if (aimSource != null)
            {
                IsFiring = aimSource.IsAiming;
                Vector3 f = aimSource.Facing;
                if (f.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(f, Vector3.up);
            }

            if (DevMode.IsAutoFiring) IsFiring = true;
            if (DevMode.IsInfiniteEnergy) _tank.Refill();

            // Rule 1: "a volley fires only if the tank can pay for the whole volley" — the cost check
            // below is for the FULL volley, not one ball at a time.
            float volleyCost = EnergyPerBall * BallsPerVolley;
            if (_depleted && _tank.Normalized >= BlasterTuning.RechargeFraction) _depleted = false;
            else if (!_depleted && !_tank.CanSpend(volleyCost)) _depleted = true;

            // This component is self-attached unconditionally from PlayerController.Awake, so it must
            // gate itself rather than relying on being added/removed — same shape Ricochet/Undertow use.
            bool emitting = WeaponSystemState.ActivePrimary == WeaponCatalog.PrimaryKind.LightMissile
                && ShouldEmit(IsFiring, !_depleted && _tank.CanSpend(volleyCost));
            _lastEmitting = emitting;

            if (!emitting) { _tickTimer = 0f; return; }

            float interval = FireInterval;
            _tickTimer -= dt;
            if (_tickTimer <= 0f)
            {
                _tickTimer += Mathf.Max(interval, 1e-4f);
                if (_tank.TrySpend(volleyCost)) FireVolley();
            }
        }

        private void FireVolley()
        {
            int count = BallsPerVolley;
            Vector3 origin = transform.position;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward; else forward.Normalize();

            int areaIndex = LightMissileBall.AreaIndexAt(origin);
            float damage = EffectiveDamagePerBall;
            float trackingBase = TrackingBaseDegPerSec;
            // MV-1211: POWER's own visible change — the same p_dmg level EffectiveDamagePerBall already
            // reads, clamped the same way, passed through so every ball in the volley shows it.
            int powerLevel = Mathf.Clamp(WeaponSystemState.TrackLevel(WeaponTrackKind.Damage), 1, PowerDamageByLevel.Length);

            // Rule 1: "balls in one volley prefer distinct targets" — one claimed-target set shared by
            // every ball this volley, each adding its own pick to it once (LightMissileBall.Tick).
            var claimed = new HashSet<IDamageable>();

            for (int i = 0; i < count; i++)
            {
                float angleOffset = (i - (count - 1) / 2f) * DefaultFanDegrees;
                Vector3 dir = Quaternion.Euler(0f, angleOffset, 0f) * forward;
                LightMissileBall.Spawn(origin, dir, DefaultSpeed, damage, trackingBase, areaIndex,
                    powerLevel, claimed);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.55f, 0.20f, 0.85f, 1f);
            Gizmos.DrawWireSphere(transform.position + transform.forward * 3f, 0.2f);
        }
#endif
    }
}
