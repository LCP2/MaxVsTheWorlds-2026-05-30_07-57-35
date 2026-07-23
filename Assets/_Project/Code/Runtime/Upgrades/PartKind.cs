namespace MaxWorlds.Upgrades
{
    /// <summary>
    /// The level-1 hose upgrade parts (YT-133, extended YT-164). Each drops exactly once across the
    /// level (the unique drop table), and the one you pick up is the one you install
    /// (dropped-part-decides).
    /// </summary>
    public enum PartKind
    {
        /// <summary>Narrows the beam — tighter, concentrated — same length.</summary>
        BeamNozzle,

        /// <summary>Narrows AND lengthens the beam — reach + focus.</summary>
        PowerNozzle,

        /// <summary>Lengthens the beam further still — the reach keeps climbing.</summary>
        RangeExtender,

        /// <summary>Widens the (by now narrow) beam back out, keeping the extended reach — long AND
        /// wide, the top of the hose tree.</summary>
        WideBore,

        /// <summary>Backpack: more water capacity, and the mount the Hydro device clips into.</summary>
        AugmentationHarness,

        /// <summary>Max moves faster.</summary>
        AccelerationEngine,

        /// <summary>Detaches the hose and self-supplies water from the air — untethers Max from the taps.</summary>
        Hydro,
    }
}
