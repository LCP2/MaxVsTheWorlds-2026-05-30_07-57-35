using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// Damage faction. Receivers reject damage whose <see cref="DamageInfo.Attacker"/>
    /// matches their own team (no friendly fire), except <see cref="Neutral"/> which
    /// always applies (environment/hazards). This is what stops a cluster of enemies
    /// from grinding itself to death regardless of how the hit is delivered.
    /// </summary>
    public enum Team { Player, Enemy, Neutral }

    /// <summary>
    /// Which of Max's arsenal dealt a hit. Nothing read this before the gated arena (WV-222): an area
    /// gate only breaks to sustained PRIMARY fire, so a receiver that cares now has a way to tell that
    /// apart from a Water Balloon splash or a future ability tick. Defaults to <see cref="Unspecified"/>
    /// so every damage call site that existed before this stays correct without being touched — only
    /// the primary weapon needs to say who it is.
    /// </summary>
    public enum DamageSource { Unspecified, PrimaryWeapon, SecondaryWeapon, Ability, Environment }

    /// <summary>
    /// A single damage application. <see cref="Soak"/> is the light elemental
    /// "soak" tag the Water Blaster applies (slice = raw damage only; the tag is
    /// a hook for future elemental synergy, YT-28). <see cref="Attacker"/> is the
    /// dealing faction (used for friendly-fire rejection). Carries the impact point
    /// so receivers can spawn hit feedback at the contact location.
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly Vector3 Point;
        public readonly Vector3 Direction;
        public readonly bool Soak;
        public readonly Team Attacker;
        public readonly DamageSource Source;

        public DamageInfo(float amount, Vector3 point, Vector3 direction, Team attacker, bool soak = false,
            DamageSource source = DamageSource.Unspecified)
        {
            Amount = amount;
            Point = point;
            Direction = direction;
            Attacker = attacker;
            Soak = soak;
            Source = source;
        }
    }

    /// <summary>
    /// Anything that can take damage (enemies, destructibles). Implemented by the
    /// YT-36 enemy; the Water Blaster (YT-35) only depends on this contract, so
    /// gadget and enemy stay decoupled. <see cref="Team"/> is the receiver's faction
    /// for friendly-fire rejection.
    /// </summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        Team Team { get; }
        void TakeDamage(in DamageInfo info);
    }

    /// <summary>
    /// Something that can be shoved by a hit — the Spray gadget's knockback (YT-64). Kept separate
    /// from <see cref="IDamageable"/> so only things that should move react (robots do; the boss and
    /// the fixed factory don't), and the gadget stays decoupled from any concrete enemy type.
    /// </summary>
    public interface IKnockbackable
    {
        /// <summary>Add a velocity impulse (m/s), decayed by the receiver.</summary>
        void ApplyKnockback(Vector3 impulse);
    }

    /// <summary>Shared friendly-fire rule: damage applies unless attacker and target
    /// share a (non-neutral) team. Pure + unit-testable.</summary>
    public static class DamageRules
    {
        public static bool Applies(Team attacker, Team target)
            => attacker == Team.Neutral || attacker != target;
    }

    /// <summary>Something that can be knocked out of action briefly without dying — the Water
    /// Balloon splash's "stop" (WV-231, spec §6a). Separate from <see cref="IKnockbackable"/>: a
    /// knockback shoves a receiver, a halt freezes whatever it was doing in place for a duration.</summary>
    public interface IHaltable
    {
        /// <summary>Freeze for at least this many seconds. Calling it again while already halted
        /// extends to the longer of the two, it never shortens an existing halt.</summary>
        void ApplyHalt(float seconds);
    }
}
