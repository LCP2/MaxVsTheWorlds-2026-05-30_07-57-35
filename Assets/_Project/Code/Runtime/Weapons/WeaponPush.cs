using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// MV-1476: the ONE shared pushback rule every player-side damaging hit on a robot applies (the World 1
    /// hose's YT-64 knockback, generalised). All tuning lives here.
    ///
    /// push displacement per hit = <see cref="BaseDisplacementPerSecond"/> / refHitsPerSecond
    ///                             x (1 + <see cref="PowerStep"/> x (POWER level - 1))
    ///
    /// Every weapon's sustained-fire displacement per second at POWER 1 therefore equals the Water
    /// Blaster's, whatever its fire rate. Resistances stay in <see cref="RobotEnemy"/> (per-archetype
    /// knockbackDecay; Dreadnought/Breaker refuse), because the impulse handed to
    /// <see cref="IKnockbackable.ApplyKnockback"/> is a velocity the receiver decays itself.
    /// </summary>
    public static class WeaponPush
    {
        /// <summary>Decay (m/s^2) of the reference robot (Rusher). Converts a wanted displacement into an
        /// impulse; heavier archetypes decay faster and so are pushed less.</summary>
        public const float ReferenceDecay = 28f;

        /// <summary>The Water Blaster's hits per second (its fire interval is 0.1 s).</summary>
        public const float HoseHitsPerSecond = 10f;

        /// <summary>Per-hit impulse the hose has always applied (MV-658 tuning).</summary>
        public const float HoseImpulse = 2.64f;

        /// <summary>Displacement per second of sustained hose fire at POWER 1, derived from the hose's
        /// own impulse: v^2 / (2 decay) per hit x hits per second.</summary>
        public const float BaseDisplacementPerSecond =
            HoseImpulse * HoseImpulse / (2f * ReferenceDecay) * HoseHitsPerSecond;

        /// <summary>+12% displacement per POWER level above 1 (level 5 = 1.48x).</summary>
        public const float PowerStep = 0.12f;

        /// <summary>Explosive hits push radially at this multiple of base.</summary>
        public const float ExplosiveMultiplier = 1.5f;

        // Reference (level-1) hits per second for each weapon, so per-hit push scales by fire rate.
        public const float RicochetHitsPerSecond = 3.0f;     // Ricochet.RateDiscsPerSecondByLevel[0]
        public const float UndertowHitsPerSecond = 10f;      // Undertow.DefaultFireInterval 0.1 s
        public const float LightMissileHitsPerSecond = 1.0f; // LightMissile interval level 1, one ball
        public const float RocketHitsPerSecond = 1.0f;
        public const float OrbitalHitsPerSecond = 2.0f;

        // MV-1480: a hit HOLDS the robot; the push is only a token nudge on top.
        /// <summary>Seconds a robot stays held after the latest player-weapon hit.</summary>
        public const float HoldSeconds = 0.3f;
        /// <summary>Max sustained-fire displacement per second at POWER 1 (m); ticket cap 0.15, set under so 2 s of simulated fire (discrete ticks, 21 hits) stays inside 0.3 m.</summary>
        public const float PushPerSecondPower1 = 0.12f;
        /// <summary>Max sustained-fire displacement per second at max POWER (m).</summary>
        public const float PushPerSecondMaxPower = 0.20f;
        public const int MaxPowerLevel = 5;
        /// <summary>Most a single rocket blast moves a robot (m).</summary>
        public const float RocketMaxPush = 0.4f;

        /// <summary>Displacement per second of sustained fire for a POWER level (m), linear 0.15 -> 0.25.</summary>
        public static float PushPerSecond(int powerLevel)
        {
            float t = Mathf.Clamp01((Mathf.Max(1, powerLevel) - 1) / (float)(MaxPowerLevel - 1));
            return Mathf.Lerp(PushPerSecondPower1, PushPerSecondMaxPower, t);
        }

        public static float PowerMultiplier(int powerLevel) => 1f + PowerStep * (Mathf.Max(1, powerLevel) - 1);

        /// <summary>Displacement (m) a single hit should move a reference robot.</summary>
        public static float DisplacementPerHit(float refHitsPerSecond, int powerLevel, float scale = 1f) =>
            Mathf.Min(PushPerSecond(powerLevel) / Mathf.Max(0.01f, refHitsPerSecond) * scale, RocketMaxPush);

        /// <summary>Impulse (m/s) that moves a reference robot <paramref name="displacement"/> metres.</summary>
        public static float ImpulseFor(float displacement) =>
            Mathf.Sqrt(2f * ReferenceDecay * Mathf.Max(0f, displacement));

        public static int CurrentPowerLevel => WeaponSystemState.TrackLevel(WeaponTrackKind.Damage);

        /// <summary>Pushes <paramref name="target"/> (if it can be pushed) along <paramref name="direction"/>
        /// (flattened to the ground plane).</summary>
        public static void Apply(object target, Vector3 direction, float refHitsPerSecond, float scale = 1f)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f) return;
            float impulse = ImpulseFor(DisplacementPerHit(refHitsPerSecond, CurrentPowerLevel, scale));
            if (impulse <= 0f) return;
            Vector3 v = direction.normalized * impulse;
            if (target is RobotEnemy robot) robot.ApplyWeaponHit(v);
            else if (target is IKnockbackable kb) kb.ApplyKnockback(v);
        }

        /// <summary>Radial push away from <paramref name="blastCentre"/> at <see cref="ExplosiveMultiplier"/>.</summary>
        public static void ApplyRadial(object target, Vector3 targetPosition, Vector3 blastCentre, float refHitsPerSecond) =>
            Apply(target, targetPosition - blastCentre, refHitsPerSecond, ExplosiveMultiplier);
    }
}
