namespace MaxWorlds.Weapons
{
    /// <summary>
    /// MV-471: "is spending this currency possible right now, anywhere on the board" — one predicate
    /// per currency, shared by the WEAPONS button's alert flash (this ticket), THE RIG board's per-node
    /// styling and its interactability gate (both follow-on tickets ask the exact same question), so it
    /// is written once rather than three times. Pure — takes the banked amount as a parameter so it can
    /// be pinned by an EditMode test without a live <see cref="MaxWorlds.Pickups.PickupWallet"/>, same
    /// idiom <see cref="CellSpend.IsCellActionAffordable"/> and <see cref="RigState.CanSpendPart"/>
    /// already follow.
    /// </summary>
    public static class RigActions
    {
        /// <summary>True if <paramref name="cellsBanked"/> CELLS would unlock or upgrade at least one
        /// node right now. <c>e_cel</c> (the cell-capacity chip) needs no special case — it is a RIG
        /// node like any other, so it is already covered by the loop.</summary>
        public static bool AnyCellActionAffordable(int cellsBanked)
        {
            foreach (string id in RigBoard.AllIds)
                if (CellSpend.IsCellActionAffordable(id, cellsBanked)) return true;
            return false;
        }

        /// <summary>MV-492: a part alone no longer raises anything — an unlock now needs
        /// <see cref="CellSpend.UnlockCostParts"/> part AND <see cref="CellSpend.UnlockCostCells"/>
        /// cells together, so "a part action is affordable" means an unlockable-and-unowned node exists
        /// AND both currencies are banked enough to unlock it, OR <paramref name="partsBanked"/> parts
        /// would forge at least one eligible fusion (fusions are still parts-only, unchanged).</summary>
        public static bool AnyPartActionAffordable(int partsBanked, int cellsBanked)
        {
            if (partsBanked >= CellSpend.UnlockCostParts && cellsBanked >= CellSpend.UnlockCostCells)
            {
                foreach (string id in RigBoard.AllIds)
                    if (RigState.IsCellUnlockable(id) && !RigState.IsOwned(id)) return true;
            }

            if (partsBanked <= 0) return false;

            foreach (var fusion in RigBoard.Fusions)
                if (!RigFusionState.IsForged(fusion.Id) && RigFusionState.IsEligible(fusion.Id) && partsBanked >= fusion.PartCost)
                    return true;

            return false;
        }
    }
}
