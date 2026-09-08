using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-729 — the FORGE row's un-forged fusions rendered as four "? ? ?" diamonds under a shared
    /// generic caption, with no name, no stated requirement and no cost on the row itself: a working
    /// system (verified wired through <see cref="RigFusionState.TryForge"/> into real gameplay) read
    /// as broken furniture. Fixed by having the locked state show the fusion's real name and a
    /// plain-words requirement (<see cref="WeaponsScreen"/>'s new <c>LockedFusionRequirementText</c>)
    /// instead of the placeholder. Testing policy (MV-465): one new test, proven to fail on base
    /// commit 4df7d44 (main HEAD before this ticket) — on that commit the locked fusion's own
    /// <see cref="WeaponsScreen.FusionNodeLabel"/>/<see cref="WeaponsScreen.FusionNodeSub"/> accessors
    /// do not exist, so this file fails to even compile there; the fix comment quotes the compile
    /// failure as the base-commit "fail".
    /// </summary>
    public sealed class MV729FusionRowLegibilityTests
    {
        private GameObject _go;
        private WeaponsScreen _screen;

        private const string PlaceholderCompact = "???";
        private const string PlaceholderSpaced = "? ? ?";

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
            _go = new GameObject("WeaponsScreen");
            _screen = _go.AddComponent<WeaponsScreen>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
        }

        [Test]
        public void FusionRowNeverShowsThePlaceholderAndAlwaysExposesNameCategoriesAndCost()
        {
            var fusion = RigBoardLayout.Fusions.First(f => f.Id == "f_bgd"); // ENERGY + MOVE, cost 30, slot B

            // ---------------------------------------------------------------- fixture: neither parent lit
            Assert.That(RigFusionState.IsEligible("f_bgd"), Is.False, "fixture: run start owns nothing in ENERGY or MOVE");
            Assert.That(RigFusionState.CategoryLit(fusion.ParentA), Is.False, "fixture: ENERGY unlit at run start");
            Assert.That(RigFusionState.CategoryLit(fusion.ParentB), Is.False, "fixture: MOVE unlit at run start");

            _screen.Open();

            // ---------------------------------------------------------------- AC2: locked+ineligible row exposes name, both categories, and cost
            // NodeLabel/NodeSub hand back the LIVE Text component, which every later Refresh() mutates
            // in place (Close/Open reuses the same node, it doesn't rebuild it) — so each checkpoint's
            // own .text is captured into a string immediately, never compared via a held reference.
            Assert.That(_screen.FusionNodeLabel("f_bgd"), Is.Not.Null, "f_bgd built no name label");
            Assert.That(_screen.FusionNodeSub("f_bgd"), Is.Not.Null, "f_bgd built no sub-label");
            string lockedLabelText = _screen.FusionNodeLabel("f_bgd").text;
            string lockedSubText = _screen.FusionNodeSub("f_bgd").text;
            Assert.That(lockedLabelText, Is.EqualTo(fusion.Label), "a locked fusion must still show its real name, not hide it behind a placeholder");
            Assert.That(lockedSubText, Does.Contain(fusion.ParentA), "the locked row must state its first required category");
            Assert.That(lockedSubText, Does.Contain(fusion.ParentB), "the locked row must state its second required category");
            Assert.That(lockedSubText, Does.Contain(fusion.CellCost.ToString()), "the locked row must state its part cost");

            // ---------------------------------------------------------------- AC1: no placeholder anywhere, in any state
            AssertNoPlaceholder(lockedLabelText, "locked label");
            AssertNoPlaceholder(lockedSubText, "locked sub");

            // ---------------------------------------------------------------- regression guard: the worst-case category
            // pairing (PRIMARY + SECONDARY, f_del) plus a HAVE flag must still fit within its own sub-label's
            // box width, not just be non-empty — naming both categories and flagging ownership is worthless
            // if the result overflows into the neighbouring diamond's own label. p_dmg (PRIMARY) is owned at
            // run start, so f_del's locked row already carries a HAVE flag with no extra fixture needed.
            // Measured 344px against the 280px box on one line before this fix split categories/cost onto
            // separate lines.
            Assert.That(RigFusionState.CategoryLit("PRIMARY"), Is.True, "fixture: p_dmg (PRIMARY) is owned at run start");
            var delSub = _screen.FusionNodeSub("f_del");
            string delSubText = delSub.text;
            Assert.That(delSubText, Does.Contain("(HAVE)"), "f_del's locked sub-label must flag PRIMARY as already lit");
            foreach (string line in delSubText.Split('\n'))
            {
                delSub.text = line; // isolate one line's own preferredWidth — same font/size as the live label
                Assert.That(delSub.preferredWidth, Is.LessThanOrEqualTo(delSub.rectTransform.sizeDelta.x),
                    $"f_del's locked sub-label line '{line}' ({delSub.preferredWidth:0}px) overflows its own box ({delSub.rectTransform.sizeDelta.x}px) and will bleed into the neighbouring diamond");
            }
            delSub.text = delSubText;

            // ---------------------------------------------------------------- one parent now lit: still ineligible, but the
            // requirement text must change to reflect it — proves "which is already met" is resolved
            // dynamically, not baked into a static "PARENTA + PARENTB" string. RigState.Changed -> Refresh
            // isn't reliably driven by the Editor outside Play mode (WeaponsScreenOpenCloseTests /
            // MV462RigBoardFixTests document the same gotcha), so force a fresh Refresh() the same way
            // every other state-change-after-Open test in this suite does: close and reopen.
            RigState.UnlockCategory(fusion.ParentA);
            RigState.AcquireCap("e_ff"); // ENERGY's root ability
            Assert.That(RigFusionState.IsEligible("f_bgd"), Is.False, "one lit parent must not be enough on its own");
            _screen.Close();
            _screen.Open();

            string onePartLitSubText = _screen.FusionNodeSub("f_bgd").text;
            Assert.That(onePartLitSubText, Is.Not.EqualTo(lockedSubText),
                "the requirement text must change once one of the two parents becomes lit");
            AssertNoPlaceholder(_screen.FusionNodeLabel("f_bgd").text, "one-parent-lit label");
            AssertNoPlaceholder(onePartLitSubText, "one-parent-lit sub");

            // ---------------------------------------------------------------- AC3: eligible renders differently from ineligible
            RigState.UnlockCategory(fusion.ParentB);
            RigState.AcquireCap("m_spd"); // MOVE's root ability
            Assert.That(RigFusionState.IsEligible("f_bgd"), Is.True, "fixture: both parents now lit");
            _screen.Close();
            _screen.Open();

            string eligibleLabelText = _screen.FusionNodeLabel("f_bgd").text;
            string eligibleSubText = _screen.FusionNodeSub("f_bgd").text;
            Assert.That(eligibleLabelText, Is.EqualTo(fusion.Label), "an eligible fusion must keep showing its real name");
            Assert.That(eligibleSubText, Is.Not.EqualTo(onePartLitSubText), "an eligible row must render its sub-label differently from an ineligible one");
            AssertNoPlaceholder(eligibleLabelText, "eligible label");
            AssertNoPlaceholder(eligibleSubText, "eligible sub");

            // ---------------------------------------------------------------- forged: still no placeholder anywhere
            PickupWallet.SetPowerCells(fusion.CellCost);
            Assert.That(PartSpend.TrySpendOnFusion("f_bgd"), Is.True, "fixture: enough cells banked to forge");
            _screen.Close();
            _screen.Open();

            string forgedLabelText = _screen.FusionNodeLabel("f_bgd").text;
            string forgedSubText = _screen.FusionNodeSub("f_bgd").text;
            Assert.That(forgedLabelText, Is.EqualTo(fusion.Label));
            Assert.That(forgedSubText, Does.Contain("FORGED"));
            AssertNoPlaceholder(forgedLabelText, "forged label");
            AssertNoPlaceholder(forgedSubText, "forged sub");
        }

        private static void AssertNoPlaceholder(string text, string what)
        {
            Assert.That(text, Does.Not.Contain(PlaceholderCompact), $"{what} must never render the literal placeholder \"???\"");
            Assert.That(text, Does.Not.Contain(PlaceholderSpaced), $"{what} must never render the spaced placeholder \"? ? ?\"");
        }
    }
}
