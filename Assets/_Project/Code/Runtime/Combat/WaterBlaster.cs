using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Player;
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
    /// All firing visuals live in <see cref="WaterVfx"/> (YT-47), which this attaches
    /// to itself at Awake and drives with cosmetic-only calls. The VFX never feeds back
    /// into fire gating, energy, or damage.
    /// </summary>
    public sealed class WaterBlaster : MonoBehaviour
    {
        [Header("Stream")]
        [SerializeField] private float range = 6f;
        [SerializeField] private float radius = 0.6f;
        [SerializeField] private float damagePerTick = 4f;
        [SerializeField] private float fireInterval = 0.1f;   // seconds between ticks
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Spray archetype (YT-64) — a threatening arc, not a thin dribble")]
        [Tooltip("Half-angle of the spray cone, degrees. Everything in this arc within range is hit.")]
        [SerializeField] private float coneHalfAngle = 35f;
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

        // --- Power ramp (YT-67) ---------------------------------------------------------------
        // The authored numbers, captured before any level-up scales them. Multipliers are always
        // applied to these, never compounded onto the live values, so re-applying is harmless.
        private float _baseDamage;
        private float _baseInterval;
        private float _baseEnergyPerSecond;

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

        private float _tickTimer;
        private bool _depleted;
        private bool _lastEmitting;
        private WaterVfx _vfx;
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
            _vfx.Init(range, Mathf.Max(radius, streamVisualRadius)); // fatter = reads as a spray fan

            // The level-up ramp rides along with the gadget, same self-attaching rule as the VFX.
            if (GetComponent<PlayerPower>() == null) gameObject.AddComponent<PlayerPower>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Energy.Tick(dt);

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
            // Spray: gather everything within range, then keep only what's inside the cone arc —
            // so one tick can wash a whole knot of robots, not a single-file tube (YT-64).
            int count = Physics.OverlapSphereNonAlloc(
                origin, range, _hits, hitMask, QueryTriggerInteraction.Ignore);

            s_buffer.Clear();
            s_contacts.Clear();
            for (int i = 0; i < count; i++)
            {
                if (_hits[i] == null) continue;
                if (_hits[i].TryGetComponent<IDamageable>(out var d) && d.IsAlive && d.Team != Team.Player
                    && !s_buffer.Contains(d)
                    && SprayHit.InCone(origin, dir, _hits[i].transform.position, range, coneHalfAngle)
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
                Vector3 end = origin + dir * range;
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
