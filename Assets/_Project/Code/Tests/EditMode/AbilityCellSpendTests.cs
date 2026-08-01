using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// WV-231: Water Balloon/Dash/Teleport spend cells through <see cref="AbilityCellSpend"/>, which
    /// applies the Power Efficiency reduction (WV-227's economy) the same way
    /// <see cref="MaxWorlds.Combat.WaterBlaster"/> already does for the primary. A spend is atomic —
    /// it either affords its whole cost or touches the wallet not at all.
    /// </summary>
    public sealed class AbilityCellSpendTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            PickupWallet.Reset();
        }

        [Test]
        public void SecondaryCostMatchesTheAuthoredDefaultWithNoEfficiency()
        {
            Assert.That(AbilityCellSpend.SecondaryCost,
                Is.EqualTo((int)CellEconomyTuning.DefaultSecondaryCellsPerUse));
        }

        [Test]
        public void SpecialCostMatchesTheAuthoredDefaultWithNoEfficiency()
        {
            Assert.That(AbilityCellSpend.SpecialCost,
                Is.EqualTo((int)CellEconomyTuning.DefaultSpecialAbilityCellsPerUse));
        }

        [Test]
        public void PowerEfficiencyReducesBothCosts()
        {
            int baseSecondary = AbilityCellSpend.SecondaryCost;
            int baseSpecial = AbilityCellSpend.SpecialCost;

            WeaponSystemState.Acquire(AbilityKind.PowerEfficiency);
            for (int i = 1; i < WeaponCatalog.MaxLevel(AbilityKind.PowerEfficiency); i++)
                WeaponSystemState.LevelUpAbility(AbilityKind.PowerEfficiency);

            Assert.That(AbilityCellSpend.SecondaryCost, Is.LessThan(baseSecondary),
                "a maxed Power Efficiency must reduce the Water Balloon cost");
            Assert.That(AbilityCellSpend.SpecialCost, Is.LessThan(baseSpecial),
                "a maxed Power Efficiency must reduce the Dash/Teleport cost");
        }

        [Test]
        public void TrySpendSecondaryDecrementsTheWalletByTheCost()
        {
            PickupWallet.AddPowerCell();
            PickupWallet.AddPowerCell();
            int before = PickupWallet.PowerCells;

            Assert.That(AbilityCellSpend.TrySpendSecondary(), Is.True);
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(before - AbilityCellSpend.SecondaryCost));
        }

        [Test]
        public void TrySpendSpecialFailsAndSpendsNothingWhenUnaffordable()
        {
            PickupWallet.Reset();   // 0 cells
            Assert.That(AbilityCellSpend.SpecialCost, Is.GreaterThan(0), "fixture assumes a real cost");

            Assert.That(AbilityCellSpend.TrySpendSpecial(), Is.False);
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0), "a failed spend must not touch the wallet");
        }
    }
}
