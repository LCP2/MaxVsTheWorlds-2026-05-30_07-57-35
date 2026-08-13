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
        public IEnumerator ThereIsNoTopOfScreenLifeWaterOrXpBarForMax()
        {
            // YT-121: Max's life and water moved to a floating stack over his head, so the top HP
            // and Energy bars are gone. MV-287 then removed the level/XP system entirely, so the
            // level pip that used to remain here is gone too — nothing should live at the top any more.
            Assert.That(Find("HP Bar"), Is.Null, "the redundant top-of-screen HP bar is still here");
            Assert.That(Find("Energy Bar"), Is.Null, "the redundant top-of-screen water/energy bar is still here");
            Assert.That(Find("XP Bar"), Is.Null, "the removed level/XP bar is still here");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheTopLeftWidgetsDoNotSitOnTopOfEachOther()
        {
            yield return null;

            // Everything that lives in the left column.
            var names = new[] { "Utility Icons", "Home Button" };
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
        public IEnumerator TheHomeButtonStaysClearOfWeaponsAndTheTwinSticks()
        {
            yield return null;

            // YT-191: the HOME button lives top-left, far from these — but it's a corner button
            // added after the fact, which is exactly how the minimap/icon overlap happened.
            Rect home = ScreenRect(Find("Home Button"));

            foreach (var n in new[] { "Weapons Button", "Move Joystick", "Aim Joystick" })
            {
                RectTransform rt = Find(n);
                Assert.IsNotNull(rt, $"'{n}' is missing from the HUD");
                Assert.IsFalse(home.Overlaps(ScreenRect(rt)),
                    $"the HOME button {home} overlaps '{n}' {ScreenRect(rt)}");
            }
        }

        [UnityTest]
        public IEnumerator ThereIsNoFullMapScreenOrButtonOnTheHud()
        {
            yield return null;

            // MV-264 brought the minimap back (YT-217's "bounded single garden" no longer describes
            // the v0.5 recut's 10-area gated arena) — but only the compact strip, not the old tappable
            // full map screen or its dedicated button. This scene has no BackyardPath/map, so the
            // strip itself never builds here (see MinimapPlayTests for that against a real map); this
            // just pins the two pieces that should stay gone regardless.
            Assert.That(Find("Map Screen"), Is.Null, "the full map screen should not have come back");
            Assert.That(Find("Map Button"), Is.Null, "the dedicated MAP button should not have come back");
        }
    }
}
