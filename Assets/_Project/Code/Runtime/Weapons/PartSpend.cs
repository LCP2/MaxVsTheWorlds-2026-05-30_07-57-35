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
    }
}
