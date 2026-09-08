using NUnit.Framework;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-689 (the one new test, per CC_AUTONOMY's testing policy): the Weapon Core morph. Starting
    /// from a World 1 board with some real investment (p_dmg raised past its run-start level, SECONDARY/
    /// ENERGY/MOVE all unlocked and spent into), collecting a Weapon Core and then THE RIG's next open
    /// must swap PRIMARY to the LPPE (p_dmg re-granted at L1 only, nothing else carried), swap SECONDARY
    /// to the mystery-locked Shoulder Rack (fully reset, <see cref="RigState.SecondaryLocked"/> true),
    /// leave ENERGY/MOVE untouched, and flip <see cref="WeaponSystemState.ActivePrimary"/> to the LPPE.
    ///
    /// MV-727 updated this test's SECONDARY assertions: the morph used to unlock SECONDARY outright
    /// (s_rkt immediately cell-buyable, no shed draft needed); it now leaves SECONDARY LOCKED too —
    /// only <c>MaxWorlds.Pickups.PickupDirector</c>'s Rack Module collection can open it.
    ///
    /// Note on the ticket's own "s_bal L2": <c>s_bal</c>'s own <c>maxLevel</c> is 1 (world-1's board,
    /// unrelated to this ticket) — L2 isn't reachable through any real spend, so this drafts s_bal to its
    /// actual cap (L1, fully owned) instead; the morph's discard behaviour this AC exists to prove is
    /// unaffected by which level SECONDARY held beforehand; what matters is that it is a real, non-zero
    /// investment.
    ///
    /// Fails on the MV-708 merge commit (6ea4315): <c>PendingMorphingModule.SetWeaponCore</c>,
    /// <c>WeaponSystemState.OpenWeaponCoreMorphIfPending</c>/<c>ApplyWeaponCoreMorph</c>,
    /// <c>RigState.SecondaryLocked</c> and <c>RigBoardLibrary</c> do not exist there — this does not
    /// compile against that commit.
    /// </summary>
    public sealed class MV689WeaponCoreMorphTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();   // also resets RigState and RigBoard back to World 1
            PendingMorphingModule.Reset();
        }

        [Test]
        public void CollectingAWeaponCoreAndOpeningTheRigMorphsPrimaryAndLocksSecondary_MV689()
        {
            // Arrange: a World 1 board with real investment across PRIMARY, SECONDARY, ENERGY and MOVE.
            RigState.RaiseLevel("p_dmg");
            RigState.RaiseLevel("p_dmg");   // p_dmg: run-start L1 -> L3

            RigState.UnlockCategory("SECONDARY");
            RigState.AcquireCap("s_bal");   // s_bal caps at L1 (world-1 board, unrelated to this ticket)

            RigState.UnlockCategory("ENERGY");
            RigState.AcquireCap("e_ff");    // e_ff: L1

            RigState.UnlockCategory("MOVE");
            RigState.AcquireCap("m_spd");
            RigState.RaiseLevel("m_spd");   // m_spd: L1 -> L2

            Assert.AreEqual(3, RigState.Level("p_dmg"));
            Assert.AreEqual(1, RigState.Level("s_bal"));
            Assert.AreEqual(1, RigState.Level("e_ff"));
            Assert.AreEqual(2, RigState.Level("m_spd"));
            Assert.AreEqual(WeaponCatalog.PrimaryKind.Rcda, WeaponSystemState.ActivePrimary);

            // Act: collect a Weapon Core, then THE RIG's next open (one Open()).
            PendingMorphingModule.SetWeaponCore();
            Assert.IsTrue(PendingMorphingModule.WeaponCorePending, "the collected core must bank, not resolve immediately");

            bool morphed = WeaponSystemState.OpenWeaponCoreMorphIfPending(worldIndex: 1);

            // Assert: the morph ran exactly once and resolved to World 2's board.
            Assert.IsTrue(morphed);
            Assert.IsFalse(PendingMorphingModule.WeaponCorePending, "the banked core must be consumed by the open");
            Assert.AreEqual(1, RigBoard.ActiveWorldIndex);

            // PRIMARY: p_dmg re-granted at L1 only — nothing else carried, nothing else owned.
            Assert.AreEqual(1, RigState.Level("p_dmg"), "p_dmg must reset to L1, not carry the RCDA's L3");
            Assert.AreEqual(0, RigState.Level("p_rng"));
            Assert.AreEqual(0, RigState.Level("p_rof"));
            Assert.AreEqual(0, RigState.Level("p_frk"));
            Assert.IsTrue(RigState.IsCategoryUnlocked("PRIMARY"));

            // SECONDARY: fully reset to the Shoulder Rack, and LOCKED (MV-727 reverses MV-694's
            // immediately-buyable shape) -- it only opens once the player finds World 2's Rack Module
            // pickup (MaxWorlds.Pickups.PickupDirector), not the instant the morph lands.
            Assert.AreEqual(0, RigState.Level("s_rkt"), "s_bal's old investment must be discarded, not carried onto s_rkt");
            Assert.IsTrue(RigState.SecondaryLocked, "SECONDARY must read as the mystery '?' immediately after the morph");
            Assert.IsFalse(RigState.IsCategoryUnlocked("SECONDARY"),
                "MV-727: SECONDARY must stay LOCKED after the morph -- s_rkt is no longer cell-buyable until the Rack Module is found");

            // ENERGY/MOVE: untouched by the morph.
            Assert.AreEqual(1, RigState.Level("e_ff"));
            Assert.AreEqual(2, RigState.Level("m_spd"));

            // The LPPE is now Max's active primary; the Shoulder Rack his active secondary.
            Assert.AreEqual(WeaponCatalog.PrimaryKind.Lppe, WeaponSystemState.ActivePrimary);
            Assert.AreEqual(SecondaryKind.ShoulderRack, WeaponSystemState.SecondaryKind);

            // MV-727: SECONDARY being locked means s_rkt can no longer be acquired via the ordinary
            // draft/cell path at all -- AcquireCap's IsReached check fails for a root node whose
            // category isn't unlocked. Only PickupDirector's Rack Module collection path (which unlocks
            // the category first) can ever grant it now.
            Assert.IsFalse(RigState.AcquireCap("s_rkt"),
                "s_rkt must not be acquirable while SECONDARY is still locked");
            Assert.IsTrue(RigState.SecondaryLocked, "SECONDARY must still read as the mystery '?' -- s_rkt was never granted");
        }
    }
}
