using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-585 — a follow-on from MV-543 (which fixed the Force Field label's CONTRAST, not its SIZE).
    /// <c>resizeTextMaxSize</c> was hard-capped at 22, which best-fit will never draw past however large
    /// the box is made, so the percentage read unreadably small on an iPhone. Fails on 3c8... (pre-fix,
    /// this branch's base commit, <c>resizeTextMaxSize = 22</c>): FIELD resolves to ~8pt at iPhone-landscape
    /// scale, under the 11pt floor this test asserts. Sole guard on this defect; do not cull (MV-465).
    /// Cull exemption: MV-585.
    /// </summary>
    public sealed class MV585ForceFieldLabelFontSizeTests
    {
        // Same "1 reference pixel = deviceHeightPt / RefH" conversion RigBoardChromeTests established for
        // THE RIG board's own 11pt legibility floor (MV-472) — HudController's canvas is likewise built at
        // RefH=1080 (ScaleWithScreenSize, match-by-height), so the maths carries over unchanged.
        private const float IPhoneHeightPt = 393f;
        private const float RefH = 1080f;
        private const float PhysicalScale = IPhoneHeightPt / RefH;

        // HudTextures.TechRings(160, 3)'s own baked geometry (Assets/_Project/Code/Runtime/UI/HudTextures.cs):
        // outer = size*0.5-1, band i sits at outer*i/rings with a ~2.2px half-width falloff. The button's
        // ring sprite is stretched (Stretch, zero padding) across the full HydroButtonSize square, so the
        // outermost band (i=rings) — the rim that visually frames the round button — maps to this inner
        // radius in the label's own local units.
        private const float RingTextureSize = 160f;
        private const float RingCount = 3f;
        private const float RingBandHalfWidthPx = 2.2f;

        [Test]
        public void ForceFieldLabelFillsTheRingLegiblyAtEveryStringItEverRenders()
        {
            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            try
            {
                var buttonRoot = FindRect(hudGo, "Force Field Button");
                Assert.That(buttonRoot, Is.Not.Null, "fixture: Force Field Button must exist");

                var label = buttonRoot.GetComponentInChildren<Text>(true);
                Assert.That(label, Is.Not.Null, "fixture: Force Field Button must carry a label");

                // AC3: the button itself must not have grown to make room.
                float hydroButtonSize = (float)typeof(HudController)
                    .GetField("HydroButtonSize", BindingFlags.NonPublic | BindingFlags.Static)
                    .GetValue(null);
                Assert.That(hydroButtonSize, Is.EqualTo(110f), "AC3: HydroButtonSize must stay at 110");

                float outerPx = RingTextureSize * 0.5f - 1f;
                float outerBandPx = outerPx * RingCount / RingCount;
                float ringInnerRadiusPx = outerBandPx - RingBandHalfWidthPx;
                float textureToLocalScale = hydroButtonSize / RingTextureSize;
                float ringInnerRadius = ringInnerRadiusPx * textureToLocalScale;

                // AC4: colour + backing treatment MV-543 shipped must be untouched by this ticket.
                Color expectedInk = (Color)typeof(HudController)
                    .GetField("ForceFieldLabelInk", BindingFlags.NonPublic | BindingFlags.Static)
                    .GetValue(null);
                Color expectedBoneWhite = (Color)typeof(HudController)
                    .GetField("BoneWhite", BindingFlags.NonPublic | BindingFlags.Static)
                    .GetValue(null);
                Assert.That(label.color, Is.EqualTo(expectedInk), "AC4: label ink colour must be unchanged from MV-543");
                var outline = label.GetComponent<Outline>();
                Assert.That(outline, Is.Not.Null, "AC4: label must still carry its MV-543 Outline");
                Assert.That(outline.effectColor, Is.EqualTo(expectedBoneWhite), "AC4: outline colour must be unchanged from MV-543");

                string originalText = label.text;
                int savedFontSize = label.fontSize;
                bool savedBestFit = label.resizeTextForBestFit;

                foreach (string s in new[] { "1%", "100%", "FIELD" })
                {
                    label.text = s;
                    label.resizeTextForBestFit = true;
                    var bestFitSettings = label.GetGenerationSettings(label.rectTransform.rect.size);
                    label.cachedTextGenerator.Populate(s, bestFitSettings);
                    float resolvedSize = label.cachedTextGenerator.fontSizeUsedForBestFit;

                    // AC1: only FIELD is the widest-overall case the ticket names for the legibility floor.
                    if (s == "FIELD")
                    {
                        float pt = resolvedSize * PhysicalScale;
                        Assert.That(pt, Is.GreaterThanOrEqualTo(11f),
                            $"AC1: FIELD resolves to {pt:0.0}pt at iPhone-landscape scale — under the 11pt floor");
                    }

                    // AC2: measure the actual rendered bounds at the resolved (fixed) size and check they
                    // clear the ring's inner radius — the same Text.preferredWidth/Height Unity's own
                    // renderer uses, just pinned to the size best-fit already chose instead of re-fitting.
                    label.resizeTextForBestFit = false;
                    label.fontSize = Mathf.RoundToInt(resolvedSize);
                    float halfWidth = label.preferredWidth * 0.5f;
                    float halfHeight = label.preferredHeight * 0.5f;
                    float halfDiagonal = Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight);

                    Assert.That(halfDiagonal, Is.LessThanOrEqualTo(ringInnerRadius),
                        $"AC2: '{s}' at resolved size {resolvedSize:0.0} has half-diagonal {halfDiagonal:0.0}, " +
                        $"past the ring's inner radius {ringInnerRadius:0.0} — it overlaps the ring stroke");
                }

                label.text = originalText;
                label.fontSize = savedFontSize;
                label.resizeTextForBestFit = savedBestFit;
            }
            finally
            {
                Object.DestroyImmediate(hudGo);
            }
        }

        private static RectTransform FindRect(GameObject go, string name)
        {
            foreach (var rt in go.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt;
            return null;
        }

        private static void InvokeLifecycle(Object component, string methodName)
        {
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        }
    }
}
