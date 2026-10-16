namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The City's RICOCHET primary's own PRIMARY-family tracks that have no legacy <see cref="WeaponTrackKind"/>
    /// equivalent (MV-1195) — <c>p_ric</c>/<c>p_aim</c> exist only in <c>rig_board.city.json</c>; POWER
    /// (<c>p_dmg</c>) is shared with every other world's primary via <see cref="WeaponTrackKind.Damage"/>
    /// instead of living here. <c>p_rof</c> is ALSO shared by name with World 2's LPPE RATE
    /// (<see cref="LppeTrackKind.Rate"/>), but resolves a completely different curve on this board (owned
    /// from level 1, discs/second rather than LPPE's unowned-from-0 fire interval) — kept as its own
    /// member here rather than reused, since the two never share a formula.
    /// </summary>
    public enum RicochetTrackKind
    {
        /// <summary>RICOCHETS (<c>p_ric</c>): bounces per disc. The board's own level IS the bounce
        /// count directly (owned from level 2 — <c>rig_board.city.json</c>'s <c>startLevel</c>) — no
        /// separate formula, unlike every other track here.</summary>
        Ricochets,

        /// <summary>AIMING (<c>p_aim</c>): the disc turns toward the nearest robot ahead once owned —
        /// 0 (unowned, dead straight) to 200 deg/s over 4 levels, with a growing lookahead distance.</summary>
        Aiming,

        /// <summary>RATE (<c>p_rof</c>): discs per second, 3.0 -&gt; 6.0 over 5 levels, owned from level 1.</summary>
        Rate,

        /// <summary>MV-1204 CAPACITY (<c>p_cap</c>): the tank's own max right now — 140 at level 0,
        /// rising to 280 at this board's own level-5 cap, same +25%/level formula as World 2's own
        /// <c>p_cap</c> (<see cref="LppeTrackKind.Capacity"/>) -- a different board, so no formula is
        /// shared, only the id's shape.</summary>
        Capacity,
    }
}
