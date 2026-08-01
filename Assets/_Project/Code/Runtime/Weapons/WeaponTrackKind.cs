namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The RCDA primary's four upgrade tracks (v0.5 recut spec §6, WV-230). All four are owned from
    /// run start at Level 1 — unlike <see cref="AbilityKind"/>, there is no "not owned" state here.
    /// </summary>
    public enum WeaponTrackKind
    {
        /// <summary>Water-tank size. Levels 1-4.</summary>
        Capacity,

        /// <summary>How fast water is used. Levels 1-4.</summary>
        WeaponEfficiency,

        /// <summary>How far the stream reaches. Levels 1-6.</summary>
        Range,

        /// <summary>How wide the stream's cone is. Levels 1-4.</summary>
        Spread,
    }
}
