using NUnit.Framework;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-511: <see cref="CellSpend.UpgradeCostFor"/> replaces the flat 10-cell
    /// <c>UpgradeCostCells</c> constant with a per-node-level escalation (5, 10, 15, 20, 20, 20...,
    /// capped at level 4), while <see cref="CellSpend.UnlockCostCells"/> stays a flat 10 (down from 20)
    /// for every unlockable node — unlock breadth is already double-gated by part scarcity and the
    /// parent level &gt;= 2 requirement, so it does not also escalate. Also pins the actual spend
    /// behavior at a mid-escalation level (AC3: a level-3 node's own upgrade), not just the pure formula
    /// — <see cref="CellSpend.TryUpgradeNode"/> must charge exactly the node's OWN level's cost, not the
    /// level it is about to become. Testing policy (MV-465): one new test, proven to fail on a named base
    /// commit — this one fails to even COMPILE against ced20ed73274dc2f18e6d55d46b2f39f7e1d2093 (main
    /// HEAD before this ticket), since <see cref="CellSpend.UpgradeCostFor"/> does not exist there (that
    /// file only ever had the flat <c>UpgradeCostCells</c> constant).
    /// </summary>
    public sealed class RigCellCostTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            PickupWallet.Reset();
            RigState.Reset();
        }

        [Test]
        public void UpgradeCostEscalatesByLevelCappedAtFour_UnlockStaysFlatAtTen()
        {
            Assert.That(CellSpend.UpgradeCostFor(1), Is.EqualTo(5));
            Assert.That(CellSpend.UpgradeCostFor(2), Is.EqualTo(10));
            Assert.That(CellSpend.UpgradeCostFor(3), Is.EqualTo(15));
            Assert.That(CellSpend.UpgradeCostFor(4), Is.EqualTo(20));
            Assert.That(CellSpend.UpgradeCostFor(5), Is.EqualTo(20));
            Assert.That(CellSpend.UpgradeCostFor(6), Is.EqualTo(20));

            Assert.That(CellSpend.UnlockCostCells, Is.EqualTo(10),
                "unlock is flat — every unlockable node costs the same 10 cells, it does not escalate");

            // AC3: the same escalation actually charged by TryUpgradeNode, not just the pure formula.
            // p_dmg starts at level 1 (RigBoard.StartLevel) — raise it (model-layer, no currency) to
            // level 3, where its own next upgrade must cost UpgradeCostFor(3) = 15.
            RigState.RaiseLevel("p_dmg");
            RigState.RaiseLevel("p_dmg");
            Assert.That(RigState.Level("p_dmg"), Is.EqualTo(3), "p_dmg raised to level 3");

            PickupWallet.SetPowerCells(14);
            Assert.That(CellSpend.TryUpgradeNode("p_dmg"), Is.False, "one cell short of a level-3 node's own cost (15) must refuse");
            Assert.That(RigState.Level("p_dmg"), Is.EqualTo(3));
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(14), "a rejected upgrade must not spend cells");

            PickupWallet.SetPowerCells(15);
            Assert.That(CellSpend.TryUpgradeNode("p_dmg"), Is.True);
            Assert.That(RigState.Level("p_dmg"), Is.EqualTo(4));
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0), "the upgrade must cost exactly UpgradeCostFor(3) = 15 cells");
        }
    }
}
