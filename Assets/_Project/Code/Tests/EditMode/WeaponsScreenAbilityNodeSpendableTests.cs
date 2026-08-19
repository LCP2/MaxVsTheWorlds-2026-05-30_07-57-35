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
    /// were ever consulted. Testing policy (MV-465): one new test, proven to fail on a named base commit
    /// — this one fails to even compile against 675522e (main HEAD before this ticket), since
    /// <see cref="WeaponsScreen.IsAbilityNodeSpendable"/> doesn't exist there.
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
            // Case 1 — owned, below max, cells cover the upgrade cost, no parts banked: the cell-upgrade
            // path MV-458 shipped but never wired to the board.
            Assert.That(RigState.IsOwned("p_dmg"), Is.True, "p_dmg is owned from run start");
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_dmg", CellSpend.UpgradeCostCells, 0), Is.True,
                "an owned, below-max node with enough cells must be spendable");

            // Case 4 — the non-interactable case: p_rng is reached but not yet CELL-unlockable (its
            // parent p_dmg is only level 1, and IsCellUnlockable needs >= 2) and it owns nothing to
            // level up with a part either. Plenty of both currencies banked must not matter.
            Assert.That(RigState.IsCellUnlockable("p_rng"), Is.False, "p_dmg is only level 1, not yet 2");
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_rng", CellSpend.UnlockCostCells, 1), Is.False,
                "a node that is neither owned-and-leveled-up-able nor cell-unlockable must stay non-interactable");

            RigState.TrySpendPart("p_dmg"); // model-layer raise, no currency involved — p_dmg to level 2

            // Case 2 — unowned, now CELL-unlockable, cells cover the unlock cost, no parts banked: the
            // unlock path the ticket's SYMPTOM (20 cells, 0 parts, nothing tappable) was entirely missing.
            Assert.That(RigState.IsCellUnlockable("p_rng"), Is.True, "p_dmg is now level 2");
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_rng", CellSpend.UnlockCostCells, 0), Is.True,
                "an unowned, cell-unlockable node with enough cells must be spendable");

            // Case 3 — the existing part-spend path, unchanged: owned, below max, a part banked, zero cells.
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_dmg", 0, 1), Is.True,
                "the pre-existing part-wildcard spend must still work with zero cells banked");
        }
    }
}
