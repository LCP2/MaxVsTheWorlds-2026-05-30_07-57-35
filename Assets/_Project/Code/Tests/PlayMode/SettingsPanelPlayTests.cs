using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The in-game Settings panel (YT-120). The panel is ALWAYS compiled and always present now —
    /// no dev flag, no build-time define — so these prove it builds unconditionally, opens and
    /// closes from the gear, applies a slider live, and stays legible on the 6-inch target.
    ///
    /// What they do NOT prove: a passing test cannot see what the build draws. The readability
    /// assertions are arithmetic on the layout constants, which is why the ticket is also verified by
    /// driving the deployed link and looking at the pixels.
    /// </summary>
    public sealed class SettingsPanelPlayTests
    {
        private GameObject _host;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DevMode.Reset();
            DevTuning.Reset();

            // The panel self-installs at AfterSceneLoad, so a play-mode test already has one. Clear
            // any pre-existing panel + canvas so there is exactly one under our control.
            foreach (var p in Object.FindObjectsByType<SettingsPanel>(FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.name == "Settings Canvas") Object.DestroyImmediate(c.gameObject);

            _host = new GameObject("SettingsPanel Test");
            _host.AddComponent<SettingsPanel>();
            yield return null;   // Start() builds
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_host != null) Object.Destroy(_host);
            DevMode.Reset();
            DevTuning.Reset();
            SafeArea.SimulatedSafeArea = null;
            SafeArea.SimulatedScreenSize = null;
            yield return null;
        }

        private Canvas PanelCanvas()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.name == "Settings Canvas") return c;
            return null;
        }

        private static Button GearButton(Canvas canvas)
        {
            var gear = canvas.transform.Find("Safe Area/Gear");
            return gear != null ? gear.GetComponent<Button>() : null;
        }

        // ---------------------------------------------------------------- always present

        [UnityTest]
        public IEnumerator ItBuildsWithNoDevFlagAtAll()
        {
            // Deliberately never touch DevMode — a release session is exactly this.
            Assert.That(DevMode.Enabled, Is.False, "precondition: not in dev mode");
            Assert.That(PanelCanvas(), Is.Not.Null,
                "The Settings panel must be present in a normal build — that is the whole ticket.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ItBuildsOneSliderPerTunableValue()
        {
            var canvas = PanelCanvas();
            var sliders = canvas.GetComponentsInChildren<Slider>(true);
            Assert.That(sliders.Length, Is.EqualTo(10),
                "Ten values: camera zoom, Max speed, robot speed, boss speed, Max max-life, water " +
                "deplete, water replenish, factory health and boss health (YT-126), plus hose " +
                "tether length (YT-129).");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ItStartsClosedAndTheGearOpensAndClosesIt()
        {
            var canvas = PanelCanvas();
            var panel = canvas.transform.Find("Safe Area/Panel").gameObject;
            Assert.That(panel.activeInHierarchy, Is.False, "the panel should not be open on spawn");

            var gear = GearButton(canvas);
            Assert.That(gear, Is.Not.Null, "no gear button to open the panel with");

            gear.onClick.Invoke();
            yield return null;
            Assert.That(panel.activeInHierarchy, Is.True, "the gear did not open the panel");

            gear.onClick.Invoke();
            yield return null;
            Assert.That(panel.activeInHierarchy, Is.False, "the gear did not close the panel again");
        }

        [UnityTest]
        public IEnumerator ItsCanvasOutranksTheHud_SoTheJoystickPadsCannotEatItsTaps()
        {
            var canvas = PanelCanvas();
            Assert.That(canvas.sortingOrder, Is.GreaterThan(100),
                "The HUD sits at 100 with invisible full-size OnScreenStick pads; a panel at or " +
                "below that order would have its drags swallowed.");
            Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Not.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheDurabilitySlidersAppearAndAreWired()
        {
            var canvas = PanelCanvas();
            var sliders = canvas.GetComponentsInChildren<Slider>(true);

            var factory = System.Array.Find(sliders, s => s.transform.parent.name == "Factory health");
            var boss = System.Array.Find(sliders, s => s.transform.parent.name == "Boss health");
            Assert.That(factory, Is.Not.Null, "no Factory health slider (YT-126)");
            Assert.That(boss, Is.Not.Null, "no Boss health slider (YT-126)");

            factory.value = 500f;
            boss.value = 3000f;
            yield return null;

            Assert.That(DevTuning.FactoryHealth, Is.EqualTo(500f).Within(0.001f),
                "the Factory health slider must drive DevTuning.FactoryHealth");
            Assert.That(DevTuning.BossHealth, Is.EqualTo(3000f).Within(0.001f),
                "the Boss health slider must drive DevTuning.BossHealth");
        }

        // ---------------------------------------------------------------- it does something

        [UnityTest]
        public IEnumerator ASliderMoveChangesTheLiveValue_WithNoDevMode()
        {
            var canvas = PanelCanvas();
            var sliders = canvas.GetComponentsInChildren<Slider>(true);
            var speed = System.Array.Find(sliders, s => s.transform.parent.name == "Max move speed");
            Assert.That(speed, Is.Not.Null);

            speed.value = 11f;
            yield return null;

            Assert.That(DevTuning.PlayerMoveSpeed, Is.Not.Null);
            Assert.That(DevTuning.PlayerMoveSpeed.Value, Is.EqualTo(11f).Within(0.001f));
            // And gameplay actually reads that override — no dev flag gating it any more (YT-120).
            Assert.That(DevTuning.Or(DevTuning.PlayerMoveSpeed, 6f), Is.EqualTo(11f).Within(0.001f),
                "a moved slider must change the number gameplay uses, with dev mode off");
        }

        // ---------------------------------------------------------------- it is on screen

        [UnityTest]
        public IEnumerator EverythingItDrawsIsOnScreen()
        {
            var canvas = PanelCanvas();
            GearButton(canvas).onClick.Invoke();   // the footer only exists to be pressed
            yield return null;

            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            var corners = new Vector3[4];

            foreach (var rt in canvas.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.name != "Panel" && rt.name != "Gear" &&
                    rt.name != "Copy current values" && rt.name != "Reset to defaults") continue;

                rt.GetWorldCorners(corners);
                foreach (var c in corners)
                    Assert.That(screen.Contains(new Vector2(c.x, c.y)), Is.True,
                        $"'{rt.name}' has a corner at ({c.x:0}, {c.y:0}), off the " +
                        $"{Screen.width}x{Screen.height} screen.");
            }
        }

        [UnityTest]
        public IEnumerator TheCopiedValueDumpStaysInsideThePanel()
        {
            var canvas = PanelCanvas();
            var panel = canvas.transform.Find("Safe Area/Panel") as RectTransform;
            GearButton(canvas).onClick.Invoke();
            yield return null;

            foreach (var b in canvas.GetComponentsInChildren<Button>(true))
                if (b.name == "Copy current values") b.onClick.Invoke();
            yield return null;

            Text dump = null;
            foreach (var t in panel.GetComponentsInChildren<Text>(true))
                if (t.text.StartsWith("# MAX tuning")) dump = t;
            Assert.That(dump, Is.Not.Null, "Copy should have filled the on-screen dump.");

            float need = dump.preferredHeight;
            Assert.That(dump.rectTransform.rect.height, Is.GreaterThanOrEqualTo(need),
                $"The dump needs {need:0} units but was given {dump.rectTransform.rect.height:0} — " +
                "the tail would render outside the panel.");

            var panelCorners = new Vector3[4];
            var dumpCorners = new Vector3[4];
            panel.GetWorldCorners(panelCorners);
            dump.rectTransform.GetWorldCorners(dumpCorners);
            foreach (var c in dumpCorners)
                Assert.That(c.y, Is.GreaterThanOrEqualTo(panelCorners[0].y - 0.5f),
                    "The dump text extends below the bottom of the panel background.");
        }

        // ---------------------------------------------------------------- phone

        [UnityTest]
        public IEnumerator WithANotch_TheGearStaysOutOfIt()
        {
            float inset = Screen.width * 0.08f;
            SafeArea.SimulatedScreenSize = new Vector2(Screen.width, Screen.height);
            SafeArea.SimulatedSafeArea = new Rect(inset, 0f, Screen.width - inset * 2f, Screen.height);
            yield return null;
            yield return null;   // SafeArea re-anchors on its own Update

            var canvas = PanelCanvas();
            var gear = canvas.transform.Find("Safe Area/Gear") as RectTransform;
            Assert.That(gear, Is.Not.Null, "the gear must hang off a safe-area root, not the canvas");

            var corners = new Vector3[4];
            gear.GetWorldCorners(corners);
            foreach (var c in corners)
                Assert.That(c.x, Is.GreaterThanOrEqualTo(inset - 0.5f),
                    $"the gear reaches x={c.x:0}, inside the {inset:0}px notch inset");
        }

        [UnityTest]
        public IEnumerator ItIsLegibleAndTouchableOnASixInchScreen()
        {
            var canvas = PanelCanvas();

            float scale = SettingsPanel.PhoneScale;
            Assert.That(scale, Is.EqualTo(0.44f).Within(0.01f),
                "If the reference resolution or match mode changed, this measurement is stale.");

            float smallestPt = SettingsPanel.SmallestFont * scale;
            Assert.That(smallestPt, Is.GreaterThanOrEqualTo(10f),
                $"Smallest text renders at {smallestPt:0.0}pt on a 6-inch screen.");

            var panel = canvas.transform.Find("Safe Area/Panel") as RectTransform;
            Assert.That(panel.sizeDelta.x * scale, Is.LessThanOrEqualTo(932f), "wider than the phone");
            Assert.That(panel.sizeDelta.y * scale, Is.LessThanOrEqualTo(430f), "taller than the phone");

            foreach (var s in canvas.GetComponentsInChildren<Slider>(true))
            {
                var rt = (RectTransform)s.transform;
                Assert.That(rt.sizeDelta.x * scale, Is.GreaterThanOrEqualTo(120f),
                    $"Slider '{s.transform.parent.name}' too short to dial precisely.");
                Assert.That(rt.sizeDelta.y * scale, Is.GreaterThanOrEqualTo(28f),
                    $"Slider '{s.transform.parent.name}' too thin — a fingertip is ~44pt.");
            }
            yield return null;
        }
    }
}
