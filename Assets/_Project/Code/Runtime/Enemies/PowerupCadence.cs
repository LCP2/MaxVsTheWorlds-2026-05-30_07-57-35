using System;
using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Power-up cadence enforcement (World &amp; Difficulty Framework, Confluence MVW 34439170 §5/§8.7,
    /// MV-268): tracks areas-since-last-power-up and guarantees a reachable parts cache once
    /// <c>powerupCadence</c> would otherwise be exceeded — satisfiable even in a shed-free area,
    /// because parts drops work anywhere (§2.6) and are a power-up channel in their own right.
    /// </summary>
    public static class PowerupCadence
    {
        /// <summary>For each of <paramref name="areaCount"/> areas (index 0 = area 1), whether it
        /// carries a power-up source: <paramref name="hasShed"/> as authored, PLUS a guaranteed parts
        /// cache injected wherever the gap since the last source would otherwise exceed
        /// <paramref name="cadence"/>. The returned array's longest run of consecutive <c>false</c> is
        /// never longer than <paramref name="cadence"/>.</summary>
        public static bool[] EnsureCoverage(int areaCount, bool[] hasShed, int cadence)
        {
            int count = Mathf.Max(0, areaCount);
            var hasPowerup = new bool[count];
            for (int i = 0; i < count; i++)
                hasPowerup[i] = hasShed != null && i < hasShed.Length && hasShed[i];

            if (cadence <= 0) return hasPowerup;

            int sinceLast = 0;
            for (int i = 0; i < count; i++)
            {
                if (hasPowerup[i]) { sinceLast = 0; continue; }

                sinceLast++;
                if (sinceLast >= cadence)
                {
                    hasPowerup[i] = true; // guaranteed parts cache — the cadence rule firing
                    sinceLast = 0;
                }
            }

            return hasPowerup;
        }

        /// <summary>The longest run of consecutive areas with no power-up source — for asserting the
        /// guarantee actually held (never exceeds the cadence it was given).</summary>
        public static int LongestGap(bool[] hasPowerup)
        {
            if (hasPowerup == null) return 0;

            int longest = 0, current = 0;
            foreach (bool has in hasPowerup)
            {
                current = has ? 0 : current + 1;
                longest = Math.Max(longest, current);
            }
            return longest;
        }
    }
}
