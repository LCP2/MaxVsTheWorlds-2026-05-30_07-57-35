using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The HUD's widgets must not sit on top of each other.
    ///
    /// This exists because they did. The map button and the minimap (YT-72/YT-73) were dropped into
    /// the top-left corner — which the utility icon column (P / ? / S) already owned — and nothing
    /// anywhere would have complained. Every widget was individually correct; the layout as a whole
    /// was not. Reading each Build* method in turn will never catch that, so the check has to be on
    /// the assembled HUD.
    /// </summary>
    public sealed class HudLayoutPlayTests
    {
        private GameObject _hudGo;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _hudGo = new GameObject("HUD");
            _hudGo.AddComponent<HudController>();
            yield return null;   // Awake builds the whole canvas
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (_hudGo != null) Object.Destroy(_hudGo);
            yield return null;
        }

        /// <summary>A widget's footprint in screen pixels.</summary>
        private static Rect ScreenRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);   // overlay canvas → world corners ARE screen pixels
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        private RectTransform Find(string name)
        {
            foreach (var rt in _hudGo.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt;
            return null;
        }

        [UnityTest]
        public IEnumerator ThereIsNoTopOfScreenLifeOrWaterBarForMax()
        {
            // YT-121: Max's life and water moved to a floating stack over his head, so the top HP
            // and Energy bars are gone. The level pip stays.
            Assert.That(Find("HP Bar"), Is.Null, "the redundant top-of-screen HP bar is still here");
            Assert.That(Find("Energy Bar"), Is.Null, "the redundant top-of-screen water/energy bar is still here");
            Assert.That(Find("XP Bar"), Is.Not.Null, "the level pip should remain");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheTopLeftWidgetsDoNotSitOnTopOfEachOther()
        {
            yield return null;

            // Everything that lives in the left column. The minimap was laid over the icons; that
            // was the bug. (The MAP button is gone since YT-123 — the minimap is the control now.)
            var names = new[] { "Utility Icons", "Minimap" };
            var rects = new List<(string name, Rect rect)>();

            foreach (var n in names)
            {
                RectTransform rt = Find(n);
                Assert.IsNotNull(rt, $"'{n}' is missing from the HUD");
                rects.Add((n, ScreenRect(rt)));
            }

            for (int i = 0; i < rects.Count; i++)
            {
                for (int j = i + 1; j < rects.Count; j++)
                {
                    Assert.IsFalse(rects[i].rect.Overlaps(rects[j].rect),
                        $"'{rects[i].name}' {rects[i].rect} overlaps '{rects[j].name}' {rects[j].rect}");
                }
            }
        }

        [UnityTest]
        public IEnumerator TheMinimapStaysOutOfTheThumbsticksAndTheAbilitySlots()
        {
            yield return null;

            Rect mini = ScreenRect(Find("Minimap"));

            foreach (var n in new[] { "Move Joystick", "Aim Joystick", "Ability Slots" })
            {
                RectTransform rt = Find(n);
                Assert.IsNotNull(rt, $"'{n}' is missing from the HUD");
                Assert.IsFalse(mini.Overlaps(ScreenRect(rt)),
                    $"the minimap is covering '{n}' — it would eat the player's input");
            }
        }

        [UnityTest]
        public IEnumerator ThereIsNoDedicatedMapButton_TheMinimapIsTheControl()
        {
            yield return null;

            // YT-123: the MAP button is gone; tapping the minimap opens the full map.
            Assert.That(Find("Map Button"), Is.Null, "the dedicated MAP button should be removed");

            var mini = Find("Minimap");
            Assert.IsNotNull(mini, "the minimap must still be present — it is the control now");
            var button = mini.GetComponent<UnityEngine.UI.Button>();
            Assert.IsNotNull(button, "the minimap is not tappable — it can no longer open the map");
        }
    }
}
