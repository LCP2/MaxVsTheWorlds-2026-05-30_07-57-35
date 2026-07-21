using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Hose;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Upgrades;
using MaxWorlds.VFX;

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
    /// before any nozzle upgrade (YT-133) narrows or lengthens it. The hose is tethered to a
    /// tap by <see cref="MaxWorlds.Hose.HoseTether"/>, which leashes how far Max can range; the
    /// spray reach here is a separate, much shorter number.
    ///
    /// All firing visuals live in <see cref="WaterVfx"/> (YT-47), which this attaches
    /// to itself at Awake and drives with cosmetic-only calls. The VFX never feeds back
    /// into fire gating, energy, or damage.
    /// </summary>
    public sealed class WaterBlaster : MonoBehaviour
    {
        [Header("Stream")]
        // Opening hose spray (YT-129): SHORT reach, WIDE arc — weak but forgiving. Nozzle
        // upgrades (YT-133) narrow/lengthen it from this base. Also baked in Backyard_Slice.unity.
        [SerializeField] private float range = 4.5f;
        [SerializeField] private float radius = 0.6f;
        [SerializeField] private float damagePerTick = 4f;
        [SerializeField] private float fireInterval = 0.1f;   // seconds between ticks
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Spray archetype (YT-64) — a threatening arc, not a thin dribble")]
        [Tooltip("Half-angle of the spray cone, degrees. Everything in this arc within range is hit.")]
        [SerializeField] private float coneHalfAngle = 48f;
        [Tooltip("Velocity (m/s) each hit shoves an enemy back — sells 'pushing the swarm back'.")]
        [SerializeField] private float knockbackForce = 5f;
        [Tooltip("Visual width of the stream, so it reads as a spray fan (cosmetic only).")]
        [SerializeField] private float streamVisualRadius = 1.1f;

        // Energy is authored in BlasterTuning, NOT here. These were [SerializeField]s until YT-80,
        // and the values baked into Backyard_Slice.unity quietly overrode every one of them — the
        // gun the code described was not the gun anyone played. Tune it there; the scene can't
        // shadow a static.
        private float energyPerTick;
        private float rechargeFraction;

        [Header("Debug")]
        [Tooltip("Draw a live fire-state overlay (diagnostics). Turn off for release.")]
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

        /// <summary>Is the stream actually coming out this frame? (Firing AND supplied.) Exposed so a
        /// test can see the Hydro condenser stall the spray at zero power cells (YT-137).</summary>
        public bool IsEmitting => _lastEmitting;

        // --- Power ramp (YT-67) ---------------------------------------------------------------
        // The authored numbers, captured before any level-up scales them. Multipliers are always
        // applied to these, never compounded onto the live values, so re-applying is harmless.
        private float _baseDamage;
        private float _baseInterval;
        private float _baseEnergyPerSecond;

        /// <summary>How far the stream actually reaches, in metres — the authored reach plus any reach
        /// the Power nozzle adds (YT-133). Public so the aim reticle (YT-84) is drawn from the number
        /// the hit test uses, rather than from a shape someone drew — the moment those two disagree,
        /// the reticle is a lie the player has been taught to trust. The serialized <c>range</c> stays
        /// the authored base; upgrades are layered on here.</summary>
        public float Range => range + UpgradeState.RangeBonus;

        /// <summary>HALF the spray's total spread, in degrees — the same convention
        /// <see cref="SprayHit.InCone"/> uses. Narrowed by any nozzle Max has fitted (YT-133).</summary>
        public float ConeHalfAngle => coneHalfAngle * UpgradeState.ConeMultiplier;

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
            RefreshUpgrades();   // fit to whatever's already installed (e.g. Max spawned into a run in progress)
        }

        private void OnDisable() => UpgradeState.Changed -= RefreshUpgrades;

        /// <summary>
        /// Re-fit the weapon to Max's installed parts (YT-133): rebuild the reticle and stream at the
        /// new reach/spread the nozzles give, and resize the tank to its upgraded capacity. Fires on
        /// every install. No-ops safely before <see cref="Awake"/> has built the sub-objects.
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
            // The cone goes in too (YT-110): the water is drawn at half the arc it damages, so the
            // spray and the reticle above it are the same weapon described twice, not two numbers
            // that happened to be authored on different days.
            _vfx.Init(range, Mathf.Max(radius, streamVisualRadius), coneHalfAngle);

            // The level-up ramp rides along with the gadget, same self-attaching rule as the VFX.
            if (GetComponent<PlayerPower>() == null) gameObject.AddComponent<PlayerPower>();

            // The aim reticle (YT-84) is built from THIS gadget's real reach and spread, so a future
            // Beam or Lob draws its own shape without anyone authoring one.
            _reticle = GetComponent<AimReticle>();
            if (_reticle == null) _reticle = gameObject.AddComponent<AimReticle>();
            _reticle.Init(transform, range, coneHalfAngle);
        }

        private HoseTether _tether;
        private float _hydroDrainAccum;
        /// <summary>Power cells the Hydro condenser burns per second of spray, before any dev override.</summary>
        public const float DefaultHydroDrainRate = 0.5f;

        /// <summary>True when the water is coming from the Hydro condenser (Hydro installed and Max is
        /// off a tap), not a tap (YT-137). Then power cells fuel it instead of the YT-106 economy.</summary>
        private bool HydroActive
        {
            get
            {
                if (!UpgradeState.Untethered) return false;
                if (_tether == null) _tether = GetComponent<HoseTether>();
                return _tether == null || !_tether.OnTap;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // Water supply (YT-137). On a tap (or with no Hydro), the YT-106 economy: the tank regens.
            // On the Hydro condenser, power cells top the tank while any remain; at empty it can't, so
            // the tank drains as Max fires and the spray stalls until he collects cells or re-taps.
            if (HydroActive)
            {
                if (PickupWallet.PowerCells > 0) Energy.Refill();
            }
            else
            {
                Energy.Tick(dt);
            }

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

            // On the Hydro condenser the water is made from power cells — no cells, no water, so the
            // spray stalls until Max collects more or re-plugs a tap (YT-137). On a tap this is false
            // and the normal YT-106 tank rules apply.
            bool hydroStarved = HydroActive && PickupWallet.PowerCells <= 0;

            bool emitting = ShouldEmit(IsFiring, !_depleted && Energy.CanSpend(energyPerTick) && !hydroStarved);
            _lastEmitting = emitting;

            // While it IS spraying on the condenser, the water is paid for in power cells — burn them
            // for the time it's actually spraying, so the meter ticks down as it's used.
            if (HydroActive && emitting)
            {
                float rate = Mathf.Max(0f, DevTuning.Or(DevTuning.HydroDrainRate, DefaultHydroDrainRate));
                _hydroDrainAccum += rate * dt;
                while (_hydroDrainAccum >= 1f && PickupWallet.TrySpendPowerCell()) _hydroDrainAccum -= 1f;
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
                d.TakeDamage(new DamageInfo(damagePerTick, point, dir, Team.Player, soak: true));
                hitSomething = true;

                // Light knockback — shove robots away from Max so the swarm visibly gives ground.
                if (comp is IKnockbackable kb)
                {
                    Vector3 push = point - origin; push.y = 0f;
                    if (push.sqrMagnitude > 1e-4f) kb.ApplyKnockback(push.normalized * knockbackForce);
                }

                // Cosmetic: splash on the target's surface facing the blaster, not at its
                // centre (which is what the damage event reports). Nothing below feeds damage.
                if (_vfx != null)
                {
                    _vfx.Splash(ContactPoint(origin, dir, s_contacts[i], point), dir, damagePerTick);
                }
            }

            // Cosmetic (YT-53 readability): with nothing hit, a hitscan weapon gives the player no
            // landing point at all — the stream just stops in mid-air. Splash where the water meets
            // the ground so it's always obvious where the shot actually went.
            if (!hitSomething && _vfx != null)
            {
                Vector3 end = origin + dir * reach;
                _vfx.Splash(new Vector3(end.x, 0f, end.z), dir, damagePerTick * 0.5f);
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
            if (!debugOverlay) return;
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
