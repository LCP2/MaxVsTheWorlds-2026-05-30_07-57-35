using UnityEngine;

namespace MaxWorlds.Pickups
{
    /// <summary>
    /// Authored magnitudes for the power-cell economy recut (WV-227) that don't yet have an owning
    /// weapon/ability component to live on: Water Balloon (secondary) and Dash/Teleport (special)
    /// don't exist until WV-231 builds them, so their per-use costs are settings only for now, ready
    /// for that ticket to spend. Same for Power Efficiency — the ability and its level don't exist
    /// yet (WV-231/WV-230), so <see cref="EfficiencyMultiplier"/> is a pure formula ready for a real
    /// level to be plugged in once one exists.
    /// </summary>
    public static class CellEconomyTuning
    {
        /// <summary>Cells a Water Balloon throw costs (v0.5 recut spec §5).</summary>
        public const float DefaultSecondaryCellsPerUse = 2f;

        /// <summary>Cells a Dash/Teleport activation costs (v0.5 recut spec §5).</summary>
        public const float DefaultSpecialAbilityCellsPerUse = 3f;

        /// <summary>Fraction each Power Efficiency level shaves off a drain — 0.1 = 10%/level, so a
        /// maxed L5 ability would halve every drain.</summary>
        public const float DefaultPowerEfficiencyReductionPerLevel = 0.1f;

        /// <summary>The drain multiplier for a given Power Efficiency level (clamped 0-5, one level
        /// per L1-5 of the ability). Level 0 — no ability yet, or not installed — is always 1x, i.e.
        /// no reduction. Pure so it's testable without any live ability state.</summary>
        public static float EfficiencyMultiplier(int level, float reductionPerLevel) =>
            Mathf.Clamp01(1f - Mathf.Clamp(level, 0, 5) * Mathf.Max(0f, reductionPerLevel));
    }
}
