namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Which weapon currently occupies Max's SECONDARY slot (MV-694) — World 1's Water Balloon throw,
    /// or World 2's auto-firing Shoulder Rack. Only one is ever live: MV-689's morph flips this, and
    /// <see cref="PlayerAbilities.TryThrowWaterBalloon"/> is a no-op unless it reads
    /// <see cref="WaterBalloon"/> here, the same way <see cref="ShoulderRack"/> (the component) only
    /// fires while this reads <see cref="ShoulderRack"/>.
    /// </summary>
    public enum SecondaryKind
    {
        WaterBalloon,
        ShoulderRack,
    }
}
