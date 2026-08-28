namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Authored magnitudes for the robot-accumulation scheme (v0.5 recut spec §1-2/§9). The gated
    /// 10-area arena is MV-222; the area-population maths (<see cref="AreaPopulation"/>) and
    /// concurrent-cap queue (<see cref="AreaSpawnQueue"/>) that spend most of these are MV-223; the
    /// heavy/brute intro-area + substitution knobs are spent by <see cref="AreaPopulation.ToughSplitForArea"/>
    /// (MV-224). Still no live "current area" index to drive any of it from — that's MV-222's own
    /// map-cutover follow-up.
    /// </summary>
    public static class RobotCompositionTuning
    {
        /// <summary>Large robots roaming Area 1 at run start (<c>startLargeCount</c>).</summary>
        public const float DefaultStartLargeCount = 4f;

        /// <summary>Small robots roaming Area 1 at run start (<c>startSmallCount</c>).</summary>
        public const float DefaultStartSmallCount = 4f;

        /// <summary>Percent population growth per area, compounding (<c>areaGrowthPct</c>).</summary>
        public const float DefaultAreaGrowthPct = 10f;

        /// <summary>Base large:small population ratio at Area 1 (<c>largeToSmallRatio</c>).</summary>
        public const float DefaultLargeToSmallRatio = 1f;

        /// <summary>Large-share drift per area, toward ~70% large by Area 10
        /// (<c>largeShareDriftPerArea</c>).</summary>
        public const float DefaultLargeShareDriftPerArea = 0.022f;

        /// <summary>Concurrent robots on screen, field-wide — the anti-flood cap
        /// (<c>maxActiveRobots</c>).</summary>
        public const float DefaultMaxActiveRobots = 18f;

        /// <summary>The one field-wide live-robot ceiling every spawn AND placement path shares
        /// (MV-612) — EnemySpawner's shed streams (<see cref="MaxWorlds.Enemies.EnemySpawner.GlobalMaxLiveEnemies"/>)
        /// and <see cref="AreaSpawnQueue"/>'s ambient release both read this same authored default
        /// (and the same <c>DevTuning.GlobalRobotBudget</c> override). Validated against
        /// <see cref="DefaultMaxActiveRobots"/> (18): up to three areas can be concurrently live at
        /// once — the current area, a previous area's still-draining overflow tail, and MV-514's
        /// pre-placed next area — so a purely per-area cap alone permits a worst case of 3 x 18 = 54
        /// concurrent robots. This sits above one area's own full cap (so a single dense room's
        /// ambient population is never itself starved by the field-wide budget) but well under that
        /// 54-robot worst case — the same 24 EnemySpawner already enforced field-wide (YT-186) before
        /// this ticket generalised it to every path. Garrison placement is the one deliberate
        /// exception: it counts toward this budget but is never blocked by it (<see cref="AreaSpawnQueue.TryTakeForGarrison(int, out EnemyKind)"/>),
        /// because a room's authored garrison must always be present the instant it's entered.</summary>
        public const float DefaultGlobalRobotBudget = 24f;

        /// <summary>Per-area robot-HP multiplier, off by default (<c>robotHpPerAreaMult</c>).</summary>
        public const float DefaultRobotHpPerAreaMult = 1f;

        /// <summary>The area the Heavy tier starts appearing in (<c>heavyIntroArea</c>).</summary>
        public const float DefaultHeavyIntroArea = 5f;

        /// <summary>The area the Brute tier starts appearing in (<c>bruteIntroArea</c>).</summary>
        public const float DefaultBruteIntroArea = 8f;

        /// <summary>Percent of large slots a tough tier substitutes for once introduced
        /// (<c>toughSubstitutionPct</c>).</summary>
        public const float DefaultToughSubstitutionPct = 25f;

        /// <summary>Seconds between contact-damage ticks for a kind that no longer lunges (MV-428:
        /// Bruiser/Heavy/Brute) while it stands touching Max — the readability fix's Change 1. Read
        /// live via <see cref="MaxWorlds.Core.DevTuning.ContactDamageCooldown"/>.</summary>
        public const float DefaultContactCooldown = 1.0f;

        /// <summary>How many Rusher/Blinker-kind robots may hold an attack token — be mid-Telegraph
        /// or mid-Lunge — at once (MV-428's Change 2). A robot without a token keeps closing and
        /// pressuring at its normal move speed instead of committing. Read live via
        /// <see cref="MaxWorlds.Core.DevTuning.LungeTokenCap"/>.</summary>
        public const float DefaultLungeTokenCap = 2f;
    }
}
