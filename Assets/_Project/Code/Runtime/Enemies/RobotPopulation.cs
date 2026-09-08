using System.Collections.Generic;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-716: how a converted robot counts for two purposes that must disagree. Excluded from the
    /// live-robot cap (MV-612) — a conversion must not suppress spawning, per the ticket's own
    /// cross-check. Still counted as "alive" for an area's clear condition until it burns out, so
    /// converting a robot delays a clear rather than skipping it (spec: "a converted robot still counts
    /// as alive for the area's clear condition until it burns out").
    /// </summary>
    public static class RobotPopulation
    {
        /// <summary>How many of <paramref name="robots"/> should count against a live-robot spawn
        /// budget — engageable and NOT currently converted.</summary>
        public static int LiveCapCount(IEnumerable<RobotEnemy> robots)
        {
            int count = 0;
            foreach (RobotEnemy r in robots)
            {
                if (r != null && r.IsEngageable && !r.IsConverted) count++;
            }
            return count;
        }

        /// <summary>True once none of <paramref name="robots"/> is engageable — a still-converted robot
        /// (engageable, just not hostile) keeps this false until it burns out.</summary>
        public static bool IsAreaClear(IEnumerable<RobotEnemy> robots)
        {
            foreach (RobotEnemy r in robots)
            {
                if (r != null && r.IsEngageable) return false;
            }
            return true;
        }
    }
}
