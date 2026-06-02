using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// A single damage application. <see cref="Soak"/> is the light elemental
    /// "soak" tag the Water Blaster applies (slice = raw damage only; the tag is
    /// a hook for future elemental synergy, YT-28). Carries the impact point so
    /// receivers can spawn hit feedback at the contact location.
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly Vector3 Point;
        public readonly Vector3 Direction;
        public readonly bool Soak;

        public DamageInfo(float amount, Vector3 point, Vector3 direction, bool soak = false)
        {
            Amount = amount;
            Point = point;
            Direction = direction;
            Soak = soak;
        }
    }

    /// <summary>
    /// Anything that can take damage (enemies, destructibles). Implemented by the
    /// YT-36 enemy; the Water Blaster (YT-35) only depends on this contract, so
    /// gadget and enemy stay decoupled.
    /// </summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        void TakeDamage(in DamageInfo info);
    }
}
