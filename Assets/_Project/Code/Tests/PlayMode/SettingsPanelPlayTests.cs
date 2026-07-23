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
            Assert.That(sliders.Length, Is.EqualTo(29),
                "Fourteen Gameplay knobs (ten, plus the four Invasion Level escalation knobs from " +
                "YT-181), the eleven Weapons-tab knobs (YT-138's seven, plus Range Extender " +
                "and Wide-Bore from YT-164, plus Spawn interval from YT-170, plus Cell drop chance " +
                "from YT-171), and the four Boss-tab brood-volley knobs (YT-157): volley interval, " +
                "adds per volley, max adds alive, volley windup.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ItHasAWeaponsTabWithItsOwnSliders()
        {
            var canvas = PanelCanvas();

            // One page container per tab (YT-138 Gameplay/Weapons, YT-157 Boss).
            RectTransform gameplay = null, weapons = null, boss = null;
            foreach (var rt in canvas.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.name == "Page GAMEPLAY") gameplay = rt;
                if (rt.name == "Page WEAPONS") weapons = rt;
                if (rt.name == "Page BOSS") boss = rt;
            }
            Assert.That(gameplay, Is.Not.Null, "no Gameplay page");
            Assert.That(weapons, Is.Not.Null, "no Weapons page — the upgrade tuning has nowhere to live");
            Assert.That(boss, Is.Not.Null, "no Boss page — the brood-volley tuning has nowhere to live (YT-157)");

            Assert.That(gameplay.GetComponentsInChildren<Slider>(true).Length, Is.EqualTo(14),
                "the Gameplay tab keeps its ten knobs plus the four Invasion Level knobs (YT-181)");
            Assert.That(weapons.GetComponentsInChildren<Slider>(true).Length, Is.EqualTo(11),
                "the Weapons tab carries the upgrade/pacing/Hydro knobs, Range Extender and Wide-Bore " +
                "(YT-164), Spawn interval (YT-170), and Cell drop chance (YT-171)");
            Assert.That(boss.GetComponentsInChildren<Slider>(true).Length, Is.EqualTo(4),
                "the Boss tab carries the four brood-volley knobs (YT-157)");
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

        [UnityTest]
        public IEnumerator TheSpawnIntervalSliderDrivesDevTuning()
        {
            // YT-170: the spawn-rate setting must actually take effect, live, from the panel.
            var canvas = PanelCanvas();
            var sliders = canvas.GetComponentsInChildren<Slider>(true);
            var spawn = System.Array.Find(sliders, s => s.transform.parent.name == "Spawn interval");
            Assert.That(spawn, Is.Not.Null, "no Spawn interval slider (YT-170)");

            spawn.value = 0.5f;
            yield return null;

            Assert.That(DevTuning.SpawnInterval, Is.Not.Null);
            Assert.That(DevTuning.SpawnInterval.Value, Is.EqualTo(0.5f).Within(0.001f),
                "moving the Spawn interval slider must drive DevTuning.SpawnInterval");
        }

        [UnityTest]
        public IEnumerator TheCellDropChanceSliderDrivesDevTuning()
        {
            // YT-171: the rusher cell-drop chance must actually take effect, live, from the panel.
            var canvas = PanelCanvas();
            var sliders = canvas.GetComponentsInChildren<Slider>(true);
            var chance = System.Array.Find(sliders, s => s.transform.parent.name == "Cell drop chance");
            Assert.That(chance, Is.Not.Null, "no Cell drop chance slider (YT-171)");

            chance.value = 0.75f;
            yield return null;

            Assert.That(DevTuning.PowerCellDropChance, Is.Not.Null);
            Assert.That(DevTuning.PowerCellDropChance.Value, Is.EqualTo(0.75f).Within(0.001f),
                "moving the Cell drop chance slider must drive DevTuning.PowerCellDropChance");
        }

        [UnityTest]
        public IEnumerator TheEscalationSlidersDriveDevTuning()
        {
            // YT-181: the Invasion Level's four knobs must actually retune the DifficultyDirector,
            // live, from the panel.
            var canvas = PanelCanvas();
            var sliders = canvas.GetComponentsInChildren<Slider>(true);

            var rate = System.Array.Find(sliders, s => s.transform.parent.name == "Escalation rate");
            var max = System.Array.Find(sliders, s => s.transform.parent.name == "Escalation max");
            Assert.That(rate, Is.Not.Null, "no Escalation rate slider (YT-181)");
            Assert.That(max, Is.Not.Null, "no Escalation max slider (YT-181)");

            rate.value = 0.2f;
            max.value = 15f;
            yield return null;

            Assert.That(DevTuning.EscalationRate, Is.EqualTo(0.2f).Within(0.001f),
                "moving the Escalation rate slider must drive DevTuning.EscalationRate");
            Assert.That(DevTuning.EscalationMax, Is.EqualTo(15f).Within(0.001f),
                "moving the Escalation max slider must drive DevTuning.EscalationMax");
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
