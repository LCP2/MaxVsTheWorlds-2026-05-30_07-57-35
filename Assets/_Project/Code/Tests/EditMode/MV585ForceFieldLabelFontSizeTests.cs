using System.Reflection;
using NUnit.Framework;
using UnityEditor;
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
    ///
    /// MV-593 — this fixture measures glyph metrics with <see cref="DeterministicMeasurementFont"/>
    /// instead of the production <c>HudFont.Get()</c>. Confirmed via CI job log (QA #92/#93) and a
    /// local diagnostic: <c>HudFont.Get()</c> resolves Unity's <c>LegacyRuntime.ttf</c> to an OS-linked
    /// dynamic font (<c>dynamic=true</c>, <c>fontNames=[Arial]</c>) — real Arial on Windows, but the
    /// GameCI Ubuntu runner has no Arial installed and substitutes a font with a visibly taller line
    /// height, which pushed 'FIELD' at the resolved size 32 to a 54.6px half-diagonal against the
    /// 52.8px ring radius (CI) vs 49.9px locally — a genuine cross-OS glyph-metric difference, not a
    /// flake. HudController.cs still ships the real HudFont for players; this test only needs a font
    /// whose glyph metrics are identical on every machine that runs it, so the box/maxSize/ring math
    /// MV-585 established stays under guard without depending on whichever font a given OS happens to
    /// substitute for "Arial". Do not revert this to <c>HudFont.Get()</c>.
    ///
    /// MV-593 also found <c>TextGenerator.fontSizeUsedForBestFit</c> is a no-op under
    /// <c>-batchmode -nographics</c>: a manual <c>Populate()</c> call always echoed back
    /// <c>GenerationSettings.fontSize</c> (the <c>AddText</c> base size, 32) verbatim, ignoring
    /// <c>resizeTextMaxSize</c> entirely — confirmed by shrinking <c>resizeTextMaxSize</c> to 20 and
    /// seeing the resolved value stay 32 on both this machine and (by the same headless flags) CI.
    /// <c>Text.preferredWidth</c>/<c>preferredHeight</c> do NOT have this problem — verified they scale
    /// correctly headless across font sizes 10..32 — so this fixture now runs its own best-fit search
    /// (<see cref="ResolveBestFitSize"/>) against those instead of trusting the engine's own search.
    /// </summary>
    public sealed class MV585ForceFieldLabelFontSizeTests
    {
        // Roboto-Regular.ttf ships inside com.unity.searcher (Apache 2.0), a package Unity's own Editor
        // UI depends on. Packages/packages-lock.json pins it to 4.9.4 for every checkout — local and CI
        // alike — so this resolves to byte-identical glyph outlines everywhere, unlike an OS-linked font.
        private const string DeterministicMeasurementFontPath =
            "Packages/com.unity.searcher/Editor/Resources/FlatSkin/Font/Roboto-Regular.ttf";

        private static Font DeterministicMeasurementFont =>
            AssetDatabase.LoadAssetAtPath<Font>(DeterministicMeasurementFontPath);


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

                var measurementFont = DeterministicMeasurementFont;
                Assert.That(measurementFont, Is.Not.Null,
                    $"fixture: deterministic measurement font must load from {DeterministicMeasurementFontPath}");

                string originalText = label.text;
                int savedFontSize = label.fontSize;
                bool savedBestFit = label.resizeTextForBestFit;
                Font originalFont = label.font;
                label.font = measurementFont;

                int maxSize = label.resizeTextMaxSize;
                int minSize = label.resizeTextMinSize;
                float boxWidth = label.rectTransform.rect.width;
                float boxHeight = label.rectTransform.rect.height;
                label.resizeTextForBestFit = false;

                foreach (string s in new[] { "1%", "100%", "FIELD" })
                {
                    label.text = s;
                    int resolvedSize = ResolveBestFitSize(label, s, maxSize, minSize, boxWidth, boxHeight);
                    label.fontSize = resolvedSize;
                    float halfWidth = label.preferredWidth * 0.5f;
                    float halfHeight = label.preferredHeight * 0.5f;

                    // AC1: only FIELD is the widest-overall case the ticket names for the legibility floor.
                    if (s == "FIELD")
                    {
                        float pt = resolvedSize * PhysicalScale;
                        Assert.That(pt, Is.GreaterThanOrEqualTo(11f),
                            $"AC1: FIELD resolves to {pt:0.0}pt at iPhone-landscape scale — under the 11pt floor");
                    }

                    // AC2: measure the actual rendered bounds at the resolved (fixed) size and check they
                    // clear the ring's inner radius.
                    float halfDiagonal = Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight);

                    Assert.That(halfDiagonal, Is.LessThanOrEqualTo(ringInnerRadius),
                        $"AC2: '{s}' at resolved size {resolvedSize} has half-diagonal {halfDiagonal:0.0}, " +
                        $"past the ring's inner radius {ringInnerRadius:0.0} — it overlaps the ring stroke");
                }

                label.text = originalText;
                label.fontSize = savedFontSize;
                label.resizeTextForBestFit = savedBestFit;
                label.font = originalFont;
            }
            finally
            {
                Object.DestroyImmediate(hudGo);
            }
        }

        // Reimplements best-fit's own search (largest size that doesn't overflow the box) against
        // Text.preferredWidth/preferredHeight, since TextGenerator.fontSizeUsedForBestFit doesn't
        // iterate under -batchmode -nographics (see class doc). Mirrors the engine's own contract:
        // scan down from resizeTextMaxSize, first size that fits both axes wins; resizeTextMinSize
        // is the floor even if nothing fits (matching Unity's own best-fit clamping behaviour).
        private static int ResolveBestFitSize(Text label, string text, int maxSize, int minSize, float boxWidth, float boxHeight)
        {
            for (int candidate = maxSize; candidate > minSize; candidate--)
            {
                label.fontSize = candidate;
                if (label.preferredWidth <= boxWidth && label.preferredHeight <= boxHeight)
                    return candidate;
            }
            return minSize;
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
