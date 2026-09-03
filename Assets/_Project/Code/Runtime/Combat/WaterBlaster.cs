using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.Upgrades;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Combat
{
    /// <summary>
    /// Slice gadget (YT-35) — Spray archetype. While <see cref="IsFiring"/> is
    /// driven true (player holds aim), auto-fires a short-range stream: ticks at
    /// a fixed cadence, draws from the water tank, sphere-casts forward, and applies damage
    /// (+soak tag) to every <see cref="IDamageable"/> in the stream.
    ///
    /// Since the weapon epic (YT-127/YT-129) this is Max's <b>garden hose</b>: the water
    /// short-circuits the robots (the existing damage, re-themed — a spray shorts them out).
    /// Its OPENING spray is deliberately short and wide — weak but forgiving — the base state
    /// before any nozzle upgrade (YT-133) narrows or lengthens it.
    ///
    /// MV-290 cut the tank entirely (always-on, no depletion); MV-299 reinstates it — the primary
    /// drains under continuous fire and auto-regenerates once the trigger is released, no cells, no
    /// pickups, no taps involved (that part of MV-290's cut, one currency/no cell-fuel, stays). See
    /// <see cref="WaterNormalized"/> for the floating gauge and <see cref="WeaponTrackKind.Capacity"/>
    /// for the upgrade that slows the drain.
    ///
    /// All firing visuals live in <see cref="WaterVfx"/> (YT-47), which this attaches
    /// to itself at Awake and drives with cosmetic-only calls. The VFX never feeds back
    /// into fire gating or damage.
    /// </summary>
    public sealed class WaterBlaster : MonoBehaviour
    {
        /// <summary>Authored base spray reach in metres (YT-129, retuned MV-280, widened MV-289): the
        /// 0.6 recut's 3m base — combined with the recalibrated robots and an under-tough Max — made
        /// Area 1 unplayable, so MV-289 widens the opening reach to a forgiving 5m. The RCDA Range
        /// track (MV-263, re-retuned MV-291) extends this base by up to ~2.5x at its max level
        /// (<see cref="WeaponCatalog.DefaultRcdaRangePerLevel"/> is tuned against THIS value — change
        /// them together). Nozzle upgrades (YT-133) narrow/lengthen it further.</summary>
        public const float DefaultRange = 5f;

        [Header("Stream")]
        // Also baked in Backyard_Slice.unity — keep that value in sync with DefaultRange above.
        [SerializeField] private float range = DefaultRange;
        [SerializeField] private float radius = 0.6f;

        /// <summary>Authored base per-tick damage (unchanged by MV-289: still 4/0.1 = 40 DPS — the
        /// retuned Rusher HP, 32 base/~45 effective, already lands the ~1.1-1.5s TTK AC1 asks for at
        /// the existing DPS, and cutting it would ripple into AreaGate.AssumedPrimaryDps and
        /// BossFight's RawDps, separately-tuned systems this ticket does not touch). Still the
        /// authored BASE at track level 1 — the RCDA Damage track (MV-291) scales up from here via
        /// <see cref="EffectiveDamagePerTick"/>, so those level-1-assuming systems are untouched.</summary>
        public const float DefaultDamagePerTick = 4f;

        [SerializeField] private float damagePerTick = DefaultDamagePerTick;
        [SerializeField] private float fireInterval = 0.1f;   // seconds between ticks
        [SerializeField] private LayerMask hitMask = ~0;

        /// <summary>Authored base spray half-angle in degrees (retuned MV-281, widened MV-289,
        /// re-narrowed MV-301, re-narrowed again MV-367, restored to its pre-MV-367 value by MV-379).
        /// MV-367 tried to make an un-upgraded beam "look weak to begin with" by narrowing THIS value —
        /// but since the hit test and the reticle both read it too, that also narrowed the actual
        /// functional spread, making robots harder to hit (Lee's playtest, MV-379). MV-379 restores it
        /// to 8° and moves the "looks weaker" job onto <see cref="WaterVfx"/>'s visual-only dials
        /// instead (YT-110/YT-187's old "water, reticle and hit test must never disagree" invariant is
        /// deliberately broken here — see WaterVfx's Init doc). Power is unaffected either way: a
        /// target dead ahead (angle 0 from the aim axis) stays inside the cone at any half-angle above
        /// 0, so level-1 TTK against a single enemy is untouched — see
        /// <see cref="MaxWorlds.Tests.EditMode.WeaponCatalogTests.NarrowedBaseConeStillHitsADeadAheadTarget_MV367"/>.
        /// The RCDA Spread track widens this further by up to its max level
        /// (<see cref="WeaponCatalog.DefaultRcdaSpreadPerLevel"/>). Nozzle upgrades (YT-133) narrow/widen
        /// it further.</summary>
        public const float DefaultConeHalfAngle = 8f;

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
        // scattering rather than giving ground. WV-225 reversed that direction to a near-zero cosmetic
        // stagger; MV-658 (Lee's 2026-09-02 tuning pass) dials it back up to a real, felt shove
        // without returning to the old 5 m/s launch. Same "authored in code, the scene can't shadow
        // it" reasoning as BlasterTuning. MV-315 had re-baked to 125% of WV-225's 0.5 (0.625).
        public const float DefaultSprayKnockback = 2.64f;   // MV-658: baked from Lee's 2026-09-02 tuning pass (was 0.625)

        [Header("Debug")]
        [Tooltip("Draw a live fire-state overlay (diagnostics) while DevMode is enabled. Never draws " +
                 "otherwise, so a normal/shipping session never shows it (MV-250).")]
        [SerializeField] private bool debugOverlay = true;

        [Header("Aim source")]
        [Tooltip("Optional. If set, fires while the player aims and orients to their facing. " +
                 "If null, IsFiring drives it directly (useful for isolated testing).")]
        [SerializeField] private PlayerController aimSource;

        /// <summary>Whether the trigger is currently held. Driven by <see cref="aimSource"/>'s
        /// aim each frame when bound. Defaults <c>false</c> — an unbound/idle blaster never
        /// auto-fires (YT-36 regression: it must NOT discharge with no aim input).</summary>
        public bool IsFiring { get; private set; }

        /// <summary>
        /// Pure fire-gate decision (unit-testable): the stream emits only while the trigger is
        /// actively held AND there is water left to spend (MV-299, reinstating the tank MV-290 cut).
        /// With no aim held (<paramref name="firingHeld"/> false) this is always false — no emission,
        /// no damage tick, no VFX.
        /// </summary>
        public static bool ShouldEmit(bool firingHeld, bool hasWater) => firingHeld && hasWater;

        /// <summary>Drive the trigger directly when there is no <see cref="aimSource"/>
        /// (isolated testing / scripted fire). Ignored on frames where a bound aim source
        /// overrides it in Update.</summary>
        public void SetFiring(bool firing) => IsFiring = firing;

        /// <summary>Is the stream actually coming out this frame? (Firing AND water available.)</summary>
        public bool IsEmitting => _lastEmitting;

        /// <summary>The water tank, 0..1 — what the floating gauge above Max reads
        /// (<see cref="MaxWorlds.Player.PlayerHealth"/>, MV-299). 1 before <see cref="Awake"/> has
        /// built the tank, so an unbuilt/isolated instance never reads as empty.</summary>
        public float WaterNormalized => _tank != null ? _tank.Normalized : 1f;

        /// <summary>Water one tick costs, given the current Depletion Rate track level (MV-299) and the
        /// weapon's actual current output (MV-368) — the authored per-second drain
        /// (<see cref="BlasterTuning.EnergyPerSecond"/>), scaled UP by how far Range/Spread (plus any
        /// nozzle) have pushed reach and cone past base, then scaled DOWN by the Depletion Rate track,
        /// spread over one fire tick. Reads <see cref="Range"/>/<see cref="ConeHalfAngle"/> — the same
        /// effective numbers the hit test and VFX use — rather than raw track levels, so it stays
        /// correct if those tracks' own curves retune again.</summary>
        public float EnergyPerTick => WeaponCatalog.EffectiveDrainPerSecond(
            DevTuning.Or(DevTuning.PrimaryDepletionRate, BlasterTuning.EnergyPerSecond),
            WeaponSystemState.TrackLevel(WeaponTrackKind.Capacity),
            WeaponCatalog.DefaultRcdaDepletionRatePerLevel,
            WeaponCatalog.DrainOutputScale(Range, DefaultRange, ConeHalfAngle, DefaultConeHalfAngle)) * fireInterval;

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

        /// <summary>How close the un-upgraded weapon's visual-only presentation (MV-379) sits to the
        /// old, fuller look — 0 at the Spread track's starting level, 1 once Spread is maxed.
        /// Deliberately independent of <see cref="ConeHalfAngle"/>: MV-379 intentionally decouples the
        /// rendered stream's THICKNESS from the functional cone's WIDTH (see <see cref="WaterVfx.Init"/>).
        /// Reads the Spread track specifically because it's the track that used to drive the
        /// (now-decoupled) visual 1:1 before this ticket — spending on Spread is still what makes the
        /// beam look fuller, even though the hit-cone no longer needs it to.</summary>
        public float VisualStrength => WeaponCatalog.VisualStrengthFraction(
            WeaponSystemState.TrackLevel(WeaponTrackKind.Spread), WeaponCatalog.MaxLevel(WeaponTrackKind.Spread));

        /// <summary>Damage one tick of the stream deals, before the RCDA Damage track's bonus.</summary>
        public float DamagePerTick => damagePerTick;

        /// <summary>Damage one tick actually deals right now — the authored base plus the RCDA Damage
        /// track's bonus (MV-291), the same "level 1 adds nothing, every level after is a visible step"
        /// shape as <see cref="Range"/> and <see cref="ConeHalfAngle"/>. This is what <see cref="FireTick"/>
        /// applies and what the splash VFX reads, so a Damage spend is felt, not just banked.</summary>
        public float EffectiveDamagePerTick => WeaponCatalog.EffectiveDamagePerTick(
            damagePerTick,
            WeaponSystemState.TrackLevel(WeaponTrackKind.Damage),
            WeaponCatalog.DefaultRcdaDamagePerLevel);

        /// <summary>Seconds between ticks.</summary>
        public float FireInterval => fireInterval;
        /// <summary>What the stream actually outputs per second — the number the player feels.</summary>
        public float DamagePerSecond => fireInterval > 0f ? EffectiveDamagePerTick / fireInterval : 0f;

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
        /// the reticle and stream at the new reach/spread. Fires on every install or track spend.
        /// No-ops safely before <see cref="Awake"/> has built the sub-objects.
        /// </summary>
        public void RefreshUpgrades()
        {
            if (_reticle != null) _reticle.Init(transform, Range, ConeHalfAngle);
            if (_vfx != null) _vfx.Init(Range, Mathf.Max(radius, streamVisualRadius), ConeHalfAngle, VisualStrength);
        }

        private float _tickTimer;
        private bool _lastEmitting;
        private bool _depleted;
        private EnergyPool _tank;
        private WaterVfx _vfx;
        private AimReticle _reticle;
        private readonly Collider[] _hits = new Collider[32];
        private static readonly HashSet<IDamageable> s_arcHit = new HashSet<IDamageable>(8);
        private static readonly List<IDamageable> s_buffer = new List<IDamageable>(8);
        // Collider that produced each buffered hit, parallel to s_buffer. Cosmetic use
        // only — it gives the splash a contact point on the target's surface.
        private static readonly List<Collider> s_contacts = new List<Collider>(8);
        // The point (per s_buffer entry) that passed the cone/line-of-sight test — a gate's is
        // the closest point on ITS collider to the aim axis (MV-302), everyone else's is their own
        // transform position. Reused as the damage/falloff/knockback point below so a gate hit at its
        // edge doesn't fall back to a falloff computed from its centre.
        private static readonly List<Vector3> s_hitPoints = new List<Vector3>(8);

        private void Awake()
        {
            // The water tank (MV-299, reinstating the tank MV-290 cut). Size is fixed — the
            // Capacity track (WeaponTrackKind.Capacity, renamed from FLOW/DepletionRate by MV-597,
            // renamed again to CAPACITY by MV-609) only scales how fast it drains, never how big it
            // is (that is a different, still-retired, tank-SIZE track from MV-290 — same word, not
            // the same mechanic).
            _tank = new EnergyPool(BlasterTuning.MaxEnergy, BlasterTuning.RegenPerSec, BlasterTuning.RegenDelay);

            // VFX attaches itself — no scene wiring, no prefab (code-driven scenes rule).
            _vfx = GetComponent<WaterVfx>();
            if (_vfx == null) _vfx = gameObject.AddComponent<WaterVfx>();
            // The cone goes in too (YT-110/YT-187): the water is drawn across the same arc it
            // damages, so the spray and the reticle above it are the same weapon described twice,
            // not two numbers that happened to be authored on different days.
            _vfx.Init(range, Mathf.Max(radius, streamVisualRadius), coneHalfAngle, VisualStrength);

            // The aim reticle (YT-84) is built from THIS gadget's real reach and spread, so a future
            // Beam or Lob draws its own shape without anyone authoring one.
            _reticle = GetComponent<AimReticle>();
            if (_reticle == null) _reticle = gameObject.AddComponent<AimReticle>();
            _reticle.Init(transform, range, coneHalfAngle);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // The Hydro burst (YT-215) is still a pressable HUD button/cooldown clock, so it still
            // needs a frame tick; it used to ride along on the tether's LateUpdate (HoseTether owned
            // the leash it released), but the leash is gone (WV-233), so the weapon — the other thing
            // that runs every frame for the armed Max — ticks it now.
            HydroBurst.Tick(dt);

            // Auto-regen (MV-299): advances only once TrySpend has stopped resetting the tank's
            // internal idle clock — i.e. purely from letting go of the trigger, no cells, no
            // pickups, no taps. Ticked every frame, firing or not, so a released trigger starts
            // recovering on its own the moment RegenDelay has passed.
            _tank.Tick(dt);

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
            if (DevMode.IsInfiniteEnergy) _tank.Refill();

            // Hysteresis (MV-299, reinstated): once the tank runs dry, lock fire out until it
            // recharges to RechargeFraction of max. Without this, an empty tank dribbles a single
            // puff every RegenDelay instead of a clean stream -> deplete -> recharge -> stream cycle.
            float costPerTick = EnergyPerTick;
            if (_depleted && _tank.Normalized >= BlasterTuning.RechargeFraction) _depleted = false;
            else if (!_depleted && !_tank.CanSpend(costPerTick)) _depleted = true;

            bool emitting = ShouldEmit(IsFiring, !_depleted && _tank.CanSpend(costPerTick));
            _lastEmitting = emitting;

            if (_vfx != null) _vfx.SetStreaming(emitting);
            if (!emitting)
            {
                _tickTimer = 0f;
                return;
            }

            _tickTimer -= dt;
            if (_tickTimer > 0f) return;
            _tickTimer = fireInterval;

            if (!_tank.TrySpend(costPerTick)) return;
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
            // The RCDA Damage track's bonus (MV-291) — the same number the splash VFX below scales
            // its droplet count from, so a maxed Damage track reads as a heavier hit, not just a
            // bigger number in a combat log nobody sees.
            float tickDamage = EffectiveDamagePerTick;
            // Spray: gather everything within range, then keep only what's inside the cone arc —
            // so one tick can wash a whole knot of robots, not a single-file tube (YT-64).
            int count = Physics.OverlapSphereNonAlloc(
                origin, reach, _hits, hitMask, QueryTriggerInteraction.Ignore);

            s_buffer.Clear();
            s_contacts.Clear();
            s_hitPoints.Clear();
            for (int i = 0; i < count; i++)
            {
                if (_hits[i] == null) continue;
                if (_hits[i].TryGetComponent<IDamageable>(out var d) && d.IsAlive && d.Team != Team.Player
                    && !s_buffer.Contains(d))
                {
                    // Gates are wide structures (MV-302): a hit test against their CENTRE point only
                    // lets you damage them dead-on, leaving both ends of a wide gate untouchable. Test
                    // (and later damage) the point on the gate's own collider closest to the aim axis
                    // instead, so fire landing anywhere across its full width registers. Every other
                    // damageable keeps testing its own transform position — normal robot targeting,
                    // where the collider IS the body, is unchanged.
                    Vector3 testPoint = d is AreaGate
                        ? ContactPoint(origin, dir, _hits[i], _hits[i].transform.position)
                        : _hits[i].transform.position;

                    // MV-386 regression: an AreaGate's leaf collider (where IDamageable actually
                    // lives) is deliberately left OFF the Cover layer — only its ThresholdObject is on
                    // Cover (see MapRuntime.BuildAreaGate), so an opened gate's swinging leaf can't
                    // re-block the sight-line it just gave up. But that means a Cover-masked cast
                    // toward the LEAF's own testPoint still hits the threshold collider sitting across
                    // the same closed doorway first — and since the threshold is a different Transform
                    // from the leaf we asked permission for, LineOfSight.Clear read that as "blocked",
                    // so a closed gate could stop a shot (correctly solid) while never registering the
                    // damage that shot carried. Ask permission for the threshold itself when the target
                    // is a gate — that's the Transform actually sitting on the Cover layer.
                    Transform sightTarget = d is AreaGate gateHit && gateHit.ThresholdObject != null
                        ? gateHit.ThresholdObject.transform
                        : _hits[i].transform;

                    if (SprayHit.InCone(origin, dir, testPoint, reach, cone)
                        // Water does not go through the shed (YT-83). This is not decoration — it is
                        // what keeps cover a DECISION instead of an exploit. If the tree broke the
                        // robots' sight of Max but not Max's spray of them, hiding would be strictly
                        // dominant: stand behind cover, kill everything in perfect safety, never come
                        // out. Cover has to cost you your shot too, or it isn't cover, it's a turret nest.
                        && LineOfSight.Clear(origin, testPoint, sightTarget))
                    {
                        s_buffer.Add(d);
                        s_contacts.Add(_hits[i]);
                        s_hitPoints.Add(testPoint);
                    }
                }
            }

            bool hitSomething = false;
            for (int i = 0; i < s_buffer.Count; i++)
            {
                var d = s_buffer[i];
                var comp = d as Component;
                Vector3 point = s_hitPoints[i];
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

            // MV-426 DELUGE (f_del): once Primary+Secondary is forged, a directly-hit target standing
            // in a WaterPuddle arcs this same tick's damage to every other wet robot in it.
            if (RigFusionState.IsForged("f_del") && WaterPuddle.Active.Count > 0)
                ApplyDelugeArc(dir, tickDamage);

            // Cosmetic (YT-53 readability): with nothing hit, a hitscan weapon gives the player no
            // landing point at all — the stream just stops in mid-air. Splash where the water meets
            // the ground so it's always obvious where the shot actually went.
            if (!hitSomething && _vfx != null)
            {
                Vector3 end = origin + dir * reach;
                _vfx.Splash(new Vector3(end.x, 0f, end.z), dir, tickDamage * 0.5f);
            }
        }

        /// <summary>DELUGE's arc (MV-426): for every target this tick's spray directly hit, find the
        /// puddle (if any) standing under it and apply this same tick's damage to every OTHER wet robot
        /// in that puddle too — "arcs between every wet robot standing in it". A robot already hit
        /// directly, or already arced-to this tick (two directly-hit targets sharing one puddle), is
        /// never double-counted.</summary>
        private void ApplyDelugeArc(Vector3 dir, float tickDamage)
        {
            s_arcHit.Clear();
            for (int i = 0; i < s_hitPoints.Count; i++)
            {
                WaterPuddle puddle = null;
                for (int p = 0; p < WaterPuddle.Active.Count; p++)
                {
                    WaterPuddle candidate = WaterPuddle.Active[p];
                    if (WaterPuddle.IsInside(candidate.Position, candidate.Radius, s_hitPoints[i]))
                    {
                        puddle = candidate;
                        break;
                    }
                }
                if (puddle == null) continue;

                int count = Physics.OverlapSphereNonAlloc(
                    puddle.Position, puddle.Radius, _hits, hitMask, QueryTriggerInteraction.Ignore);
                for (int j = 0; j < count; j++)
                {
                    if (_hits[j] == null) continue;
                    if (!_hits[j].TryGetComponent<IDamageable>(out var wet) || !wet.IsAlive || wet.Team == Team.Player) continue;
                    if (s_buffer.Contains(wet)) continue;   // already hit directly this tick
                    if (!s_arcHit.Add(wet)) continue;       // already arced to this tick
                    wet.TakeDamage(new DamageInfo(tickDamage, _hits[j].transform.position, dir, Team.Player,
                        soak: true, source: DamageSource.PrimaryWeapon));
                }
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
                       $"emitting={_lastEmitting}  tank={WaterNormalized:0.00}";
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
