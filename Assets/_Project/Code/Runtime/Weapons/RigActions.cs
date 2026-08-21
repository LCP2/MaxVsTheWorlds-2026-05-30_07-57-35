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

        /// <summary>MV-515: is cashing a Supercell for <see cref="MaxWorlds.Pickups.PickupWallet.SupercellCellValue"/>
        /// cells actually possible right now — at least one banked AND room in the reserve for the full
        /// top-up. Pure, mirroring <see cref="AnyCellActionAffordable"/>'s "takes the banked amounts as
        /// parameters" idiom so it can be pinned by an EditMode test without a live
        /// <see cref="MaxWorlds.Pickups.PickupWallet"/>.</summary>
        public static bool AnySupercellActionAffordable(int supercellsBanked, int powerCells, int capacity) =>
            supercellsBanked > 0 && capacity - powerCells >= MaxWorlds.Pickups.PickupWallet.SupercellCellValue;
    }
}
