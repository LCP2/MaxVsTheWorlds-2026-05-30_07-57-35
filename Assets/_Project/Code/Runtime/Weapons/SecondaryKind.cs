namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Which weapon currently occupies Max's SECONDARY slot (MV-694) — World 1's Water Balloon throw,
    /// World 2/3/City's auto-firing Shoulder Rack, or World 5's always-on Orbitals (MV-1236). Only one
    /// is ever live: MV-689's morph flips this, and <see cref="PlayerAbilities.TryThrowWaterBalloon"/>
    /// is a no-op unless it reads <see cref="WaterBalloon"/> here, the same way
    /// <see cref="ShoulderRack"/> (the component) only fires while this reads
    /// <see cref="ShoulderRack"/>, and <see cref="Orbitals"/> (the component) only orbits while this
    /// reads <see cref="Orbitals"/>.
    /// </summary>
    public enum SecondaryKind
    {
        WaterBalloon,
        ShoulderRack,

        /// <summary>MV-1236: World 5's gold orbs — appended, not inserted, same "never renumber an
        /// existing member" rule <see cref="MaxWorlds.Enemies.RobotEnemy.State"/> documents.</summary>
        Orbitals,
    }
}
