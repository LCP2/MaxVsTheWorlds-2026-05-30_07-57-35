namespace MaxWorlds.Pickups
{
    /// <summary>
    /// What a dropped collectible is (YT-131). A <see cref="PickupKind.PowerCell"/> banks into the HUD's
    /// power-cell reserve; a <see cref="PickupKind.Supercell"/> (MV-515, renamed from "Part") banks a
    /// Supercell — a 10-cell top-up, cashed in explicitly via THE RIG's top-bar tray. A
    /// <see cref="PickupKind.Device"/> is a shed's drop (WV-229): walking over it grants the
    /// <see cref="Pickup.Ability"/> it carries outright. <see cref="PickupKind.PowerCellSecondary"/>
    /// (MV-672) is the new, separate "Power Cells" currency — named distinctly from
    /// <see cref="PickupKind.PowerCell"/> on purpose: that member is the one that, post Issue 1's
    /// rename, displays to the player as "Parts", so reusing its name for the actual new Power Cells
    /// currency would collide with the wrong C# identity.
    /// </summary>
    public enum PickupKind
    {
        PowerCell,
        Supercell,
        Device,
        PowerCellSecondary,
    }
}
