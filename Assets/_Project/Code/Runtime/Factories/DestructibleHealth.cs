using System;
using UnityEngine;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// Plain health model for a destructible structure (the Mower Hutch factory, YT-37).
    /// Pure logic (no MonoBehaviour) so damage accumulation and the destroy-once semantics
    /// are unit-testable; the factory's health bar binds <see cref="Normalized"/> +
    /// <see cref="Changed"/>, and it fires <see cref="Destroyed"/> exactly once at 0 HP.
    /// </summary>
    public sealed class DestructibleHealth
    {
        public float Max { get; }
        public float Current { get; private set; }
        public bool IsAlive => Current > 0f;
        public float Normalized => Max > 0f ? Current / Max : 0f;

        /// <summary>Fired on any HP change. Arg = current HP.</summary>
        public event Action<float> Changed;

        /// <summary>Fired once, the moment HP reaches zero.</summary>
        public event Action Destroyed;

        public DestructibleHealth(float max)
        {
            Max = Mathf.Max(1f, max);
            Current = Max;
        }

        /// <summary>Apply damage. Returns true only on the hit that destroys it.</summary>
        public bool TakeDamage(float amount)
        {
            if (!IsAlive || amount <= 0f) return false;
            Current = Mathf.Max(0f, Current - amount);
            Changed?.Invoke(Current);
            if (Current <= 0f)
            {
                Destroyed?.Invoke();
                return true;
            }
            return false;
        }
    }
}
