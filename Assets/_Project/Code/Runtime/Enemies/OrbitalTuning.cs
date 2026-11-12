using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// ORBITAL's (MV-1344) live-tunable numbers. The authored defaults are consts here; every accessor
    /// goes through <see cref="DevTuning.Or"/>, so the Settings panel's "ORBITAL" tab changes live
    /// missiles and robots on the next frame. Rules 3-12 of the ticket are these numbers.
    /// </summary>
    public static class OrbitalTuning
    {
        public const float DefaultMissileSpeed = 6.5f;
        public const float DefaultTurnAtLaunch = 45f;       // deg/s
        public const float DefaultTurnGrowth = 10f;         // deg/s gained per second airborne
        public const float DefaultLife = 5f;
        public const float DefaultFireInterval = 1.25f;
        public const float DefaultMaxInFlight = 4f;
        public const float DefaultOuterAngle = 110f;
        public const float DefaultInnerAngle = 70f;
        public const float DefaultDamage = 8f;
        public const float DefaultTrailSeconds = 0.8f;
        public const float DefaultRobotHp = 45f;

        /// <summary>The 0.25 s glow on the firing tube before each launch (rule 3).</summary>
        public const float GlowSeconds = 0.25f;
        /// <summary>Crown spin-up once Max is first within <see cref="SpinUpRange"/> (rule 2).</summary>
        public const float SpinUpSeconds = 0.6f;
        public const float SpinUpRange = 11f;
        /// <summary>A missile flies straight for this long before it may steer (rule 4).</summary>
        public const float StraightMetres = 1f;
        public const float FlightHeight = 1.7f;
        public const float ContactRadius = 0.45f;
        public const float BallDiameter = 0.45f;
        /// <summary>Hard world-wide pool budget (rule 11).</summary>
        public const int WorldBudget = 12;
        /// <summary>Killing the ORBITAL pops its missiles within this long (rule 9).</summary>
        public const float PopAllWithinSeconds = 0.3f;

        public static float MissileSpeed => DevTuning.Or(DevTuning.OrbitalMissileSpeed, DefaultMissileSpeed);
        public static float TurnAtLaunch => DevTuning.Or(DevTuning.OrbitalTurnAtLaunch, DefaultTurnAtLaunch);
        public static float TurnGrowth => DevTuning.Or(DevTuning.OrbitalTurnGrowth, DefaultTurnGrowth);
        public static float Life => DevTuning.Or(DevTuning.OrbitalLife, DefaultLife);
        public static float FireInterval => Mathf.Max(0.05f, DevTuning.Or(DevTuning.OrbitalFireInterval, DefaultFireInterval));
        public static int MaxInFlight => Mathf.Max(1, Mathf.RoundToInt(DevTuning.Or(DevTuning.OrbitalMaxInFlight, DefaultMaxInFlight)));
        public static float OuterAngle => DevTuning.Or(DevTuning.OrbitalOuterAngle, DefaultOuterAngle);
        public static float InnerAngle => DevTuning.Or(DevTuning.OrbitalInnerAngle, DefaultInnerAngle);
        public static float Damage => DevTuning.Or(DevTuning.OrbitalDamage, DefaultDamage);
        public static float TrailSeconds => DevTuning.Or(DevTuning.OrbitalTrailSeconds, DefaultTrailSeconds);
        public static float RobotHp => DevTuning.Or(DevTuning.OrbitalRobotHp, DefaultRobotHp);

        /// <summary>The launch-angle sequence step <paramref name="step"/> (0-based, never ends): -outer,
        /// -inner, +inner, +outer, then reversed (+outer, +inner, -inner, -outer), and repeats (rule 4).
        /// Degrees measured from the line to Max.</summary>
        public static float LaunchAngle(int step)
        {
            int i = ((step % 8) + 8) % 8;
            switch (i)
            {
                case 0: return -OuterAngle;
                case 1: return -InnerAngle;
                case 2: return InnerAngle;
                case 3: return OuterAngle;
                case 4: return OuterAngle;
                case 5: return InnerAngle;
                case 6: return -InnerAngle;
                default: return -OuterAngle;
            }
        }
    }
}
