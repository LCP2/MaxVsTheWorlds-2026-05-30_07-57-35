using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// THE RIG 5/5 (MV-426) — the FORGE's four fusions. Covers the AC this worker can prove without a
    /// PlayMode test: eligibility gated on both parent categories being lit (independent of banked
    /// cells, MV-423.png vs -noparts.png), the exact 30-cell forge cost (MV-515: converted from 3
    /// parts — a Supercell is worth exactly 10 cells) failing cleanly below it, a forged fusion
    /// occupying its named HUD slot in place of LOCKED, and no Morphing Module draft ever offering a
    /// fusion id. The four effects' actual gameplay behaviour (DELUGE's arc, BLINKGUARD's left-behind
    /// bubble, OVERCHARGE's fire-rate double, SKIRMISH's area-survival/teleport-snap) needs a live
    /// scene to observe end-to-end and is intentionally NOT covered here — see the fix comment on
    /// MV-426 for the PlayMode gap this project's autonomy contract asks to be noted rather than worked
    /// around with a forbidden PlayMode test.
    /// </summary>
    public sealed class RigFusionForgeTests
    {
        [SetUp]
        public void SetUp()
        {
            RigState.Reset();
            RigFusionState.ResetForTests();
            PickupWallet.Reset();   // MV-457: also calls RigState.Reset() — the category unlock below must come AFTER this
            // This suite is about fusion ELIGIBILITY (ownership-gated), not MV-457's shed/category-lock
            // gate (RigStateTests owns that) — force every category open so a root id this file drafts
            // directly stays reached, as it always was before MV-457.
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
        }

        [TearDown]
        public void TearDown()
        {
            RigState.Reset();
            RigFusionState.ResetForTests();
            PickupWallet.Reset();
        }

        // ---------------------------------------------------------------- schema sanity

        [Test]
        public void AllFourFusionsAreDefinedWithTheirAuthoredParentsSlotAndCost()
        {
            AssertFusion("f_del", "PRIMARY", "SECONDARY", "B", 30);
            AssertFusion("f_bgd", "ENERGY", "MOVE", "B", 30);
            AssertFusion("f_ovc", "ENERGY", "SUPPORT", "U", 30);
            AssertFusion("f_skr", "MOVE", "SUPPORT", "U", 30);
        }

        private static void AssertFusion(string id, string parentA, string parentB, string hudSlot, int cost)
        {
            Assert.That(RigBoard.TryGetFusion(id, out var def), Is.True, $"'{id}' must be a defined fusion");
            Assert.That(def.ParentA, Is.EqualTo(parentA));
            Assert.That(def.ParentB, Is.EqualTo(parentB));
            Assert.That(def.HudSlot, Is.EqualTo(hudSlot));
            Assert.That(def.CellCost, Is.EqualTo(cost));
        }

        // ---------------------------------------------------------------- AC1: both parents must be lit

        [Test]
        public void AFusionCannotBeForgedUntilBothParentCategoriesAreLit_AllFour()
        {
            // Run start: p_dmg (PRIMARY) is the only owned ability anywhere — no category but
            // PRIMARY is lit, so every fusion must read ineligible.
            AssertIneligibleThenEligibleOnceBothLit("f_del", "p_dmg", "s_bal");
            RigState.Reset();
            AssertIneligibleThenEligibleOnceBothLit("f_bgd", "e_ff", "m_spd");
            RigState.Reset();
            AssertIneligibleThenEligibleOnceBothLit("f_ovc", "e_ff", "u_sen");
            RigState.Reset();
            AssertIneligibleThenEligibleOnceBothLit("f_skr", "m_spd", "u_sen");
        }

        /// <summary><paramref name="rootA"/>/<paramref name="rootB"/> are root (parentless) abilities
        /// of the fusion's own two parent categories, per model.rules — <c>RigState.AcquireCap</c>
        /// grants them directly with no ancestor chain to walk first. Self-unlocks each root's own
        /// category before drafting it (MV-457): the caller (<see cref="AFusionCannotBeForgedUntilBothParentCategoriesAreLit_AllFour"/>)
        /// calls <see cref="RigState.Reset"/> between fusions, which re-locks everything but PRIMARY, so
        /// this can't rely on a one-time SetUp unlock.</summary>
        private static void AssertIneligibleThenEligibleOnceBothLit(string fusionId, string rootA, string rootB)
        {
            Assert.That(RigFusionState.IsEligible(fusionId), Is.False,
                $"'{fusionId}' must be ineligible with neither parent category owned beyond the run-start baseline");

            RigState.UnlockCategory(RigBoard.Category(rootA));
            RigState.AcquireCap(rootA);
            Assert.That(RigFusionState.IsEligible(fusionId), Is.False,
                $"'{fusionId}' must stay ineligible with only ONE parent category lit ({rootA})");

            RigState.UnlockCategory(RigBoard.Category(rootB));
            RigState.AcquireCap(rootB);
            Assert.That(RigFusionState.IsEligible(fusionId), Is.True,
                $"'{fusionId}' must become eligible once BOTH parent categories are lit ({rootA} + {rootB})");
        }

        [Test]
        public void EligibilityIsIndependentOfBankedCells_ReadyAtZeroBanked()
        {
            // MV-423.png vs -noparts.png: OVERCHARGE stays amber/ready with the cell count irrelevant.
            RigState.AcquireCap("e_ff");
            RigState.AcquireCap("u_sen");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0));
            Assert.That(RigFusionState.IsEligible("f_ovc"), Is.True,
                "readiness reads off owned categories only, never the currently-banked cell count");
        }

        // ---------------------------------------------------------------- AC4 (MV-515): exact 30-cell cost

        [Test]
        public void ForgingDeductsExactlyThirtyCellsAndFailsCleanlyBelowIt()
        {
            RigState.AcquireCap("p_dmg"); // already owned at run start, but explicit for clarity
            RigState.AcquireCap("s_bal");
            RigState.AcquireCap("e_cel"); // capacity 30 — room for the fusion's own 30-cell cost
            PickupWallet.SetPowerCells(29);

            Assert.That(PartSpend.TrySpendOnFusion("f_del"), Is.False, "29 cells must not be enough to forge a 30-cell fusion");
            Assert.That(RigFusionState.IsForged("f_del"), Is.False);
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(29), "a failed forge must not spend anything");

            PickupWallet.SetPowerCells(30);
            Assert.That(PartSpend.TrySpendOnFusion("f_del"), Is.True, "30 cells must forge a 30-cell fusion");
            Assert.That(RigFusionState.IsForged("f_del"), Is.True);
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0), "forging must spend exactly the fusion's own cost, not more or less");
        }

        [Test]
        public void ForgingFailsWithoutEligibilityEvenWithEnoughCells()
        {
            RigState.AcquireCap("e_cel"); // capacity 30 — room for the fusion's own 30-cell cost
            PickupWallet.SetPowerCells(30);

            Assert.That(PartSpend.TrySpendOnFusion("f_del"), Is.False,
                "30 cells banked is not enough on its own — SECONDARY has never been touched");
            Assert.That(RigFusionState.IsForged("f_del"), Is.False);
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(30), "an ineligible forge attempt must not spend anything");
        }

        [Test]
        public void ForgingTwiceFailsTheSecondTime_AlreadyForged()
        {
            RigState.AcquireCap("s_bal");
            RigState.AcquireCap("e_cel"); // capacity 30 — room for the fusion's own 30-cell cost
            PickupWallet.SetPowerCells(30);
            Assert.That(PartSpend.TrySpendOnFusion("f_del"), Is.True);

            PickupWallet.SetPowerCells(30);
            Assert.That(PartSpend.TrySpendOnFusion("f_del"), Is.False, "an already-forged fusion cannot be forged again");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(30), "the second, refused attempt must not spend anything");
        }

        // ---------------------------------------------------------------- AC3: occupies its named HUD slot

        [Test]
        public void AForgedFusionOccupiesItsNamedHudSlot_AndNoOtherFusionClaimsIt()
        {
            RigState.AcquireCap("s_bal");
            RigState.AcquireCap("e_cel"); // capacity 30 — room for the fusion's own 30-cell cost
            PickupWallet.SetPowerCells(30);
            Assert.That(PartSpend.TrySpendOnFusion("f_del"), Is.True);

            Assert.That(RigFusionState.ForgedInSlot("B"), Is.EqualTo("f_del"));
            Assert.That(RigFusionState.ForgedInSlot("U"), Is.Null, "slot U must still read LOCKED — nothing forged there");
        }

        // ---------------------------------------------------------------- AC4: never a draft candidate

        [Test]
        public void NoMorphingModuleDraftEverOffersAFusion()
        {
            // Light every category so every ability in the tree is reached — the widest possible
            // candidate pool a draft could ever offer.
            foreach (string rootId in new[] { "p_dmg", "s_bal", "e_ff", "m_spd", "u_sen" })
                RigState.AcquireCap(rootId);

            var eligible = new System.Collections.Generic.HashSet<string>(RigState.EligibleCapIds());
            foreach (var fusion in RigBoard.Fusions)
                Assert.That(eligible.Contains(fusion.Id), Is.False, $"'{fusion.Id}' must never appear in a Morphing Module draft pool");
        }

    }
}
