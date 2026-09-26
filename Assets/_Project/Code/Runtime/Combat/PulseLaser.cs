using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.UI;
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
    [MaxWorlds.Core.PerfSection("combat")]
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

        /// <summary>MV-844: POWER's (<c>p_dmg</c>'s) own visual-strength fraction — 0 at L1 (today's
        /// bolt, unchanged) rising to 1 at World 2's own cap. Reads <see cref="RigBoard.MaxLevel"/> live
        /// rather than <see cref="WeaponCatalog.MaxLevel(WeaponTrackKind)"/>'s hardcoded 4, since that
        /// cap stays World 1 (RCDA)-only while World 2's <c>p_dmg</c> now diverges to 8 — same
        /// live-read-over-literal shape <see cref="WeaponCatalog.MaxLevel(AbilityKind)"/> uses for Force
        /// Field's own per-world cap (MV-840).</summary>
        public float PowerVisualStrength => WeaponCatalog.VisualStrengthFraction(
            WeaponSystemState.TrackLevel(WeaponTrackKind.Damage), RigBoard.MaxLevel("p_dmg"));

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

        /// <summary>MV-846 CAPACITY (<c>p_cap</c>): the tank's own max right now — 140 at level 0,
        /// rising to 315 at the track's level-5 cap. Distinct from World 1's <c>p_flw</c>, which cuts
        /// the RCDA's drain and never touches a tank's max at all.</summary>
        public float EffectiveMaxEnergy => WeaponCatalog.EffectiveMaxEnergy(
            BlasterTuning.MaxEnergy, WeaponSystemState.LppeTrackLevel(LppeTrackKind.Capacity),
            WeaponCatalog.DefaultLppeCapacityPerLevel);

        private float _tickTimer;
        private bool _lastEmitting;
        private bool _depleted;
        private bool _windupFired;
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

        /// <summary>MV-858 ARC's own range: how far from the hit point the arc reaches, in metres
        /// (spec: "within 8 m of the hit point").</summary>
        public const float ArcRange = 8f;

        /// <summary>MV-858 ARC's own damage share: the fraction of the landing pulse's damage the arc
        /// deals to the robot it reaches (spec: "50% of the pulse's damage").</summary>
        public const float ArcDamageFraction = 0.5f;

        /// <summary>MV-862 FOCUS: how long <see cref="CurrentTarget"/> keeps reporting the last pulse's
        /// lock after <see cref="FireTick"/> stops running — the spec's "while Max is firing or has
        /// fired in the last 0.5s", so a Sentinel with FOCUS on keeps sharing Max's target through the
        /// gaps between pulses, not just on the exact frame one lands.</summary>
        public const float CurrentTargetHoldSeconds = 0.5f;

        private RobotEnemy _lastPulseTarget;
        private float _timeSinceLastPulse = float.MaxValue;

        /// <summary>MV-862 FOCUS: the robot Max's own LPPE most recently locked onto, or null — held
        /// for <see cref="CurrentTargetHoldSeconds"/> after the pulse that locked it fired, and cleared
        /// instantly (read live off <see cref="RobotEnemy.IsAlive"/>, never cached) the moment that
        /// robot dies, even inside the hold window.</summary>
        public RobotEnemy CurrentTarget =>
            _lastPulseTarget != null && _lastPulseTarget.IsAlive && _timeSinceLastPulse <= CurrentTargetHoldSeconds
                ? _lastPulseTarget : null;

        private void Awake()
        {
            _tank = new EnergyPool(EffectiveMaxEnergy, BlasterTuning.RegenPerSec, BlasterTuning.RegenDelay);

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
            // MV-846: buying a CAPACITY level must raise the tank's max immediately and hand the
            // difference straight to the current charge (EnergyPool.Retune's own "a bigger tank you
            // have to earn back is a worse upgrade than one that just tops you up" rule) — checked
            // every frame rather than on a WeaponSystemState.Changed subscription since Retune is
            // already a no-op once the max stops moving.
            _tank.Retune(EffectiveMaxEnergy);
            _tank.Tick(dt);
            TickHitStreaks(dt);
            _timeSinceLastPulse += dt;

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
            if (!emitting) { _tickTimer = 0f; _windupFired = false; return; }

            _tickTimer -= dt;

            // MV-770 spec part 2, item 1: the windup plays inside this SAME countdown, never by
            // delaying it (the ticket's own "do not change fire cadence" line) — once within the lead
            // window of a shot that will land Shock (predicted off the existing hit-streak state, not
            // by re-deriving SeekerPulse's own target lock), play it once and wait for the real fire.
            if (!_windupFired && _tickTimer > 0f
                && _tickTimer <= CombatVfxTuning.LppeWindup().LeadSeconds && AnyStreakAtShockThreshold())
            {
                _windupFired = true;
                if (_vfx != null) _vfx.Windup(transform.position, transform.forward);
            }

            if (_tickTimer > 0f) return;
            _tickTimer = PulseInterval;
            _windupFired = false;

            if (!_tank.TrySpend(cost)) return;
            FireTick();
        }

        /// <summary>True while some tracked robot's hit streak sits one hit away from Shock (spec:
        /// "every 4th pulse hit") and its window hasn't lapsed — the windup's own prediction of "the
        /// next pulse that lands will trigger Shock", built entirely from state <see cref="RegisterHit"/>
        /// already tracks rather than re-deriving <see cref="SeekerPulse"/>'s own target lock.</summary>
        private bool AnyStreakAtShockThreshold()
        {
            foreach (var pair in _hitStreak)
            {
                if (pair.Value % ShockHitInterval != ShockHitInterval - 1) continue;
                if (_hitStreakTimer.TryGetValue(pair.Key, out float remaining) && remaining > 0f) return true;
            }
            return false;
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
                powerLevelFraction: PowerVisualStrength);
            LastSpawnedPulseForTests = pulse;
            _lastPulseTarget = pulse.Target;
            _timeSinceLastPulse = 0f;

            // MV-758: the muzzle punctuation — one per shot, under 0.22s cadence so it can't smear.
            if (_vfx != null) _vfx.Muzzle(origin, dir);

            // MV-770 spec part 2, item 2: one weapon-arm recoil kick per pulse — MaxRig owns the actual
            // kick (same "signal in, presentation elsewhere" split as ShockPulseLanded/RocketMuzzle).
            HudSignals.EmitLppePulseFired(origin, dir);
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
            if (isShockHit)
            {
                target.Stun(ShockStunSeconds);
                // MV-770: "this half matters more than the pixels" — the punctuation hit is the one
                // GameFeel wires hitstop/shake to, never a plain pulse (a stream of hitstops on every
                // hit would read as lag, not weight).
                HudSignals.EmitShockPulseLanded(target.transform.position);
            }

            // MV-758: the impact beat — a normal flash+sparks, or the Shock-carrying hit's own
            // visibly distinct beat, so "a player must be able to count to the stun by eye" (spec).
            // +0.6m: a robot's transform.position is its ground-level pivot (CombatVfx.OnDamage's own
            // "pos + Vector3.up * 0.6f" convention) — landing the flash there instead reads as hitting
            // the floor, not the robot.
            if (_vfx != null) _vfx.Impact(target.transform.position + Vector3.up * 0.6f, damage, isShockHit);

            TryArc(target, damage);
        }

        /// <summary>MV-858 ARC (<c>p_frk</c>, relabelled from FORK): every pulse hit that lands on a
        /// robot — kill or not — arcs once to the nearest OTHER alive robot within <see cref="ArcRange"/>
        /// of the hit point with a clear line of sight from it, dealing <see cref="ArcDamageFraction"/>
        /// of the landing pulse's own damage. Replaces the old kill/near-death-only trigger (Lee: "it
        /// works inconsistently (almost never)") — this fires on literally every hit, so a group fight
        /// reads as constantly arcing rather than the old rare tell. Applied directly (no travelling
        /// second pulse), so there is nothing left for it to invoke a further arc from — "cannot arc
        /// again" is true by construction, not by a canFork-style flag.</summary>
        private void TryArc(RobotEnemy hitTarget, float hitDamage)
        {
            if (WeaponSystemState.LppeTrackLevel(LppeTrackKind.Arc) < 1) return;

            Vector3 hitPoint = hitTarget.transform.position;
            RobotEnemy next = NearestOtherAliveRobotWithLineOfSight(hitTarget, hitPoint, ArcRange);
            if (next == null) return;

            // MV-858: dormant robots count as valid arc targets, and the arc wakes them (spec) —
            // unlike the old FORK search, which excluded a dormant candidate outright.
            if (next.IsDormant) next.Activate();

            Vector3 targetPoint = next.transform.position;
            next.TakeDamage(new DamageInfo(hitDamage * ArcDamageFraction, targetPoint,
                (targetPoint - hitPoint).normalized, Team.Player, source: DamageSource.PrimaryWeapon));

            if (_vfx != null) _vfx.Arc(hitPoint, targetPoint);
        }

        /// <summary>The nearest alive robot other than <paramref name="exclude"/> within
        /// <paramref name="range"/> of <paramref name="from"/> with a clear line of sight from it (MV-858
        /// spec: "with a clear line of sight from it"), ON THE SAME COMBAT LEVEL as <paramref name="from"/>
        /// (MV-944) — ARC's own target pick. Dormant candidates are included on purpose (spec: "Dormant
        /// robots count"); no lock-cone angle check, same reasoning the old FORK search used: the cone
        /// gates the ORIGINAL shot's acquisition, an arc is a direct release at whatever else is nearby.</summary>
        private static RobotEnemy NearestOtherAliveRobotWithLineOfSight(RobotEnemy exclude, Vector3 from, float range)
        {
            var active = RobotEnemy.Active;
            MapData map = EnemyNavigation.Map;
            RobotEnemy best = null;
            float bestSq = float.MaxValue;
            float rangeSq = range * range;

            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy candidate = active[i];
                if (candidate == null || candidate == exclude || !candidate.IsAlive) continue;
                if (!CombatLevel.SameLevel(map, from, candidate.transform.position)) continue;

                float distSq = (candidate.transform.position - from).sqrMagnitude;
                if (distSq > rangeSq) continue;
                if (HomingSteering.BlockedByGeometry(from, candidate.transform.position, out _)) continue;
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
