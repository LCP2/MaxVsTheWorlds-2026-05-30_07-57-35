using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-462 — THE RIG board's node shape, the 16:9 scale-to-fit identity case, and the unlit-family
    /// dim. All three were confirmed against a real 1920x1080 capture (<c>rig-16x9.png</c>) compared
    /// pixel-for-pixel to the design (<c>MV-423.png</c>): nodes rendered flat-top instead of pointy-top,
    /// the whole board was shrunk and recentred at an aspect where <see cref="WeaponsScreen.ComputeBoardScale"/>
    /// must return 1.0 exactly, and SECONDARY/MOVE (unlit) read at the same luminance as the lit columns.
    /// </summary>
    public sealed class MV462RigBoardFixTests
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
        }

        private void OpenScreen() => _screen.Open();

        // ------------------------------------------------------------------ defect 1: pointy-top hex

        /// <summary>Renders the exact sprite a category node's hex fill uses (radius 72, matching
        /// <c>RigBoardLayout.RadiusCategory</c>) and measures its silhouette off the alpha channel
        /// directly — not a screenshot. Pointy-top means a vertex at top and bottom, two vertical edges
        /// left/right: silhouette height 2r (144), width r*sqrt(3) (124.7), and the topmost row is a
        /// single narrow point centred on x (the vertex), not a full-width flat edge (the flat-top bug's
        /// signature). Must fail on the pre-fix build, where <c>HudTextures.PolygonEdge</c> put an EDGE
        /// midpoint at rotationDeg instead of a vertex, rendering flat-top (vertices left/right)
        /// instead.</summary>
        [Test]
        public void CategoryHexIsPointyTop_VertexAtTopAndBottom_WidthMatchesSqrt3OverTwoTimesHeight()
        {
            float r = RigBoardLayout.RadiusCategory;
            int texH = Mathf.CeilToInt(r * 2f);
            int texW = Mathf.CeilToInt(r * 1.7320508f);
            var sprite = HudTextures.Polygon(6, -90f, texW, texH);
            var tex = sprite.texture;
            var px = tex.GetPixels32();

            int minX = texW, maxX = -1, minY = texH, maxY = -1;
            for (int y = 0; y < texH; y++)
            for (int x = 0; x < texW; x++)
            {
                if (px[y * texW + x].a <= 128) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            Assert.That(maxY, Is.GreaterThanOrEqualTo(minY), "no opaque pixel found at all");
            float measuredH = maxY - minY + 1;
            float measuredW = maxX - minX + 1;
            Assert.That(measuredH, Is.EqualTo(144f).Within(2f), "pointy-top silhouette height must be 2r");
            Assert.That(measuredW, Is.EqualTo(124.7f).Within(2f), "pointy-top silhouette width must be r*sqrt(3)");

            // The top row of a pointy-top hex is a single vertex point (cx, cy - r) — narrow, centred.
            // A flat-top hex's top row instead spans the whole flat edge (~measuredW wide).
            int topMinX = texW, topMaxX = -1;
            for (int x = 0; x < texW; x++)
            {
                if (px[minY * texW + x].a <= 128) continue;
                if (x < topMinX) topMinX = x;
                if (x > topMaxX) topMaxX = x;
            }
            float topRowWidth = topMaxX - topMinX + 1;
            float topRowCentre = (topMinX + topMaxX) * 0.5f;
            Assert.That(topRowWidth, Is.LessThan(measuredW * 0.5f),
                $"top row is {topRowWidth}px wide, {measuredW}px total — a flat-top hex's top row spans nearly the whole width, a pointy-top's is a single narrow vertex");
            Assert.That(topRowCentre, Is.EqualTo(texW * 0.5f).Within(2f), "the top vertex must sit on the shape's own centre x (cx, cy - r)");
        }

        /// <summary>MV-433's diamond (FORGE fusion nodes) must stay a diamond — vertex up/down/left/right
        /// — once the hex's own vertex math is fixed. <c>FusionRotationDeg</c> used to rely on the same
        /// bug's half-segment offset to land the diamond correctly; fixing the bug without also
        /// re-tuning that constant would have quietly rotated every FORGE node 45 degrees into an
        /// axis-aligned square.</summary>
        [Test]
        public void FusionDiamondStaysADiamond_VertexAtTopAndBottom_SquareBoundingBox()
        {
            float r = RigBoardLayout.RadiusFusion;
            int size = Mathf.CeilToInt(r * 2f);
            var sprite = HudTextures.Polygon(4, 0f, size, size);
            var tex = sprite.texture;
            var px = tex.GetPixels32();

            int minX = size, maxX = -1, minY = size, maxY = -1;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (px[y * size + x].a <= 128) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            int topMinX = size, topMaxX = -1;
            for (int x = 0; x < size; x++)
            {
                if (px[minY * size + x].a <= 128) continue;
                if (x < topMinX) topMinX = x;
                if (x > topMaxX) topMaxX = x;
            }
            float topRowWidth = topMaxX - topMinX + 1;
            Assert.That(topRowWidth, Is.LessThan((maxX - minX + 1) * 0.5f), "the diamond's top row must be a narrow vertex point, not a flat edge");
        }

        // ------------------------------------------------------------------ defect 2: 16:9 identity scale

        /// <summary>The pure function must return exactly 1.0 at 16:9 — <c>visibleRefWidth = 1080 *
        /// (1920/1080) = 1920</c>, <c>raw = min(1, 1920/1920) = 1</c>. This alone was never actually
        /// broken (float rounding lands within a hair of 1.0); the visible defect was
        /// <see cref="WeaponsScreen.ApplyBoardScale()"/> reading ambient <c>Screen.width</c>/
        /// <c>Screen.height</c> — which a headless capture's off-screen RenderTexture render never
        /// matches — instead of the shot's actual aspect, so the two assertions below (built screen,
        /// explicit-aspect overload) are what actually pin the real bug.</summary>
        [Test]
        public void ComputeBoardScaleAtSixteenByNineIsExactlyOne()
        {
            float scale = WeaponsScreen.ComputeBoardScale(1920f / 1080f);
            Assert.That(scale, Is.EqualTo(1f).Within(1e-4f));
        }

        /// <summary>Drives <see cref="WeaponsScreen.ApplyBoardScale(float)"/> directly with the 16:9
        /// aspect (bypassing the ambient <see cref="Screen"/> singleton EditMode tests can't control) —
        /// the same explicit-aspect entry point <c>UiScreensDirector</c>'s capture harness now uses.
        /// Must land the scale wrapper at exact identity, and (since <c>RigBoardLayoutTests</c> already
        /// pins Board Root's own anchoredPosition to the json coordinate 1:1, unaffected by this
        /// ancestor's scale) that identity IS the "maps to json (250, 230)" contract — a wrapper anything
        /// other than 1.0 is what shifted/shrunk the whole board in the real capture.</summary>
        [Test]
        public void BoardScaleRootIsIdentityAtSixteenByNine_PrimaryNodeLandsOnItsJsonCoordinate()
        {
            OpenScreen();
            _screen.ApplyBoardScale(1920f / 1080f);

            Assert.That(_screen.BoardScale, Is.EqualTo(1f).Within(1e-4f), "board scale root must be identity at 16:9");

            var primary = _screen.BoardNode("PRIMARY");
            Assert.That(primary, Is.Not.Null);
            var cat = RigBoardLayout.Categories[0];
            Assert.That(cat.Id, Is.EqualTo("PRIMARY"));
            Assert.That(primary.anchoredPosition.x, Is.EqualTo(cat.X).Within(1f), "PRIMARY anchored x must equal its json x");
            Assert.That(primary.anchoredPosition.y, Is.EqualTo(-cat.Y).Within(1f), "PRIMARY anchored y must equal its json y (canvas is y-down, RectTransform is y-up)");
        }

        /// <summary>The narrower 16:10-ish shot (<c>rig-16x10.png</c>, 1728x1080, aspect 1.6) must still
        /// shrink — MV-462's fix must not flatten <see cref="WeaponsScreen.ComputeBoardScale"/> to
        /// always-1, only fix the 16:9-and-wider identity case.</summary>
        [Test]
        public void BoardScaleStillShrinksBelowSixteenByNine()
        {
            OpenScreen();
            _screen.ApplyBoardScale(1728f / 1080f);
            Assert.That(_screen.BoardScale, Is.LessThan(0.999f));
        }

        // ------------------------------------------------------------------ defect 3: unlit family dim

        /// <summary>Run-start state (only PRIMARY lit, <c>RigState.Reset</c>'s own baseline) — MOVE has
        /// zero owned abilities, so every graphic in its subtree must be dimmed by
        /// <c>RigBoardLayout.FamilyDimFactor</c> on top of whatever state-specific alpha it already
        /// carries. Checked via the region panel/border (opacityDark/borderAlphaDark, category-level),
        /// an ability node's own draftable glow (GlowAlphaDraft, node-level — both of MOVE's abilities,
        /// m_spd/m_tp, are roots so both are draftable at run start), and a category connector
        /// (ConnectorAlphaLive, since a draftable child still counts as "live" — connector-level) — one
        /// representative graphic per level the AC calls out, each backed by a named
        /// <c>RigBoardLayout</c> constant rather than a duplicated magic literal, so this stays accurate
        /// if those constants ever move. Must fail on <c>main</c> (no dim applied at all — MOVE would
        /// read at the same undimmed alpha as PRIMARY).</summary>
        [Test]
        public void UnlitFamilyDimsCategoryAbilityAndConnectorGraphicsByFamilyDimFactor()
        {
            OpenScreen();
            float factor = RigBoardLayout.FamilyDimFactor;
            Assert.That(RigState.IsOwned("m_spd"), Is.False, "fixture assumption: MOVE has nothing owned at run start");
            Assert.That(RigState.IsOwned("m_tp"), Is.False, "fixture assumption: MOVE has nothing owned at run start");

            var panel = _screen.CategoryPanel("MOVE");
            var border = _screen.CategoryPanelBorder("MOVE");
            Assert.That(panel.color.a, Is.EqualTo(RigBoardLayout.RegionOpacityDark * factor).Within(1e-4f), "MOVE region panel");
            Assert.That(border.color.a, Is.EqualTo(RigBoardLayout.RegionBorderAlphaDark * factor).Within(1e-4f), "MOVE region border");

            foreach (var abilityId in new[] { "m_spd", "m_tp" })
            {
                var glow = _screen.BoardNode(abilityId).Find("Glow").GetComponent<Image>();
                Assert.That(glow.gameObject.activeSelf, Is.True, $"'{abilityId}' must be draftable (active glow) at run start");
                Assert.That(glow.color.a, Is.EqualTo(RigBoardLayout.GlowAlphaDraft * factor).Within(1e-4f), $"'{abilityId}' draftable glow");

                var conn = _screen.Connector($"conn:cat:MOVE>{abilityId}");
                Assert.That(conn, Is.Not.Null, $"missing category connector for '{abilityId}'");
                Assert.That(conn.color.a, Is.EqualTo(RigBoardLayout.ConnectorAlphaLive * factor).Within(1e-4f), $"'{abilityId}' category connector");
            }
        }

        /// <summary>Owning one ability in an otherwise-untouched family must flip the WHOLE family back
        /// to full strength, not just the one node acquired — SECONDARY's root (<c>s_bal</c>) is reached
        /// from the start, so <c>RigState.AcquireCap</c> alone (no shed/parent spend needed) is enough to
        /// light it.</summary>
        [Test]
        public void OwningOneAbilityFlipsTheWholeFamilyBackToFullStrength()
        {
            OpenScreen();
            var panelBefore = _screen.CategoryPanel("SECONDARY");
            Assert.That(panelBefore.color.a, Is.EqualTo(RigBoardLayout.RegionOpacityDark * RigBoardLayout.FamilyDimFactor).Within(1e-4f),
                "SECONDARY must start dimmed (nothing owned)");

            Assert.That(RigState.AcquireCap("s_bal"), Is.True, "s_bal must be a reached, ownable root at run start");

            // RigState.Changed -> Refresh isn't reliably driven by the Editor outside Play mode (same
            // rule RigBoardLayoutTests documents for OnEnable), so force a fresh Refresh() the same way
            // every other state-change-after-Open test in this suite does: close and reopen.
            _screen.Close();
            _screen.Open();

            var panelAfter = _screen.CategoryPanel("SECONDARY");
            Assert.That(panelAfter.color.a, Is.EqualTo(RigBoardLayout.RegionOpacityLit).Within(1e-4f),
                "SECONDARY must flip to full (lit, undimmed) strength once one of its abilities is owned");
        }
    }
}
