using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-620 — the node cost tag (icon + number) was the one label on THE RIG board never routed
    /// through <see cref="RigBoardLayout"/>'s phone/standard font ladder: hardcoded at 14, rendering at
    /// 5.1pt on phone (14*393/1080) and 9.6pt on iPad mini (14*744/1080), both under the project's
    /// 11pt floor and the reason Lee couldn't read the number or its glyph in the 29 Aug playtest. Sole
    /// guard on this defect; do not cull. Testing policy (MV-465): one new test, proven to fail on base
    /// commit f272e17 (main HEAD before this ticket) — that commit has neither
    /// <see cref="RigBoardLayout.CostFontSize"/>/<see cref="RigBoardLayout.CostFontSizePhone"/> nor
    /// <see cref="WeaponsScreen.NodePillBg"/>, so this file fails to even compile there.
    ///
    /// AC7 (swap the cost-tag glyph back to the PowerCell icon for both owned and unowned states) is
    /// deliberately NOT implemented or asserted here. <c>WeaponHudIcons.PowerCell</c> caches its sprite
    /// under a single fixed key regardless of the requested size, so both states would resolve to the
    /// exact same <c>Sprite</c> reference — which directly breaks <c>MV520RigCostAlwaysVisibleTests</c>'s
    /// own guarded assertion that the unlock and upgrade glyphs "must be two distinct sprites,
    /// distinguishable without reading the number" (its AC2, marked sole-guard/do-not-cull). Per this
    /// ticket's own AC8 instruction — "if one genuinely conflicts with this change, stop and comment
    /// rather than editing it" — <c>WeaponsScreen.cs</c>'s glyph assignment at the cost-tag call site is
    /// left unchanged; flagged in the hand-off comment for Lee's call on which regression should win.
    /// </summary>
    public sealed class MV620RigCostTagLegibilityTests
    {
        private GameObject _go;
        private WeaponsScreen _screen;

        private const float PhoneHeightPt = 393f;
        private const float TabletHeightPt = 744f;
        private const float RefH = 1080f;
        private const float LegibilityFloorPt = 11f;

        private const float StandardAspect = 1920f / 1080f;
        private const float PhoneAspect = 2340f / 1080f;

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
        public void CostTagClearsTheLegibilityFloor_OnTheFontLadder_WithNoClippingOrOverlap()
        {
            // ---------------------------------------------------------------- AC1/AC2: rendered-pt floor
            float phoneRenderedPt = RigBoardLayout.CostFontSizePhone * (PhoneHeightPt / RefH);
            Assert.That(phoneRenderedPt, Is.GreaterThanOrEqualTo(LegibilityFloorPt),
                $"CostFontSizePhone ({RigBoardLayout.CostFontSizePhone}) renders at {phoneRenderedPt:0.0}pt on a real iPhone, under the {LegibilityFloorPt}pt floor");

            float tabletRenderedPt = RigBoardLayout.CostFontSize * (TabletHeightPt / RefH);
            Assert.That(tabletRenderedPt, Is.GreaterThanOrEqualTo(LegibilityFloorPt),
                $"CostFontSize ({RigBoardLayout.CostFontSize}) renders at {tabletRenderedPt:0.0}pt on iPad mini, under the {LegibilityFloorPt}pt floor");

            // ---------------------------------------------------------------- AC3: parity with the level pill
            Assert.That(RigBoardLayout.CostFontSizePhone, Is.GreaterThanOrEqualTo(RigBoardLayout.LevelPillFontSizePhone),
                "the cost tag must never render smaller than the level pill beside it");

            // ---------------------------------------------------------------- AC4: the literal is gone — built value, phone vs standard
            _screen.Open();
            Assert.That(RigState.IsOwned("p_dmg"), Is.True, "fixture: p_dmg (DAMAGE) is owned at run start");

            _screen.ApplyBoardScale(StandardAspect);
            var standardCostText = _screen.NodeCostText("p_dmg");
            Assert.That(standardCostText, Is.Not.Null, "p_dmg built no cost-text component in standard mode");
            int standardFontSize = standardCostText.fontSize;

            _screen.ApplyBoardScale(PhoneAspect);
            var phoneCostText = _screen.NodeCostText("p_dmg");
            Assert.That(phoneCostText, Is.Not.Null, "p_dmg built no cost-text component in phone mode");
            Assert.That(phoneCostText.fontSize, Is.EqualTo(Mathf.RoundToInt(RigBoardLayout.CostFontSizePhone)),
                $"phone-mode cost text must resolve to CostFontSizePhone ({RigBoardLayout.CostFontSizePhone}), not the old hardcoded 14");
            Assert.That(phoneCostText.fontSize, Is.GreaterThan(standardFontSize),
                "phone-mode cost text must render strictly larger than standard mode's own");

            // ---------------------------------------------------------------- AC5: no clipping at the widest cost ("20"), both modes
            AssertNoClip(phoneCostText, "phone");
            _screen.ApplyBoardScale(StandardAspect);
            AssertNoClip(_screen.NodeCostText("p_dmg"), "standard");

            // ---------------------------------------------------------------- AC6: no overlap, tag sits strictly in the pill/label collar, both modes
            AssertTagFitsCollar("standard");
            _screen.ApplyBoardScale(PhoneAspect);
            AssertTagFitsCollar("phone");
        }

        private static void AssertNoClip(Text text, string modeName)
        {
            text.text = "20";
            Assert.That(text.rectTransform.sizeDelta.x, Is.GreaterThanOrEqualTo(text.preferredWidth),
                $"{modeName} cost text box ({text.rectTransform.sizeDelta.x:0.0}px) is narrower than \"20\" needs ({text.preferredWidth:0.0}px) — will clip");
        }

        private void AssertTagFitsCollar(string modeName)
        {
            var icon = _screen.NodeCostIcon("p_dmg");
            var text = _screen.NodeCostText("p_dmg");
            var pill = _screen.NodePillBg("p_dmg");
            var label = _screen.NodeLabel("p_dmg");

            Rect iconRect = RectInParent(icon.rectTransform);
            Rect textRect = RectInParent(text.rectTransform);
            Assert.That(iconRect.Overlaps(textRect), Is.False, $"{modeName}: cost icon and cost text rects overlap");

            float tagTop = Mathf.Max(iconRect.yMax, textRect.yMax);
            float tagBottom = Mathf.Min(iconRect.yMin, textRect.yMin);
            float pillBottom = pill.rectTransform.anchoredPosition.y - pill.rectTransform.sizeDelta.y * 0.5f;
            float labelTop = label.rectTransform.anchoredPosition.y + 12f;

            Assert.That(tagTop, Is.LessThanOrEqualTo(pillBottom + 0.01f),
                $"{modeName}: cost tag's top ({tagTop:0.0}) pokes above the level pill's bottom edge ({pillBottom:0.0})");
            Assert.That(tagBottom, Is.GreaterThanOrEqualTo(labelTop - 0.01f),
                $"{modeName}: cost tag's bottom ({tagBottom:0.0}) pokes below the label's top edge ({labelTop:0.0})");
        }

        private static Rect RectInParent(RectTransform rt)
        {
            Vector2 pos = rt.anchoredPosition;
            Vector2 size = rt.sizeDelta;
            return new Rect(pos.x - size.x * 0.5f, pos.y - size.y * 0.5f, size.x, size.y);
        }
    }
}
