namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The abilities a shed can grant (v0.5 recut spec §4/§6, WV-230; Power Efficiency retired by
    /// MV-290 — abilities are gated on cooldown only now, so there is no cell drain left to reduce).
    /// Dash was removed entirely by MV-359 — Force Field (MV-361) takes over its "escape danger"
    /// role, and it is not restored by anything below. Water Balloon was removed from this pool by
    /// MV-370 (made a primary add-on, owned from run start) and restored by MV-380 after Lee's
    /// playtest found an un-acquired-feeling ability usable from the very first second — its own
    /// three upgrade tracks (<see cref="WaterBalloonTrackKind"/>) stay owned-from-run-start and
    /// unchanged; only the base throw's acquisition gate came back. Auto-fire (MV-373) is its own
    /// entry, acquirable only once <see cref="WaterBalloon"/> itself is owned (see
    /// <see cref="WeaponSystemState.Unacquired"/>'s prerequisite filter) — none of what's below is
    /// owned at run start — each is acquired by destroying a shed (WV-229) and hidden from the
    /// weapons screen until then (no locked teasers). Declaration order is the shed drop-pool's
    /// fixed order.
    /// </summary>
    public enum AbilityKind
    {
        /// <summary>Passive move-speed boost. Levels 1-4.</summary>
        Speed,

        /// <summary>Blink. Levels 1-4: an aimed blink at every level (MV-292), distance grows per level.</summary>
        Teleport,

        /// <summary>Passive — shortens every OTHER active ability's cooldown. Levels 1-5.</summary>
        WeaponCooldown,

        /// <summary>Unlocks the Water Balloon throw itself (MV-380, restoring MV-231's "each ability
        /// only works once acquired" rule that MV-370 silently dropped). A boolean unlock, not a
        /// leveled magnitude — cap 1 — since the actual throw/splash/repeat-fire numbers are owned by
        /// <see cref="WaterBalloonTrackKind"/>'s three tracks, unaffected by this gate.</summary>
        WaterBalloon,

        /// <summary>Unlocks auto-aimed Water Balloon throws (MV-373's targeting logic). A prerequisite
        /// chain: never offered by a shed until <see cref="WaterBalloon"/> is already owned (MV-380).
        /// Also a boolean unlock, cap 1 — once owned, <see cref="WeaponSystemState.WaterBalloonAutoFireEnabled"/>
        /// is the player-facing on/off toggle on top of it.</summary>
        WaterBalloonAutoFire,
    }
}
