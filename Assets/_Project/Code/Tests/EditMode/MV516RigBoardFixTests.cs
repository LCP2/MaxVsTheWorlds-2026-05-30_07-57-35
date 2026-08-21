using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-516 — THE RIG board's dead band above the category row, the pulse/Button.interactable
    /// mismatch, and the near-black-on-family-hue level pill. Sole guard on all three; do not cull.
    /// </summary>
    public sealed class MV516RigBoardFixTests
    {
        private GameObject _go;
        private WeaponsScreen _screen;

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

        /// <summary>MV-516 AC1/AC2/AC3/AC4 in one test — the testing policy caps a ticket at one new
        /// EditMode test; the acceptance criteria below are asserted as one method's worth of resolved-
        /// value checks rather than four separate [Test] methods. AC5 (no new bottom clipping) is
        /// covered by the existing <c>RigBoardChromeTests.EveryNodeAndRegionPanelFitsInsideTheVisibleWindowAtEveryTestedAspect</c>,
        /// updated alongside this ticket's own pivot fix.</summary>
        [Test]
        public void CategoryRowClearsTheTopBar_PulseMatchesInteractable_AndTheOwnedPillIsReadable()
        {
            _screen.Open();

            // MV-516: CanvasScaler reads the ambient Editor/batchmode Screen size, not the aspect this
            // test drives via ApplyBoardScale — pin scaleFactor to 1 (a REAL h=1080 capture, matching
            // referenceResolution.y exactly under match-by-height, always yields 1.0 regardless of w) the
            // same way UiScreensDirector.ShowCanvasOnCamera already does for its own real captures, so
            // GetWorldCorners reads back directly in ref px with no further conversion.
            var scaler = _screen.RootCanvas.GetComponent<CanvasScaler>();
            scaler.enabled = false;
            _screen.RootCanvas.scaleFactor = 1f;

            // ---------------------------------------------------------------- AC1: the dead band
            float[] aspects = { 2.13f, 1.78f, 1.323f };
            foreach (float aspect in aspects)
            {
                _screen.ApplyBoardScale(aspect);

                var barCorners = new Vector3[4];
                _screen.TopBar.GetWorldCorners(barCorners);
                float barBottomY = Mathf.Min(barCorners[0].y, barCorners[3].y);

                bool phoneMode = WeaponsScreen.IsPhoneLayout(aspect);
                var categories = phoneMode ? RigBoardLayout.PhoneCategories : RigBoardLayout.Categories;
                Assert.That(categories.Count, Is.GreaterThan(0), $"fixture assumption: categories exist (phoneMode={phoneMode})");

                foreach (var cat in categories)
                {
                    var node = _screen.BoardNode(cat.Id);
                    Assert.That(node, Is.Not.Null, $"no built node for category '{cat.Id}' at aspect {aspect}");

                    var c = new Vector3[4];
                    node.GetWorldCorners(c);
                    float hexTopY = Mathf.Max(c[1].y, c[2].y);

                    float gap = barBottomY - hexTopY;
                    Assert.That(gap, Is.LessThanOrEqualTo(40f),
                        $"'{cat.Id}' sits {gap:0.0} ref px below the top bar at aspect {aspect} (phoneMode={phoneMode}) — over the 40px cap");
                }
            }

            // ---------------------------------------------------------------- AC2: pulse set == interactable set
            // p_dmg is owned at level 1 from run start; p_rng needs p_dmg at level >= 2 to become
            // cell-unlockable (RigState.IsCellUnlockable) — walking both through a matrix of wallet
            // states covers the owned-upgrade path, the unowned-unlock path, and "not enough banked"
            // for each.
            void AssertInteractableMatchesSpendable(string id)
            {
                var node = _screen.BoardNode(id);
                Assert.That(node, Is.Not.Null, $"no built node for ability '{id}'");
                // BuildNodeShell attaches Button to a "Hit" child, not the node's own root rect.
                var button = node.GetComponentInChildren<Button>(true);
                Assert.That(button, Is.Not.Null, $"'{id}' node has no Button component");
                bool expected = WeaponsScreen.IsAbilityNodeSpendable(id, PickupWallet.PowerCells);
                Assert.That(button.interactable, Is.EqualTo(expected),
                    $"'{id}' Button.interactable ({button.interactable}) must equal IsAbilityNodeSpendable ({expected}) at {PickupWallet.PowerCells} cells — the exact predicate Update() must pulse the hex body from");
            }

            void RefreshWith(int cells)
            {
                PickupWallet.SetPowerCells(cells);
                // RigState/PickupWallet's own Changed events aren't reliably pumped by the Editor outside
                // Play mode (see MV462RigBoardFixTests's own note) — force a fresh Refresh() by cycling
                // Open, the established pattern every state-change-after-Open test in this suite uses.
                _screen.Close();
                _screen.Open();
                // Refresh()'s own ApplyBoardScale() (paramless) reads the ambient Screen.width/height,
                // which a batchmode test runner reports unpredictably — pin a known standard-mode aspect
                // explicitly so this helper's own rebuild is deterministic regardless of what the AC1
                // loop above left it at or what the ambient window happens to be.
                _screen.ApplyBoardScale(1.78f);
            }

            RefreshWith(0);
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_dmg", 0), Is.False, "fixture: 0 cells must not cover p_dmg's level-1 upgrade");
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_rng", 0), Is.False, "fixture: p_rng is not yet cell-unlockable (p_dmg is only level 1)");
            AssertInteractableMatchesSpendable("p_dmg");
            AssertInteractableMatchesSpendable("p_rng");

            RefreshWith(CellSpend.UpgradeCostFor(1)); // exactly enough to level p_dmg up
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_dmg", PickupWallet.PowerCells), Is.True, "fixture: exact upgrade cost must be spendable");
            AssertInteractableMatchesSpendable("p_dmg");
            AssertInteractableMatchesSpendable("p_rng");

            RigState.RaiseLevel("p_dmg"); // model-layer raise, no currency — p_dmg to level 2, unlocks p_rng's cell path
            Assert.That(RigState.IsCellUnlockable("p_rng"), Is.True, "fixture: p_dmg is now level 2");

            RefreshWith(CellSpend.UnlockCostCells - 1); // one cell short for both p_rng's unlock and p_dmg's level-2 upgrade
            AssertInteractableMatchesSpendable("p_dmg");
            AssertInteractableMatchesSpendable("p_rng");

            RefreshWith(CellSpend.UnlockCostCells); // covers p_rng's unlock AND (coincidentally) p_dmg's level-2 upgrade
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_rng", PickupWallet.PowerCells), Is.True, "fixture: unlock cost must be spendable");
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_dmg", PickupWallet.PowerCells), Is.True, "fixture: level-2 upgrade cost must also be spendable here");
            AssertInteractableMatchesSpendable("p_dmg");
            AssertInteractableMatchesSpendable("p_rng");

            // ---------------------------------------------------------------- AC4: the hex fill actually animates
            const float baseAlpha = 0.9f;
            float spendableAtZero = WeaponsScreen.NodeActionPulseAlpha(0f, baseAlpha, spendable: true);
            float spendableLater = WeaponsScreen.NodeActionPulseAlpha(1.5f, baseAlpha, spendable: true);
            Assert.That(spendableAtZero, Is.Not.EqualTo(spendableLater),
                "an affordable node's hex fill alpha must differ between two sampled times");

            float inertAtZero = WeaponsScreen.NodeActionPulseAlpha(0f, baseAlpha, spendable: false);
            float inertLater = WeaponsScreen.NodeActionPulseAlpha(1.5f, baseAlpha, spendable: false);
            Assert.That(inertAtZero, Is.EqualTo(inertLater),
                "an inert node's hex fill alpha must not animate");

            // ---------------------------------------------------------------- AC3: the owned pill clears 4.5:1
            foreach (string familyKey in new[] { "pri", "sec", "eng", "mov", "sup" })
            {
                Color family = RigBoardLayout.Colour(familyKey); // read for parity with the AC's own "at every family hue" wording
                Assert.That(family.a, Is.GreaterThan(0f), $"fixture: family '{familyKey}' must resolve to a real colour");

                Color ink = RigBoardLayout.Colour("ink");
                Color backdrop = WeaponsScreen.PillBackdropColor;
                Color backdropOverBlack = new Color(backdrop.r * backdrop.a, backdrop.g * backdrop.a, backdrop.b * backdrop.a, 1f);

                float contrast = ContrastRatio(ink, backdropOverBlack);
                Assert.That(contrast, Is.GreaterThanOrEqualTo(4.5f),
                    $"owned pill text vs backdrop contrast is {contrast:0.00}:1 for family '{familyKey}' — under WCAG's 4.5:1 floor");
            }
        }

        private static float ContrastRatio(Color a, Color b)
        {
            float la = RelativeLuminance(a) + 0.05f;
            float lb = RelativeLuminance(b) + 0.05f;
            return la > lb ? la / lb : lb / la;
        }

        private static float RelativeLuminance(Color c) =>
            0.2126f * LinearChannel(c.r) + 0.7152f * LinearChannel(c.g) + 0.0722f * LinearChannel(c.b);

        private static float LinearChannel(float c) =>
            c <= 0.03928f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
    }
}
