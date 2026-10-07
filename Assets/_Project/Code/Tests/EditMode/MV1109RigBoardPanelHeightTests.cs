using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1109 — THE RIG's family background panels now size their bottom edge from the real content
    /// they hold (lowest label, since labels sit below both a node's own hex and its cost chip) instead
    /// of a fixed <c>regionRect.h</c>. The fixed height was sized for standard mode's own row schedule
    /// and silently reused for phone mode's much deeper one (same height, but phone's own panel top sits
    /// at 0 instead of standard's 150) — on Lee's own device (phone mode), World 1's SPREAD/FIRE RATE
    /// labels and World 3's HOLD/RADIUS/CLUSTER/CELL MAGNETO and SUPPORT's SPEED/COST/SLOTS labels all
    /// rendered below their own family's tinted panel, on plain black. Sole guard on the fix; do not cull.
    /// </summary>
    public sealed class MV1109RigBoardPanelHeightTests
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
            RigBoard.UseWorld(0);
            RigBoardLayout.ResetForTests();
        }

        /// <summary>Every family's own panel must cover every one of its own ability nodes' hex, cost
        /// chip and name label — the label is the real floor (see this class's own doc comment above) —
        /// and no two family panels may overlap, across every shipped board (World 1/2/3) and a
        /// representative phone + two standard aspects (same three MV-594's own width-axis test already
        /// uses: 2.13 phone, 1.78/1.33 standard).
        ///
        /// Fails on base commit d30d293: at aspect 2.13 (phone mode), every family's panel bottom sits at
        /// a fixed <c>RegionRectYPhone(0) + RegionRectH(706) = 706</c> ref px, while phone mode's own
        /// tier3 row (<c>Tier3YPhone = 740</c>) plus its ability radius (64) already sits past that before
        /// any label offset is even added — World 1's own PRIMARY panel fails on exactly that first:
        /// "'p_spr' hex bottom escapes 'PRIMARY' panel (world 0, aspect 2.13) Expected: greater than or
        /// equal to -367.0f But was: -464.0f".</summary>
        [Test]
        public void FamilyPanelsCoverEveryNodeLabelAndCostChip_AcrossWorldsAndAspects()
        {
            _screen.Open();

            // MV-516/MV-594 idiom: pin scaleFactor to 1 so GetWorldCorners reads back directly in ref px.
            var scaler = _screen.RootCanvas.GetComponent<CanvasScaler>();
            scaler.enabled = false;
            _screen.RootCanvas.scaleFactor = 1f;

            Vector2 WorldYRange(RectTransform rt)
            {
                var c = new Vector3[4];
                rt.GetWorldCorners(c);
                return new Vector2(Mathf.Min(c[0].y, c[2].y), Mathf.Max(c[0].y, c[2].y));
            }

            Vector2 WorldXRange(RectTransform rt)
            {
                var c = new Vector3[4];
                rt.GetWorldCorners(c);
                return new Vector2(Mathf.Min(c[0].x, c[2].x), Mathf.Max(c[0].x, c[2].x));
            }

            float[] aspects = { 2.13f, 1.78f, 1.33f };
            int[] worlds = { 0, 1, 2 };

            foreach (int world in worlds)
            {
                RigBoard.UseWorld(world);
                _screen.RebuildBoard();

                foreach (float aspect in aspects)
                {
                    _screen.ApplyBoardScale(aspect);
                    bool phoneMode = WeaponsScreen.IsPhoneLayout(aspect);
                    var categories = phoneMode ? RigBoardLayout.PhoneCategories : RigBoardLayout.Categories;
                    var abilities = phoneMode ? RigBoardLayout.PhoneAbilities : RigBoardLayout.Abilities;

                    foreach (var cat in categories)
                    {
                        var panel = _screen.CategoryPanel(cat.Id);
                        Assert.That(panel, Is.Not.Null, $"no panel for '{cat.Id}' world {world} aspect {aspect}");
                        var panelYRange = WorldYRange(panel.rectTransform);

                        foreach (var ab in abilities)
                        {
                            if (ab.Category != cat.Id) continue;

                            // WorldYRange's .x is the MIN world Y (this UI's +Y-up space, so the
                            // screen-BOTTOM edge of the rect); .y is the MAX (the screen-TOP edge).
                            var hexRange = WorldYRange(_screen.BoardNode(ab.Id));
                            Assert.That(hexRange.x, Is.GreaterThanOrEqualTo(panelYRange.x - 1f),
                                $"'{ab.Id}' hex bottom escapes '{cat.Id}' panel (world {world}, aspect {aspect})");
                            Assert.That(hexRange.y, Is.LessThanOrEqualTo(panelYRange.y + 1f),
                                $"'{ab.Id}' hex top escapes '{cat.Id}' panel (world {world}, aspect {aspect})");

                            var labelRange = WorldYRange(_screen.NodeLabel(ab.Id).rectTransform);
                            Assert.That(labelRange.x, Is.GreaterThanOrEqualTo(panelYRange.x - 1f),
                                $"'{ab.Id}' label bottom ({labelRange.x:0.0}) escapes '{cat.Id}' panel bottom ({panelYRange.x:0.0}) (world {world}, aspect {aspect})");
                            Assert.That(labelRange.y, Is.LessThanOrEqualTo(panelYRange.y + 1f),
                                $"'{ab.Id}' label top escapes '{cat.Id}' panel (world {world}, aspect {aspect})");

                            var chipIconRange = WorldYRange(_screen.NodeCostIcon(ab.Id).rectTransform);
                            Assert.That(chipIconRange.x, Is.GreaterThanOrEqualTo(panelYRange.x - 1f),
                                $"'{ab.Id}' cost icon escapes '{cat.Id}' panel (world {world}, aspect {aspect})");
                            var chipTextRange = WorldYRange(_screen.NodeCostText(ab.Id).rectTransform);
                            Assert.That(chipTextRange.x, Is.GreaterThanOrEqualTo(panelYRange.x - 1f),
                                $"'{ab.Id}' cost text escapes '{cat.Id}' panel (world {world}, aspect {aspect})");
                        }
                    }

                    // No two family panels may overlap (AABB over both axes).
                    for (int i = 0; i < categories.Count; i++)
                    {
                        var a = _screen.CategoryPanel(categories[i].Id).rectTransform;
                        var aX = WorldXRange(a);
                        var aY = WorldYRange(a);
                        for (int j = i + 1; j < categories.Count; j++)
                        {
                            var b = _screen.CategoryPanel(categories[j].Id).rectTransform;
                            var bX = WorldXRange(b);
                            var bY = WorldYRange(b);
                            bool overlapX = aX.x < bX.y - 1f && bX.x < aX.y - 1f;
                            bool overlapY = aY.x < bY.y - 1f && bY.x < aY.y - 1f;
                            Assert.That(overlapX && overlapY, Is.False,
                                $"'{categories[i].Id}' and '{categories[j].Id}' panels overlap (world {world}, aspect {aspect})");
                        }
                    }
                }
            }
        }
    }
}
