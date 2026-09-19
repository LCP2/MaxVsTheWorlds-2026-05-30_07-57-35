namespace MaxWorlds.Weapons
{
    /// <summary>
    /// World 2's LPPE primary's own PRIMARY-family tracks that have no legacy <see cref="WeaponTrackKind"/>
    /// equivalent (MV-768) — <c>p_rof</c>/<c>p_frk</c> exist only in <c>rig_board.world2.json</c>, unlike
    /// <c>p_dmg</c>/<c>p_rng</c>, which are shared with World 1's RCDA via <see cref="WeaponTrackKind"/>.
    /// </summary>
    public enum LppeTrackKind
    {
        /// <summary>RATE (<c>p_rof</c>): fire interval 0.22s -&gt; 0.16s over 4 levels.</summary>
        Rate,

        /// <summary>FORK (<c>p_frk</c>): a killing pulse releases one further pulse at the next target.
        /// One level, no chaining.</summary>
        Fork,

        /// <summary>CAPACITY (<c>p_cap</c>, MV-846): enlarges the LPPE's own energy tank max, 140 -&gt;
        /// 315 over 5 levels. Distinct from World 1's <c>p_flw</c> (which cuts the RCDA's drain and has
        /// no LPPE consumer at all) — do not conflate the two, their levels mean different things.</summary>
        Capacity,
    }
}
