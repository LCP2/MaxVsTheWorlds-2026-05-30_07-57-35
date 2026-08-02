using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Robot-accumulation maths for the gated 10-area arena (v0.5 recut spec §2, MV-223): how many
    /// robots roam a given area, and what fraction of them are large vs small. Pure + unit-testable —
    /// no MonoBehaviour, no live "current area" to read from yet.
    ///
    /// Landed as engine capability only, same idiom as <see cref="MaxWorlds.Arena.AreaGate"/>
    /// (MV-222): the shipped map has no runtime area index to hook this to (MV-222's own map-cutover
    /// follow-up still has to make that call), so every method here takes its area as an explicit
    /// 1-based <c>areaIndex</c> parameter rather than reading one off live state. Nothing calls this
    /// yet.
    /// </summary>
    public static class AreaPopulation
    {
        /// <summary>Total robots roaming <paramref name="areaIndex"/> (1-based): the Area 1 seed
        /// (<paramref name="startLargeCount"/> + <paramref name="startSmallCount"/>) compounded by
        /// <paramref name="areaGrowthPct"/>% for every area beyond the first — <c>P(n) =
        /// round(seed * (1 + areaGrowthPct/100)^(n-1))</c>.</summary>
        public static int TotalForArea(int areaIndex, float startLargeCount, float startSmallCount,
            float areaGrowthPct)
        {
            float seed = Mathf.Max(0f, startLargeCount) + Mathf.Max(0f, startSmallCount);
            float growth = Mathf.Pow(1f + areaGrowthPct / 100f, Mathf.Max(0, areaIndex - 1));
            return Mathf.RoundToInt(seed * growth);
        }

        /// <summary>The large-robot share [0,1] of <paramref name="areaIndex"/>'s population: the
        /// Area 1 base share — <paramref name="largeToSmallRatio"/> expressed as
        /// <c>ratio / (ratio + 1)</c> — drifting upward by <paramref name="largeShareDriftPerArea"/>
        /// per area beyond the first, clamped to a valid fraction.</summary>
        public static float LargeShareForArea(int areaIndex, float largeToSmallRatio,
            float largeShareDriftPerArea)
        {
            float baseShare = largeToSmallRatio / (largeToSmallRatio + 1f);
            float drifted = baseShare + largeShareDriftPerArea * Mathf.Max(0, areaIndex - 1);
            return Mathf.Clamp01(drifted);
        }

        /// <summary>The area's population split into (large, small) counts. Small is always the
        /// remainder of <see cref="TotalForArea"/> minus the rounded large share, rather than rounded
        /// independently, so the pair always sums to the area total exactly.</summary>
        public static (int Large, int Small) ComposeForArea(int areaIndex,
            float startLargeCount, float startSmallCount, float areaGrowthPct,
            float largeToSmallRatio, float largeShareDriftPerArea)
        {
            int total = TotalForArea(areaIndex, startLargeCount, startSmallCount, areaGrowthPct);
            float share = LargeShareForArea(areaIndex, largeToSmallRatio, largeShareDriftPerArea);
            int large = Mathf.RoundToInt(total * share);
            return (large, total - large);
        }
    }
}
