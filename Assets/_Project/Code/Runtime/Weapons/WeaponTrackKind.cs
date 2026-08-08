namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The RCDA primary's upgrade tracks (v0.5 recut spec §6, WV-230; Capacity/Weapon Efficiency
    /// retired by MV-290; Damage added by MV-291 to fix the curve having no third axis at all;
    /// MV-299 reinstates a drain-rate track — NOT the old tank-SIZE "Capacity" track MV-290 retired,
    /// this is drain-rate, the old "Efficiency" concept renamed). All four are owned from run start
    /// at Level 1 — unlike <see cref="AbilityKind"/>, there is no "not owned" state here.
    /// </summary>
    public enum WeaponTrackKind
    {
        /// <summary>How far the stream reaches. Levels 1-6.</summary>
        Range,

        /// <summary>How wide the stream's cone is. Levels 1-6 (MV-291: unified with Range/Damage so
        /// every track offers the same 5 upgrade steps).</summary>
        Spread,

        /// <summary>How hard each tick hits. Levels 1-6 (MV-291).</summary>
        Damage,

        /// <summary>How fast the water tank drains under continuous fire. Levels 1-6 (MV-299,
        /// reinstating the tank MV-290 cut): higher levels slow the drain, so Max can fire longer
        /// before the tank runs dry. Does NOT change the tank's SIZE — see
        /// <see cref="WeaponCatalog.EffectiveDrainPerSecond"/>.</summary>
        DepletionRate,
    }
}
