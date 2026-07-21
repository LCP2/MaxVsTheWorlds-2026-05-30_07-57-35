namespace MaxWorlds.Upgrades
{
    /// <summary>
    /// The five level-1 hose upgrade parts (YT-133). Each drops exactly once across the level (the
    /// unique drop table), and the one you pick up is the one you install (dropped-part-decides).
    /// </summary>
    public enum PartKind
    {
        /// <summary>Narrows the beam — tighter, concentrated — same length.</summary>
        BeamNozzle,

        /// <summary>Narrows AND lengthens the beam — reach + focus.</summary>
        PowerNozzle,

        /// <summary>Backpack: more water capacity, and the mount the Hydro device clips into.</summary>
        AugmentationHarness,

        /// <summary>Max moves faster.</summary>
        AccelerationEngine,

        /// <summary>Detaches the hose and self-supplies water from the air — untethers Max from the taps.</summary>
        Hydro,
    }
}
