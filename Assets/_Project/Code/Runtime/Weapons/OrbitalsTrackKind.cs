namespace MaxWorlds.Weapons
{
    /// <summary>
    /// World 5's Orbitals secondary (MV-1236) — its own SECONDARY-family RIG subtree (<c>s_orb</c>
    /// root, <c>s_rad</c>/<c>s_odm</c>/<c>s_spd</c> all direct children of it), replacing the Shoulder
    /// Rack's <c>s_rkt</c>/<c>s_spl</c>/<c>s_sal</c>/<c>s_rld</c>/<c>s_clu</c> subtree on
    /// <c>rig_board.ship.json</c> now that the Mothership row's <c>SecondaryWeapon</c> reads
    /// <see cref="SecondaryKind.Orbitals"/>. Unlike Shoulder Rack, every node here starts owned at
    /// level 1 (the ticket's own "Always on once owned; nothing to press" — there is no 0-&gt;1 unlock
    /// gate the way <c>s_rkt</c> gated the rack's firing).
    /// </summary>
    public enum OrbitalsTrackKind
    {
        /// <summary>ORBS (<c>s_orb</c>): how many orbs circle Max — this subtree's own root cap.</summary>
        OrbCount,

        /// <summary>REACH (<c>s_rad</c>): the orbit radius, metres.</summary>
        Reach,

        /// <summary>POWER (<c>s_odm</c>): damage dealt per touch.</summary>
        Power,

        /// <summary>SPIN (<c>s_spd</c>): orbit speed, degrees/sec.</summary>
        Spin,
    }
}
