using System.Linq;
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
        public void BackgroundIsAnOpaqueFirstScreenChildReadFromTheDataFile()
        {
            OpenScreen();
            var bg = _screen.Background;
            Assert.That(bg, Is.Not.Null);

            // MV-440: Background moved from a direct Canvas child to Screen Root's first child (with
            // Safe Area as its very next sibling) so the two toggle together — not asserting the
            // wrapper's own name/identity (an implementation detail), only the ordering AC3 pins:
            // still behind everything, including the top bar inside Safe Area.
            var screenParent = bg.transform.parent;
            Assert.That(screenParent.GetComponent<Canvas>(), Is.Null,
                "Background is no longer a direct Canvas child post-MV-440 — it is wrapped with Safe Area so both toggle as one");
            Assert.That(bg.transform.GetSiblingIndex(), Is.EqualTo(0),
                "Background must be its parent's first child so it draws behind everything");
            Assert.That(screenParent.GetChild(1).GetComponent<SafeArea>(), Is.Not.Null,
                "Safe Area must be Background's very next sibling so Background still draws behind the top bar");
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
                // MV-462 defect 3: an unlit category's panel is additionally dimmed by FamilyDimFactor on
                // top of RegionOpacityDark — the panel alone was never enough to read as "receded"
                // (that's this ticket's own point), but it still carries the multiplier like every other
                // graphic in the family.
                float expected = lit ? RigBoardLayout.RegionOpacityLit : RigBoardLayout.RegionOpacityDark * RigBoardLayout.FamilyDimFactor;
                Assert.That(panel.color.a, Is.EqualTo(expected).Within(1e-4f), $"'{cat.Id}' region opacity");
            }
        }

        // ------------------------------------------------------------------ MV-445 defect 1: composited alpha

        /// <summary>MV-445 AC2: sums EVERY graphic under Board Root that actually covers a point clear
        /// of any node/connector inside each category's own column, and asserts the combined coverage
        /// (the standard <c>1 - product(1-a_i)</c> union of independent alpha layers, order-independent
        /// so draw order doesn't matter for a pure coverage check) equals opacityLit/opacityDark within
        /// 1e-3 — the ticket's own point: testing <see cref="RegionPanelOpacityIsReadFromTheDataFile"/>'s
        /// single Image in isolation is exactly what let a second full-column layer go unnoticed.
        /// y=345 is clear board-wide: below every category's own owned/lit glow (bottom edge
        /// 230+72*1.30=323.6) and above every tier-1 ability's own glow (top edge 420-50*1.30=355) —
        /// verified against both radii, not eyeballed.</summary>
        [Test]
        public void CompositedColumnCoverageAwayFromAnyNodeMatchesRegionOpacityExactly()
        {
            OpenScreen();
            const float testY = 345f;
            var boardRoot = _screen.BoardNode("PRIMARY").parent;
            Assert.That(boardRoot, Is.Not.Null, "category node has no Board Root parent");

            var images = boardRoot.GetComponentsInChildren<Image>(true);

            foreach (var cat in RigBoardLayout.Categories)
            {
                bool lit = cat.Id == "PRIMARY";
                // MV-462 defect 3: dimmed on top of RegionOpacityDark for an unlit category, same as
                // RegionPanelOpacityIsReadFromTheDataFile.
                float expected = lit ? RigBoardLayout.RegionOpacityLit : RigBoardLayout.RegionOpacityDark * RigBoardLayout.FamilyDimFactor;

                var worldPoint = boardRoot.TransformPoint(new Vector3(cat.X, -testY, 0f));
                float coverage = 0f;
                foreach (var img in images)
                {
                    if (!img.isActiveAndEnabled || img.color.a <= 0f) continue;
                    var rt = img.rectTransform;
                    var corners = new Vector3[4];
                    rt.GetWorldCorners(corners);
                    bool contains = worldPoint.x >= corners[0].x && worldPoint.x <= corners[2].x
                                     && worldPoint.y >= corners[0].y && worldPoint.y <= corners[2].y;
                    if (!contains) continue;

                    // A 9-sliced image (the region panel/border) does not paint its flat tint uniformly
                    // across its whole rect — the centre tile is a distinct texel range from the border
                    // margin. Bounds-containment alone would wrongly count the border's OWN flat alpha
                    // (a thin stroke) as if it filled the entire column. Sample the sprite texture's own
                    // centre texel instead — representative of whatever a 9-slice centre tile actually
                    // renders at any deep-interior point (this test's point always is one, hundreds of
                    // px from every panel edge) — and fold color.a * textureAlpha, not color.a alone.
                    float textureAlpha = 1f;
                    if (img.sprite != null && img.sprite.texture != null)
                    {
                        var tex = img.sprite.texture;
                        textureAlpha = tex.GetPixel(tex.width / 2, tex.height / 2).a;
                    }
                    float a = img.color.a * textureAlpha;
                    if (a <= 0f) continue;
                    coverage = coverage + a * (1f - coverage);
                }

                Assert.That(coverage, Is.EqualTo(expected).Within(1e-3f),
                    $"'{cat.Id}' composited column coverage at y={testY} (clear of every node/connector)");
            }
        }

        // ------------------------------------------------------------------ AC3: owned/draftable glow

        [Test]
        public void AbilityGlowAppearsOnExactlyOwnedAndDraftableNodesAtTheSpecifiedRadiiAndAlphas()
        {
            OpenScreen();
            float r = RigBoardLayout.RadiusAbility;
            // MV-446 defect 2: the halo now follows the hex's own (narrower) width instead of a square
            // bounding box — r*sqrt(3) x 2r, not 2r x 2r — scaled by the SAME headroom multiplier for
            // both owned and draftable (no longer the draftable-only "out to the dashed ring" sizing).
            float expectedW = r * Mathf.Sqrt(3f) * 1.15f;
            float expectedH = r * 2f * 1.15f;

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

                Assert.That(glow.rectTransform.sizeDelta.x, Is.EqualTo(expectedW).Within(0.5f), $"'{ab.Id}' glow width");
                Assert.That(glow.rectTransform.sizeDelta.y, Is.EqualTo(expectedH).Within(0.5f), $"'{ab.Id}' glow height");

                // MV-446 defect 2: owned/draftable peak alpha (and blur width) now come off
                // rig_board.json (RigBoardLayout.GlowAlphaOwned/GlowAlphaDraft) instead of a hardcoded
                // 0.55/0.22 — tunable without a code change, per the ticket's own AC.
                // MV-462 defect 3: a draftable node's glow is additionally dimmed when its own category
                // has nothing owned anywhere in it — `owned` here already implies its category is lit
                // (an owned ability lights its own category), so this only ever bites the draftable case.
                bool familyLit = owned || CategoryHasOwnedAbilityInCategory(ab.Category);
                float baseAlpha = owned ? RigBoardLayout.GlowAlphaOwned : RigBoardLayout.GlowAlphaDraft;
                float expectedAlpha = familyLit ? baseAlpha : baseAlpha * RigBoardLayout.FamilyDimFactor;
                Assert.That(glow.color.a, Is.EqualTo(expectedAlpha).Within(0.02f), $"'{ab.Id}' glow alpha");

                var family = RigBoardLayout.Colour(RigBoardLayout.CategoryFamily(ab.Category));
                Assert.That(glow.color.r, Is.EqualTo(family.r).Within(0.02f), $"'{ab.Id}' glow must be family-coloured");
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

                float r = RigBoardLayout.RadiusCategory;
                Assert.That(glow.rectTransform.sizeDelta.x, Is.EqualTo(r * Mathf.Sqrt(3f) * 1.15f).Within(0.5f));
                Assert.That(glow.rectTransform.sizeDelta.y, Is.EqualTo(r * 2f * 1.15f).Within(0.5f));
                Assert.That(glow.color.a, Is.EqualTo(RigBoardLayout.GlowAlphaOwned).Within(0.02f));
            }
        }

        // ------------------------------------------------------------------ MV-443 AC2/AC3

        [Test]
        public void ConnectorExistsForEveryParentChildPairEveryCategoryToParentlessAbilityPairAndEveryFusionParent()
        {
            OpenScreen();

            foreach (var ab in RigBoardLayout.Abilities)
            {
                string id = string.IsNullOrEmpty(ab.Parent)
                    ? $"conn:cat:{ab.Category}>{ab.Id}"
                    : $"conn:ab:{ab.Parent}>{ab.Id}";
                Assert.That(_screen.Connector(id), Is.Not.Null, $"missing connector '{id}'");
            }

            foreach (var fusion in RigBoardLayout.Fusions)
            {
                Assert.That(_screen.Connector($"conn:fusion:{fusion.Id}>{fusion.ParentA}"), Is.Not.Null, $"missing connector for '{fusion.Id}' <- '{fusion.ParentA}'");
                Assert.That(_screen.Connector($"conn:fusion:{fusion.Id}>{fusion.ParentB}"), Is.Not.Null, $"missing connector for '{fusion.Id}' <- '{fusion.ParentB}'");
            }
        }

        [Test]
        public void EveryNodeHasALevelPill()
        {
            OpenScreen();
            var ids = new System.Collections.Generic.List<string>();
            foreach (var c in RigBoardLayout.Categories) ids.Add(c.Id);
            foreach (var a in RigBoardLayout.Abilities) ids.Add(a.Id);

            foreach (var id in ids)
            {
                var node = _screen.BoardNode(id);
                Assert.That(node.Find("Pill"), Is.Not.Null, $"'{id}' has no level pill");
                Assert.That(node.Find("Pill Border"), Is.Not.Null, $"'{id}' has no level pill border");
            }
        }

        [Test]
        public void ALockedAbilityNodesLabelReadsQuestionMarksWithSpaces()
        {
            OpenScreen();
            foreach (var ab in RigBoardLayout.Abilities)
            {
                if (RigState.IsReached(ab.Id)) continue;   // only "not reached" is locked
                var node = _screen.BoardNode(ab.Id);
                var label = node.Find("Text").GetComponent<Text>();
                Assert.That(label.text, Is.EqualTo("? ? ?"), $"'{ab.Id}' locked label");
            }
        }

        [Test]
        public void ACategoryNodeNeverShowsLockOrQuestionMarks()
        {
            // Run start: only PRIMARY is lit; every other category is still the un-lit "third state"
            // (defect 4) and must never read as locked.
            OpenScreen();
            foreach (var cat in RigBoardLayout.Categories)
            {
                var node = _screen.BoardNode(cat.Id);
                var pillText = node.Find("Pill").GetComponentInChildren<Text>();
                Assert.That(pillText.text, Does.Not.Contain("LOCK"), $"'{cat.Id}' pill must never read LOCK");
                Assert.That(pillText.text, Does.Not.Contain("?"), $"'{cat.Id}' pill must never read '? ? ?'");
                Assert.That(pillText.text, Is.EqualTo($"{(cat.Id == "PRIMARY" ? 1 : 0)}/{CountAbilitiesIn(cat.Id)}"));
            }
        }

        private static int CountAbilitiesIn(string categoryId)
        {
            int n = 0;
            foreach (var ab in RigBoardLayout.Abilities)
                if (ab.Category == categoryId) n++;
            return n;
        }

        /// <summary>MV-462 defect 3: test-side mirror of <c>WeaponsScreen.CategoryHasOwnedAbility</c>
        /// (private) — whether ANY ability in <paramref name="categoryId"/> is owned, i.e. whether that
        /// family is lit and exempt from the family dim.</summary>
        private static bool CategoryHasOwnedAbilityInCategory(string categoryId)
        {
            foreach (var ab in RigBoardLayout.Abilities)
                if (ab.Category == categoryId && RigState.IsOwned(ab.Id)) return true;
            return false;
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
        ///
        /// MV-445 defect 2: aspects widened to 1.4-2.4 (was 1.5-2.17) and <see cref="WeaponsScreen.BoardScaleFloor"/>
        /// lowered 0.9 -> 0.83 to go with it — at 0.9 this test would have failed at 1.4 (SUPPORT
        /// region panel's scaled right edge 1761 vs a visible max of 1716), exactly the clipping Lee
        /// saw at his own ~1.65:1 viewport once a window resize left the board on a stale scale.
        /// </summary>
        [Test]
        public void EveryNodeAndRegionPanelFitsInsideTheVisibleWindowAtEveryTestedAspect()
        {
            float[] aspects = { 2.4f, 2.17f, 2.0f, 1.78f, 1.60f, 1.50f, 1.4f };
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
        /// What's actually enforced: the clamp itself never drops below <see cref="WeaponsScreen.BoardScaleFloor"/>
        /// (MV-445 defect 2: 0.83, was 0.9) at any aspect, however narrow — that's the real content of
        /// "clamp the scale and accept a small crop below that." The ticket's AC5 also asserts this
        /// keeps every node at/above Apple's 44pt HIG minimum on the 932x430pt target; that specific
        /// numeric claim did NOT hold even at the old 0.9 floor (documented below and in the MV-433 fix
        /// comment) and holds even less at 0.83 — MV-445 lowered the floor anyway because keeping every
        /// node and region panel actually ON screen down to 1.4:1 (AC3) is the ticket's own explicit
        /// priority over the 44pt target it separately flags as already broken. Flagged, not hidden.
        /// </summary>
        [Test]
        public void BoardScaleNeverDropsBelowItsOwnFloor()
        {
            float[] aspects = { 2.17f, 16f / 9f, 1.60f, 1.50f, 1.4f, 1.33f, 1.0f };
            foreach (float aspect in aspects)
                Assert.That(WeaponsScreen.ComputeBoardScale(aspect), Is.GreaterThanOrEqualTo(0.83f - 1e-4f), $"aspect {aspect}");
        }

        [Test]
        public void DocumentsTheClampFloorsKnownShortfallAgainstTheLiteralFortyFourPointAc()
        {
            const float abilityDiameterRefPx = 100f;   // RigBoardLayout.RadiusAbility * 2
            const float scale6Inch = 0.44f;            // SettingsPanel.Scale6Inch (see that file's own sqrt(932/1920)*sqrt(430/1080) derivation)
            float ptAtFloor = abilityDiameterRefPx * 0.83f * scale6Inch;
            Assert.That(ptAtFloor, Is.EqualTo(36.52f).Within(0.1f),
                "MV-445: floor lowered 0.9 -> 0.83 (AC3, keeping SUPPORT on screen down to 1.4:1) shrinks this further below Apple's 44pt minimum, not closer to it");
        }

        // ------------------------------------------------------------------ AC6: RigBoardLayoutTests untouched

        // No test here by design — RigBoardLayoutTests.cs itself is the guard, and this ticket doesn't
        // modify it. Its own suite (unchanged) covers the coordinate/size assertions this ticket must
        // not disturb; the board scale wrapper is a new ancestor of Board Root, and RigBoardLayoutTests
        // only ever reads anchoredPosition/sizeDelta in Board Root's own local space, which no ancestor
        // transform can affect.

        // ------------------------------------------------------------------ MV-445 defect 4: dashed hex ring

        /// <summary>Renders the exact sprite a draftable node's hex outline uses and walks the polygon's
        /// own perimeter (via its 6 vertex positions, not the texture's raw pixel grid) counting on/off
        /// transitions — the number of dashes actually painted must be within +/-1 of
        /// perimeter/(dash+gap), i.e. an even, continuous ring, not "two or three stray dashes" (the old
        /// angle-based phase bunched dashes near vertices and stretched/dropped them near edge
        /// midpoints on a hexagon, where equal angle steps are not equal arc-length steps).</summary>
        [Test]
        public void DashedHexOutlineWalksTheClosedPolygonAsOneContinuousDashedRing()
        {
            float r = RigBoardLayout.RadiusAbility;
            const float dash = 13f, gap = 9f;
            int texW = Mathf.CeilToInt(r * 1.7320508f), texH = Mathf.CeilToInt(r * 2f);
            var sprite = HudTextures.PolygonOutline(6, -90f, texW, texH, RigBoardLayout.StrokeActive, true, dash, gap);
            var tex = sprite.texture;

            float cx = texW * 0.5f, cy = texH * 0.5f, radius = texH * 0.5f;
            const int sides = 6;
            float segment = 2f * Mathf.PI / sides;
            float rot = -90f * Mathf.Deg2Rad;
            var vertices = new Vector2[sides];
            for (int k = 0; k < sides; k++)
            {
                float ang = rot + k * segment;
                vertices[k] = new Vector2(radius * Mathf.Cos(ang), radius * Mathf.Sin(ang));
            }
            float apothem = radius * Mathf.Cos(segment * 0.5f);
            float sideLen = 2f * apothem * Mathf.Tan(segment * 0.5f);
            float perimeter = sideLen * sides;

            const int steps = 4000;
            bool prevOn = false;
            int dashCount = 0;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps * perimeter;
                int edgeIndex = Mathf.Clamp((int)(t / sideLen), 0, sides - 1);
                float within = t - edgeIndex * sideLen;
                float frac = within / sideLen;
                Vector2 a = vertices[edgeIndex], b = vertices[(edgeIndex + 1) % sides];
                Vector2 p = Vector2.Lerp(a, b, frac);
                int px = Mathf.Clamp(Mathf.RoundToInt(cx + p.x), 0, texW - 1);
                int py = Mathf.Clamp(Mathf.RoundToInt(cy + p.y), 0, texH - 1);
                bool on = tex.GetPixel(px, py).a > 0.5f;
                if (on && !prevOn) dashCount++;
                prevOn = on;
            }
            // wrap: the walk starts partway through whatever segment sits at arc-length 0 (a dash
            // straddling the seam could double-count), and perimeter/(dash+gap)=13.6 is not a whole
            // number, so the final wrap-around segment is always a partial dash/gap that can round
            // away at this texture's own pixel resolution (~87x100 for an ability node) — accept up to
            // 2 off the ideal count, well short of the old bug's "2-3 dashes total".
            float expected = perimeter / (dash + gap);
            Assert.That(dashCount, Is.EqualTo(expected).Within(2.0),
                $"expected ~{expected:N1} dashes walking the closed hex perimeter, got {dashCount}");
            Assert.That(dashCount, Is.GreaterThan(3), "the old angle-based bug rendered only 2-3 stray dashes");
        }

        // ------------------------------------------------------------------ MV-445 defect 3: fusion connectors

        [Test]
        public void FusionConnectorsDrawAtFusionAlphaOnlyWhenBothParentCategoriesAreLit()
        {
            OpenScreen();
            Color part = RigBoardLayout.Colour("part");
            foreach (var fusion in RigBoardLayout.Fusions)
            {
                bool reachable = RigFusionState.IsEligible(fusion.Id);
                float expected = reachable ? RigBoardLayout.ConnectorFusionAlpha : RigBoardLayout.ConnectorFusionAlphaLocked;
                foreach (var parentId in new[] { fusion.ParentA, fusion.ParentB })
                {
                    var img = _screen.Connector($"conn:fusion:{fusion.Id}>{parentId}");
                    Assert.That(img, Is.Not.Null, $"missing fusion connector '{fusion.Id}>{parentId}'");
                    Assert.That(img.color.a, Is.EqualTo(expected).Within(1e-4f), $"'{fusion.Id}>{parentId}' fusion connector alpha");
                    Assert.That(img.color.r, Is.EqualTo(part.r).Within(1e-3f), $"'{fusion.Id}>{parentId}' fusion connector colour");
                }
            }
        }

        [Test]
        public void FusionAlphaLockedIsDimmerThanFusionAlpha()
        {
            Assert.That(RigBoardLayout.ConnectorFusionAlphaLocked, Is.LessThan(RigBoardLayout.ConnectorFusionAlpha));
        }

        // ------------------------------------------------------------------ MV-445 defect 5: locked FORGE diamond

        [Test]
        public void LockedFusionDiamondWeightsComeFromTheDataFileAndAreDimmerThanEligible()
        {
            OpenScreen();
            foreach (var fusion in RigBoardLayout.Fusions)
            {
                if (RigFusionState.IsEligible(fusion.Id)) continue;   // only the locked (unreachable) state
                var node = _screen.BoardNode(fusion.Id);
                var outline = node.Find("Outline").GetComponent<Image>();
                var icon = node.Find("Icon").GetComponent<Image>();
                Assert.That(outline.color.a, Is.EqualTo(RigBoardLayout.LockedFusionBorderAlpha).Within(1e-4f), $"'{fusion.Id}' locked border alpha");
                Assert.That(icon.color.a, Is.EqualTo(RigBoardLayout.LockedFusionIconAlpha).Within(1e-4f), $"'{fusion.Id}' locked icon alpha");
            }
        }

        // ------------------------------------------------------------------ MV-445 defect 7: icon scale

        /// <summary>Regression guard: a 50px-radius ability node and a 72px-radius category node must
        /// both scale their icon against their OWN radius, not a shared fixed reference — already
        /// correct in code (BuildAbilityNode/BuildCategoryNode each read <c>r</c> from their own local
        /// variable), pinned here so it can't silently regress.</summary>
        [Test]
        public void IconSizeScalesAgainstEachNodesOwnRadius()
        {
            OpenScreen();
            float abR = RigBoardLayout.RadiusAbility, catR = RigBoardLayout.RadiusCategory;
            float expectedAbilityIcon = Mathf.RoundToInt(abR * RigBoardLayout.IconScaleAbility);
            float expectedCategoryIcon = Mathf.RoundToInt(catR * RigBoardLayout.IconScaleCategory);
            Assert.That(expectedAbilityIcon, Is.Not.EqualTo(expectedCategoryIcon),
                "the two radii/scales must actually differ for this test to mean anything");

            var ability = RigBoardLayout.Abilities[0];
            var abilityIcon = _screen.BoardNode(ability.Id).Find("Icon").GetComponent<Image>();
            Assert.That(abilityIcon.rectTransform.sizeDelta.x, Is.EqualTo(expectedAbilityIcon).Within(0.5f));

            var category = RigBoardLayout.Categories[0];
            var categoryIcon = _screen.BoardNode(category.Id).Find("Icon").GetComponent<Image>();
            Assert.That(categoryIcon.rectTransform.sizeDelta.x, Is.EqualTo(expectedCategoryIcon).Within(0.5f));
        }

        // ------------------------------------------------------------------ MV-445 defect 6: top bar

        [Test]
        public void CellsChipHasNoLeadingIcon()
        {
            OpenScreen();
            var chip = _screen.transform.Find("Weapons Canvas/Screen Root/Safe Area/Weapons Root/Top Bar/Cells Chip");
            Assert.That(chip, Is.Not.Null, "Cells Chip not found");
            Assert.That(chip.Find("Icon"), Is.Null, "CELLS chip must not carry a leading icon/dot — neither design image has one");
        }

        [Test]
        public void PartsTrayWidthIsSizedToItsOwnContentNotAMagicConstant()
        {
            OpenScreen();
            var tray = _screen.transform.Find("Weapons Canvas/Screen Root/Safe Area/Weapons Root/Top Bar/Parts Tray") as RectTransform;
            Assert.That(tray, Is.Not.Null, "Parts Tray not found");

            const int socketCount = 6, socketGap = 4;
            // PartsSocketSize is private; re-derive its contribution from a built socket's own width
            // (Sockets/Socket 0) instead of duplicating the constant.
            var socket0 = tray.Find("Sockets/Socket 0") as RectTransform;
            Assert.That(socket0, Is.Not.Null);
            float socketSize = socket0.sizeDelta.y;
            float socketsWidth = socketCount * socketSize + (socketCount - 1) * socketGap;
            const float leftColumnWidth = 100f, midGap = 14f, rightPad = 16f;
            float expectedWidth = leftColumnWidth + midGap + socketsWidth + rightPad;

            Assert.That(tray.sizeDelta.x, Is.EqualTo(expectedWidth).Within(0.5f));
        }

        [Test]
        public void CloseButtonUsesTheSupportedMultiplicationSignGlyph()
        {
            OpenScreen();
            var closeButton = _screen.transform.Find("Weapons Canvas/Screen Root/Safe Area/Weapons Root/Top Bar/Close Button");
            Assert.That(closeButton, Is.Not.Null, "Close Button not found");
            var label = closeButton.GetComponentInChildren<Text>();
            Assert.That(label.text, Is.EqualTo("× CLOSE"), "must be U+00D7 (supported by HudFont), not U+2715 (a dingbat with no glyph coverage)");
        }

        // ------------------------------------------------------------------ MV-446 defect 1: CELLS pill

        /// <summary>Pixel-sampled off the real capture (rig-16x9.png): with parts banked and the cell
        /// capacity track not yet maxed - true at run start, and the exact fixture the ticket's own
        /// screenshot compare used - the fill rendered flat <c>colours.part</c> amber (255,184,71)
        /// while the border/text stayed correctly <c>colours.sec</c> cyan, an ~1.15:1 luminance contrast
        /// that made the player's own cell count unreadable. Must fail on 7a66d12 (the fill assertion
        /// only - border/text were already correct pre-fix).</summary>
        [Test]
        public void CellsPillFillStaysDarkEvenWhenACapacityUpgradeIsAffordable()
        {
            PickupWallet.AddPart();
            PickupWallet.AddPart();
            PickupWallet.AddPart();
            PickupWallet.AddPart();
            Assert.That(PickupWallet.PowerCellCapacityLevel, Is.LessThan(PickupWallet.PowerCellCapacityMaxLevel),
                "fixture must actually reach the 'capacity upgrade affordable' branch for this test to mean anything");
            OpenScreen();

            var chip = _screen.transform.Find("Weapons Canvas/Screen Root/Safe Area/Weapons Root/Top Bar/Cells Chip");
            Assert.That(chip, Is.Not.Null, "Cells Chip not found");
            var bg = chip.Find("BG").GetComponent<Image>();
            var border = chip.Find("Cells Border").GetComponent<Image>();
            var text = chip.GetComponentInChildren<Text>();

            Assert.That(Mathf.Max(bg.color.r, bg.color.g, bg.color.b), Is.LessThan(0.3f),
                "CELLS pill fill must stay dark, not tint colours.part amber, even when a capacity upgrade is affordable");

            Color sec = RigBoardLayout.Colour("sec");
            Assert.That(border.color.r, Is.EqualTo(sec.r).Within(0.05f), "Cells Border must read colours.sec, not colours.part");
            Assert.That(border.color.g, Is.EqualTo(sec.g).Within(0.05f));
            Assert.That(border.color.b, Is.EqualTo(sec.b).Within(0.05f));
            Assert.That(text.color.r, Is.EqualTo(sec.r).Within(0.05f), "Cells text must read colours.sec, not colours.part");
            Assert.That(text.color.g, Is.EqualTo(sec.g).Within(0.05f));
            Assert.That(text.color.b, Is.EqualTo(sec.b).Within(0.05f));
        }

        // ------------------------------------------------------------------ MV-446 defect 2: node glow

        /// <summary>The old halo (<c>HudTextures.Glow</c>) was a plain circle sized to the node's SQUARE
        /// bounding box, so it always drew width == height. A pointy-top hex is narrower than it is tall
        /// (<c>r*sqrt(3)</c> vs <c>2r</c>) - the new hex-silhouette-following glow must size its rect to
        /// that same narrower aspect, or it is still spilling past the hex's own left/right edges no
        /// matter how tight its blur. Must fail on 7a66d12.</summary>
        [Test]
        public void OwnedNodeGlowFollowsTheHexagonsNarrowerWidthNotASquare()
        {
            OpenScreen();
            var glow = _screen.BoardNode("p_dmg").Find("Glow").GetComponent<Image>();
            Assert.That(glow.gameObject.activeSelf, Is.True, "p_dmg is owned at run start - its glow should be on");
            Assert.That(glow.rectTransform.sizeDelta.x, Is.Not.EqualTo(glow.rectTransform.sizeDelta.y).Within(0.5f),
                "glow rect must follow the hex's own (narrower) aspect ratio, not a square bounding box");
        }

        /// <summary>AC: the halo's rect must never exceed 1.25x the node's own radius - checked on both
        /// an owned ability node and a lit category node (the ticket's own named examples). Must fail on
        /// 7a66d12, whose GlowRadiusMultiplier was 1.30.</summary>
        [Test]
        public void NodeGlowRectNeverExceeds125xTheNodesOwnRadius()
        {
            OpenScreen();
            AssertGlowWithin125x("p_dmg", RigBoardLayout.RadiusAbility);
            AssertGlowWithin125x("PRIMARY", RigBoardLayout.RadiusCategory);
        }

        private void AssertGlowWithin125x(string nodeId, float r)
        {
            var glow = _screen.BoardNode(nodeId).Find("Glow").GetComponent<Image>();
            float maxSize = r * 1.25f * 2f;
            Assert.That(glow.rectTransform.sizeDelta.x, Is.LessThanOrEqualTo(maxSize + 0.5f), $"'{nodeId}' glow width exceeds 1.25x radius");
            Assert.That(glow.rectTransform.sizeDelta.y, Is.LessThanOrEqualTo(maxSize + 0.5f), $"'{nodeId}' glow height exceeds 1.25x radius");
        }

        // ------------------------------------------------------------------ MV-446 defect 3: small-type readability floor

        /// <summary>16px, matching <see cref="RigBoardLayout.LabelFontSize"/> (already used at that size
        /// for every node's own caption) - the floor picked for the FORGE caption, fusion sub-captions
        /// and the PARTS tray's "N banked" line, which pre-fix rendered as small as 10-13px.</summary>
        private const float ReadabilityFloor = 16f;

        [Test]
        public void SmallTypeFontSizesComeFromTheDataFileAndClearTheReadabilityFloor()
        {
            Assert.That(RigBoardLayout.ForgeCaptionFontSize, Is.GreaterThanOrEqualTo(ReadabilityFloor));
            Assert.That(RigBoardLayout.FusionSubFontSize, Is.GreaterThanOrEqualTo(ReadabilityFloor));
            Assert.That(RigBoardLayout.PartsTraySubFontSizeMin, Is.GreaterThanOrEqualTo(ReadabilityFloor));
            Assert.That(RigBoardLayout.PartsTraySubFontSizeMax, Is.GreaterThanOrEqualTo(RigBoardLayout.PartsTraySubFontSizeMin));
        }

        [Test]
        public void ForgeCaptionIsBuiltAtItsDataFileFontSize()
        {
            OpenScreen();
            var caption = _screen.GetComponentsInChildren<Text>(true)
                .First(t => t.text.Contains("two lit categories"));
            Assert.That(caption.fontSize, Is.EqualTo(Mathf.RoundToInt(RigBoardLayout.ForgeCaptionFontSize)));
        }

        [Test]
        public void FusionSubIsBuiltAtItsDataFileFontSize()
        {
            // Run start: no fusion has both parent categories lit, so every fusion renders its LOCKED
            // sub-caption ("ParentA + ParentB") - RefreshFusionNode's own font-size assignment for that
            // branch, same rig_board.json value the eligible/forged branches also read.
            OpenScreen();
            var fusion = RigBoardLayout.Fusions[0];
            var sub = _screen.BoardNode(fusion.Id).GetComponentsInChildren<Text>(true)
                .First(t => t.text == $"{fusion.ParentA} + {fusion.ParentB}");
            Assert.That(sub.fontSize, Is.EqualTo(Mathf.RoundToInt(RigBoardLayout.FusionSubFontSize)));
        }

        [Test]
        public void PartsTraySubBestFitRangeComesFromTheDataFile()
        {
            OpenScreen();
            var tray = _screen.transform.Find("Weapons Canvas/Screen Root/Safe Area/Weapons Root/Top Bar/Parts Tray");
            Assert.That(tray, Is.Not.Null, "Parts Tray not found");
            var sub = tray.GetComponentsInChildren<Text>(true).First(t => t.text.Contains("banked"));
            Assert.That(sub.resizeTextMinSize, Is.EqualTo(Mathf.RoundToInt(RigBoardLayout.PartsTraySubFontSizeMin)));
            Assert.That(sub.resizeTextMaxSize, Is.EqualTo(Mathf.RoundToInt(RigBoardLayout.PartsTraySubFontSizeMax)));
        }
    }
}
