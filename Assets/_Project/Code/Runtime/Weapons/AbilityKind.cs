namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The abilities a shed can grant (v0.5 recut spec §4/§6, WV-230; Power Efficiency retired by
    /// MV-290 — abilities are gated on cooldown only now, so there is no cell drain left to reduce).
    /// Dash was removed entirely by MV-359 — Force Field (MV-361) takes over its "escape danger"
    /// role, and it is not restored by anything below. Water Balloon was removed by MV-370: it's now
    /// a primary add-on (<see cref="WaterBalloonTrackKind"/>), owned from run start like the RCDA's
    /// own tracks, not a shed-drop find. None of what remains is owned at run start — each is
    /// acquired by destroying a shed (WV-229) and hidden from the weapons screen until then (no
    /// locked teasers). Declaration order is the shed drop-pool's fixed order.
    /// </summary>
    public enum AbilityKind
    {
        /// <summary>Passive move-speed boost. Levels 1-4.</summary>
        Speed,

        /// <summary>Blink. Levels 1-4: an aimed blink at every level (MV-292), distance grows per level.</summary>
        Teleport,

        /// <summary>Passive — shortens every OTHER active ability's cooldown. Levels 1-5.</summary>
        WeaponCooldown,
    }
}
