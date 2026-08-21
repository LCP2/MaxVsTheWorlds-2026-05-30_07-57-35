using NUnit.Framework;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-469: pre-ticket, <c>WeaponsScreen.RefreshAbilityNode</c>'s interactable guard was
    /// <c>RigState.CanSpendPart(id) &amp;&amp; banked > 0</c> — a node was untappable however many cells
    /// were banked, since neither the owned-node cell-upgrade path nor the unowned-node cell-unlock path
    /// were ever consulted. MV-515 retired MV-492's two-currency unlock gate — a node is spendable off
    /// cells alone again, on both the upgrade and the unlock path.
    /// </summary>
    public sealed class WeaponsScreenAbilityNodeSpendableTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            PickupWallet.Reset();
            RigState.Reset();
        }

        [Test]
        public void SpendableReflectsEveryLegalSpendAndNothingElse()
        {
            // Case 1 — owned, below max, cells cover the upgrade cost: the cell-upgrade path MV-458
            // shipped but never wired to the board.
            Assert.That(RigState.IsOwned("p_dmg"), Is.True, "p_dmg is owned from run start");
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_dmg", CellSpend.UpgradeCostFor(RigState.Level("p_dmg"))), Is.True,
                "an owned, below-max node with enough cells must be spendable");

            // Case 3 — the non-interactable case: p_rng is reached but not yet CELL-unlockable (its
            // parent p_dmg is only level 1, and IsCellUnlockable needs >= 2) and it owns nothing to
            // level up either. Plenty of cells banked must not matter.
            Assert.That(RigState.IsCellUnlockable("p_rng"), Is.False, "p_dmg is only level 1, not yet 2");
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_rng", CellSpend.UnlockCostCells), Is.False,
                "a node that is neither owned-and-leveled-up-able nor cell-unlockable must stay non-interactable");

            RigState.RaiseLevel("p_dmg"); // model-layer raise, no currency involved — p_dmg to level 2

            // Case 2 — unowned, now CELL-unlockable, cells cover the unlock cost: MV-515 dropped the
            // MV-492 banked-Supercell requirement, so cells alone are enough again.
            Assert.That(RigState.IsCellUnlockable("p_rng"), Is.True, "p_dmg is now level 2");
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_rng", CellSpend.UnlockCostCells), Is.True,
                "an unowned, cell-unlockable node with enough cells must be spendable — MV-515: cells alone");

            // One cell short of the unlock cost must still refuse.
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_rng", CellSpend.UnlockCostCells - 1), Is.False,
                "one cell short of the unlock cost must not be spendable");
        }
    }
}
