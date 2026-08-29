using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-623 (DECISION, Lee 29 Aug 2026): Slots (<c>u_slt</c>) become an expensive late-game purchase —
    /// 40 cells per level, up to 3 sentinels — and deploying a sentinel costs 5 cells again, re-raising
    /// MV-579's 0-cost exception now that MV-604's cap-recall means a deploy can never be an
    /// unrecoverable loss. One test, per CC_AUTONOMY's "at most one new test per ticket" rule — this is
    /// that one, covering AC1-AC9 (AC10 is the full EditMode suite, run separately).
    ///
    /// Proven to fail on the pre-fix commit (old constants:
    /// <c>AbilityTuning.SentinelDeploymentSlots(level) = Mathf.Max(1, level)</c>,
    /// <c>AbilityTuning.DefaultSentinelCost = 0</c>, <c>rig_board.json</c>'s <c>u_slt.maxLevel = 4</c>,
    /// and <c>CellSpend</c> has no per-node override — every node including <c>u_slt</c> costs the flat
    /// <c>UnlockCostCells</c>/<c>UpgradeCostFor(level)</c>) — failure output quoted in the MV-623 fix
    /// comment.
    /// </summary>
    public sealed class MV623SentinelEconomyTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            PickupWallet.Reset();
            RigState.Reset();
            WeaponSystemState.Reset();
            Sentinel.DestroyAllActive();
        }

        /// <summary>Cells-path setup: unlocks SUPPORT, then <c>u_sen</c>, then raises <c>u_hp</c> (u_slt's
        /// own RIG parent) to level 2 via real <see cref="CellSpend"/> spends — the exact chain
        /// <see cref="RigState.IsCellUnlockable"/> requires before <c>u_slt</c> itself becomes
        /// cell-unlockable. Leaves <c>u_slt</c> unowned but purchasable, wallet untouched by the caller.</summary>
        private static void SeedRigUpToSlotsUnlockable()
        {
            RigState.UnlockCategory(RigBoard.Category("u_sen"));

            PickupWallet.SetPowerCells(CellSpend.UnlockCostFor("u_sen"));
            Assert.That(CellSpend.TryUnlockNode("u_sen"), Is.True, "setup: u_sen must unlock via cells");

            PickupWallet.SetPowerCells(CellSpend.UnlockCostFor("u_hp"));
            Assert.That(CellSpend.TryUnlockNode("u_hp"), Is.True, "setup: u_hp must unlock via cells");

            PickupWallet.SetPowerCells(CellSpend.UpgradeCostFor("u_hp", RigState.Level("u_hp")));
            Assert.That(CellSpend.TryUpgradeNode("u_hp"), Is.True, "setup: u_hp must reach level 2 via cells");

            Assert.That(RigState.IsCellUnlockable("u_slt"), Is.True, "setup: u_slt must now be cell-unlockable");
        }

        [Test]
        public void SentinelEconomyMatchesTheNewTargetsAcrossSlotsPriceCeilingAndDeployCost()
        {
            // ---------------------------------------------------------------- AC1: slots, no dead level, strictly increasing
            int slots0 = AbilityTuning.SentinelDeploymentSlots(0);
            int slots1 = AbilityTuning.SentinelDeploymentSlots(1);
            int slots2 = AbilityTuning.SentinelDeploymentSlots(2);
            Assert.That(slots0, Is.EqualTo(1));
            Assert.That(slots1, Is.EqualTo(2));
            Assert.That(slots2, Is.EqualTo(3));
            Assert.That(slots1, Is.GreaterThan(slots0), "level 1 must strictly beat level 0 — no dead level");
            Assert.That(slots2, Is.GreaterThan(slots1), "level 2 must strictly beat level 1");

            // ---------------------------------------------------------------- AC2: ceiling
            Assert.That(RigBoard.MaxLevel("u_slt"), Is.EqualTo(2));
            Assert.That(AbilityTuning.SentinelDeploymentSlots(RigBoard.MaxLevel("u_slt")), Is.EqualTo(3));

            // ---------------------------------------------------------------- AC3: slot price, both paths
            Assert.That(CellSpend.UnlockCostFor("u_slt"), Is.EqualTo(40));
            Assert.That(CellSpend.UpgradeCostFor("u_slt", 1), Is.EqualTo(40));

            // ---------------------------------------------------------------- AC4/AC5: display and spend agree, affordability boundary
            SeedRigUpToSlotsUnlockable();

            Assert.That(CellSpend.PotentialCellCost("u_slt"), Is.EqualTo(40), "unlock: displayed price must be 40");
            Assert.That(CellSpend.CurrentCellCost("u_slt"), Is.EqualTo(40), "unlock: actionable price must be 40");
            Assert.That(CellSpend.IsCellActionAffordable("u_slt", 39), Is.False, "unlock: 39 cells must not afford 40");
            Assert.That(CellSpend.IsCellActionAffordable("u_slt", 40), Is.True, "unlock: 40 cells must afford 40");

            PickupWallet.SetPowerCells(50);
            int walletBeforeUnlock = PickupWallet.PowerCells;
            Assert.That(CellSpend.TryUnlockNode("u_slt"), Is.True);
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(walletBeforeUnlock - 40),
                "the unlock must spend exactly 40 cells, not the global UnlockCostCells (10)");

            Assert.That(CellSpend.PotentialCellCost("u_slt"), Is.EqualTo(40), "upgrade: displayed price must also be 40");
            Assert.That(CellSpend.CurrentCellCost("u_slt"), Is.EqualTo(40), "upgrade: actionable price must also be 40");
            Assert.That(CellSpend.IsCellActionAffordable("u_slt", 39), Is.False, "upgrade: 39 cells must not afford 40");
            Assert.That(CellSpend.IsCellActionAffordable("u_slt", 40), Is.True, "upgrade: 40 cells must afford 40");

            PickupWallet.SetPowerCells(50);
            int walletBeforeUpgrade = PickupWallet.PowerCells;
            Assert.That(CellSpend.TryUpgradeNode("u_slt"), Is.True);
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(walletBeforeUpgrade - 40),
                "the upgrade must spend exactly 40 cells, not the global UpgradeCostFor(level)");
            Assert.That(RigState.Level("u_slt"), Is.EqualTo(2), "u_slt must now be at its new ceiling");

            // ---------------------------------------------------------------- AC6: no other node changed price
            foreach (string id in new[] { "u_sen", "e_cel", "p_dmg", "s_bal" })
            {
                Assert.That(CellSpend.UnlockCostFor(id), Is.EqualTo(10), $"'{id}' unlock price must stay the untouched flat 10");
                Assert.That(CellSpend.UpgradeCostFor(id, 1), Is.EqualTo(5), $"'{id}' upgrade price at level 1 must stay untouched");
                Assert.That(CellSpend.UpgradeCostFor(id, 2), Is.EqualTo(10), $"'{id}' upgrade price at level 2 must stay untouched");
                Assert.That(CellSpend.UpgradeCostFor(id, 3), Is.EqualTo(15), $"'{id}' upgrade price at level 3 must stay untouched");
                Assert.That(CellSpend.UpgradeCostFor(id, 4), Is.EqualTo(20), $"'{id}' upgrade price at level 4 must stay untouched");
                Assert.That(CellSpend.UpgradeCostFor(id, 5), Is.EqualTo(20), $"'{id}' upgrade price at level 5 must stay untouched (escalation cap)");
            }

            // ---------------------------------------------------------------- AC7: deploy cost
            Assert.That(AbilityTuning.DefaultSentinelCost, Is.EqualTo(5));
            Assert.That(AbilityTuning.SentinelCost(
                0, AbilityTuning.DefaultSentinelCost, AbilityTuning.DefaultSentinelCostReductionPerLevel), Is.EqualTo(5));

            // ---------------------------------------------------------------- AC8: deploy actually charges
            // Fresh RIG/wallet state: only u_sen owned (deploy needs acquisition, not slot spend).
            PickupWallet.Reset();
            RigState.Reset();
            WeaponSystemState.Reset();
            RigState.UnlockCategory(RigBoard.Category("u_sen"));
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            PickupWallet.SetPowerCells(5);

            var maxGo = new GameObject("Max");
            var abilities = maxGo.AddComponent<PlayerAbilities>();
            try
            {
                Assert.That(abilities.TryDeploySentinel(new Vector3(5f, 0f, 0f)), Is.True);
                Assert.That(PickupWallet.PowerCells, Is.EqualTo(0), "the deploy must cost exactly 5 cells");
                Assert.That(Sentinel.Active.Count, Is.EqualTo(1));

                PickupWallet.SetPowerCells(4);
                int countBeforeRefusal = Sentinel.Active.Count;
                Assert.That(abilities.TryDeploySentinel(new Vector3(20f, 0f, 0f)), Is.False,
                    "4 cells must not afford the 5-cell deploy");
                Assert.That(PickupWallet.PowerCells, Is.EqualTo(4), "a refused deploy must not spend cells");
                Assert.That(Sentinel.Active.Count, Is.EqualTo(countBeforeRefusal), "a refused deploy must not place a sentinel");

                // ---------------------------------------------------------------- AC9: cap-recall still works
                RigState.AcquireCap("u_hp");  // reaches u_slt (u_hp's own RIG child), free draft path
                RigState.AcquireCap("u_slt"); // level 1 -> 2 slots
                RigState.RaiseLevel("u_slt"); // level 2 -> 3 slots, the new ceiling (was 3 raises pre-MV-623, now 2)
                Assert.That(PlayerAbilities.SentinelDeploymentCap, Is.EqualTo(3));

                Sentinel.DestroyAllActive();
                PickupWallet.SetPowerCells(999);

                Assert.That(abilities.TryDeploySentinel(new Vector3(5f, 0f, 0f)), Is.True);
                Assert.That(abilities.TryDeploySentinel(new Vector3(20f, 0f, 0f)), Is.True);
                Assert.That(abilities.TryDeploySentinel(new Vector3(50f, 0f, 0f)), Is.True);
                Assert.That(Sentinel.Active.Count, Is.EqualTo(3), "precondition: cap reached exactly");

                Assert.That(abilities.TryDeploySentinel(new Vector3(1f, 0f, 0f)), Is.True,
                    "deployment must never be refused for lack of a slot — MV-604's cap-recall must survive");
                Assert.That(Sentinel.Active.Count, Is.EqualTo(3), "must stay at the cap, never grow past it");
            }
            finally
            {
                Sentinel.DestroyAllActive();
                Object.DestroyImmediate(maxGo);
            }
        }
    }
}
