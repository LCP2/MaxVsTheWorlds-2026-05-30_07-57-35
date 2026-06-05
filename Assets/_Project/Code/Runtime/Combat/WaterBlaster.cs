using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Player;

namespace MaxWorlds.Combat
{
    /// <summary>
    /// Slice gadget (YT-35) — Spray archetype. While <see cref="IsFiring"/> is
    /// driven true (player holds aim), auto-fires a short-range stream: ticks at
    /// a fixed cadence, spends energy, sphere-casts forward, and applies damage
    /// (+soak tag) to every <see cref="IDamageable"/> in the stream. Firing VFX is
    /// code-driven (a ParticleSystem built at runtime), per the "no authored asset"
    /// rule. Energy binds to the HUD (YT-30) via <see cref="Energy"/>.
    /// </summary>
    public sealed class WaterBlaster : MonoBehaviour
    {
        [Header("Stream")]
        [SerializeField] private float range = 6f;
        [SerializeField] private float radius = 0.6f;
        [SerializeField] private float damagePerTick = 4f;
        [SerializeField] private float fireInterval = 0.1f;   // seconds between ticks
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Energy")]
        [SerializeField] private float maxEnergy = 100f;
        [SerializeField] private float energyPerTick = 1.5f;
        [SerializeField] private float regenPerSec = 35f;
        [SerializeField] private float regenDelay = 0.4f;
        [Tooltip("After the tank empties, fire stays locked until energy recharges to " +
                 "this fraction of max (hysteresis — prevents single-puff dribbling).")]
        [Range(0.1f, 1f)]
        [SerializeField] private float rechargeFraction = 0.35f;

        [Header("Feedback")]
        [SerializeField] private Color streamColor = new Color(0.4f, 0.75f, 1f, 1f);

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

        private float _tickTimer;
        private bool _depleted;
        private bool _lastEmitting;
        private ParticleSystem _stream;
        private readonly Collider[] _hits = new Collider[32];
        private static readonly List<IDamageable> s_buffer = new List<IDamageable>(8);

        private void Awake()
        {
            Energy = new EnergyPool(maxEnergy, regenPerSec, regenDelay);
            _stream = BuildStreamParticles();
            // Guarantee the stream is genuinely stopped at start (bookkeeping + reality
            // agree), so the "act only on change" guard in SetStreamEmitting is valid.
            _streamOn = false;
            _stream.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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

            // Hysteresis: once the tank runs dry, lock fire out until it recharges to
            // rechargeFraction of max. Without this, an empty tank dribbles a single
            // puff every regenDelay (the "clouds of bubbles" stutter) instead of a
            // clean stream → deplete → recharge → stream cycle.
            if (_depleted && Energy.Normalized >= rechargeFraction) _depleted = false;
            else if (!_depleted && !Energy.CanSpend(energyPerTick)) _depleted = true;

            bool emitting = ShouldEmit(IsFiring, !_depleted && Energy.CanSpend(energyPerTick));
            _lastEmitting = emitting;
            SetStreamEmitting(emitting);
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
            // Spherecast volume along the stream; gather unique damageables.
            int count = Physics.OverlapCapsuleNonAlloc(
                origin, origin + dir * range, radius, _hits, hitMask, QueryTriggerInteraction.Ignore);

            s_buffer.Clear();
            for (int i = 0; i < count; i++)
            {
                if (_hits[i] == null) continue;
                if (_hits[i].TryGetComponent<IDamageable>(out var d) && d.IsAlive && !s_buffer.Contains(d))
                {
                    s_buffer.Add(d);
                }
            }

            foreach (var d in s_buffer)
            {
                var comp = d as Component;
                Vector3 point = comp != null ? comp.transform.position : origin + dir * range;
                d.TakeDamage(new DamageInfo(damagePerTick, point, dir, soak: true));
            }
        }

        private ParticleSystem BuildStreamParticles()
        {
            var go = new GameObject("WaterStreamVFX");
            go.transform.SetParent(transform, worldPositionStays: false);
            var ps = go.AddComponent<ParticleSystem>();

            // Fast, small, dense droplets reading as a continuous stream — not fat
            // bubbles. Lifetime ~ range/speed so particles travel the stream length.
            float speed = range / 0.35f;
            var main = ps.main;
            // AddComponent<ParticleSystem> defaults playOnAwake=true, which made the
            // stream emit continuously regardless of IsFiring (it piled onto any body
            // touching Max = the stray "bubbles"). Force it off and start stopped.
            main.playOnAwake = false;
            main.startSpeed = speed;
            main.startLifetime = range / speed;        // reaches stream end, no further
            main.startSize = radius * 0.35f;           // droplets, not radius-wide bubbles
            main.startColor = streamColor;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 90f;               // dense enough to read as continuous

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 3f;                          // tighter spray
            shape.radius = radius * 0.2f;

            return ps;
        }

        private bool _streamOn;

        private void SetStreamEmitting(bool on)
        {
            if (_stream == null || on == _streamOn) return; // only act on change — no per-frame churn
            _streamOn = on;
            var emission = _stream.emission;
            emission.enabled = on;
            if (on) _stream.Play();
            else _stream.Stop(true, ParticleSystemStopBehavior.StopEmitting); // existing particles fade
        }

        private void OnGUI()
        {
            if (!debugOverlay) return;
            bool aiming = aimSource != null && aimSource.IsAiming;
            string s = $"Blaster: IsFiring={IsFiring}  aimSource.IsAiming={aiming}  " +
                       $"emitting={_lastEmitting}  streamPlaying={(_stream != null && _stream.isPlaying)}  " +
                       $"energy={Energy?.Normalized:0.00}  depleted={_depleted}";
            GUI.color = _lastEmitting ? Color.cyan : Color.white;
            GUI.Label(new Rect(12f, 64f, 900f, 24f), s);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = streamColor;
            Gizmos.DrawWireSphere(transform.position + transform.forward * range, radius);
        }
#endif
    }
}
