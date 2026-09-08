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
    /// currency would collide with the wrong C# identity. <see cref="PickupKind.WeaponCore"/> (MV-689)
    /// is the World 1 finale drop (MV-698 spawns it): walking over it banks
    /// <see cref="MaxWorlds.Weapons.PendingMorphingModule.SetWeaponCore"/>, the same "banks, doesn't
    /// force-open THE RIG" shape <see cref="Device"/> already uses. <see cref="PickupKind.RackModule"/>
    /// (MV-727) is World 2's Shoulder Rack pickup: the FIRST Replicator destroyed each run drops one in
    /// addition to its normal shed-equivalent drop; walking over it unlocks the SECONDARY category AND
    /// grants <c>s_rkt</c> at level 1 outright, for free — reversing MV-694's "buy it with cells the
    /// instant the Weapon Core morph lands" shape.
    /// </summary>
    public enum PickupKind
    {
        PowerCell,
        Supercell,
        Device,
        PowerCellSecondary,
        WeaponCore,
        RackModule,
    }
}
