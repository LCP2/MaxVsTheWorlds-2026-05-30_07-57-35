using NUnit.Framework;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-458: power cells become THE RIG board's primary currency — a node's 0-&gt;1 unlock now costs
    /// <see cref="CellSpend.UnlockCostCells"/> cells and requires its parent at level &gt;= 2
    /// (<see cref="RigState.IsCellUnlockable"/>, tightened from the level &gt;= 1 gate
    /// <see cref="RigState.IsReached"/> still uses for the unrelated, production-dead ability-level
    /// Morphing Module draft pool). Testing policy (MV-465): one new test, proven to fail on a named
    /// base commit — this one fails to even compile against 6efe823 (main HEAD before this ticket),
    /// since neither <see cref="CellSpend"/> nor <see cref="RigState.IsCellUnlockable"/> exist there.
    /// </summary>
    public sealed class CellSpendTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            PickupWallet.Reset();
            WeaponSystemState.Reset();
        }

        [Test]
        public void UnlockingANodeCostsTwentyCellsAndNeedsItsParentAtLevelTwo()
        {
            // p_dmg owns the run-start ability at level 1 (RigBoard.StartLevel) — enough to satisfy the
            // OLD level >= 1 gate but not MV-458's tightened level >= 2 requirement.
            PickupWallet.SetPowerCells(20);

            Assert.That(CellSpend.TryUnlockNode("p_rng"), Is.False,
                "p_dmg is only level 1 — the tightened parent >= 2 gate must reject this");
            Assert.That(RigState.Level("p_rng"), Is.EqualTo(0));
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(20), "a rejected unlock must not spend cells");

            RigState.TrySpendPart("p_dmg"); // model-layer raise, no currency involved — p_dmg to level 2

            Assert.That(CellSpend.TryUnlockNode("p_rng"), Is.True,
                "p_dmg is now level 2 — the unlock must succeed");
            Assert.That(RigState.Level("p_rng"), Is.EqualTo(1));
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0), "the unlock must cost exactly 20 cells");
        }
    }
}
