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

        /// <summary>True if a single PART would raise at least one owned node right now, or
        /// <paramref name="partsBanked"/> parts would forge at least one eligible fusion.</summary>
        public static bool AnyPartActionAffordable(int partsBanked)
        {
            if (partsBanked <= 0) return false;

            foreach (string id in RigBoard.AllIds)
                if (RigState.CanSpendPart(id)) return true;

            foreach (var fusion in RigBoard.Fusions)
                if (!RigFusionState.IsForged(fusion.Id) && RigFusionState.IsEligible(fusion.Id) && partsBanked >= fusion.PartCost)
                    return true;

            return false;
        }
    }
}
