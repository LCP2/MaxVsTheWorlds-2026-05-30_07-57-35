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
        public const float DefaultCellsPerLargeKill = 1f;

        /// <summary>Large-robot kills between upgrade-part drops (WV-226, v0.5 recut spec §5
        /// <c>partsPerLargeKills</c>) — the fourth large kill, and every fourth one after, also drops
        /// a part on top of that kill's cell. Same fallback-only role as
        /// <see cref="DefaultCellsPerLargeKill"/> once a per-area budget is available.</summary>
        public const float DefaultPartsPerLargeKills = 4f;

        /// <summary>Total power cells authored for area 1's full large-kill haul (MV-375) — the
        /// intercept of <see cref="CellsForArea"/>.</summary>
        public const float CellsAreaIntercept = 4f;

        /// <summary>Total power cells the per-area budget rises by for every area past the first
        /// (MV-375) — a straight line, deliberately NOT the compounding rate
        /// <see cref="MaxWorlds.Enemies.DifficultyEngine.TargetBudget"/> grows the robot population by.
        /// Cells are the less-scarce of the two currencies, so this rises a little faster than
        /// <see cref="PartsAreaSlope"/>.</summary>
        public const float CellsAreaSlope = 2f;

        /// <summary>Total upgrade parts authored for area 1's full large-kill haul (MV-375) — the
        /// intercept of <see cref="PartsForArea"/>. Parts are the scarce, meaningful currency (per the
        /// ticket), so this starts lower than cells.</summary>
        public const float PartsAreaIntercept = 1f;

        /// <summary>Total upgrade parts the per-area budget rises by for every area past the first
        /// (MV-375) — deliberately shallow, since the ticket calls for the late-arena part flood to be
        /// "cut hardest": a gentler slope than cells keeps the run's most meaningful currency scarce
        /// all the way through the final areas instead of spiking there.</summary>
        public const float PartsAreaSlope = 0.5f;

        /// <summary>The authored total power cells for a full clear of 1-based <paramref name="areaIndex"/>
        /// (MV-375) — <c>intercept + slope × (area - 1)</c>, a straight line by construction so it can
        /// never compound the way a flat per-kill rate did once multiplied by
        /// <see cref="MaxWorlds.Enemies.DifficultyEngine.TargetBudget"/>'s exponential population growth.
        /// <see cref="MaxWorlds.Pickups.PickupDirector"/> divides this by the area's actual solved
        /// large-kill count to get each kill's real drop, so the run's cell curve holds to exactly this
        /// authored line no matter how population tuning (MV-284/MV-365) changes underneath it.</summary>
        public static float CellsForArea(int areaIndex) =>
            Mathf.Max(0f, CellsAreaIntercept + CellsAreaSlope * Mathf.Max(0, areaIndex - 1));

        /// <summary>The authored total upgrade parts for a full clear of 1-based <paramref name="areaIndex"/>
        /// (MV-375) — same straight-line construction as <see cref="CellsForArea"/>, with a shallower
        /// slope so the late-arena flood the ticket describes is cut at the source instead of capped.</summary>
        public static float PartsForArea(int areaIndex) =>
            Mathf.Max(0f, PartsAreaIntercept + PartsAreaSlope * Mathf.Max(0, areaIndex - 1));
    }
}
