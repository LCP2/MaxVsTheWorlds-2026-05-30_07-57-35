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
        [SerializeField] private float energyPerTick = 2.5f;
        [SerializeField] private float regenPerSec = 25f;
        [SerializeField] private float regenDelay = 0.6f;

        [Header("Feedback")]
        [SerializeField] private Color streamColor = new Color(0.4f, 0.75f, 1f, 1f);

        [Header("Aim source")]
        [Tooltip("Optional. If set, fires while the player aims and orients to their facing. " +
                 "If null, IsFiring drives it directly (useful for isolated testing).")]
        [SerializeField] private PlayerController aimSource;

        public EnergyPool Energy { get; private set; }

        /// <summary>Fire gate. When no <see cref="aimSource"/> is set, drive this directly.
        /// Defaults true so the gadget is verifiable in isolation.</summary>
        public bool IsFiring { get; set; } = true;

        private float _tickTimer;
        private ParticleSystem _stream;
        private readonly Collider[] _hits = new Collider[32];
        private static readonly List<IDamageable> s_buffer = new List<IDamageable>(8);

        private void Awake()
        {
            Energy = new EnergyPool(maxEnergy, regenPerSec, regenDelay);
            _stream = BuildStreamParticles();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Energy.Tick(dt);

            // If bound to a player, fire while aiming and orient along their facing.
            if (aimSource != null)
            {
                IsFiring = aimSource.IsAiming;
                Vector3 f = aimSource.Facing;
                if (f.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(f, Vector3.up);
                }
            }

            bool emitting = IsFiring && Energy.CanSpend(energyPerTick);
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
            ps.Stop();

            var main = ps.main;
            main.startSpeed = range / 0.4f;
            main.startLifetime = 0.4f;
            main.startSize = radius;
            main.startColor = streamColor;
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 120f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = radius * 0.4f;

            return ps;
        }

        private void SetStreamEmitting(bool on)
        {
            if (_stream == null) return;
            var emission = _stream.emission;
            emission.enabled = on;
            if (on && !_stream.isPlaying) _stream.Play();
            else if (!on && _stream.isPlaying) _stream.Stop(true, ParticleSystemStopBehavior.StopEmitting);
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
