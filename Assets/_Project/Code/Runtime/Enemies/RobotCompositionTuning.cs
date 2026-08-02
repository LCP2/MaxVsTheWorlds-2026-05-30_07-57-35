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

        /// <summary>Per-area robot-HP multiplier, off by default (<c>robotHpPerAreaMult</c>).</summary>
        public const float DefaultRobotHpPerAreaMult = 1f;

        /// <summary>The area the Heavy tier starts appearing in (<c>heavyIntroArea</c>).</summary>
        public const float DefaultHeavyIntroArea = 5f;

        /// <summary>The area the Brute tier starts appearing in (<c>bruteIntroArea</c>).</summary>
        public const float DefaultBruteIntroArea = 8f;

        /// <summary>Percent of large slots a tough tier substitutes for once introduced
        /// (<c>toughSubstitutionPct</c>).</summary>
        public const float DefaultToughSubstitutionPct = 25f;
    }
}
