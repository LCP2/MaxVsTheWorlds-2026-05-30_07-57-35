using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// A short-lived ground AoE used by Big Bermuda (YT-27) — grass-clipping puddles left by
    /// a charge, and the enrage blade-rain impacts. After a brief arm delay it ticks damage to
    /// anything of a hostile team standing inside its radius, then despawns. Attacker is
    /// Team.Enemy, so it hurts Max but not the boss or robots (friendly-fire rule). Built and
    /// destroyed in code — a flat greybox disc stands in for the effect.
    /// </summary>
    public sealed class DamageZone : MonoBehaviour
    {
        private float _radius;
        private float _damage;
        private float _tickInterval;
        private float _armDelay;
        private float _life;
        private float _age;
        private float _sinceTick;

        private static readonly Collider[] s_hits = new Collider[16];
        private static readonly List<IDamageable> s_buffer = new List<IDamageable>(8);

        // Read-only windows into existing state, for the readability VFX (YT-53) to draw a
        // danger indicator that fills as the zone arms. No behaviour change.
        public float Radius => _radius;
        public bool IsArming => _age < _armDelay;
        public float ArmProgress => _armDelay <= 0f ? 1f : Mathf.Clamp01(_age / _armDelay);

        /// <summary>Spawn a damage zone at a world position.</summary>
        public static DamageZone Spawn(Vector3 pos, float radius, float damage, float life,
            float armDelay, Color color, float tickInterval = 0.4f)
        {
            var go = new GameObject("DamageZone");
            go.transform.position = pos;
            var zone = go.AddComponent<DamageZone>();
            zone._radius = Mathf.Max(0.1f, radius);
            zone._damage = Mathf.Max(0f, damage);
            zone._life = Mathf.Max(0.1f, life);
            zone._armDelay = Mathf.Max(0f, armDelay);
            zone._tickInterval = Mathf.Max(0.05f, tickInterval);
            zone.BuildVisual(color);
            return zone;
        }

        private void BuildVisual(Color color)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Visual";
            var col = disc.GetComponent<Collider>();
            if (col != null) Destroy(col); // visual only — damage is by overlap query
            disc.transform.SetParent(transform, false);
            disc.transform.localScale = new Vector3(_radius * 2f, 0.05f, _radius * 2f);
            var rend = disc.GetComponent<Renderer>();
            if (rend != null)
            {
                var mpb = new MaterialPropertyBlock();
                rend.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", color);
                rend.SetPropertyBlock(mpb);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_age >= _life) { Destroy(gameObject); return; }
            if (_age < _armDelay) return; // telegraph window — no damage yet

            _sinceTick += dt;
            if (_sinceTick < _tickInterval) return;
            _sinceTick = 0f;
            ApplyDamage();
        }

        private void ApplyDamage()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, _radius, s_hits,
                ~0, QueryTriggerInteraction.Ignore);
            s_buffer.Clear();
            for (int i = 0; i < count; i++)
            {
                if (s_hits[i] == null) continue;
                if (s_hits[i].TryGetComponent<IDamageable>(out var d) && d.IsAlive
                    && DamageRules.Applies(Team.Enemy, d.Team) && !s_buffer.Contains(d))
                {
                    s_buffer.Add(d);
                }
            }
            foreach (var d in s_buffer)
            {
                d.TakeDamage(new DamageInfo(_damage, transform.position, Vector3.up, Team.Enemy));
            }
        }
    }
}
