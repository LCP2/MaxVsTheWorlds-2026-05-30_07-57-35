using System;
using NUnit.Framework;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-530: <see cref="RigState.IsCellUnlockable"/>'s parent gate was a bare
    /// <c>Level(parent) &gt;= 2</c>, but three roots &mdash; <c>s_bal</c> (BALLOON), <c>u_sen</c>
    /// (SENTINEL) and <c>s_aut</c> &mdash; cap at <c>maxLevel: 1</c> and can never reach 2, permanently
    /// stranding their six children (<c>s_spl</c>, <c>s_lob</c>, <c>s_rte</c> via <c>s_aut</c>,
    /// <c>u_dmg</c>, <c>u_rng</c>, <c>u_hp</c>) behind <see cref="CellSpend.TryUnlockNode"/> &mdash; the
    /// actual cells-spend path a player uses on THE RIG board (<see cref="RigState.AcquireCap"/>'s own
    /// gate, <see cref="RigState.IsReached"/>, is unaffected and production-dead for this purpose; see
    /// CellSpendTests' own doc comment). The fix widens the gate to
    /// <c>Level(parent) &gt;= Math.Min(2, parent's own MaxLevel)</c> &mdash; this is the test that would
    /// have caught the defect: sweeps every one of the 23 nodes with real cell spends and, for parents
    /// that CAN reach level 2, also proves MV-458's original depth-before-breadth intent survives (a
    /// child stays locked at parent level 1, unlocks only at level 2).
    /// </summary>
    public sealed class MV530RigParentCapGateTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            PickupWallet.Reset();
            WeaponSystemState.Reset();
        }

        /// <summary>Unlocks <paramref name="id"/> itself via a real <see cref="CellSpend.TryUnlockNode"/>
        /// spend &mdash; unlocking its own category first if it's a root, or recursively raising its
        /// whole ancestor chain to whatever <see cref="RigState.IsCellUnlockable"/> now requires
        /// otherwise. A no-op if already owned.</summary>
        private static void UnlockViaCells(string id)
        {
            if (RigState.IsOwned(id)) return;

            string parent = RigBoard.Parent(id);
            if (string.IsNullOrEmpty(parent))
                RigState.UnlockCategory(RigBoard.Category(id));
            else
                RaiseToLevel(parent, Math.Min(2, RigBoard.MaxLevel(parent)));

            PickupWallet.SetPowerCells(CellSpend.UnlockCostCells);
            Assert.That(CellSpend.TryUnlockNode(id), Is.True, $"setup: '{id}' must unlock via cells");
        }

        /// <summary>Raises <paramref name="id"/> up to <paramref name="target"/> via real
        /// <see cref="CellSpend.TryUpgradeNode"/> spends, unlocking it first via
        /// <see cref="UnlockViaCells"/> if it isn't owned yet.</summary>
        private static void RaiseToLevel(string id, int target)
        {
            UnlockViaCells(id);
            while (RigState.Level(id) < target)
            {
                PickupWallet.SetPowerCells(CellSpend.UpgradeCostFor(RigState.Level(id)));
                Assert.That(CellSpend.TryUpgradeNode(id), Is.True, $"setup: '{id}' must upgrade toward level {target}");
            }
        }

        [Test]
        public void EveryNodeUnlocksViaCellsAndMv458sParentGateStillHoldsWhereTheParentCanReachTwo()
        {
            foreach (string id in RigBoard.AllIds)
            {
                PickupWallet.Reset();
                WeaponSystemState.Reset();
                if (RigState.IsOwned(id)) continue; // p_dmg only — already owned at run start

                string parent = RigBoard.Parent(id);
                if (string.IsNullOrEmpty(parent))
                {
                    RigState.UnlockCategory(RigBoard.Category(id));
                }
                else
                {
                    RaiseToLevel(parent, 1);
                    int cap = RigBoard.MaxLevel(parent);

                    if (cap >= 2)
                    {
                        // MV-458 regression guard: a parent that CAN exceed 1 must still gate its
                        // child behind the FULL two levels, not one — must not have been loosened.
                        Assert.That(RigState.IsCellUnlockable(id), Is.False,
                            $"'{id}' must stay locked while its parent '{parent}' sits at level 1 (cap {cap})");
                        RaiseToLevel(parent, 2);
                    }
                }

                Assert.That(RigState.IsCellUnlockable(id), Is.True,
                    $"'{id}' must be cell-unlockable once its ancestor chain is satisfied");

                PickupWallet.SetPowerCells(CellSpend.UnlockCostCells);
                Assert.That(CellSpend.TryUnlockNode(id), Is.True,
                    $"'{id}' must be reachable by a real player action sequence from a fresh run");
            }
        }
    }
}
