using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-654 (Lee, 2 Sep 2026 device screenshot): SUPPORT &gt; SLOTS (<c>u_slt</c>) read <c>2/2</c> — its
    /// own node level over its own level cap — while Lee actually had 3 deployable sentinels. Both
    /// numbers were correct for what they measured; neither was the number the player acts on
    /// (<see cref="AbilityTuning.SentinelDeploymentSlots"/>, MV-623: level 0 already grants 1 sentinel).
    /// <c>u_slt</c> is the one node on the board whose level number isn't the number that matters, so its
    /// pill now derives from <see cref="AbilityTuning.SentinelDeploymentSlots"/> in both the owned and
    /// draftable branches, while every other node keeps the generic <c>level/maxLevel</c> read.
    ///
    /// One test, per CC_AUTONOMY's "at most one new test per ticket" rule. Tier 2 (resolved value):
    /// reads the built <see cref="WeaponsScreen.NodePillText"/> component's resolved <c>.text</c> after
    /// real <see cref="RigState"/>/<see cref="CellSpend"/> transitions, never an authored constant.
    ///
    /// Each level is checked on a FRESH <see cref="WeaponsScreen"/>, state seeded before
    /// <see cref="WeaponsScreen.Open"/>: an EditMode-constructed <see cref="GameObject"/> never runs
    /// Unity's <c>OnEnable</c> (no <c>[ExecuteAlways]</c>, and EditMode tests never tick a frame), so
    /// <see cref="WeaponsScreen"/>'s live <see cref="RigState.Changed"/> subscription — confirmed by
    /// instrumentation to never fire in this harness — never repaints an already-open screen. Every
    /// other EditMode test that reads a built pill (e.g. <c>MV620RigCostTagLegibilityTests</c>) follows
    /// the same shape: mutate state, THEN open.
    ///
    /// Proven to fail on the pre-fix commit 63d654a (main HEAD before this ticket): at <c>u_slt</c>
    /// level 0 (draftable) the assertion failed with <c>Expected: "1/3" But was: "40"</c> — the pre-fix
    /// draftable branch drew the unlock cost, not deployable sentinels. Full output quoted in the
    /// MV-654 fix comment.
    /// </summary>
    public sealed class MV654SlotsPillDeployableSentinelsTests
    {
        [TearDown]
        public void TearDown()
        {
            PickupWallet.Reset();
            RigState.Reset();
            WeaponSystemState.Reset();
            Time.timeScale = 1f;
        }

        private static Text OpenAndReadSlotsPill(out GameObject go)
        {
            go = new GameObject("WeaponsScreen");
            var screen = go.AddComponent<WeaponsScreen>();
            screen.Open();
            return screen.NodePillText("u_slt");
        }

        [Test]
        public void SlotsPillReadsDeployableSentinels_AtEveryLevel_WhileOtherNodesKeepLevelOverMax()
        {
            // ---------------------------------------------------------------- AC1 (level 0, draftable): 1/3
            // Real cells-path setup — the exact chain RigState.IsCellUnlockable("u_slt") requires
            // before it becomes draftable, same as MV623SentinelEconomyTests' SeedRigUpToSlotsUnlockable.
            RigState.UnlockCategory(RigBoard.Category("u_sen"));
            PickupWallet.SetPowerCells(CellSpend.UnlockCostFor("u_sen"));
            Assert.That(CellSpend.TryUnlockNode("u_sen"), Is.True, "setup: u_sen must unlock via cells");
            PickupWallet.SetPowerCells(CellSpend.UnlockCostFor("u_hp"));
            Assert.That(CellSpend.TryUnlockNode("u_hp"), Is.True, "setup: u_hp must unlock via cells");
            PickupWallet.SetPowerCells(CellSpend.UpgradeCostFor("u_hp", RigState.Level("u_hp")));
            Assert.That(CellSpend.TryUpgradeNode("u_hp"), Is.True, "setup: u_hp must reach level 2 via cells");
            Assert.That(RigState.IsCellUnlockable("u_slt"), Is.True, "setup: u_slt must now be cell-unlockable (draftable)");
            Assert.That(RigState.IsOwned("u_slt"), Is.False, "setup: u_slt must still be unowned at level 0");

            var pill0 = OpenAndReadSlotsPill(out var go0);
            try
            {
                Assert.That(pill0, Is.Not.Null, "u_slt built no pill-text component");
                Assert.That(pill0.text, Is.EqualTo("1/3"),
                    "level 0 (draftable): pill must read deployable sentinels (1/3), not the unlock cost or 0/2");
            }
            finally { Object.DestroyImmediate(go0); }

            // ---------------------------------------------------------------- AC1 (level 1, owned): 2/3
            PickupWallet.SetPowerCells(CellSpend.UnlockCostFor("u_slt"));
            Assert.That(CellSpend.TryUnlockNode("u_slt"), Is.True, "u_slt must unlock via cells");
            Assert.That(RigState.Level("u_slt"), Is.EqualTo(1));

            var pill1 = OpenAndReadSlotsPill(out var go1);
            try
            {
                Assert.That(pill1, Is.Not.Null, "u_slt built no pill-text component");
                Assert.That(pill1.text, Is.EqualTo("2/3"),
                    "level 1 (owned): pill must read deployable sentinels (2/3), not the node level (1/2)");
            }
            finally { Object.DestroyImmediate(go1); }

            // ---------------------------------------------------------------- AC1 (level 2, owned/maxed): 3/3
            PickupWallet.SetPowerCells(CellSpend.UpgradeCostFor("u_slt", RigState.Level("u_slt")));
            Assert.That(CellSpend.TryUpgradeNode("u_slt"), Is.True, "u_slt must upgrade to its ceiling via cells");
            Assert.That(RigState.Level("u_slt"), Is.EqualTo(2));

            var pill2 = OpenAndReadSlotsPill(out var go2);
            try
            {
                Assert.That(pill2, Is.Not.Null, "u_slt built no pill-text component");
                Assert.That(pill2.text, Is.EqualTo("3/3"),
                    "level 2 (owned, maxed): pill must read deployable sentinels (3/3), not the node level (2/2)");

                // ------------------------------------------------------------ AC4: every other node unchanged
                // p_dmg is owned from run start (RigBoard.StartLevel) and must keep the generic level/maxLevel read.
                Assert.That(RigState.IsOwned("p_dmg"), Is.True, "fixture: p_dmg is owned at run start");
                var screen2 = go2.GetComponent<WeaponsScreen>();
                var dmgPill = screen2.NodePillText("p_dmg");
                Assert.That(dmgPill, Is.Not.Null, "p_dmg built no pill-text component");
                Assert.That(dmgPill.text, Is.EqualTo($"{RigState.Level("p_dmg")}/{RigBoard.MaxLevel("p_dmg")}"),
                    "p_dmg's pill must stay the generic level/maxLevel read, untouched by the u_slt special-case");
            }
            finally { Object.DestroyImmediate(go2); }
        }
    }
}
