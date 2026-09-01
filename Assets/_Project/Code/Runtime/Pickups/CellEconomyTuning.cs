using UnityEngine;

namespace MaxWorlds.Pickups
{
    /// <summary>
    /// Authored magnitudes for the drop economy (WV-226). MV-290 retired power cells as ability
    /// fuel (abilities are cooldown-gated only, the primary never depletes); MV-458 gave them a new
    /// role instead — THE RIG board's primary progression currency (<see cref="MaxWorlds.Weapons.CellSpend"/>),
    /// not merely a display-only counter any more.
    /// </summary>
    public static class CellEconomyTuning
    {
        /// <summary>Power cells a large-robot kill drops, guaranteed (WV-226, v0.5 recut spec §5
        /// <c>cellsPerLargeKill</c>) — small robots drop nothing at all. Used as the flat fallback rate
        /// when a per-area budget can't be resolved (no live area context, e.g. a test scene) or a
        /// dev-tuning override is active (<see cref="MaxWorlds.Core.DevTuning.CellsPerLargeKill"/>).</summary>
        public const float DefaultCellsPerLargeKill = 0.75f;

        /// <summary>Total power cells authored for area 1's full large-kill haul (MV-375) — the
        /// intercept of <see cref="CellsForArea"/>.</summary>
        public const float CellsAreaIntercept = 3f;

        /// <summary>Total power cells the per-area budget rises by for every area past the first
        /// (MV-375) — a straight line, deliberately NOT the compounding rate
        /// <see cref="MaxWorlds.Enemies.DifficultyEngine.TargetBudget"/> grows the robot population by.</summary>
        public const float CellsAreaSlope = 1.5f;

        /// <summary>The authored total power cells for a full clear of 1-based <paramref name="areaIndex"/>
        /// (MV-375) — <c>intercept + slope × (area - 1)</c>, a straight line by construction so it can
        /// never compound the way a flat per-kill rate did once multiplied by
        /// <see cref="MaxWorlds.Enemies.DifficultyEngine.TargetBudget"/>'s exponential population growth.
        /// <see cref="MaxWorlds.Pickups.PickupDirector"/> divides this by the area's actual solved
        /// large-kill count to get each kill's real drop, so the run's cell curve holds to exactly this
        /// authored line no matter how population tuning (MV-284/MV-365) changes underneath it.</summary>
        public static float CellsForArea(int areaIndex) =>
            Mathf.Max(0f, CellsAreaIntercept + CellsAreaSlope * Mathf.Max(0, areaIndex - 1));
    }
}
