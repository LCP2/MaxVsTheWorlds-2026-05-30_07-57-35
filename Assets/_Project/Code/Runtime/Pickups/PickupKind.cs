namespace MaxWorlds.Pickups
{
    /// <summary>
    /// What a dropped collectible is (YT-131). A <see cref="PickupKind.PowerCell"/> banks into the HUD
    /// counter (a future currency, no gameplay use yet); a <see cref="PickupKind.Part"/> raises the
    /// flashing upgrade icon that drives the upgrade screen (YT-132). The specific part identities and
    /// their effects are YT-133 — here a part is generic.
    /// </summary>
    public enum PickupKind
    {
        PowerCell,
        Part,
    }
}
