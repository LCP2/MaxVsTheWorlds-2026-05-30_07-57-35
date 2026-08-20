using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-489 — sole guard on the category-label best-fit contradiction. The ticket's own hypothesis
    /// (a bare <c>shell.Label.fontSize = CategoryLabelFontSize</c> write silently losing to best-fit)
    /// turned out to already be gone by the time this ticket was picked up — MV-472 had reworked
    /// <c>BuildCategoryNode</c> to derive <c>resizeTextMinSize</c>/<c>resizeTextMaxSize</c> from
    /// <c>CategoryLabelFontSize</c> and never writes a separate bare <c>fontSize</c> at all. But the
    /// underlying defect was still live under a different mechanism, found by writing this very test
    /// and watching it fail against MOVE (a category label that unquestionably fits its column at
    /// 36pt): Unity's own best-fit resolution (<c>TextGenerator.Populate</c>/<c>fontSizeUsedForBestFit</c>,
    /// the exact call <c>Text.OnPopulateMesh</c> makes to render) is bounded ABOVE by the base
    /// <c>Text.fontSize</c> field as well as <c>resizeTextMaxSize</c> — and <c>BuildNodeShell</c> leaves
    /// that field at the ABILITY size (<see cref="RigBoardLayout.LabelFontSizePhone"/>, 32) it was
    /// created with, never touched afterward. So the label was still silently rendering at 32 despite
    /// <c>resizeTextMaxSize</c> correctly reading 36. A test asserting the raw <c>resizeTextMaxSize</c>
    /// property (or the old, now-absent bare <c>fontSize</c> write) would pass on that still-broken
    /// build — exactly the trap this ticket exists to close. This asserts the RESOLVED size Unity's own
    /// TextGenerator actually chooses, cull-exempt per the MV-465 testing policy's Rule 1 (this is the
    /// sole new test MV-489 adds).
    /// </summary>
    public sealed class WeaponsScreenCategoryLabelFontSizeTests
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
            // MV-506: Open() (called below to reach the resolved font size) pauses via Time.timeScale
            // = 0 and is never Close()d in this suite — destroying the GameObject skips WeaponsScreen's
            // own restore path, so this must reset it directly or it leaks into whatever test runs
            // next (and, at the tail of a full batch run, into ProjectSettings/TimeManager.asset itself
            // — Unity persists the live engine timeScale there on quit).
            Time.timeScale = 1f;
        }

        /// <summary>Resolves the MOVE category label's actual best-fit size the same way Unity's
        /// renderer does — <see cref="TextGenerator.Populate"/> against the label's real, built
        /// RectTransform extents — and asserts it lands at <see cref="RigBoardLayout.CategoryLabelFontSizePhone"/>
        /// (36), not the mismatched ability cap (<see cref="RigBoardLayout.LabelFontSizePhone"/>, 32) a
        /// regression back to the pre-fix shared cap would silently re-impose. MOVE, not SECONDARY: MOVE
        /// is short enough to comfortably clear its own column at 36pt, so a resolved value below 36
        /// here can only mean the applied cap itself is wrong — never a legitimate shrink-to-fit for a
        /// long word (SECONDARY's own column width is MV-491's separate, still-open concern; this test
        /// must not couple to it).</summary>
        [Test]
        public void MoveCategoryLabelResolvesToCategoryFontSizeNotAbilityCapAtPhoneAspect()
        {
            _screen.Open();
            _screen.ApplyBoardScale(977f / 458f);   // MV-472's own registered phone capture aspect

            var node = _screen.BoardNode("MOVE");
            Assert.That(node, Is.Not.Null, "MOVE category node must exist");
            var label = node.Find("Text")?.GetComponent<Text>();
            Assert.That(label, Is.Not.Null, "MOVE category node must carry a label Text");
            Assert.That(label.resizeTextForBestFit, Is.True, "fixture assumption: label must be best-fit driven");
            Assert.That(label.rectTransform.sizeDelta.y, Is.EqualTo(60f),
                "fixture assumption: phone-mode category label box height must be 60 (confirms _phoneMode was true at build time)");
            Assert.That((float)label.resizeTextMaxSize, Is.EqualTo(RigBoardLayout.CategoryLabelFontSizePhone),
                $"BuildCategoryNode's own configured cap (raw component property, before any generation) must be {RigBoardLayout.CategoryLabelFontSizePhone}");

            var settings = label.GetGenerationSettings(label.rectTransform.rect.size);
            Assert.That((float)settings.resizeTextMaxSize, Is.EqualTo(RigBoardLayout.CategoryLabelFontSizePhone),
                "GetGenerationSettings must carry the same cap through to the TextGenerationSettings struct");
            label.cachedTextGenerator.Populate(label.text, settings);
            float resolved = label.cachedTextGenerator.fontSizeUsedForBestFit;

            Assert.That(resolved, Is.EqualTo(RigBoardLayout.CategoryLabelFontSizePhone).Within(0.5f),
                $"resolved size {resolved} must land at the category cap ({RigBoardLayout.CategoryLabelFontSizePhone}), " +
                $"not the mismatched ability cap ({RigBoardLayout.LabelFontSizePhone}) MV-489 exists to close");
        }
    }
}
