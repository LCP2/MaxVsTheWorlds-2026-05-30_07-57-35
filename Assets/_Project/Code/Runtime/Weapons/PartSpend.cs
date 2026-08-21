using MaxWorlds.Pickups;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Forges a FORGE fusion by spending banked cells (MV-426; MV-515 converted the cost from parts to
    /// cells — a Supercell is worth exactly <see cref="PickupWallet.SupercellCellValue"/> cells, so a
    /// fusion's cost is simply expressed in the one currency that remains). MV-515 also retired this
    /// class's other per-track and per-ability spend methods — dead code with no runtime caller once
    /// THE RIG board replaced the legacy per-track/ability part spend.
    /// </summary>
    public static class PartSpend
    {
        /// <summary>Forge a FORGE fusion (MV-426, cost converted to cells by MV-515). Fails cleanly
        /// (nothing spent) below the fusion's own cost, if it's already forged, or if either parent
        /// category isn't lit yet — same "check the sink can accept it BEFORE touching the bank" order
        /// every other spend in this codebase follows.</summary>
        public static bool TrySpendOnFusion(string fusionId)
        {
            if (!RigBoard.TryGetFusion(fusionId, out var def)) return false;
            if (PickupWallet.PowerCells < def.CellCost) return false;
            if (!RigFusionState.TryForge(fusionId)) return false;
            PickupWallet.TrySpendPowerCells(def.CellCost);
            return true;
        }
    }
}
