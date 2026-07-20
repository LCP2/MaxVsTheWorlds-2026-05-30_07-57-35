using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

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
        private GroundRing _ring;

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

        /// <summary>
        /// How high the puddle draws. Below the arming telegraph (0.03) so the mark the player has
        /// to react to always wins, above the always-on ground anchors (0.02) so a boss attack is
        /// never hidden under a footprint ring.
        /// </summary>
        public const float ZoneLift = 0.026f;

        /// <summary>
        /// The mark on the lawn (YT-113).
        ///
        /// This was a CYLINDER parented at the zone's own position, and both halves of that were
        /// wrong. The zone is a damage SPHERE whose centre sits at chest height — blade-rain spawns
        /// at y=1, grass at the boss's mid-body — so a visual drawn at the zone's origin floated a
        /// metre above the grass instead of lying on it. That is the "white circles around the
        /// player" and the "green circles around the boss": they were never on the ground.
        ///
        /// It also assigned no material, relying on the surface director to hand it one a frame
        /// later. Grass puddles spawn every 0.18s for the length of a charge, so there was always
        /// one wearing Unity's default material — untextured and untinted, which is white in the
        /// editor and worse in a build. An untinted disc IS the white circle.
        ///
        /// <see cref="GroundRing"/> already solves every part of this: a flat quad, a real
        /// alpha-blended material at creation, tint through a property block, and an explicit place
        /// in the lift order. It is also skipped by the surface sweep, so nothing can repaint it.
        /// </summary>
        private void BuildVisual(Color color)
        {
            // Splat, not a hard disc: this is a puddle of clippings and a scatter of blades, and a
            // geometric circle is what made them read as "random discs" rather than as something
            // the boss did.
            _ring = GroundRing.Create("Visual", VfxMaterials.Splat());
            _ring.Lift = ZoneLift;
            _ring.transform.SetParent(transform, false);
            _ring.Show(Grounded(transform.position), _radius, color);
        }

        /// <summary>The lawn directly under a point. The zone's own Y is the centre of its damage
        /// sphere, which is nowhere near the ground it should be drawing on.</summary>
        private static Vector3 Grounded(Vector3 p) => new Vector3(p.x, 0f, p.z);

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
