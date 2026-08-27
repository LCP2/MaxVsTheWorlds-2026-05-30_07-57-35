using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-594 — THE RIG's family background panels are sized to the real bounds of the nodes they
    /// contain instead of a midpoint between category centres. Sole guard on the fix; do not cull.
    /// </summary>
    public sealed class MV594RigBoardFixTests
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
            // MV-594: AC3's own sub-test overrides RigBoardLayout's cached geometry mid-test —
            // unconditionally reload the real authored JSON so no later test in the same batch run sees
            // the overridden padX.
            RigBoardLayout.ResetForTests();
        }

        /// <summary>MV-594 AC1-AC4 in one test — the testing policy caps a ticket at one new EditMode
        /// test; the four acceptance criteria below are asserted as one method's worth of resolved-value
        /// checks rather than four separate [Test] methods.
        ///
        /// AC1/AC2 (containment + no cross-family overlap) are checked off <c>GetWorldCorners</c> at the
        /// three aspects the ticket names (2.13, 1.78, 1.33), covering one phone-mode aspect (2.13) and
        /// two standard-mode ones. The literal "margin equals RegionRectPadX (88) within 1 unit" clause
        /// cannot hold at every boundary against the REAL authored data: every family in
        /// <c>rig_board.json</c> sits closer to its neighbour (~37 raw px in standard mode, ~14 in phone)
        /// than 2x88 would need, so the panel builder's own neighbour-clamp (added so a panel can never
        /// engulf another family's node — the more fundamental correctness requirement, and AC2's own
        /// text) caps the achieved margin short of the full 88 everywhere in this data set. This test
        /// asserts what is actually true post-fix: full containment, a margin that is never negative
        /// (clipped) and never exceeds padX, and never overlapping a neighbour — AC3 below proves padX
        /// itself is genuinely read by driving it to a value small enough that the clamp does not
        /// saturate.</summary>
        [Test]
        public void FamilyPanelsContainOnlyTheirOwnNodesWithinPadXMargin_ReadPadX_AndLeaveNodePositionsUnchanged()
        {
            _screen.Open();

            // MV-516 idiom: pin scaleFactor to 1 so GetWorldCorners reads back directly in ref px.
            var scaler = _screen.RootCanvas.GetComponent<CanvasScaler>();
            scaler.enabled = false;
            _screen.RootCanvas.scaleFactor = 1f;

            // ---------------------------------------------------------------- AC4: node positions unchanged
            foreach (var cat in RigBoardLayout.Categories)
            {
                var node = _screen.BoardNode(cat.Id);
                Assert.That(node, Is.Not.Null, $"no built node for category '{cat.Id}'");
                Assert.That(node.anchoredPosition.x, Is.EqualTo(cat.X).Within(0.01f),
                    $"'{cat.Id}' anchored x must be untouched by the panel-bounds fix");
            }
            foreach (var ab in RigBoardLayout.Abilities)
            {
                var node = _screen.BoardNode(ab.Id);
                Assert.That(node, Is.Not.Null, $"no built node for ability '{ab.Id}'");
                Assert.That(node.anchoredPosition.x, Is.EqualTo(ab.X).Within(0.01f),
                    $"'{ab.Id}' anchored x must be untouched by the panel-bounds fix");
            }

            // ---------------------------------------------------------------- AC1 + AC2
            float padX = RigBoardLayout.RegionRectPadX;
            float[] aspects = { 2.13f, 1.78f, 1.33f };
            foreach (float aspect in aspects)
            {
                _screen.ApplyBoardScale(aspect);
                bool phoneMode = WeaponsScreen.IsPhoneLayout(aspect);
                var categories = phoneMode ? RigBoardLayout.PhoneCategories : RigBoardLayout.Categories;
                var abilities = phoneMode ? RigBoardLayout.PhoneAbilities : RigBoardLayout.Abilities;

                Vector2 WorldXRange(RectTransform rt)
                {
                    var c = new Vector3[4];
                    rt.GetWorldCorners(c);
                    return new Vector2(Mathf.Min(c[0].x, c[2].x), Mathf.Max(c[0].x, c[2].x));
                }

                foreach (var cat in categories)
                {
                    var panel = _screen.CategoryPanel(cat.Id);
                    Assert.That(panel, Is.Not.Null, $"no panel for '{cat.Id}' at aspect {aspect} (phoneMode={phoneMode})");
                    var panelRange = WorldXRange(panel.rectTransform);

                    var ownEdges = new List<float>();
                    var catNode = _screen.BoardNode(cat.Id);
                    Assert.That(catNode, Is.Not.Null, $"no built node for category '{cat.Id}'");
                    var catRange = WorldXRange(catNode);
                    ownEdges.Add(catRange.x);
                    ownEdges.Add(catRange.y);

                    foreach (var ab in abilities)
                    {
                        if (ab.Category != cat.Id) continue;
                        var abNode = _screen.BoardNode(ab.Id);
                        Assert.That(abNode, Is.Not.Null, $"no built node for ability '{ab.Id}'");
                        var abRange = WorldXRange(abNode);

                        // AC1: every node of this family must sit fully inside its own panel.
                        Assert.That(abRange.x, Is.GreaterThanOrEqualTo(panelRange.x - 1f),
                            $"'{ab.Id}' left edge escapes '{cat.Id}' panel at aspect {aspect} (phoneMode={phoneMode})");
                        Assert.That(abRange.y, Is.LessThanOrEqualTo(panelRange.y + 1f),
                            $"'{ab.Id}' right edge escapes '{cat.Id}' panel at aspect {aspect} (phoneMode={phoneMode})");

                        ownEdges.Add(abRange.x);
                        ownEdges.Add(abRange.y);
                    }

                    float ownLeftmost = ownEdges[0], ownRightmost = ownEdges[0];
                    foreach (float e in ownEdges) { ownLeftmost = Mathf.Min(ownLeftmost, e); ownRightmost = Mathf.Max(ownRightmost, e); }

                    float leftMargin = ownLeftmost - panelRange.x;
                    float rightMargin = panelRange.y - ownRightmost;
                    Assert.That(leftMargin, Is.InRange(-1f, padX + 1f),
                        $"'{cat.Id}' left margin {leftMargin:0.0} at aspect {aspect} (phoneMode={phoneMode}) must sit between 0 and RegionRectPadX ({padX})");
                    Assert.That(rightMargin, Is.InRange(-1f, padX + 1f),
                        $"'{cat.Id}' right margin {rightMargin:0.0} at aspect {aspect} (phoneMode={phoneMode}) must sit between 0 and RegionRectPadX ({padX})");

                    // AC2: no node belonging to a DIFFERENT family may fall inside this panel.
                    foreach (var other in abilities)
                    {
                        if (other.Category == cat.Id) continue;
                        var otherNode = _screen.BoardNode(other.Id);
                        var otherRange = WorldXRange(otherNode);
                        bool fullyInside = otherRange.x >= panelRange.x - 1f && otherRange.y <= panelRange.y + 1f;
                        Assert.That(fullyInside, Is.False,
                            $"'{other.Id}' (family '{other.Category}') falls inside '{cat.Id}' panel at aspect {aspect} (phoneMode={phoneMode})");
                    }
                    foreach (var other in categories)
                    {
                        if (other.Id == cat.Id) continue;
                        var otherNode = _screen.BoardNode(other.Id);
                        var otherRange = WorldXRange(otherNode);
                        bool fullyInside = otherRange.x >= panelRange.x - 1f && otherRange.y <= panelRange.y + 1f;
                        Assert.That(fullyInside, Is.False,
                            $"'{other.Id}' category node falls inside '{cat.Id}' panel at aspect {aspect} (phoneMode={phoneMode})");
                    }
                }
            }

            // ---------------------------------------------------------------- AC3: RegionRectPadX is actually read
            //
            // The authored 88 saturates the clamp above at every boundary in the real data (its own
            // interior half-gap is ~18.7 standard / ~7.1 phone), so a panel WIDTH measured only at 88
            // would look identical whether or not the code ever consulted RegionRectPadX at all. Drive it
            // to two small values safely under that saturation point instead and confirm ENERGY's panel
            // (an interior family, clamped on both sides) tracks the change 1:1 on each edge. Toggling
            // through a phone-mode aspect and back forces WeaponsScreen to rebuild the board (it only
            // rebuilds panels on a phone/standard verdict flip), so each measurement reflects the padX
            // value live at that rebuild.
            void RebuildStandardWith(float regionPadX)
            {
                RigBoardLayout.SetRegionRectPadXForTests(regionPadX);
                _screen.ApplyBoardScale(2.2f);   // force phone mode, drops the stale standard panels
                _screen.ApplyBoardScale(1.78f);  // force back to standard mode, rebuilds with the new padX
            }

            RebuildStandardWith(5f);
            float widthAt5 = _screen.CategoryPanel("ENERGY").rectTransform.sizeDelta.x;

            RebuildStandardWith(15f);
            float widthAt15 = _screen.CategoryPanel("ENERGY").rectTransform.sizeDelta.x;

            Assert.That(widthAt15 - widthAt5, Is.EqualTo(2f * (15f - 5f)).Within(0.5f),
                $"ENERGY panel width must grow by 2x the RegionRectPadX delta when neither value saturates the neighbour clamp (5px -> {widthAt5:0.0}, 15px -> {widthAt15:0.0})");
        }
    }
}
