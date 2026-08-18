using MaxWorlds.Pickups;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Spends one banked part on a chosen owned track or ability (WV-228) — the glue between
    /// <see cref="PickupWallet"/>'s fungible token count and <see cref="WeaponSystemState"/>'s
    /// level-up primitives. A part is only actually spent when the level-up succeeds: an unowned
    /// ability or a track/ability already at its cap ("unowned/locked items can't be upgraded", spec
    /// §5) leaves the bank untouched.
    /// </summary>
    public static class PartSpend
    {
        /// <summary>Spend one banked part to raise an RCDA track by a level. Every track is owned from
        /// run start, so the only way this fails is an empty bank or the track's own cap.</summary>
        public static bool TrySpendOnTrack(WeaponTrackKind kind)
        {
            if (PickupWallet.PartsBanked <= 0) return false;
            if (!WeaponSystemState.LevelUpTrack(kind)) return false;
            PickupWallet.TrySpendPart();
            return true;
        }

        /// <summary>Spend one banked part to raise an OWNED ability by a level. Fails without spending
        /// if the ability hasn't been acquired yet (WV-229) or is already at its cap.</summary>
        public static bool TrySpendOnAbility(AbilityKind kind)
        {
            if (PickupWallet.PartsBanked <= 0) return false;
            if (!WeaponSystemState.LevelUpAbility(kind)) return false;
            PickupWallet.TrySpendPart();
            return true;
        }

        /// <summary>Spend one banked part to raise a Water Balloon track by a level (MV-370). Every
        /// track is owned from run start, same as an RCDA track, so the only way this fails is an
        /// empty bank or the track's own cap.</summary>
        public static bool TrySpendOnWaterBalloonTrack(WaterBalloonTrackKind kind)
        {
            if (PickupWallet.PartsBanked <= 0) return false;
            if (!WeaponSystemState.LevelUpWaterBalloonTrack(kind)) return false;
            PickupWallet.TrySpendPart();
            return true;
        }

        /// <summary>Spend one banked part to raise Cell Storage (<c>e_cel</c>) by a level (MV-374,
        /// relocated onto THE RIG by MV-422). A general player-stat track rather than a weapon one, so
        /// it levels up directly on <see cref="PickupWallet"/> instead of through
        /// <see cref="WeaponSystemState"/>. <c>e_cel</c> is a <c>cap</c> — fails until it has been
        /// taken in a Morphing Module draft, same "unowned/locked items can't be upgraded" rule
        /// <see cref="TrySpendOnAbility"/> enforces — beyond that, the only way this fails is an empty
        /// bank or the level cap.</summary>
        public static bool TrySpendOnCellCapacity()
        {
            if (PickupWallet.PartsBanked <= 0) return false;
            if (!PickupWallet.LevelUpCellCapacity()) return false;
            PickupWallet.TrySpendPart();
            return true;
        }

        /// <summary>Spend one banked part directly on a THE RIG node id (MV-423 board), bypassing the
        /// legacy per-enum wrappers above — every one of them already resolves to
        /// <see cref="RigState.TrySpendPart"/> under the hood (MV-422), so a node the board draws by
        /// its own <c>rig_board.json</c> id (e.g. <c>e_mag</c>, which has no legacy enum at all) can
        /// spend the same way they do.</summary>
        public static bool TrySpendOnRigNode(string id)
        {
            if (PickupWallet.PartsBanked <= 0) return false;
            if (!RigState.TrySpendPart(id)) return false;
            PickupWallet.TrySpendPart();
            return true;
        }
    }
}
