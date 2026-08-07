namespace MaxWorlds.Pickups
{
    /// <summary>
    /// Authored magnitudes for the drop economy (WV-226). MV-290 retired power cells as fuel/currency
    /// (abilities are cooldown-gated only, the primary never depletes) — cells still drop and bank as
    /// a vestigial, display-only counter (their original pre-WV-227 role), so
    /// <see cref="DefaultCellsPerLargeKill"/> stays live even though nothing spends a cell any more.
    /// </summary>
    public static class CellEconomyTuning
    {
        /// <summary>Power cells a large-robot kill drops, guaranteed (WV-226, v0.5 recut spec §5
        /// <c>cellsPerLargeKill</c>) — small robots drop nothing at all.</summary>
        public const float DefaultCellsPerLargeKill = 1f;

        /// <summary>Large-robot kills between upgrade-part drops (WV-226, v0.5 recut spec §5
        /// <c>partsPerLargeKills</c>) — the fourth large kill, and every fourth one after, also drops
        /// a part on top of that kill's cell.</summary>
        public const float DefaultPartsPerLargeKills = 4f;
    }
}
