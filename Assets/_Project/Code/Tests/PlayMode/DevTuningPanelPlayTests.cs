using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using MaxWorlds.Core;
using MaxWorlds.Dev;
using MaxWorlds.UI;

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
            // Static seams: clear here rather than at the end of the test that sets them, so a
            // failing assert can't leak a simulated notch into every test that follows.
            SafeArea.SimulatedSafeArea = null;
            SafeArea.SimulatedScreenSize = null;
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
        /// Everything the panel draws has to actually be ON the display.
        ///
        /// This exists because the first cut passed every other test in this file and was still
        /// broken: the panel rect was anchored to screen centre with a top-left pivot, so it hung
        /// down and right of centre and its footer — including the "Copy current values" button that
        /// is the ticket's deliverable — fell off the bottom of the screen. Asserting the panel's
        /// SIZE fits, which is what the readability test does, cannot catch that. Only its rendered
        /// corners can.
        /// </summary>
        [UnityTest]
        public IEnumerator EverythingItDrawsIsOnScreen()
        {
            DevMode.Enabled = true;
            yield return null;
            yield return null;

            var canvas = FindPanelCanvas();
            Assert.That(canvas, Is.Not.Null);

            // Open it: the footer only exists to be pressed, so it has to be reachable.
            var gear = canvas.GetComponentInChildren<Button>(true);
            gear.onClick.Invoke();
            yield return null;

            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            var corners = new Vector3[4];

            foreach (var rt in canvas.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.name != "Panel" && rt.name != "Gear" &&
                    rt.name != "Copy current values" && rt.name != "Reset to defaults") continue;

                // Overlay canvas -> world corners ARE screen pixels.
                rt.GetWorldCorners(corners);
                foreach (var c in corners)
                {
                    Assert.That(screen.Contains(new Vector2(c.x, c.y)), Is.True,
                        $"'{rt.name}' has a corner at ({c.x:0}, {c.y:0}), outside the " +
                        $"{Screen.width}x{Screen.height} screen — it is drawn off the display.");
                }
            }
        }

        /// <summary>
        /// The value dump has to stay inside the panel background.
        ///
        /// It didn't: the dump rect was given 110 units for eight lines that need ~230, and with
        /// overflow allowed the tail printed over the game instead of on the panel. Sizing fixed it;
        /// this pins the relationship so a future extra knob can't quietly reopen it.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCopiedValueDumpStaysInsideThePanel()
        {
            DevMode.Enabled = true;
            yield return null;
            yield return null;

            var canvas = FindPanelCanvas();
            var panel = canvas.transform.Find("Safe Area/Panel") as RectTransform;
            Assert.That(panel, Is.Not.Null);

            // Press Copy so the dump actually has its full content in it.
            foreach (var b in canvas.GetComponentsInChildren<Button>(true))
            {
                if (b.name == "Copy current values") b.onClick.Invoke();
            }
            yield return null;

            Text dump = null;
            foreach (var t in panel.GetComponentsInChildren<Text>(true))
            {
                if (t.text.StartsWith("# MAX tuning")) dump = t;
            }
            Assert.That(dump, Is.Not.Null, "Copy should have filled the on-screen dump.");

            var panelCorners = new Vector3[4];
            var dumpCorners = new Vector3[4];
            panel.GetWorldCorners(panelCorners);
            dump.rectTransform.GetWorldCorners(dumpCorners);

            float need = dump.preferredHeight;
            Assert.That(dump.rectTransform.rect.height, Is.GreaterThanOrEqualTo(need),
                $"The dump needs {need:0} units for its lines but was given " +
                $"{dump.rectTransform.rect.height:0} — the tail would render outside the panel.");

            foreach (var c in dumpCorners)
            {
                Assert.That(c.y, Is.GreaterThanOrEqualTo(panelCorners[0].y - 0.5f),
                    "The dump text extends below the bottom of the panel background.");
            }
        }

        /// <summary>
        /// The gear is edge-anchored, and on a landscape iPhone the notch inset is about 44pt.
        /// Without a safe-area parent it sits ~9pt in from the edge and lands underneath it — on
        /// precisely the device this ticket exists to serve.
        /// </summary>
        [UnityTest]
        public IEnumerator WithANotch_TheGearStaysOutOfIt()
        {
            float inset = Screen.width * 0.08f;
            SafeArea.SimulatedScreenSize = new Vector2(Screen.width, Screen.height);
            SafeArea.SimulatedSafeArea =
                new Rect(inset, 0f, Screen.width - inset * 2f, Screen.height);

            DevMode.Enabled = true;
            yield return null;
            yield return null;
            yield return null;   // SafeArea re-anchors on its own Update

            var canvas = FindPanelCanvas();
            Assert.That(canvas, Is.Not.Null);

            var gear = canvas.transform.Find("Safe Area/Gear") as RectTransform;
            Assert.That(gear, Is.Not.Null,
                "The gear must hang off a safe-area root, not straight off the canvas.");

            var corners = new Vector3[4];
            gear.GetWorldCorners(corners);
            foreach (var c in corners)
            {
                Assert.That(c.x, Is.GreaterThanOrEqualTo(inset - 0.5f),
                    $"The gear reaches x={c.x:0}, inside the {inset:0}px notch inset — it would be " +
                    "under the notch on a landscape iPhone.");
            }
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
            var panel = canvas.transform.Find("Safe Area/Panel") as RectTransform;
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
