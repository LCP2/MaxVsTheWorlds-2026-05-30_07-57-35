using System;
using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// A world's <c>pacingRhythm</c> dial (Confluence MVW 34439170 §5/§8.5) — a gentle saw-tooth of
    /// per-area multipliers (build-up → peak → relief, not a straight ramp) that
    /// <see cref="DifficultyEngine.TargetBudget"/> multiplies onto the growth curve. This is the
    /// PACING lever (§2.2): when/how fast a budget's robots arrive, never their stats.
    /// </summary>
    [Serializable]
    public sealed class PacingRhythm
    {
        /// <summary>One multiplier per area, 1-based area 1 at index 0. An area past the end of the
        /// array holds at the last authored value rather than throwing — a world's rhythm is authored
        /// for its own area count, and a caller asking one area too far should not crash.</summary>
        public float[] multipliers = { 1f };

        public PacingRhythm() { }

        public PacingRhythm(params float[] multipliers) => this.multipliers = multipliers;

        public float MultiplierForArea(int areaIndex)
        {
            if (multipliers == null || multipliers.Length == 0) return 1f;
            int i = Mathf.Clamp(areaIndex - 1, 0, multipliers.Length - 1);
            return multipliers[i];
        }
    }
}
