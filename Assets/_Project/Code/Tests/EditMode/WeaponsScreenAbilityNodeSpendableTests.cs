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
    /// were ever consulted. MV-492: the unlock path additionally needs a banked part (cells alone are no
    /// longer enough), and a part alone never makes an owned node spendable (that's cells-only now, see
    /// <see cref="CellSpend.TryUpgradeNode"/>) — cases 2 and 3 below were updated for that model change.
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
            // Case 1 — owned, below max, cells cover the upgrade cost, any number of parts banked
            // (irrelevant to an upgrade): the cell-upgrade path MV-458 shipped but never wired to the board.
            Assert.That(RigState.IsOwned("p_dmg"), Is.True, "p_dmg is owned from run start");
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_dmg", CellSpend.UpgradeCostFor(RigState.Level("p_dmg")), 0), Is.True,
                "an owned, below-max node with enough cells must be spendable");

            // Case 4 — the non-interactable case: p_rng is reached but not yet CELL-unlockable (its
            // parent p_dmg is only level 1, and IsCellUnlockable needs >= 2) and it owns nothing to
            // level up either. Plenty of both currencies banked must not matter.
            Assert.That(RigState.IsCellUnlockable("p_rng"), Is.False, "p_dmg is only level 1, not yet 2");
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_rng", CellSpend.UnlockCostCells, 1), Is.False,
                "a node that is neither owned-and-leveled-up-able nor cell-unlockable must stay non-interactable");

            RigState.RaiseLevel("p_dmg"); // model-layer raise, no currency involved — p_dmg to level 2

            // Case 2 — unowned, now CELL-unlockable, cells cover the unlock cost, but ZERO parts banked:
            // MV-492 requires both currencies together, so this must now refuse.
            Assert.That(RigState.IsCellUnlockable("p_rng"), Is.True, "p_dmg is now level 2");
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_rng", CellSpend.UnlockCostCells, 0), Is.False,
                "MV-492: cells alone must no longer unlock a node — a part is required too");

            // With the part banked as well, the same unlock becomes spendable.
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_rng", CellSpend.UnlockCostCells, CellSpend.UnlockCostParts), Is.True,
                "an unowned, cell-unlockable node with both enough cells AND a banked part must be spendable");

            // Case 3 — MV-492: a part alone (zero cells) never buys a level outright anymore, owned or not.
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_dmg", 0, 1), Is.False,
                "a part can never substitute for cells on an owned node's upgrade");
        }
    }
}
