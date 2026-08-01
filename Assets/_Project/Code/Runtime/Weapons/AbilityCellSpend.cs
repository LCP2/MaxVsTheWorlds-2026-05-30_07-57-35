using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Pickups;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Where the ability backbone's two remaining cell-drain settings (spec §5/§9,
    /// <c>secondaryCellsPerUse</c>/<c>specialAbilityCellsPerUse</c>) actually get spent (WV-231) — one
    /// place so Water Balloon, Dash and Teleport all apply the Power Efficiency reduction (WV-227) the
    /// same way <see cref="MaxWorlds.Combat.WaterBlaster"/> already does for the primary's per-minute
    /// drain, rather than three near-identical copies of the same rounding.
    /// </summary>
    public static class AbilityCellSpend
    {
        private static float Efficiency => CellEconomyTuning.EfficiencyMultiplier(
            WeaponSystemState.AbilityLevel(AbilityKind.PowerEfficiency),
            DevTuning.Or(DevTuning.PowerEfficiencyReductionPerLevel, CellEconomyTuning.DefaultPowerEfficiencyReductionPerLevel));

        /// <summary>Cells a Water Balloon throw costs right now, Power Efficiency applied and rounded
        /// to a whole cell — the wallet only ever holds whole cells.</summary>
        public static int SecondaryCost => Mathf.Max(0, Mathf.RoundToInt(
            DevTuning.Or(DevTuning.SecondaryCellsPerUse, CellEconomyTuning.DefaultSecondaryCellsPerUse) * Efficiency));

        /// <summary>Cells a Dash/Teleport activation costs right now, Power Efficiency applied.</summary>
        public static int SpecialCost => Mathf.Max(0, Mathf.RoundToInt(
            DevTuning.Or(DevTuning.SpecialAbilityCellsPerUse, CellEconomyTuning.DefaultSpecialAbilityCellsPerUse) * Efficiency));

        /// <summary>Spend a Water Balloon throw's cost atomically. False (nothing spent) if the
        /// reserve can't cover it.</summary>
        public static bool TrySpendSecondary() => PickupWallet.TrySpendPowerCells(SecondaryCost);

        /// <summary>Spend a Dash/Teleport activation's cost atomically.</summary>
        public static bool TrySpendSpecial() => PickupWallet.TrySpendPowerCells(SpecialCost);
    }
}
