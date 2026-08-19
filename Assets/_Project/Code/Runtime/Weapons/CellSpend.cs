using MaxWorlds.Pickups;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Spends banked POWER CELLS on THE RIG board (MV-458) — cells become the primary progression
    /// currency. Parts alone could never fund the tree (76 part-bought levels + 12 fusion parts = 88,
    /// against ~17 parts/run); cells are the abundant drop (~380/run) with almost nowhere to go, so
    /// they now pay for both a node's 0-&gt;1 unlock and its further levels. Parts survive as the rare
    /// accelerant via the existing <see cref="PartSpend.TrySpendOnRigNode"/> path, unchanged by this
    /// class. Same "check the sink can accept it BEFORE touching the bank" order every other spend in
    /// this codebase follows.
    /// </summary>
    public static class CellSpend
    {
        /// <summary>Cost to unlock a new node — its 0-&gt;1 grant — with cells.</summary>
        public const int UnlockCostCells = 20;

        /// <summary>Cost to raise an already-owned node by one level with cells.</summary>
        public const int UpgradeCostCells = 10;

        /// <summary>Unlock <paramref name="id"/> for <see cref="UnlockCostCells"/> cells. Requires
        /// <see cref="RigState.IsCellUnlockable"/> (its category unlocked and, for a non-root node, its
        /// parent at level &gt;= 2). No special case for <c>e_cel</c> or any other node — every RIG node
        /// unlocks the same way now. Grants through <see cref="WeaponSystemState.AcquireById"/> (MV-435
        /// AC5's own sanctioned entry point) rather than calling the RIG model layer's own grant
        /// primitive directly — that would silently skip <see cref="WeaponSystemState.Changed"/> and
        /// leave any HUD control gated on it stuck stale.</summary>
        public static bool TryUnlockNode(string id)
        {
            if (PickupWallet.PowerCells < UnlockCostCells) return false;
            if (!RigState.IsCellUnlockable(id)) return false;
            if (!WeaponSystemState.AcquireById(id)) return false;
            PickupWallet.TrySpendPowerCells(UnlockCostCells);
            return true;
        }

        /// <summary>Raise an already-owned <paramref name="id"/> by one level for <see cref="UpgradeCostCells"/>
        /// cells — the same "unowned/locked items can't be upgraded" gate every other spend in this
        /// codebase enforces (<see cref="RigState.TrySpendPart"/>'s own owned/below-cap check; naming
        /// aside, that method is the currency-agnostic "raise one level" primitive both
        /// <see cref="PartSpend"/> and this class spend against).</summary>
        public static bool TryUpgradeNode(string id)
        {
            if (PickupWallet.PowerCells < UpgradeCostCells) return false;
            if (!RigState.TrySpendPart(id)) return false;
            PickupWallet.TrySpendPowerCells(UpgradeCostCells);
            return true;
        }
    }
}
