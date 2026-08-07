using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// The Bomber's slow homing missile (MV-293). Free-flying — not pooled, not parented to the
    /// spawner — because it's short-lived and self-destroys on impact or timeout, the same "one and
    /// done" lifetime as the death VFX it plays a similar role to.
    ///
    /// Homing is deliberately GENTLE (<see cref="TurnRateDegPerSec"/>): a player who is actually
    /// moving can juke it, which is the whole point of a "slow homing" threat over a hitscan one — it
    /// pressures you into not standing still, it doesn't guarantee a hit the way a perfectly-tracking
    /// projectile would.
    /// </summary>
    public sealed class HomingMissile : MonoBehaviour
    {
        private const float TurnRateDegPerSec = 90f;
        private const float ContactRadius = 0.5f;
        private const float MaxLifetime = 6f;

        private Transform _target;
        private IDamageable _targetDamageable;
        private float _speed;
        private float _damage;
        private float _splashRadius;
        private float _age;
        private bool _detonated;

        /// <summary>Launch one missile from <paramref name="origin"/> toward <paramref name="target"/>.
        /// <paramref name="damage"/>/<paramref name="splashRadius"/> come straight off the Bomber's
        /// <see cref="EnemyArchetype.ContactDamage"/>/<see cref="EnemyArchetype.ContactRadius"/> — same
        /// numbers, ranged-attack meaning.</summary>
        public static HomingMissile Fire(Vector3 origin, Transform target, float speed, float damage,
            float splashRadius)
        {
            var go = new GameObject("HomingMissile (stand-in)");
            go.transform.position = origin;

            Vector3 aim = target != null ? target.position - origin : Vector3.forward;
            aim.y = 0f;
            if (aim.sqrMagnitude < 1e-4f) aim = Vector3.forward;
            go.transform.rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);

            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Body";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = Vector3.one * 0.35f;
            Object.Destroy(visual.GetComponent<Collider>()); // manual proximity check below, not physics

            var missile = go.AddComponent<HomingMissile>();
            missile.Init(target, speed, damage, splashRadius);
            return missile;
        }

        private void Init(Transform target, float speed, float damage, float splashRadius)
        {
            _target = target;
            _targetDamageable = target != null ? target.GetComponent<IDamageable>() : null;
            _speed = speed;
            _damage = damage;
            _splashRadius = splashRadius;
        }

        private void Update()
        {
            if (_detonated) return;
            float dt = Time.deltaTime;
            _age += dt;

            if (_target != null)
            {
                Vector3 to = _target.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 1e-4f)
                {
                    Quaternion wanted = Quaternion.LookRotation(to.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted,
                        TurnRateDegPerSec * dt);
                }
            }

            transform.position += transform.forward * (_speed * dt);

            bool closeEnough = _target != null &&
                (transform.position - _target.position).sqrMagnitude <= ContactRadius * ContactRadius;
            if (closeEnough || _age >= MaxLifetime) Detonate();
        }

        private void Detonate()
        {
            _detonated = true;

            if (_targetDamageable != null && _targetDamageable.IsAlive && _target != null &&
                (transform.position - _target.position).magnitude <= _splashRadius)
            {
                _targetDamageable.TakeDamage(
                    new DamageInfo(_damage, transform.position, transform.forward, Team.Enemy));
            }

            Destroy(gameObject);
        }
    }
}
