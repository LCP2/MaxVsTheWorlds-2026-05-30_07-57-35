namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The RCDA primary's upgrade tracks (v0.5 recut spec §6, WV-230; Capacity/Weapon Efficiency
    /// retired by MV-290 — the primary never depletes, so a tank size/drain-rate track has nothing
    /// left to upgrade). Both are owned from run start at Level 1 — unlike <see cref="AbilityKind"/>,
    /// there is no "not owned" state here.
    /// </summary>
    public enum WeaponTrackKind
    {
        /// <summary>How far the stream reaches. Levels 1-6.</summary>
        Range,

        /// <summary>How wide the stream's cone is. Levels 1-4.</summary>
        Spread,
    }
}
