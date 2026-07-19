using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using MaxWorlds.Core;
using MaxWorlds.Dev;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The dev tuning panel (YT-105) — that it exists only in dev mode, that it can be touched, and
    /// that it is legible on the 6-inch target the Craft Bible makes non-negotiable.
    ///
    /// Worth stating what these do NOT prove: a passing test here cannot see what the build draws.
    /// The readability assertions below are arithmetic on the layout constants, which is why the
    /// ticket is also verified by driving the deployed WebGL link and looking at the pixels.
    /// </summary>
    public sealed class DevTuningPanelPlayTests
    {
        private GameObject _host;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DevMode.Reset();
            _host = new GameObject("DevTuningPanel Test");
            _host.AddComponent<DevTuningPanel>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_host != null) Object.Destroy(_host);
            DevMode.Reset();
            yield return null;
        }

        private Canvas FindPanelCanvas()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c.name == "DevTuning Canvas") return c;
            }
            return null;
        }

        [UnityTest]
        public IEnumerator WithDevModeOff_NothingIsBuilt()
        {
            yield return null;
            yield return null;

            Assert.That(FindPanelCanvas(), Is.Null,
                "The panel must not exist in a release session — this is the whole 'not present in " +
                "release config' acceptance criterion.");
        }

        [UnityTest]
        public IEnumerator WithDevModeOn_ItBuildsOneSliderPerTunableValue()
        {
            DevMode.Enabled = true;
            yield return null;
            yield return null;

            var canvas = FindPanelCanvas();
            Assert.That(canvas, Is.Not.Null, "Dev mode is on; the panel should have built.");

            var sliders = canvas.GetComponentsInChildren<Slider>(true);
            Assert.That(sliders.Length, Is.EqualTo(7),
                "Seven values are listed in the ticket: camera zoom, Max speed, robot speed, boss " +
                "speed, Max max-life, water deplete, water replenish.");
        }

        [UnityTest]
        public IEnumerator ItsCanvasOutranksTheHud_SoTheJoystickPadsCannotEatItsTaps()
        {
            DevMode.Enabled = true;
            yield return null;
            yield return null;

            var canvas = FindPanelCanvas();
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.sortingOrder, Is.GreaterThan(100),
                "The HUD canvas sits at 100 and carries invisible full-size OnScreenStick pads. A " +
                "panel at or below that order would have its slider drags swallowed by them.");
            Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Not.Null,
                "Without a raycaster nothing on the panel is touchable at all.");
        }

        [UnityTest]
        public IEnumerator TurningDevModeOff_TearsThePanelBackDown()
        {
            DevMode.Enabled = true;
            yield return null;
            yield return null;
            Assert.That(FindPanelCanvas(), Is.Not.Null, "precondition: panel built");

            DevMode.Reset();
            yield return null;
            yield return null;

            Assert.That(FindPanelCanvas(), Is.Null,
                "Switching dev mode off must remove the panel, not just stop updating it.");
        }

        [UnityTest]
        public IEnumerator ASliderMoveChangesTheLiveValue()
        {
            DevMode.Enabled = true;
            yield return null;
            yield return null;

            var canvas = FindPanelCanvas();
            var sliders = canvas.GetComponentsInChildren<Slider>(true);

            // Drive the slider the way a finger does — through onValueChanged, which is what the
            // EventSystem invokes on drag.
            var speed = System.Array.Find(sliders, s => s.transform.parent.name == "Max move speed");
            Assert.That(speed, Is.Not.Null, "Expected a slider under the 'Max move speed' row.");

            speed.value = 11f;
            yield return null;

            Assert.That(DevTuning.PlayerMoveSpeed, Is.Not.Null);
            Assert.That(DevTuning.PlayerMoveSpeed.Value, Is.EqualTo(11f).Within(0.001f),
                "Moving the slider must apply live, not on a rebuild.");
        }

        /// <summary>
        /// The Craft Bible's phone rule wants a measurement, not an assurance. The panel is laid out
        /// in 1920x1080 reference units; on an iPhone Plus in landscape (932x430pt) the
        /// ScaleWithScreenSize / match-0.5 factor is sqrt(932/1920)*sqrt(430/1080) = 0.44. So these
        /// convert the layout constants into points and check them against the thresholds that
        /// actually matter on a handset.
        /// </summary>
        [UnityTest]
        public IEnumerator ItIsLegibleAndTouchableOnASixInchScreen()
        {
            DevMode.Enabled = true;
            yield return null;
            yield return null;

            var canvas = FindPanelCanvas();
            Assert.That(canvas, Is.Not.Null);

            float scale = DevTuningPanel.PhoneScale;
            Assert.That(scale, Is.EqualTo(0.44f).Within(0.01f),
                "If the reference resolution or match mode changed, this whole measurement is stale.");

            float smallestPt = DevTuningPanel.SmallestFont * scale;
            Assert.That(smallestPt, Is.GreaterThanOrEqualTo(10f),
                $"Smallest text renders at {smallestPt:0.0}pt on a 6-inch screen; below ~10pt it " +
                "stops being readable at arm's length.");

            // Every slider must be a real finger target and must sit inside the screen.
            var panel = canvas.transform.Find("Panel") as RectTransform;
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.sizeDelta.x * scale, Is.LessThanOrEqualTo(932f),
                "The panel is wider than a landscape phone screen.");
            Assert.That(panel.sizeDelta.y * scale, Is.LessThanOrEqualTo(430f),
                "The panel is taller than a landscape phone screen, so knobs would be off-screen.");

            foreach (var s in canvas.GetComponentsInChildren<Slider>(true))
            {
                var rt = (RectTransform)s.transform;
                float widthPt = rt.sizeDelta.x * scale;
                float heightPt = rt.sizeDelta.y * scale;
                // Unity's Slider accepts a drag anywhere in its rect, so the target is the whole
                // rect rather than just the handle.
                Assert.That(widthPt, Is.GreaterThanOrEqualTo(120f),
                    $"Slider '{s.transform.parent.name}' is only {widthPt:0}pt wide — too short to " +
                    "dial a value with any precision.");
                Assert.That(heightPt, Is.GreaterThanOrEqualTo(28f),
                    $"Slider '{s.transform.parent.name}' is only {heightPt:0}pt tall — a fingertip " +
                    "is ~44pt, and below ~28pt this becomes a miss-and-retry.");
            }
        }
    }
}
