using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Upgrades;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Combat
{
    /// <summary>
    /// Slice gadget (YT-35) — Spray archetype. While <see cref="IsFiring"/> is
    /// driven true (player holds aim), auto-fires a short-range stream: ticks at
    /// a fixed cadence, spends energy, sphere-casts forward, and applies damage
    /// (+soak tag) to every <see cref="IDamageable"/> in the stream. Energy binds
    /// to the HUD (YT-30) via <see cref="Energy"/>.
    ///
    /// Since the weapon epic (YT-127/YT-129) this is Max's <b>garden hose</b>: the water
    /// short-circuits the robots (the existing damage, re-themed — a spray shorts them out).
    /// Its OPENING spray is deliberately short and wide — weak but forgiving — the base state
    /// before any nozzle upgrade (YT-133) narrows or lengthens it. The hose no longer tethers
    /// to a tap (WV-233 reverses YT-129/130): Max carries it freely and it self-supplies from
    /// power cells (see <see cref="Update"/>); the spray reach here is a separate, much
    /// shorter number, unrelated to how far Max may roam.
    ///
    /// All firing visuals live in <see cref="WaterVfx"/> (YT-47), which this attaches
    /// to itself at Awake and drives with cosmetic-only calls. The VFX never feeds back
    /// into fire gating, energy, or damage.
    /// </summary>
    public sealed class WaterBlaster : MonoBehaviour
    {
        /// <summary>Authored base spray reach in metres (YT-129, retuned MV-280, widened MV-289): the
        /// 0.6 recut's 3m base — combined with the recalibrated robots and an under-tough Max — made
        /// Area 1 unplayable, so MV-289 widens the opening reach to a forgiving 5m. The RCDA Range
        /// track (MV-263) extends this base by up to 3x at its max level
        /// (<see cref="WeaponCatalog.DefaultRcdaRangePerLevel"/> is tuned against THIS value — change
        /// them together). Nozzle upgrades (YT-133) narrow/lengthen it further.</summary>
        public const float DefaultRange = 5f;

        [Header("Stream")]
        // Also baked in Backyard_Slice.unity — keep that value in sync with DefaultRange above.
        [SerializeField] private float range = DefaultRange;
        [SerializeField] private float radius = 0.6f;
        // Unchanged by MV-289 (still 4/0.1 = 40 DPS): the retuned Rusher HP (32 base, ~45 effective)
        // already lands the ~1.1-1.5s TTK AC1 asks for at the existing DPS — cutting DPS too would
        // ripple into AreaGate.AssumedPrimaryDps, the World1 EPL/MPL band calibration and BossFight's
        // RawDps, three separately-tuned systems this ticket does not touch.
        [SerializeField] private float damagePerTick = 4f;
        [SerializeField] private float fireInterval = 0.1f;   // seconds between ticks
        [SerializeField] private LayerMask hitMask = ~0;

        /// <summary>Authored base spray half-angle in degrees (retuned MV-281, widened MV-289): the
        /// 0.6 recut's ~10° total arc read as unplayably narrow for Area 1's opening fight, so MV-289
        /// widens it to a forgiving ~45° total arc. The RCDA Spread track widens this further by up to
        /// its max level (<see cref="WeaponCatalog.DefaultRcdaSpreadPerLevel"/> is retuned against THIS
        /// value to hold the same ~100° total ceiling — change them together). Nozzle upgrades
        /// (YT-133) narrow/widen it further.</summary>
        public const float DefaultConeHalfAngle = 22.5f;

        /// <summary>Damage multiplier at the outer edge of the spray cone (MV-281). Full power (1x) on
        /// the centre-line, linearly falling to this at the cone's half-angle — see
        /// <see cref="SprayHit.DamageFalloff"/>. The spray reads as a real fan with a hot core, not a
        /// uniform-power wall.</summary>
        public const float DefaultEdgeDamageMultiplier = 0.4f;

        [Header("Spray archetype (YT-64) — a threatening arc, not a thin dribble")]
        [Tooltip("Half-angle of the spray cone, degrees. Everything in this arc within range is hit.")]
        // Also baked in Backyard_Slice.unity — keep that value in sync with DefaultConeHalfAngle above.
        [SerializeField] private float coneHalfAngle = DefaultConeHalfAngle;
        [Tooltip("Visual width of the stream, so it reads as a spray fan (cosmetic only).")]
        [SerializeField] private float streamVisualRadius = 1.1f;

        // Deliberately NOT a [SerializeField] (WV-225): knockbackForce used to be one, and
        // Backyard_Slice.unity carried a baked 5 m/s that read as a real launch — the swarm visibly
        // scattering rather than giving ground. WV-225 reverses that direction: a near-zero cosmetic
        // stagger only, so sustained fire doesn't fling robots around. Same "authored in code, the
        // scene can't shadow it" reasoning as BlasterTuning.
        public const float DefaultSprayKnockback = 0.5f;

        // Energy is authored in BlasterTuning, NOT here. These were [SerializeField]s until YT-80,
        // and the values baked into Backyard_Slice.unity quietly overrode every one of them — the
        // gun the code described was not the gun anyone played. Tune it there; the scene can't
        // shadow a static.
        private float energyPerTick;
        private float rechargeFraction;

        [Header("Debug")]
        [Tooltip("Draw a live fire-state overlay (diagnostics) while DevMode is enabled. Never draws " +
                 "otherwise, so a normal/shipping session never shows it (MV-250).")]
        [SerializeField] private bool debugOverlay = true;

        [Header("Aim source")]
        [Tooltip("Optional. If set, fires while the player aims and orients to their facing. " +
                 "If null, IsFiring drives it directly (useful for isolated testing).")]
        [SerializeField] private PlayerController aimSource;

        public EnergyPool Energy { get; private set; }

        /// <summary>Whether the trigger is currently held. Driven by <see cref="aimSource"/>'s
        /// aim each frame when bound. Defaults <c>false</c> — an unbound/idle blaster never
        /// auto-fires (YT-36 regression: it must NOT discharge with no aim input).</summary>
        public bool IsFiring { get; private set; }

        /// <summary>
        /// Pure fire-gate decision (unit-testable): the stream emits only while the
        /// trigger is actively held AND there is enough energy for a tick. With no
        /// aim held (<paramref name="firingHeld"/> false) this is always false — no
        /// emission, no damage tick, no VFX.
        /// </summary>
        public static bool ShouldEmit(bool firingHeld, bool hasEnergy) => firingHeld && hasEnergy;

        /// <summary>Drive the trigger directly when there is no <see cref="aimSource"/>
        /// (isolated testing / scripted fire). Ignored on frames where a bound aim source
        /// overrides it in Update.</summary>
        public void SetFiring(bool firing) => IsFiring = firing;

        /// <summary>Is the stream actually coming out this frame? (Firing AND energy available.)
        /// Cells being empty no longer stalls this — see <see cref="IsWeakened"/>.</summary>
        public bool IsEmitting => _lastEmitting;

        /// <summary>Outgoing damage multiplier while the power-cell reserve is empty (MV-243 fix).
        /// Mirrors <see cref="MaxWorlds.Player.PlayerHealth.IsWeakened"/>'s "empty = weakened, not
        /// blocked" rule (WV-227) on the output side: the stream keeps firing at reduced effect rather
        /// than stalling, so a fresh run (always 0 cells) can still land the kills that earn its first
        /// cell instead of deadlocking forever.</summary>
        public const float DefaultWeakenedFireDamageMultiplier = 0.5f;

        /// <summary>True with an empty power-cell reserve — the stream still emits but hits softer
        /// (<see cref="DefaultWeakenedFireDamageMultiplier"/>) until Max collects more.</summary>
        public bool IsWeakened => PickupWallet.PowerCells <= 0;

        // --- Power ramp (YT-67) ---------------------------------------------------------------
        // The authored numbers, captured before any level-up scales them. Multipliers are always
        // applied to these, never compounded onto the live values, so re-applying is harmless.
        private float _baseDamage;
        private float _baseInterval;
        private float _baseEnergyPerSecond;

        /// <summary>How far the stream actually reaches, in metres — the authored reach plus any reach
        /// the Power nozzle adds (YT-133) plus the RCDA Range track's own bonus (MV-263). Public so the
        /// aim reticle (YT-84) is drawn from the number the hit test uses, rather than from a shape
        /// someone drew — the moment those two disagree, the reticle is a lie the player has been
        /// taught to trust. The serialized <c>range</c> stays the authored base; upgrades are layered
        /// on here.</summary>
        public float Range => WeaponCatalog.EffectiveRange(
            range + UpgradeState.RangeBonus,
            WeaponSystemState.TrackLevel(WeaponTrackKind.Range),
            WeaponCatalog.DefaultRcdaRangePerLevel);

        /// <summary>HALF the spray's total spread, in degrees — the same convention
        /// <see cref="SprayHit.InCone"/> uses. Narrowed by any nozzle Max has fitted (YT-133), widened
        /// by the RCDA Spread track (MV-263).</summary>
        public float ConeHalfAngle => WeaponCatalog.EffectiveConeHalfAngle(
            coneHalfAngle * UpgradeState.ConeMultiplier,
            WeaponSystemState.TrackLevel(WeaponTrackKind.Spread),
            WeaponCatalog.DefaultRcdaSpreadPerLevel);

        /// <summary>Damage one tick of the stream deals, after the power ramp.</summary>
        public float DamagePerTick => damagePerTick;
        /// <summary>Seconds between ticks, after the power ramp.</summary>
        public float FireInterval => fireInterval;
        /// <summary>Energy one tick costs, after the power ramp.</summary>
        public float EnergyPerTick => energyPerTick;
        /// <summary>What the stream actually outputs per second — the number the player feels.</summary>
        public float DamagePerSecond => fireInterval > 0f ? damagePerTick / fireInterval : 0f;
        /// <summary>What holding the trigger actually costs per second. Held CONSTANT by the ramp.</summary>
        public float EnergyPerSecond => fireInterval > 0f ? energyPerTick / fireInterval : 0f;

        /// <summary>
        /// Scale the stream by the power ramp (YT-67).
        ///
        /// The energy cost is re-derived so that holding the trigger costs the same PER SECOND as
        /// it always did. That's the whole trick, and without it a fire-rate boost is a lie: more
        /// ticks per second at the same cost per tick just drains the tank proportionally faster,
        /// so the player fires more often, runs dry sooner, and ends up doing the same damage per
        /// tankful. The upgrade would have felt like nothing. Now the pump gets faster, not
        /// thirstier, and the boost is real damage rather than a shuffled cost.
        /// </summary>
        public void ApplyPower(float damageMultiplier, float fireRateMultiplier)
        {
            damagePerTick = _baseDamage * Mathf.Max(0f, damageMultiplier);
            fireInterval = _baseInterval / Mathf.Max(0.01f, fireRateMultiplier);
            energyPerTick = _baseEnergyPerSecond * fireInterval;
        }

        /// <summary>
        /// Re-read the drain/refill numbers through <see cref="DevTuning"/> (YT-105). Called by the
        /// tuning panel after a slider move.
        ///
        /// Drain is re-derived against the CURRENT <see cref="fireInterval"/>, not the authored one,
        /// so tuning the tank mid-run doesn't quietly undo the power ramp that's already applied.
        /// </summary>
        public void RefreshDevTuning()
        {
            _baseEnergyPerSecond = DevTuning.Or(DevTuning.BlasterDrainPerSecond, BlasterTuning.EnergyPerSecond);
            energyPerTick = _baseEnergyPerSecond * fireInterval;
            if (Energy != null)
            {
                Energy.RegenPerSec = DevTuning.Or(DevTuning.BlasterRegenPerSecond, BlasterTuning.RegenPerSec);
            }
        }

        private void OnEnable()
        {
            UpgradeState.Changed += RefreshUpgrades;
            // The RCDA Range/Spread tracks (WV-230) also feed Range/ConeHalfAngle above, so a spend
            // on either has to re-fit the reticle and stream the same way an installed part does
            // (MV-263) — without this the level goes up but nothing on screen or in the hit test moves.
            WeaponSystemState.Changed += RefreshUpgrades;
            RefreshUpgrades();   // fit to whatever's already installed (e.g. Max spawned into a run in progress)
        }

        private void OnDisable()
        {
            UpgradeState.Changed -= RefreshUpgrades;
            WeaponSystemState.Changed -= RefreshUpgrades;
        }

        /// <summary>
        /// Re-fit the weapon to Max's installed parts and RCDA track levels (YT-133/MV-263): rebuild
        /// the reticle and stream at the new reach/spread, and resize the tank to its upgraded
        /// capacity. Fires on every install or track spend. No-ops safely before <see cref="Awake"/>
        /// has built the sub-objects.
        /// </summary>
        public void RefreshUpgrades()
        {
            if (_reticle != null) _reticle.Init(transform, Range, ConeHalfAngle);
            if (_vfx != null) _vfx.Init(Range, Mathf.Max(radius, streamVisualRadius), ConeHalfAngle);
            if (Energy != null) Energy.Retune(BlasterTuning.MaxEnergy + UpgradeState.CapacityBonus);
        }

        private float _tickTimer;
        private bool _depleted;
        private bool _lastEmitting;
        private WaterVfx _vfx;
        private AimReticle _reticle;
        private readonly Collider[] _hits = new Collider[32];
        private static readonly List<IDamageable> s_buffer = new List<IDamageable>(8);
        // Collider that produced each buffered hit, parallel to s_buffer. Cosmetic use
        // only — it gives the splash a contact point on the target's surface.
        private static readonly List<Collider> s_contacts = new List<Collider>(8);

        private void Awake()
        {
            Energy = new EnergyPool(
                BlasterTuning.MaxEnergy, BlasterTuning.RegenPerSec, BlasterTuning.RegenDelay);
            rechargeFraction = BlasterTuning.RechargeFraction;

            // Per-tick cost is derived from the per-second cost, because per-second is the number
            // that was authored and the one the ramp holds constant (YT-67/YT-80).
            energyPerTick = BlasterTuning.EnergyPerSecond * fireInterval;

            // Capture the authored numbers before anything scales them (YT-67).
            _baseDamage = damagePerTick;
            _baseInterval = fireInterval;
            _baseEnergyPerSecond = BlasterTuning.EnergyPerSecond;

            // VFX attaches itself — no scene wiring, no prefab (code-driven scenes rule).
            _vfx = GetComponent<WaterVfx>();
            if (_vfx == null) _vfx = gameObject.AddComponent<WaterVfx>();
            // The cone goes in too (YT-110/YT-187): the water is drawn across the same arc it
            // damages, so the spray and the reticle above it are the same weapon described twice,
            // not two numbers that happened to be authored on different days.
            _vfx.Init(range, Mathf.Max(radius, streamVisualRadius), coneHalfAngle);

            // The level-up ramp rides along with the gadget, same self-attaching rule as the VFX.
            if (GetComponent<PlayerPower>() == null) gameObject.AddComponent<PlayerPower>();

            // The aim reticle (YT-84) is built from THIS gadget's real reach and spread, so a future
            // Beam or Lob draws its own shape without anyone authoring one.
            _reticle = GetComponent<AimReticle>();
            if (_reticle == null) _reticle = gameObject.AddComponent<AimReticle>();
            _reticle.Init(transform, range, coneHalfAngle);
        }

        private float _cellDrainAccum;
        /// <summary>Cells the primary weapon burns per MINUTE of spray, before any dev override or
        /// Power Efficiency reduction (WV-227's economy recut — supersedes the old cells/sec number).
        /// WV-233 generalised this from metering only the Hydro condenser while untethered (YT-137) to
        /// ALL primary fire, now that the hose has detached from taps entirely.</summary>
        public const float DefaultPrimaryCellsPerMin = 6f;

        private void Update()
        {
            float dt = Time.deltaTime;

            // The Hydro burst (YT-215) is still a pressable HUD button/cooldown clock, so it still
            // needs a frame tick; it used to ride along on the tether's LateUpdate (HoseTether owned
            // the leash it released), but the leash is gone (WV-233), so the weapon — the other thing
            // that runs every frame for the armed Max — ticks it now.
            HydroBurst.Tick(dt);

            // Water supply (WV-233): the hose is self-supplied from power cells, always — no tap to
            // regen from. While cells remain, they top the tank; at empty they can't, so the tank
            // drains as Max fires and the spray stalls until he collects more (generalises the old
            // Hydro-condenser-only rule, YT-137, to all primary fire).
            //
            // MV-266: at empty cells the tank must still run its OWN regen clock (BlasterTuning's
            // RegenPerSec/RegenDelay/RechargeFraction — "Water refill rate" in the tuning panel), not
            // sit dead. A fresh run always starts at 0 cells, so before this fix the tank had no way to
            // recover once drained: Energy.Tick() (the only thing that advances natural regen) was never
            // called, so "Water deplete rate" ran the tank down once and it never came back — the run
            // was unwinnable before the first kill could ever earn a cell. Cells still top the tank
            // instantly on pickup/while held (unlimited-feeling water once earned); it's only the
            // empty-cell case that now recovers on its own instead of staying dead.
            if (PickupWallet.PowerCells > 0) Energy.Refill();
            else Energy.Tick(dt);

            // Trigger is held only while the player is actively aiming. When bound,
            // orient along their facing too. If unbound, IsFiring stays false (no
            // auto-discharge) unless a test/other system drives it via SetFiring.
            if (aimSource != null)
            {
                IsFiring = aimSource.IsAiming;
                Vector3 f = aimSource.Facing;
                if (f.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(f, Vector3.up);
                }
            }

            // The reticle brightens while the player aims and stays as a whisper otherwise, so reach
            // is always legible without the lawn being permanently painted (YT-84). Cosmetic: it is
            // told what the gadget is doing and never gets a say in it.
            if (_reticle != null) _reticle.SetAiming(IsFiring);

            // Dev/filming only; both are false in a normal session (YT-60).
            if (DevMode.IsAutoFiring) IsFiring = true;
            if (DevMode.IsInfiniteEnergy) Energy.Refill();

            // Hysteresis: once the tank runs dry, lock fire out until it recharges to
            // rechargeFraction of max. Without this, an empty tank dribbles a single
            // puff every regenDelay (the "clouds of bubbles" stutter) instead of a
            // clean stream → deplete → recharge → stream cycle.
            if (_depleted && Energy.Normalized >= rechargeFraction) _depleted = false;
            else if (!_depleted && !Energy.CanSpend(energyPerTick)) _depleted = true;

            bool emitting = ShouldEmit(IsFiring, !_depleted && Energy.CanSpend(energyPerTick));
            _lastEmitting = emitting;

            // While it IS spraying, the water is paid for in power cells — burn them for the time it's
            // actually spraying, so the meter ticks down as it's used (WV-227, generalised by WV-233).
            // Empty cells weaken the output (IsWeakened, MV-243) rather than stopping the spend loop —
            // there's nothing left to spend, so TrySpendPowerCell() below just self-limits at 0.
            if (emitting)
            {
                float perMin = Mathf.Max(0f, DevTuning.Or(DevTuning.PrimaryCellsPerMin, DefaultPrimaryCellsPerMin));
                // Power Efficiency's real level (WV-230) — 0 (no reduction) until the ability is
                // acquired from a shed (WV-229); the ability's own effect (WV-231) is just this level.
                float efficiency = CellEconomyTuning.EfficiencyMultiplier(
                    WeaponSystemState.AbilityLevel(AbilityKind.PowerEfficiency),
                    DevTuning.Or(DevTuning.PowerEfficiencyReductionPerLevel, CellEconomyTuning.DefaultPowerEfficiencyReductionPerLevel));
                float rate = (perMin / 60f) * efficiency;
                _cellDrainAccum += rate * dt;
                while (_cellDrainAccum >= 1f && PickupWallet.TrySpendPowerCell()) _cellDrainAccum -= 1f;
            }

            if (_vfx != null) _vfx.SetStreaming(emitting);
            if (!emitting)
            {
                _tickTimer = 0f;
                return;
            }

            _tickTimer -= dt;
            if (_tickTimer > 0f) return;
            _tickTimer = fireInterval;

            if (!Energy.TrySpend(energyPerTick)) return;
            FireTick();
        }

        private void FireTick()
        {
            Vector3 origin = transform.position;
            Vector3 dir = transform.forward;
            // Effective reach/spread after any nozzle upgrades (YT-133) — the hit test, the reticle
            // and the VFX all read these same numbers, so the beam you see is the beam that hits.
            float reach = Range;
            float cone = ConeHalfAngle;
            // Empty power cells weaken the hit, not the reach/spread/aim (MV-243) — a starved shot
            // still finds and marks its targets, it just hits softer.
            float tickDamage = damagePerTick * (IsWeakened ? DefaultWeakenedFireDamageMultiplier : 1f);
            // Spray: gather everything within range, then keep only what's inside the cone arc —
            // so one tick can wash a whole knot of robots, not a single-file tube (YT-64).
            int count = Physics.OverlapSphereNonAlloc(
                origin, reach, _hits, hitMask, QueryTriggerInteraction.Ignore);

            s_buffer.Clear();
            s_contacts.Clear();
            for (int i = 0; i < count; i++)
            {
                if (_hits[i] == null) continue;
                if (_hits[i].TryGetComponent<IDamageable>(out var d) && d.IsAlive && d.Team != Team.Player
                    && !s_buffer.Contains(d)
                    && SprayHit.InCone(origin, dir, _hits[i].transform.position, reach, cone)
                    // Water does not go through the shed (YT-83). This is not decoration — it is what
                    // keeps cover a DECISION instead of an exploit. If the tree broke the robots'
                    // sight of Max but not Max's spray of them, hiding would be strictly dominant:
                    // stand behind cover, kill everything in perfect safety, never come out. Cover
                    // has to cost you your shot too, or it isn't cover, it's a turret nest.
                    && LineOfSight.Clear(origin, _hits[i].transform.position, _hits[i].transform))
                {
                    s_buffer.Add(d);
                    s_contacts.Add(_hits[i]);
                }
            }

            bool hitSomething = false;
            for (int i = 0; i < s_buffer.Count; i++)
            {
                var d = s_buffer[i];
                var comp = d as Component;
                Vector3 point = comp != null ? comp.transform.position : origin + dir * range;
                // Falloff (MV-281): full power on the centre-line, softening toward the cone's
                // outer edge — the same angle the cone test above already approved this hit on.
                float falloff = SprayHit.DamageFalloff(
                    SprayHit.AngleDeg(origin, dir, point), cone, DefaultEdgeDamageMultiplier);
                d.TakeDamage(new DamageInfo(tickDamage * falloff, point, dir, Team.Player, soak: true,
                    source: DamageSource.PrimaryWeapon));
                hitSomething = true;

                // Cosmetic stagger only (WV-225) — no meaningful positional launch any more.
                if (comp is IKnockbackable kb)
                {
                    Vector3 push = point - origin; push.y = 0f;
                    float sprayKnockback = DevTuning.Or(DevTuning.SprayKnockback, DefaultSprayKnockback);
                    if (push.sqrMagnitude > 1e-4f && sprayKnockback > 0f)
                        kb.ApplyKnockback(push.normalized * sprayKnockback);
                }

                // Cosmetic: splash on the target's surface facing the blaster, not at its
                // centre (which is what the damage event reports). Nothing below feeds damage.
                if (_vfx != null)
                {
                    _vfx.Splash(ContactPoint(origin, dir, s_contacts[i], point), dir, tickDamage);
                }
            }

            // Cosmetic (YT-53 readability): with nothing hit, a hitscan weapon gives the player no
            // landing point at all — the stream just stops in mid-air. Splash where the water meets
            // the ground so it's always obvious where the shot actually went.
            if (!hitSomething && _vfx != null)
            {
                Vector3 end = origin + dir * reach;
                _vfx.Splash(new Vector3(end.x, 0f, end.z), dir, tickDamage * 0.5f);
            }
        }

        /// <summary>Where the stream visually lands on a body: the point on its collider
        /// closest to the stream's axis. Falls back to <paramref name="fallback"/> if the
        /// collider can't answer (non-convex mesh colliders reject ClosestPoint).</summary>
        private static Vector3 ContactPoint(Vector3 origin, Vector3 dir, Collider col, Vector3 fallback)
        {
            if (col == null) return fallback;
            Vector3 onAxis = WaterVfxTuning.NearestPointOnRay(origin, dir, float.MaxValue, col.bounds.center);
            var mesh = col as MeshCollider;
            if (mesh != null && !mesh.convex) return col.ClosestPointOnBounds(onAxis);
            return col.ClosestPoint(onAxis);
        }

        private void OnGUI()
        {
            if (!debugOverlay || !DevMode.Enabled) return;
            bool aiming = aimSource != null && aimSource.IsAiming;
            string s = $"Blaster: IsFiring={IsFiring}  aimSource.IsAiming={aiming}  " +
                       $"emitting={_lastEmitting}  " +
                       $"energy={Energy?.Normalized:0.00}  depleted={_depleted}";
            GUI.color = _lastEmitting ? Color.cyan : Color.white;
            GUI.Label(new Rect(12f, 64f, 900f, 24f), s);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.31f, 0.76f, 0.97f, 1f);
            Gizmos.DrawWireSphere(transform.position + transform.forward * range, radius);
        }
#endif
    }
}
