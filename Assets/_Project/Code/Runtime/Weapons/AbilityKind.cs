namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The six abilities a shed can grant (v0.5 recut spec §4/§6, WV-230). None are owned at run
    /// start — each is acquired by destroying a shed (WV-229) and hidden from the weapons screen
    /// until then (no locked teasers). Declaration order is the shed drop-pool's fixed order.
    /// </summary>
    public enum AbilityKind
    {
        /// <summary>Joystick-aimed lob. Levels 1-3; level raises throw DISTANCE, not damage or rate.</summary>
        WaterBalloon,

        /// <summary>Passive move-speed boost. Levels 1-4.</summary>
        Speed,

        /// <summary>Directional dash. Single unlock, Level 1 only.</summary>
        Dash,

        /// <summary>Blink. Levels 1-2: L1 random, L2 aimed/movement-directed.</summary>
        Teleport,

        /// <summary>Passive — reduces the three cell-drain rates. Levels 1-5.</summary>
        PowerEfficiency,

        /// <summary>Passive — shortens every OTHER active ability's cooldown. Levels 1-5.</summary>
        WeaponCooldown,
    }
}
