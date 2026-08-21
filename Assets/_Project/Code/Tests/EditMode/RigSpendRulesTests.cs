using NUnit.Framework;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-492: unlocking a new RIG node (its 0-&gt;1 grant) now needs TWO currencies — 1 banked part
    /// AND <see cref="CellSpend.UnlockCostCells"/> cells, never either currency alone. Upgrading an
    /// already-owned node stays cells-only, untouched by whatever is banked in parts. Testing policy
    /// (MV-465): one new test, proven to fail on a named base commit — this one fails to even compile
    /// against 1b5c5892686445ec623abbfe7288329880c6830b (main HEAD before this ticket), since neither
    /// <see cref="RigState.RaiseLevel"/> nor <see cref="CellSpend.UnlockCostParts"/> exist there
    /// (<see cref="CellSpend.TryUnlockNode"/> only ever checked/spent cells).
    /// </summary>
    public sealed class RigSpendRulesTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            PickupWallet.Reset();
            WeaponSystemState.Reset();
        }

        [Test]
        public void UnlockingANodeNeedsOnePartAndUnlockCostCells_UpgradingStaysCellsOnly()
        {
            // p_dmg starts owned at level 1 — raise it to level 2 so its child p_rng becomes cell-unlockable.
            RigState.RaiseLevel("p_dmg");
            Assert.That(RigState.IsCellUnlockable("p_rng"), Is.True, "p_dmg is now level 2");

            // 0 parts, UnlockCostCells cells: must fail and consume NOTHING.
            PickupWallet.SetPowerCells(CellSpend.UnlockCostCells);
            Assert.That(CellSpend.TryUnlockNode("p_rng"), Is.False, "no banked part must refuse the unlock");
            Assert.That(RigState.Level("p_rng"), Is.EqualTo(0));
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(CellSpend.UnlockCostCells), "a rejected unlock must not spend cells");
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(0));

            // 1 part, one cell short: must fail and consume NOTHING.
            PickupWallet.AddPart();
            PickupWallet.TrySpendPowerCells(1);
            Assert.That(CellSpend.TryUnlockNode("p_rng"), Is.False, "one cell short of the unlock cost must refuse");
            Assert.That(RigState.Level("p_rng"), Is.EqualTo(0));
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1), "a rejected unlock must not spend the banked part");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(CellSpend.UnlockCostCells - 1));

            // 1 part, UnlockCostCells cells: must succeed and spend EXACTLY both.
            PickupWallet.SetPowerCells(CellSpend.UnlockCostCells);
            Assert.That(CellSpend.TryUnlockNode("p_rng"), Is.True);
            Assert.That(RigState.Level("p_rng"), Is.EqualTo(1));
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(0), "the unlock must cost exactly 1 part");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0), "the unlock must cost exactly UnlockCostCells cells");

            // Upgrading the now-owned node: cells only — any number of banked parts, PartsBanked untouched.
            // p_rng just unlocked to level 1, so its own upgrade (to level 2) costs UpgradeCostFor(1).
            PickupWallet.AddPart();
            PickupWallet.AddPart();
            int partsBeforeUpgrade = PickupWallet.PartsBanked;
            PickupWallet.SetPowerCells(CellSpend.UpgradeCostFor(RigState.Level("p_rng")));
            Assert.That(CellSpend.TryUpgradeNode("p_rng"), Is.True);
            Assert.That(RigState.Level("p_rng"), Is.EqualTo(2));
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(partsBeforeUpgrade), "an upgrade must never touch banked parts");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0), "the upgrade must cost exactly UpgradeCostFor(1) cells");
        }
    }
}
