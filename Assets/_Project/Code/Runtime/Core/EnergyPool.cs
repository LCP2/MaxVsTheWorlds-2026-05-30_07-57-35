using System;
using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// Plain energy/ammo model for the slice gadget (YT-35). Drains while firing,
    /// regenerates after a short idle delay. Pure logic (no MonoBehaviour) so it's
    /// unit-testable; the HUD (YT-30) binds <see cref="Normalized"/> + <see cref="Changed"/>.
    /// </summary>
    public sealed class EnergyPool
    {
        public float Max { get; }
        public float Current { get; private set; }

        /// <summary>
        /// Refill rate per second. Settable so the dev tuning panel can retune a live tank without
        /// rebuilding it (YT-105) — rebuilding would reset <see cref="Current"/> mid-fight and lose
        /// the HUD's <see cref="Changed"/> subscription.
        /// </summary>
        public float RegenPerSec
        {
            get => _regenPerSec;
            set => _regenPerSec = Mathf.Max(0f, value);
        }

        private float _regenPerSec;
        private readonly float _regenDelay;
        private float _timeSinceDrain;

        /// <summary>Fired whenever Current changes (HUD subscribes).</summary>
        public event Action<float> Changed;

        public float Normalized => Max > 0f ? Current / Max : 0f;

        public EnergyPool(float max, float regenPerSec, float regenDelay)
        {
            Max = Mathf.Max(0f, max);
            _regenPerSec = Mathf.Max(0f, regenPerSec);
            _regenDelay = Mathf.Max(0f, regenDelay);
            Current = Max;
        }

        /// <summary>True if at least <paramref name="cost"/> energy is available.</summary>
        public bool CanSpend(float cost) => Current >= cost;

        /// <summary>Spend energy if available. Returns false (no change) if insufficient.</summary>
        public bool TrySpend(float cost)
        {
            if (cost <= 0f) return true;
            if (Current < cost) return false;
            Current -= cost;
            _timeSinceDrain = 0f;
            Changed?.Invoke(Current);
            return true;
        }

        /// <summary>Top the tank straight back up. Used by dev/filming mode (YT-60) so the stream
        /// can be held indefinitely; nothing in a normal session calls it.</summary>
        public void Refill()
        {
            if (Current >= Max) return;
            Current = Max;
            Changed?.Invoke(Current);
        }

        /// <summary>Advance regen. Call once per frame with deltaTime.</summary>
        public void Tick(float dt)
        {
            if (Current >= Max) return;
            _timeSinceDrain += dt;
            if (_timeSinceDrain < _regenDelay) return;
            float before = Current;
            Current = Mathf.Min(Max, Current + _regenPerSec * dt);
            if (!Mathf.Approximately(before, Current)) Changed?.Invoke(Current);
        }
    }
}
