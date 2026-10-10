using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1158 (Lee, 10 Oct, World 1 on his phone): the SENTINEL button's can't-afford state drew
    /// "NO PARTS" wider than the button, spilling across the Balloon joystick. Root cause:
    /// <c>HudController.AddText</c> defaults <c>horizontalOverflow</c> to <c>Overflow</c>, so
    /// <c>resizeTextForBestFit</c>'s own search ignores the rect's WIDTH entirely (it only shrinks to
    /// fit height) — a string too wide for the box at the resolved max size still overflows it. Fix:
    /// the can't-afford state now shows a red stop sign plus a live "have/cost" Parts count instead of
    /// "NO PARTS", "FULL" became "MAX", and the label gained Wrap/Truncate overflow.
    ///
    /// One new test (MV-465 Rule 1). Tier 2 (resolved values): <c>TextGenerator.fontSizeUsedForBestFit</c>
    /// is a no-op under <c>-batchmode -nographics</c> (established by <c>MV585ForceFieldLabelFontSizeTests</c>),
    /// so resolved font size is found via the same manual best-fit search against
    /// <c>Text.preferredWidth</c>/<c>preferredHeight</c> that fixture uses — confirmed to scale correctly
    /// headless across font sizes.
    ///
    /// Fails on base commit 7db54db — <c>BuildSentinelButton</c> never builds a stop-sign child at all,
    /// so the fixture's reflection lookup of the private <c>_sentinelStopSignRoot</c> field returns null
    /// and the very first fixture assertion fails before any AC is checked. Exact output quoted in the
    /// fix comment.
    /// </summary>
    public sealed class MV1158SentinelButtonPartsShortfallTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly FieldInfo BackyardPathMapField = typeof(BackyardPath).GetField("_map", NonPublicInstance);

        // Same deterministic, package-pinned font MV585ForceFieldLabelFontSizeTests uses — identical
        // glyph outlines on every machine, unlike the OS-linked font HudFont.Get() resolves to.
        private const string DeterministicMeasurementFontPath =
            "Packages/com.unity.searcher/Editor/Resources/FlatSkin/Font/Roboto-Regular.ttf";

        private static Font DeterministicMeasurementFont =>
            AssetDatabase.LoadAssetAtPath<Font>(DeterministicMeasurementFontPath);

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset(); // also resets RigState
            Sentinel.DestroyAllActive();
            Sentinel.ResetRegistry();
        }

        [Test]
        public void SentinelButtonShowsStopSignAndCountWhenShortOnParts_SentinelWhenAffordable_MaxWhenFull()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            Sentinel.DestroyAllActive();
            Sentinel.ResetRegistry();

            WeaponSystemState.Acquire(AbilityKind.Sentinels);

            var origin = new Vector3(91234f, 0f, 61234f);
            var maxGo = new GameObject("MV1158-Max");
            maxGo.transform.position = origin;
            var abilities = maxGo.AddComponent<PlayerAbilities>();

            var pathGo = new GameObject("MV1158-backyard-path");
            var path = pathGo.AddComponent<BackyardPath>();
            BackyardPathMapField.SetValue(path, new MapData
            {
                deckHeight = 2.5f,
                zones = new[] { new MapZone { id = "a1", x = origin.x, z = origin.z, width = 40f, depth = 40f, level = 0 } },
            });

            PickupWallet.SetPowerCells(5);

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");

            try
            {
                Assert.That(GetField(hud, "_abilities"), Is.SameAs(abilities), "fixture: HUD must have found the real PlayerAbilities");

                var stopSignRoot = GetField(hud, "_sentinelStopSignRoot") as RectTransform;
                Assert.That(stopSignRoot, Is.Not.Null, "fixture: Sentinel Button must carry a stop-sign root");
                var countLabel = GetField(hud, "_sentinelCountLabel") as Text;
                Assert.That(countLabel, Is.Not.Null, "fixture: Sentinel Button must carry a have/cost count label");
                var sentinelLabel = GetField(hud, "_sentinelLabel") as Text;
                Assert.That(sentinelLabel, Is.Not.Null, "fixture: Sentinel Button must carry its main label");

                float hydroButtonSize = (float)typeof(HudController).GetField("HydroButtonSize", NonPublicStatic).GetValue(null);

                var measurementFont = DeterministicMeasurementFont;
                Assert.That(measurementFont, Is.Not.Null, $"fixture: deterministic measurement font must load from {DeterministicMeasurementFontPath}");
                countLabel.font = measurementFont;
                countLabel.resizeTextForBestFit = false;

                // ---- AC1: Parts (5) below cost (20) — stop sign + "5/20", resolved size >= 36pt, fits the button.
                Assert.That(PlayerAbilities.SentinelCost, Is.EqualTo(20), "fixture: default u_cst level 0 cost must be 20");
                InvokeUpdateSentinelButton(hud);

                Assert.That(sentinelLabel.text, Is.EqualTo(""), "AC1: the main label must carry no text while the stop sign/count are showing");
                Assert.That(stopSignRoot.gameObject.activeSelf, Is.True, "AC1: the stop sign must be active when Parts fall short of cost");
                Assert.That(countLabel.text, Is.EqualTo("5/20"), "AC1: the count label must read have/cost");

                int maxSize = countLabel.resizeTextMaxSize, minSize = countLabel.resizeTextMinSize;
                float boxWidth = countLabel.rectTransform.rect.width, boxHeight = countLabel.rectTransform.rect.height;
                int resolved = ResolveBestFitSize(countLabel, "5/20", maxSize, minSize, boxWidth, boxHeight);
                countLabel.fontSize = resolved;
                Assert.That(resolved, Is.GreaterThanOrEqualTo(36), $"AC1: '5/20' must resolve to at least 36pt — was {resolved}");
                Assert.That(countLabel.preferredWidth, Is.LessThanOrEqualTo(hydroButtonSize),
                    $"AC1: '5/20' at resolved size {resolved} has preferred width {countLabel.preferredWidth:0.0}, wider than the {hydroButtonSize} button");

                // ---- AC1: Parts (20) at cost (20) — stop sign hidden, "SENTINEL".
                PickupWallet.SetPowerCells(20);
                InvokeUpdateSentinelButton(hud);

                Assert.That(stopSignRoot.gameObject.activeSelf, Is.False, "AC1: the stop sign must hide once Parts meet the cost");
                Assert.That(sentinelLabel.text, Is.EqualTo("SENTINEL"), "AC1: an affordable, ready slot must read SENTINEL");
                Assert.That(countLabel.text, Is.EqualTo(""), "AC1: the count label must clear once affordable");

                sentinelLabel.font = measurementFont;
                sentinelLabel.resizeTextForBestFit = false;
                int sMax = sentinelLabel.resizeTextMaxSize, sMin = sentinelLabel.resizeTextMinSize;
                float sBoxW = sentinelLabel.rectTransform.rect.width, sBoxH = sentinelLabel.rectTransform.rect.height;
                int sResolved = ResolveBestFitSize(sentinelLabel, "SENTINEL", sMax, sMin, sBoxW, sBoxH);
                sentinelLabel.fontSize = sResolved;
                Assert.That(sentinelLabel.preferredWidth, Is.LessThanOrEqualTo(hydroButtonSize),
                    $"AC1: SENTINEL at resolved size {sResolved} has preferred width {sentinelLabel.preferredWidth:0.0}, wider than the {hydroButtonSize} button");

                // ---- AC1: every slot in use — "MAX", stop sign stays hidden.
                var outcome = abilities.TryDeploySentinelNearMax();
                Assert.That(outcome, Is.EqualTo(PlayerAbilities.SentinelDeployOutcome.Deployed), "fixture: the MAX case needs one real deployed sentinel");
                typeof(PlayerAbilities).GetField("_sentinelCooldown", NonPublicInstance).SetValue(abilities, 0f);

                InvokeUpdateSentinelButton(hud);

                Assert.That(stopSignRoot.gameObject.activeSelf, Is.False, "AC1: the stop sign must stay hidden in the MAX state");
                Assert.That(sentinelLabel.text, Is.EqualTo("MAX"), "AC1: every slot in use must read MAX, not FULL");

                sentinelLabel.text = "MAX";
                int mResolved = ResolveBestFitSize(sentinelLabel, "MAX", sMax, sMin, sBoxW, sBoxH);
                sentinelLabel.fontSize = mResolved;
                Assert.That(sentinelLabel.preferredWidth, Is.LessThanOrEqualTo(hydroButtonSize),
                    $"AC1: MAX at resolved size {mResolved} has preferred width {sentinelLabel.preferredWidth:0.0}, wider than the {hydroButtonSize} button");
            }
            finally
            {
                Object.DestroyImmediate(hudGo);
                Object.DestroyImmediate(pathGo);
                Object.DestroyImmediate(maxGo);
            }
        }

        private static void InvokeUpdateSentinelButton(HudController hud)
        {
            typeof(HudController).GetMethod("UpdateSentinelButton", NonPublicInstance).Invoke(hud, new object[] { 0f });
        }

        private static object GetField(object instance, string name) =>
            typeof(HudController).GetField(name, NonPublicInstance).GetValue(instance);

        // Reimplements best-fit's own search (largest size that doesn't overflow the box) against
        // Text.preferredWidth/preferredHeight, since TextGenerator.fontSizeUsedForBestFit doesn't
        // iterate under -batchmode -nographics — same shape MV585ForceFieldLabelFontSizeTests uses.
        private static int ResolveBestFitSize(Text label, string text, int maxSize, int minSize, float boxWidth, float boxHeight)
        {
            label.text = text;
            for (int candidate = maxSize; candidate > minSize; candidate--)
            {
                label.fontSize = candidate;
                if (label.preferredWidth <= boxWidth && label.preferredHeight <= boxHeight)
                    return candidate;
            }
            return minSize;
        }

        private static void InvokeLifecycle(Object component, string methodName)
        {
            component.GetType().GetMethod(methodName, NonPublicInstance).Invoke(component, null);
        }
    }
}
