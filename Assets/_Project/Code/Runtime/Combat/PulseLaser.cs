using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Combat
{
    /// <summary>
    /// World 2's primary (MV-708) — the Locked-Phase Pulse Emitter (LPPE): fires discrete homing
    /// pulses (<see cref="SeekerPulse"/>) rather than <see cref="WaterBlaster"/>'s continuous cone. Same
    /// one-thumb control (auto-fires while the aim stick is held) and the same <see cref="BlasterTuning"/>
    /// tank shape, so swapping which primary is active (MV-689) never changes how firing FEELS to hold,
    /// only what comes out.
    ///
    /// Adds Shock (the World 2 special): every <see cref="ShockHitInterval"/>th pulse to land on the
    /// SAME robot within <see cref="ShockWindowSeconds"/> of the last one stuns it. Tracked here, not on
    /// <see cref="SeekerPulse"/>, since the combo spans many short-lived pulse instances fired over time.
    /// </summary>
    public sealed class PulseLaser : MonoBehaviour
    {
        public const float DefaultPulseInterval = 0.22f;
        public const float DefaultDamagePerPulse = 9f;
        public const float DefaultLockRange = 14f;
        public const float DefaultLockHalfAngle = 35f;

        /// <summary>MV-768 RATE (<c>p_rof</c>): the fire interval a maxed track reaches (board comment:
        /// "0.22s -&gt; 0.16s over these 4 levels").</summary>
        public const float DefaultRateFloorInterval = 0.16f;

        /// <summary>Flat per-pulse cost from the same tank shape as <see cref="BlasterTuning"/> (spec:
        /// "2.7 per pulse ... matching the RCDA drain") — at the authored 0.22s cadence this is ~12.3/s,
        /// within a hair of the RCDA's own 12.16/s (<see cref="BlasterTuning.EnergyPerSecond"/>).</summary>
        public const float DefaultEnergyPerPulse = 2.7f;

        public const float DefaultPulseSpeed = 18f;
        public const float DefaultPulseTurnRateDegPerSec = 360f;
        public const float DefaultPulseLifetime = 1.2f;

        /// <summary>Every 4th pulse hit on the same robot triggers Shock (spec: "every 4th pulse hit").</summary>
        public const int ShockHitInterval = 4;
        /// <summary>The rolling window a hit streak survives without a fresh hit (spec: "within 3s").</summary>
        public const float ShockWindowSeconds = 3f;
        /// <summary>How long Shock freezes its target (spec: "stuns it 0.5s").</summary>
        public const float ShockStunSeconds = 0.5f;

        [Header("Pulse cadence")]
        [SerializeField] private float pulseInterval = DefaultPulseInterval;
        [SerializeField] private float damagePerPulse = DefaultDamagePerPulse;
        [SerializeField] private float lockRange = DefaultLockRange;

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

        /// <summary>The energy tank, 0..1 — same tank shape the RCDA drains from.</summary>
        public float EnergyNormalized => _tank != null ? _tank.Normalized : 1f;

        /// <summary>How far the lock cone reaches, in metres — scaled by the RCDA's own Range track
        /// (spec: "LockRange 14m scaled by the Range track").</summary>
        public float LockRange => WeaponCatalog.EffectiveRange(
            lockRange, WeaponSystemState.TrackLevel(WeaponTrackKind.Range), WeaponCatalog.DefaultRcdaRangePerLevel);

        /// <summary>Damage one pulse deals right now — scaled by the same Damage track curve as the RCDA.</summary>
        public float EffectiveDamagePerPulse => WeaponCatalog.EffectiveDamagePerTick(
            damagePerPulse, WeaponSystemState.TrackLevel(WeaponTrackKind.Damage), WeaponCatalog.DefaultRcdaDamagePerLevel);

        /// <summary>The interval between pulses right now — scaled by RATE (<c>p_rof</c>, MV-768),
        /// exactly as <see cref="EffectiveDamagePerPulse"/>/<see cref="LockRange"/> are scaled by
        /// Damage/Range. Routed through <see cref="WeaponSystemState"/>, never <see cref="RigState"/>
        /// directly, same rule every other track here follows.</summary>
        public float PulseInterval => WeaponCatalog.EffectivePulseInterval(
            pulseInterval, WeaponSystemState.LppeTrackLevel(LppeTrackKind.Rate), DefaultRateFloorInterval,
            WeaponCatalog.MaxLevel(LppeTrackKind.Rate));

        /// <summary>Energy one pulse costs — a flat authored number (spec), not track-scaled the way
        /// the RCDA's per-tick cost is.</summary>
        public float EnergyPerPulse => DefaultEnergyPerPulse;

        private float _tickTimer;
        private bool _lastEmitting;
        private bool _depleted;
        private EnergyPool _tank;
        private LppeVfx _vfx;

        private readonly Dictionary<RobotEnemy, int> _hitStreak = new Dictionary<RobotEnemy, int>();
        private readonly Dictionary<RobotEnemy, float> _hitStreakTimer = new Dictionary<RobotEnemy, float>();
        private readonly List<RobotEnemy> _expiredStreaksScratch = new List<RobotEnemy>();
        private readonly List<KeyValuePair<RobotEnemy, float>> _decrementedStreaksScratch =
            new List<KeyValuePair<RobotEnemy, float>>();

        /// <summary>The pulse the most recent <see cref="FireTick"/> spawned, or null before the first
        /// shot — the same "public accessor for a test" idiom as <c>HomingMissile.ShaftColorForTests</c>.</summary>
        public SeekerPulse LastSpawnedPulseForTests { get; private set; }

        /// <summary>MV-768 FORK (<c>p_frk</c>): the pulse most recently released by
        /// <see cref="RegisterKill"/>, or null if none has fired yet — the resolved value the ticket's
        /// own test asserts against, same idiom as <see cref="LastSpawnedPulseForTests"/>.</summary>
        public SeekerPulse LastForkedPulseForTests { get; private set; }

        private void Awake()
        {
            _tank = new EnergyPool(BlasterTuning.MaxEnergy, BlasterTuning.RegenPerSec, BlasterTuning.RegenDelay);

            // MV-758: same "resolve-or-attach, then Init explicitly" shape as WaterBlaster/WaterVfx —
            // neither Awake nor OnEnable reliably run for AddComponent outside Play mode.
            _vfx = GetComponent<LppeVfx>();
            if (_vfx == null) _vfx = gameObject.AddComponent<LppeVfx>();
            _vfx.Init();

            // MV-739: self-attached from PlayerController.Awake (code-driven scenes, no scene
            // wiring) — unlike WaterBlaster, which is baked into Backyard_Slice.unity with aimSource
            // pre-wired by the old Stage35BlasterScaffold editor tool, this instance never goes
            // through that scaffold, so it resolves its own aim source here the first time it needs
            // one. Left alone (stays null) for a bare test fixture built on its own GameObject.
            if (aimSource == null) aimSource = GetComponent<PlayerController>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _tank.Tick(dt);
            TickHitStreaks(dt);

            if (aimSource != null)
            {
                IsFiring = aimSource.IsAiming;
                Vector3 f = aimSource.Facing;
                if (f.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(f, Vector3.up);
            }

            if (DevMode.IsAutoFiring) IsFiring = true;
            if (DevMode.IsInfiniteEnergy) _tank.Refill();

            float cost = EnergyPerPulse;
            if (_depleted && _tank.Normalized >= BlasterTuning.RechargeFraction) _depleted = false;
            else if (!_depleted && !_tank.CanSpend(cost)) _depleted = true;

            // MV-739: only actually fires while the LPPE is the equipped primary — this component is
            // self-attached unconditionally from PlayerController.Awake (see ShoulderRack's own
            // SecondaryKind gate for the same self-attach-then-no-op shape), so it must gate itself
            // rather than relying on being added/removed.
            bool emitting = WeaponSystemState.ActivePrimary == WeaponCatalog.PrimaryKind.Lppe
                && ShouldEmit(IsFiring, !_depleted && _tank.CanSpend(cost));
            _lastEmitting = emitting;
            if (!emitting) { _tickTimer = 0f; return; }

            _tickTimer -= dt;
            if (_tickTimer > 0f) return;
            _tickTimer = PulseInterval;

            if (!_tank.TrySpend(cost)) return;
            FireTick();
        }

        /// <summary>Decays every tracked robot's Shock hit-streak window, dropping it once it lapses —
        /// so a robot that hasn't been hit again for <see cref="ShockWindowSeconds"/> starts its combo
        /// over rather than banking hits from a fight that ended.</summary>
        private void TickHitStreaks(float dt)
        {
            if (_hitStreakTimer.Count == 0) return;

            _expiredStreaksScratch.Clear();
            _decrementedStreaksScratch.Clear();
            foreach (var pair in _hitStreakTimer)
            {
                float remaining = pair.Value - dt;
                if (remaining <= 0f) _expiredStreaksScratch.Add(pair.Key);
                else _decrementedStreaksScratch.Add(new KeyValuePair<RobotEnemy, float>(pair.Key, remaining));
            }
            // Apply writes only after the enumeration above has fully closed -- writing through the
            // indexer to an EXISTING key still bumps Dictionary's version counter, so doing it inside
            // the foreach throws InvalidOperationException on the very next MoveNext() (MV-751).
            for (int i = 0; i < _decrementedStreaksScratch.Count; i++)
                _hitStreakTimer[_decrementedStreaksScratch[i].Key] = _decrementedStreaksScratch[i].Value;
            for (int i = 0; i < _expiredStreaksScratch.Count; i++)
            {
                _hitStreakTimer.Remove(_expiredStreaksScratch[i]);
                _hitStreak.Remove(_expiredStreaksScratch[i]);
            }
        }

        private void FireTick()
        {
            Vector3 origin = transform.position;
            Vector3 dir = transform.forward;

            SeekerPulse pulse = SeekerPulse.Fire(origin, dir, DefaultPulseSpeed, DefaultPulseTurnRateDegPerSec,
                DefaultPulseLifetime, EffectiveDamagePerPulse, LockRange, DefaultLockHalfAngle, RegisterHit,
                onKill: RegisterKill);
            LastSpawnedPulseForTests = pulse;

            // MV-758: the muzzle punctuation — one per shot, under 0.22s cadence so it can't smear.
            if (_vfx != null) _vfx.Muzzle(origin, dir);
        }

        /// <summary>Shock: every <see cref="ShockHitInterval"/>th pulse to land on the SAME robot within
        /// the rolling window stuns it. Bosses are immune by construction — <see cref="SeekerPulse"/>
        /// only ever locks onto a <see cref="RobotEnemy"/>, which a boss (e.g. <c>BigBermudaBoss</c>) is
        /// never, so no boss-specific exclusion is needed here.</summary>
        private void RegisterHit(RobotEnemy target, float damage)
        {
            if (target == null) return;
            int count = _hitStreak.TryGetValue(target, out int c) ? c + 1 : 1;
            _hitStreak[target] = count;
            _hitStreakTimer[target] = ShockWindowSeconds;

            bool isShockHit = count % ShockHitInterval == 0;
            if (isShockHit) target.Stun(ShockStunSeconds);

            // MV-758: the impact beat — a normal flash+sparks, or the Shock-carrying hit's own
            // visibly distinct beat, so "a player must be able to count to the stun by eye" (spec).
            // +0.6m: a robot's transform.position is its ground-level pivot (CombatVfx.OnDamage's own
            // "pos + Vector3.up * 0.6f" convention) — landing the flash there instead reads as hitting
            // the floor, not the robot.
            if (_vfx != null) _vfx.Impact(target.transform.position + Vector3.up * 0.6f, damage, isShockHit);
        }

        /// <summary>MV-768 FORK (<c>p_frk</c>): a pulse whose damage KILLED its locked target releases
        /// one further pulse at the nearest OTHER valid target within the LPPE's current lock range,
        /// fired from the kill point. Never chains — <see cref="SeekerPulse.Fire"/> is called with
        /// <c>canFork: false</c>, so however many targets the forked pulse itself goes on to kill, it
        /// can never trigger a further fork (the board comment's own "must not chain" rule, enforced by
        /// <see cref="SeekerPulse.ApplyHit"/> never reporting a kill for a pulse fired that way).</summary>
        private void RegisterKill(RobotEnemy killedTarget, Vector3 point)
        {
            if (WeaponSystemState.LppeTrackLevel(LppeTrackKind.Fork) < 1) return;

            RobotEnemy next = NearestOtherAliveRobotInRange(killedTarget, point, LockRange);
            if (next == null) return;

            LastForkedPulseForTests = SeekerPulse.Fire(point, next.transform.position - point,
                DefaultPulseSpeed, DefaultPulseTurnRateDegPerSec, DefaultPulseLifetime,
                EffectiveDamagePerPulse, LockRange, DefaultLockHalfAngle, RegisterHit,
                forcedTarget: next, canFork: false);
        }

        /// <summary>The nearest alive, awake robot other than <paramref name="exclude"/> within
        /// <paramref name="range"/> of <paramref name="from"/> — FORK's own target pick. No lock-cone
        /// angle check: the cone gates the ORIGINAL shot's acquisition; a fork is a direct release at
        /// whatever else is nearby, same shape as <see cref="SeekerPulse"/>'s own
        /// <c>AcquireTarget</c> minus the angle term.</summary>
        private static RobotEnemy NearestOtherAliveRobotInRange(RobotEnemy exclude, Vector3 from, float range)
        {
            var active = RobotEnemy.Active;
            RobotEnemy best = null;
            float bestSq = float.MaxValue;
            float rangeSq = range * range;

            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy candidate = active[i];
                if (candidate == null || candidate == exclude || !candidate.IsAlive || candidate.IsDormant) continue;

                float distSq = (candidate.transform.position - from).sqrMagnitude;
                if (distSq > rangeSq) continue;
                if (distSq < bestSq) { bestSq = distSq; best = candidate; }
            }
            return best;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.55f, 0.95f, 1f, 1f);
            Gizmos.DrawWireSphere(transform.position + transform.forward * lockRange, 0.3f);
        }
#endif
    }
}
