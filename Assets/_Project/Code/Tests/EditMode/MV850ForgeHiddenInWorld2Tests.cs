using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-850 (the one new test): the FORGE fusions are broken against World 2's LPPE/Shoulder Rack kit
    /// (DELUGE only touches World 1 weapons, "SLOT B/U" has no caller, OVERCHARGE never spends a cell,
    /// SKIRMISH lost half its effect to MV-579) — Lee's decision (2026-09-19) is to hide the row and
    /// mask forged-in-World-1 effects until a redesign, rather than ship broken fusions into World 2.
    ///
    /// Forges <c>f_bgd</c> while World 1's board is active (the only world it can be forged in), then
    /// switches to World 2's board and asserts <see cref="RigFusionState.IsForged"/> reads false there
    /// and the rebuilt <see cref="WeaponsScreen"/> has no FORGE node for it — then switches back to
    /// World 1 and asserts both come back, proving the underlying forge was masked, not deleted.
    ///
    /// Fails on base commit 10fce02 (MV-13 HEAD before this ticket): quoted in the fix comment.
    /// </summary>
    public sealed class MV850ForgeHiddenInWorld2Tests
    {
        private GameObject _go;
        private WeaponsScreen _screen;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();   // also puts RigBoard back on World 1
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();
            RigBoardLayout.UseWorld(0);
            Time.timeScale = 1f;
            _go = new GameObject("WeaponsScreen");
            _screen = _go.AddComponent<WeaponsScreen>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            RigBoard.UseWorld(0);
            RigBoard.ResetForTests();
            RigBoardLayout.UseWorld(0);
            RigBoardLayout.ResetForTests();
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
        }

        [Test]
        public void ForgedFusionIsMaskedAndHasNoBoardNodeInWorld2_ButComesBackInWorld1()
        {
            // ---------------------------------------------------------------- fixture: forge f_bgd on World 1
            RigState.UnlockCategory("ENERGY");
            RigState.AcquireCap("e_ff");
            RigState.UnlockCategory("MOVE");
            RigState.AcquireCap("m_spd");
            RigState.AcquireCap("e_cel"); // capacity 30 — room for the fusion's own 30-cell cost
            PickupWallet.SetPowerCells(30);
            Assert.That(PartSpend.TrySpendOnFusion("f_bgd"), Is.True, "fixture: f_bgd must forge cleanly on World 1");
            Assert.That(RigFusionState.IsForged("f_bgd"), Is.True, "fixture: f_bgd must read forged on World 1 right after forging it");

            _screen.Open();
            Assert.That(_screen.FusionNodeLabel("f_bgd"), Is.Not.Null, "fixture: World 1's board must still build a FORGE node for f_bgd");

            // ---------------------------------------------------------------- AC: World 2 hides the row and masks the forge
            // RebuildBoard syncs RigBoardLayout to RigBoard.ActiveWorldIndex itself (MV-753) — no
            // separate RigBoardLayout.UseWorld call needed here.
            RigBoard.UseWorld(1);
            _screen.RebuildBoard();

            Assert.That(RigFusionState.IsForged("f_bgd"), Is.False,
                "a fusion forged in World 1 must have no effect in World 2 — IsForged must read false there");
            Assert.That(_screen.FusionNodeLabel("f_bgd"), Is.Null,
                "World 2's board must build no FORGE node at all for f_bgd");
            Assert.That(_screen.FusionNodeSub("f_bgd"), Is.Null,
                "World 2's board must build no FORGE sub-label either");

            // ---------------------------------------------------------------- back to World 1: the forge was masked, not deleted
            RigBoard.UseWorld(0);
            _screen.RebuildBoard();

            Assert.That(RigFusionState.IsForged("f_bgd"), Is.True,
                "returning to World 1 must reveal the same forge again — it was never actually cleared");
            Assert.That(_screen.FusionNodeLabel("f_bgd"), Is.Not.Null,
                "World 1's board must build the FORGE node for f_bgd again");
        }
    }
}
