using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1165 (the one new test): Lee, 11 Oct 2026 — "World 2, the cost of all upgrades is too high.
    /// Just notch it down by 10 or 15%." Chosen: 13%, via <see cref="WorldDefinition.UpgradeCostScale"/>
    /// (0.87 on the Stormdrain's row). Asserts the resolved cell prices <see cref="CellSpend.UnlockCostFor"/>
    /// and <see cref="CellSpend.UpgradeCostFor(string,int)"/> actually return for World 2, and that World 1
    /// and World 3 are untouched.
    ///
    /// Fails on base commit 7db54db (before this ticket): <c>WorldDefinition</c> has no
    /// <c>UpgradeCostScale</c> field, so every World 2 price below is still the un-discounted 1x price
    /// (e.g. <c>UnlockCostFor("e_cel")</c> returns 10, not 9).
    /// </summary>
    public sealed class MV1165World2RigPriceCutTests
    {
        [SetUp]
        public void SetUp() => RigBoard.ResetForTests();

        [TearDown]
        public void TearDown() => RigBoard.ResetForTests();

        [Test]
        public void World2PricesAreCutThirteenPercentWhileWorld1AndWorld3AreUnchanged()
        {
            // ------------------------------------------------------------ World 2 (Stormdrain): 13% off.
            RigBoard.UseWorld(1);

            Assert.AreEqual(9, CellSpend.UnlockCostFor("e_cel"), "World 2 ENERGY unlock must be 9 (10 * 0.87)");
            Assert.AreEqual(4, CellSpend.UpgradeCostFor("e_cel", 0), "World 2 ENERGY upgrade level 0 must be 4 (5 * 0.87)");
            Assert.AreEqual(9, CellSpend.UpgradeCostFor("e_cel", 1), "World 2 ENERGY upgrade level 1 must be 9 (10 * 0.87)");
            Assert.AreEqual(13, CellSpend.UpgradeCostFor("e_cel", 2), "World 2 ENERGY upgrade level 2 must be 13 (15 * 0.87)");
            Assert.AreEqual(17, CellSpend.UpgradeCostFor("e_cel", 3), "World 2 ENERGY upgrade level 3 must be 17 (20 * 0.87)");

            Assert.AreEqual(22, CellSpend.UnlockCostFor("p_dmg"), "World 2 PRIMARY unlock must be 22 (10 * 2.5 * 0.87)");
            Assert.AreEqual(11, CellSpend.UpgradeCostFor("p_dmg", 0), "World 2 PRIMARY upgrade level 0 must be 11 (5 * 2.5 * 0.87)");
            Assert.AreEqual(22, CellSpend.UpgradeCostFor("p_dmg", 1), "World 2 PRIMARY upgrade level 1 must be 22 (10 * 2.5 * 0.87)");
            Assert.AreEqual(33, CellSpend.UpgradeCostFor("p_dmg", 2), "World 2 PRIMARY upgrade level 2 must be 33 (15 * 2.5 * 0.87)");
            Assert.AreEqual(44, CellSpend.UpgradeCostFor("p_dmg", 3), "World 2 PRIMARY upgrade level 3 must be 44 (20 * 2.5 * 0.87)");

            Assert.AreEqual(35, CellSpend.UnlockCostFor("u_slt"), "World 2 Slots unlock must be 35 (40 * 0.87)");
            Assert.AreEqual(35, CellSpend.UpgradeCostFor("u_slt", 0), "World 2 Slots upgrade must be 35 (40 * 0.87), same flat price as unlock");

            // ------------------------------------------------------------ World 1 (Backyard): unchanged.
            RigBoard.UseWorld(0);

            Assert.AreEqual(10, CellSpend.UnlockCostFor("e_cel"), "World 1 ENERGY unlock must stay 10");
            Assert.AreEqual(5, CellSpend.UpgradeCostFor("e_cel", 0), "World 1 ENERGY upgrade level 0 must stay 5");
            Assert.AreEqual(10, CellSpend.UnlockCostFor("p_dmg"), "World 1 PRIMARY unlock must stay 10 (1x multiplier)");
            Assert.AreEqual(5, CellSpend.UpgradeCostFor("p_dmg", 0), "World 1 PRIMARY upgrade level 0 must stay 5 (1x multiplier)");
            Assert.AreEqual(40, CellSpend.UnlockCostFor("u_slt"), "World 1 Slots unlock must stay 40");
            Assert.AreEqual(40, CellSpend.UpgradeCostFor("u_slt", 0), "World 1 Slots upgrade must stay 40");

            // ------------------------------------------------------------ World 3 (Reef): unchanged.
            RigBoard.UseWorld(2);

            Assert.AreEqual(10, CellSpend.UnlockCostFor("e_cel"), "World 3 ENERGY unlock must stay 10");
            Assert.AreEqual(5, CellSpend.UpgradeCostFor("e_cel", 0), "World 3 ENERGY upgrade level 0 must stay 5");
            Assert.AreEqual(25, CellSpend.UnlockCostFor("p_dmg"), "World 3 PRIMARY unlock must stay 25 (10 * 2.5)");
            Assert.AreEqual(25, CellSpend.UpgradeCostFor("p_dmg", 1), "World 3 PRIMARY upgrade level 1 must stay 25 (10 * 2.5)");
            Assert.AreEqual(50, CellSpend.UpgradeCostFor("p_dmg", 3), "World 3 PRIMARY upgrade level 3 must stay 50 (20 * 2.5)");
            Assert.AreEqual(40, CellSpend.UnlockCostFor("u_slt"), "World 3 Slots unlock must stay 40");
            Assert.AreEqual(40, CellSpend.UpgradeCostFor("u_slt", 0), "World 3 Slots upgrade must stay 40");
        }
    }
}
