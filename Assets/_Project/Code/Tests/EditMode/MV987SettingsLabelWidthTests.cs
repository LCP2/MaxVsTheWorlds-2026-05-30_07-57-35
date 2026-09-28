using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Save;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-987 — the Home screen's top-right SETTINGS button rendered its label on two lines
    /// ("SETTING" / "S") on iPhone: <c>HomeScreen.BuildSettingsButton</c> sized the button
    /// <c>w = 220f</c>, giving a 128-wide label rect at 28pt bold, narrower than the word. Fails on
    /// base commit 8c86feb: the label's RESOLVED <c>preferredWidth</c> exceeds its RESOLVED rect
    /// width there (quoted in the fix comment). Sole guard on this defect; do not cull.
    ///
    /// Builds the real Home screen (Tier 2: resolved rect/preferredWidth after
    /// <c>LayoutRebuilder.ForceRebuildLayoutImmediate</c>), same idiom as
    /// <see cref="MV960HomeLayoutTests"/> and <see cref="MV986HomeCardStatusTests"/>, then reads the
    /// SETTINGS button's label <c>Text</c> by name.
    /// </summary>
    public sealed class MV987SettingsLabelWidthTests
    {
        private string _dir;
        private GameObject _go;
        private HomeScreen _home;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-save-tests-mv987");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            for (int i = 0; i < SaveSystem.SlotCount; i++) SaveSystem.Delete(i);   // deterministic: every card starts Empty
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            SaveSystem.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            Time.timeScale = 1f;
        }

        [Test]
        public void SettingsLabel_ResolvedPreferredWidth_FitsResolvedRectWidth()
        {
            _go = new GameObject("HomeScreen");
            _home = _go.AddComponent<HomeScreen>();
            _home.Open();

            var canvas = _home.GetComponentInChildren<Canvas>(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);

            Transform settingsButton = null;
            foreach (var btn in _home.GetComponentsInChildren<Button>(true))
            {
                if (btn.gameObject.name == "SETTINGS") { settingsButton = btn.transform; break; }
            }
            Assert.That(settingsButton, Is.Not.Null, "fixture: expected a 'SETTINGS' button under Home");

            var label = settingsButton.GetComponentInChildren<Text>(true);
            Assert.That(label, Is.Not.Null, "fixture: expected the SETTINGS button to carry a label Text");
            Assert.That(label.text, Is.EqualTo("SETTINGS"));

            float rectWidth = label.rectTransform.rect.width;
            float preferredWidth = label.preferredWidth;

            Assert.That(preferredWidth, Is.LessThanOrEqualTo(rectWidth),
                $"SETTINGS label's resolved preferredWidth {preferredWidth:0.0} exceeds its resolved rect width {rectWidth:0.0} — it will wrap to a second line");
        }
    }
}
