namespace MaxWorlds.Weapons
{
    /// <summary>
    /// World 5's LIGHT MISSILE primary's own PRIMARY-family tracks that have no legacy
    /// <see cref="WeaponTrackKind"/> equivalent (MV-1210) — <c>p_cnt</c>/<c>p_trk</c> exist only in
    /// <c>rig_board.ship.json</c>; POWER (<c>p_dmg</c>) is shared with every other world's primary via
    /// <see cref="WeaponTrackKind.Damage"/> instead of living here, same shape as
    /// <see cref="RicochetTrackKind"/>. <c>p_rof</c>/<c>p_cap</c> are ALSO shared by name with other
    /// worlds' own RATE/CAPACITY tracks, but resolve LIGHT MISSILE's own curve on this board — kept as
    /// their own members here rather than reused, since no two of these ever share a formula.
    /// </summary>
    public enum LightMissileTrackKind
    {
        /// <summary>COUNT (<c>p_cnt</c>): balls per volley. The board's own level IS the ball count
        /// directly (owned from level 1) — no separate formula.</summary>
        Count,

        /// <summary>TRACKING (<c>p_trk</c>): a ball's base turn rate, before the growth it picks up the
        /// longer it stays airborne — 170 deg/s unowned at level 0, rising to 350 deg/s over 4 levels.</summary>
        Tracking,

        /// <summary>RATE (<c>p_rof</c>): seconds between volleys, 1.0 -&gt; 0.6 over 5 levels, owned
        /// from level 1.</summary>
        Rate,

        /// <summary>CAPACITY (<c>p_cap</c>): the tank's own max right now — 140 at level 0, rising to
        /// 280 at this board's own level-5 cap, same +25%/level formula as every other primary's own
        /// <c>p_cap</c> (MV-1204).</summary>
        Capacity,
    }
}
