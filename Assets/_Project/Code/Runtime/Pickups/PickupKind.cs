namespace MaxWorlds.Pickups
{
    /// <summary>
    /// What a dropped collectible is (YT-131). A <see cref="PickupKind.PowerCell"/> banks into the HUD's
    /// power-cell reserve; a <see cref="PickupKind.Supercell"/> (MV-515, renamed from "Part") banks a
    /// Supercell — a 10-cell top-up, cashed in explicitly via THE RIG's top-bar tray. A
    /// <see cref="PickupKind.Device"/> is a shed's drop (WV-229): walking over it grants the
    /// <see cref="Pickup.Ability"/> it carries outright.
    /// </summary>
    public enum PickupKind
    {
        PowerCell,
        Supercell,
        Device,
    }
}
