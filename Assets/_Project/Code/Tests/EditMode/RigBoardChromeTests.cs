using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-433 — THE RIG board's chrome, on top of MV-423's node layout (<see cref="RigBoardLayoutTests"/>,
    /// deliberately left untouched by this ticket): the opaque page backdrop, each category region
    /// panel's near-invisible tint, the owned/draftable node halo, and the scale-to-fit wrapper that
    /// keeps every node and region panel on screen at aspect ratios narrower than 16:9. Same
    /// build-state-then-open idiom <see cref="RigBoardLayoutTests"/> documents — no coroutine / Play
    /// mode needed, and CC_AUTONOMY.md bars authoring PlayMode tests outright.
    /// </summary>
    public sealed class RigBoardChromeTests
    {
        private GameObject _go;
        private WeaponsScreen _screen;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            _go = new GameObject("WeaponsScreen");
            _screen = _go.AddComponent<WeaponsScreen>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            WeaponSystemState.Reset();
            PickupWallet.Reset();
        }

        private void OpenScreen() => _screen.Open();

        // ------------------------------------------------------------------ AC1: opaque backdrop

        [Test]
        public void BackgroundIsAnOpaqueFirstCanvasChildReadFromTheDataFile()
        {
            OpenScreen();
            var bg = _screen.Background;
            Assert.That(bg, Is.Not.Null);
            Assert.That(bg.transform.parent.GetComponent<Canvas>(), Is.Not.Null,
                "Background must be a direct child of the Rig's own Canvas, not Safe Area, so it sits behind the top bar too");
            Assert.That(bg.transform.GetSiblingIndex(), Is.EqualTo(0),
                "Background must be the canvas's first child so it draws behind everything");
            Assert.That(bg.color.a, Is.EqualTo(1f).Within(1e-3f), "backdrop must be fully opaque, not a scrim");

            var expected = RigBoardLayout.Colour("base");
            Assert.That(bg.color.r, Is.EqualTo(expected.r).Within(1e-3f));
            Assert.That(bg.color.g, Is.EqualTo(expected.g).Within(1e-3f));
            Assert.That(bg.color.b, Is.EqualTo(expected.b).Within(1e-3f));
        }

        // ------------------------------------------------------------------ AC2: region panel opacity

        [Test]
        public void RegionPanelOpacityIsReadFromTheDataFile()
        {
            // Run start: p_dmg (PRIMARY) is the only owned ability, so PRIMARY is the only lit category
            // (RigState's own "run start" rule, restated in rig_board.json's model.$comment).
            OpenScreen();
            foreach (var cat in RigBoardLayout.Categories)
            {
                var panel = _screen.CategoryPanel(cat.Id);
                Assert.That(panel, Is.Not.Null, $"no region panel for '{cat.Id}'");
                bool lit = cat.Id == "PRIMARY";
                float expected = lit ? RigBoardLayout.RegionOpacityLit : RigBoardLayout.RegionOpacityDark;
                Assert.That(panel.color.a, Is.EqualTo(expected).Within(1e-3f), $"'{cat.Id}' region opacity");
            }
        }

        // ------------------------------------------------------------------ AC3: owned/draftable glow

        [Test]
        public void AbilityGlowAppearsOnExactlyOwnedAndDraftableNodesAtTheSpecifiedRadiiAndAlphas()
        {
            OpenScreen();
            float r = RigBoardLayout.RadiusAbility;

            foreach (var ab in RigBoardLayout.Abilities)
            {
                var node = _screen.BoardNode(ab.Id);
                var glow = node.Find("Glow").GetComponent<Image>();

                bool owned = RigState.IsOwned(ab.Id);
                bool reached = RigState.IsReached(ab.Id);
                bool draftable = ab.Kind == "cap" && reached && !owned;
                bool expectedActive = owned || draftable;

                Assert.That(glow.gameObject.activeSelf, Is.EqualTo(expectedActive), $"'{ab.Id}' glow active");
                if (!expectedActive) continue;

                float expectedDiameter = owned
                    ? r * 1.30f * 2f
                    : (r + RigBoardLayout.CapOuterRingOffset) * 2f;
                Assert.That(glow.rectTransform.sizeDelta.x, Is.EqualTo(expectedDiameter).Within(0.5f), $"'{ab.Id}' glow diameter");
                Assert.That(glow.rectTransform.sizeDelta.y, Is.EqualTo(expectedDiameter).Within(0.5f), $"'{ab.Id}' glow diameter");

                float expectedAlpha = owned ? 0.28f : 0.22f;
                Assert.That(glow.color.a, Is.EqualTo(expectedAlpha).Within(0.02f), $"'{ab.Id}' glow alpha");

                if (!owned)
                {
                    var module = RigBoardLayout.Colour("module");
                    Assert.That(glow.color.r, Is.EqualTo(module.r).Within(0.02f), $"'{ab.Id}' draftable glow must be module cyan");
                }
            }
        }

        [Test]
        public void CategoryGlowAppearsOnlyOnLitCategoriesAtTheSpecifiedRadiusAndAlpha()
        {
            OpenScreen();
            foreach (var cat in RigBoardLayout.Categories)
            {
                var node = _screen.BoardNode(cat.Id);
                var glow = node.Find("Glow").GetComponent<Image>();
                bool lit = cat.Id == "PRIMARY";
                Assert.That(glow.gameObject.activeSelf, Is.EqualTo(lit), $"'{cat.Id}' category glow");
                if (!lit) continue;

                float expectedDiameter = RigBoardLayout.RadiusCategory * 1.30f * 2f;
                Assert.That(glow.rectTransform.sizeDelta.x, Is.EqualTo(expectedDiameter).Within(0.5f));
                Assert.That(glow.color.a, Is.EqualTo(0.28f).Within(0.02f));
            }
        }

        // ------------------------------------------------------------------ AC4: scale-to-fit keeps every node on screen

        /// <summary>
        /// Before MV-433 the board never scaled (always 1:1 in its 1920x1080 frame), so at any aspect
        /// narrower than 16:9 the rightmost SUPPORT nodes (<c>u_hp</c>/<c>u_slt</c>, x=1790, r=50) and
        /// the SUPPORT region panel simply ran off the visible edge — e.g. at 1.60:1 the visible window
        /// is x in [96, 1824] (<see cref="WeaponsScreen.VisibleRefXWindow"/>) but u_hp/u_slt's raw right
        /// edge is 1840. This test would fail on the pre-MV-433 code for exactly that reason (no
        /// <see cref="WeaponsScreen.ComputeBoardScale"/>/scale wrapper existed to bring it back in); it
        /// passes now because every node and region panel is checked post-scale, at the same clamped
        /// factor <see cref="WeaponsScreen.ApplyBoardScale"/> actually applies to the board.
        /// </summary>
        [Test]
        public void EveryNodeAndRegionPanelFitsInsideTheVisibleWindowAtEveryTestedAspect()
        {
            float[] aspects = { 2.17f, 16f / 9f, 1.60f, 1.50f };
            var categories = RigBoardLayout.Categories;
            int n = categories.Count;
            float spacing = n > 1 ? categories[1].X - categories[0].X : 0f;
            const float boardCentreX = 1920f * 0.5f;

            foreach (float aspect in aspects)
            {
                float scale = WeaponsScreen.ComputeBoardScale(aspect);
                var window = WeaponsScreen.VisibleRefXWindow(aspect);

                void AssertFits(string id, float rawX, float halfExtent)
                {
                    float scaledX = boardCentreX + (rawX - boardCentreX) * scale;
                    float scaledHalf = halfExtent * scale;
                    Assert.That(scaledX - scaledHalf, Is.GreaterThanOrEqualTo(window.MinX),
                        $"'{id}' left edge clipped at aspect {aspect}");
                    Assert.That(scaledX + scaledHalf, Is.LessThanOrEqualTo(window.MaxX),
                        $"'{id}' right edge clipped at aspect {aspect}");
                }

                foreach (var cat in categories) AssertFits(cat.Id, cat.X, RigBoardLayout.RadiusCategory);
                foreach (var ab in RigBoardLayout.Abilities) AssertFits(ab.Id, ab.X, RigBoardLayout.RadiusAbility);
                foreach (var fu in RigBoardLayout.Fusions) AssertFits(fu.Id, fu.X, RigBoardLayout.RadiusFusion);

                for (int i = 0; i < n; i++)
                {
                    float left = i == 0 ? categories[i].X - spacing * 0.5f : (categories[i - 1].X + categories[i].X) * 0.5f;
                    float right = i == n - 1 ? categories[i].X + spacing * 0.5f : (categories[i].X + categories[i + 1].X) * 0.5f;
                    AssertFits($"{categories[i].Id} Panel", (left + right) * 0.5f, (right - left) * 0.5f);
                }
            }
        }

        // ------------------------------------------------------------------ AC5: the scale clamp never chases the crop below its own floor

        /// <summary>
        /// What's actually enforced: the clamp itself never drops below 0.9 at any aspect, however
        /// narrow — that's the real content of "clamp the scale at 0.9 and accept a small crop below
        /// that." The ticket's AC5 also asserts this keeps every node at/above Apple's 44pt HIG minimum
        /// on the 932x430pt target; that specific numeric claim does NOT hold for the ability nodes at
        /// this clamp (documented below and in the MV-433 fix comment) — the ticket's own "Fix" section
        /// computes the identical number (90 ref-px -> 39.6pt) and calls it "under Apple's 44pt minimum"
        /// in the very same breath as prescribing this same 0.9 floor, so the two clauses disagree with
        /// each other, not just with the implementation. Flagged rather than asserted as true.
        /// </summary>
        [Test]
        public void BoardScaleNeverDropsBelowItsOwnFloor()
        {
            float[] aspects = { 2.17f, 16f / 9f, 1.60f, 1.50f, 1.33f, 1.0f };
            foreach (float aspect in aspects)
                Assert.That(WeaponsScreen.ComputeBoardScale(aspect), Is.GreaterThanOrEqualTo(0.9f - 1e-4f), $"aspect {aspect}");
        }

        [Test]
        public void DocumentsTheClampFloorsKnownShortfallAgainstTheLiteralFortyFourPointAc()
        {
            const float abilityDiameterRefPx = 100f;   // RigBoardLayout.RadiusAbility * 2
            const float scale6Inch = 0.44f;            // SettingsPanel.Scale6Inch (see that file's own sqrt(932/1920)*sqrt(430/1080) derivation)
            float ptAtFloor = abilityDiameterRefPx * 0.9f * scale6Inch;
            Assert.That(ptAtFloor, Is.EqualTo(39.6f).Within(0.1f),
                "matches the MV-433 ticket's own worked example: even at the 0.9 floor this is under Apple's 44pt minimum");
        }

        // ------------------------------------------------------------------ AC6: RigBoardLayoutTests untouched

        // No test here by design — RigBoardLayoutTests.cs itself is the guard, and this ticket doesn't
        // modify it. Its own suite (unchanged) covers the coordinate/size assertions this ticket must
        // not disturb; the board scale wrapper is a new ancestor of Board Root, and RigBoardLayoutTests
        // only ever reads anchoredPosition/sizeDelta in Board Root's own local space, which no ancestor
        // transform can affect.
    }
}
