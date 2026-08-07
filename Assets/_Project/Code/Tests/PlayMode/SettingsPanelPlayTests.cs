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
            DevTuning.ClearSaved();
            Time.timeScale = 1f;

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
            DevTuning.ClearSaved();
            SafeArea.SimulatedSafeArea = null;
            SafeArea.SimulatedScreenSize = null;
            Time.timeScale = 1f;   // never leave the world frozen for the next test
            yield return null;
        }

        private Canvas PanelCanvas()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.name == "Settings Canvas") return c;
            return null;
        }

        // The slider itself only runs 0..1 now (YT-205, piecewise-normalised around each knob's
        // default) — this drives a slider to a real target value the way a player's drag would.
        private static void SetSliderToValue(Slider slider, float value)
        {
            var range = slider.GetComponent<SettingsPanel.SliderRange>();
            slider.value = SettingsPanel.ValueToPos(range.Min, range.Max, range.Default, value);
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
            Assert.That(sliders.Length, Is.EqualTo(66),
                "WV-234 restructured the panel into five tabs — Enemies (19), Economy (10), Weapons " +
                "(16), Arena (14), Feel (7) — 66 total. This includes the full v0.5 recut spec §9 " +
                "list: the gated-arena/robot-composition knobs (settings only until WV-222/223/224) " +
                "and the ability magnitudes that already had a DevTuning override but no slider to " +
                "reach them until now (Water Balloon/Dash/Teleport cooldowns, Weapon Cooldown, " +
                "Water Balloon distance/splash/damage/stop, Speed %/level).");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ItHasFiveTabsWithTheirOwnSliders()
        {
            var canvas = PanelCanvas();

            // One page container per tab (WV-234: Enemies / Economy / Weapons / Arena / Feel).
            RectTransform enemies = null, economy = null, weapons = null, arena = null, feel = null;
            foreach (var rt in canvas.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.name == "Page ENEMIES") enemies = rt;
                if (rt.name == "Page ECONOMY") economy = rt;
                if (rt.name == "Page WEAPONS") weapons = rt;
                if (rt.name == "Page ARENA") arena = rt;
                if (rt.name == "Page FEEL") feel = rt;
            }
            Assert.That(enemies, Is.Not.Null, "no Enemies page");
            Assert.That(economy, Is.Not.Null, "no Economy page");
            Assert.That(weapons, Is.Not.Null, "no Weapons page — the upgrade/ability tuning has nowhere to live");
            Assert.That(arena, Is.Not.Null, "no Arena page — the gated-area tuning has nowhere to live");
            Assert.That(feel, Is.Not.Null, "no Feel page");

            Assert.That(enemies.GetComponentsInChildren<Slider>(true).Length, Is.EqualTo(19),
                "robots and the robot-accumulation scheme (spec §1-2/§9)");
            Assert.That(economy.GetComponentsInChildren<Slider>(true).Length, Is.EqualTo(10),
                "the power-cell/part drains and drops, plus Hydro's burst timing");
            Assert.That(weapons.GetComponentsInChildren<Slider>(true).Length, Is.EqualTo(16),
                "the primary's upgrade-part magnitudes plus every acquired-ability magnitude");
            Assert.That(arena.GetComponentsInChildren<Slider>(true).Length, Is.EqualTo(14),
                "the run's pacing/escalation knobs, the boss brood-volley knobs, and the gated-arena " +
                "knobs (spec §1/§9)");
            Assert.That(feel.GetComponentsInChildren<Slider>(true).Length, Is.EqualTo(7),
                "camera + Max's own handling + the spray's cosmetic knockback");
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
        public IEnumerator EnteringSettingsPausesTheGame_AndClosingResumesIt()
        {
            // WV-234, spec §8: "Entering the Settings area pauses the game."
            float before = Time.timeScale;
            var canvas = PanelCanvas();
            var gear = GearButton(canvas);

            gear.onClick.Invoke();
            yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(0f), "opening Settings must pause the game");

            gear.onClick.Invoke();
            yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(before).Within(0.001f),
                "closing Settings must resume at whatever speed it paused from");
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

            SetSliderToValue(factory, 500f);
            SetSliderToValue(boss, 3000f);
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

            SetSliderToValue(speed, 11f);
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

            SetSliderToValue(spawn, 0.5f);
            yield return null;

            Assert.That(DevTuning.SpawnInterval, Is.Not.Null);
            Assert.That(DevTuning.SpawnInterval.Value, Is.EqualTo(0.5f).Within(0.001f),
                "moving the Spawn interval slider must drive DevTuning.SpawnInterval");
        }

        [UnityTest]
        public IEnumerator ThePartsPerLargeKillSliderDrivesDevTuning()
        {
            // WV-226: the large-kill part pacing must actually take effect, live, from the panel.
            var canvas = PanelCanvas();
            var sliders = canvas.GetComponentsInChildren<Slider>(true);
            var pacing = System.Array.Find(sliders, s => s.transform.parent.name == "Parts/large kill");
            Assert.That(pacing, Is.Not.Null, "no Parts/large kill slider (WV-226)");

            SetSliderToValue(pacing, 6f);
            yield return null;

            Assert.That(DevTuning.PartsPerLargeKills, Is.Not.Null);
            Assert.That(DevTuning.PartsPerLargeKills.Value, Is.EqualTo(6f).Within(0.001f),
                "moving the Parts/large kill slider must drive DevTuning.PartsPerLargeKills");
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

            SetSliderToValue(rate, 0.2f);
            SetSliderToValue(max, 15f);
            yield return null;

            Assert.That(DevTuning.EscalationRate, Is.EqualTo(0.2f).Within(0.001f),
                "moving the Escalation rate slider must drive DevTuning.EscalationRate");
            Assert.That(DevTuning.EscalationMax, Is.EqualTo(15f).Within(0.001f),
                "moving the Escalation max slider must drive DevTuning.EscalationMax");
        }

        [UnityTest]
        public IEnumerator TheNewRecutKnobsDriveDevTuning()
        {
            // Spot-checks: one World & Difficulty Framework dial (MV-275, replacing the old
            // settings-only gated-arena knobs) and one ability knob that already had a live consumer
            // (PlayerAbilities) but no slider to reach it until WV-234.
            var canvas = PanelCanvas();
            var sliders = canvas.GetComponentsInChildren<Slider>(true);

            var baseThreat = System.Array.Find(sliders, s => s.transform.parent.name == "Base threat");
            var balloonDist = System.Array.Find(sliders, s => s.transform.parent.name == "Balloon base dist");
            Assert.That(baseThreat, Is.Not.Null, "no Base threat slider (World & Difficulty Framework)");
            Assert.That(balloonDist, Is.Not.Null, "no Balloon base dist slider (spec §6a/§9)");

            SetSliderToValue(baseThreat, 20f);
            SetSliderToValue(balloonDist, 6f);
            yield return null;

            Assert.That(DevTuning.WorldBaseThreat, Is.EqualTo(20f).Within(0.001f),
                "moving the Base threat slider must drive DevTuning.WorldBaseThreat");
            Assert.That(DevTuning.WaterBalloonBaseDistance, Is.EqualTo(6f).Within(0.001f),
                "moving the Balloon base distance slider must drive DevTuning.WaterBalloonBaseDistance");
        }

        // ---------------------------------------------------------------- it saves (YT-201)

        private static Button FindButton(Canvas canvas, string name)
        {
            foreach (var b in canvas.GetComponentsInChildren<Button>(true))
                if (b.name == name) return b;
            return null;
        }

        [UnityTest]
        public IEnumerator TheSaveButtonPersistsTheCurrentTuning_AndItSurvivesASimulatedRelaunch()
        {
            var canvas = PanelCanvas();
            var sliders = canvas.GetComponentsInChildren<Slider>(true);
            var speed = System.Array.Find(sliders, s => s.transform.parent.name == "Max move speed");
            Assert.That(speed, Is.Not.Null);
            SetSliderToValue(speed, 11f);
            yield return null;

            var save = FindButton(canvas, "Save settings");
            Assert.That(save, Is.Not.Null, "no Save settings button (YT-201)");
            save.onClick.Invoke();

            Assert.That(DevTuning.HasSaved, Is.True, "Save settings must persist to PlayerPrefs");

            // A fresh launch starts with every override null (a new process, not this test's live
            // objects) — DevTuning.Reset() simulates that without actually restarting Unity.
            DevTuning.Reset();
            Assert.That(DevTuning.PlayerMoveSpeed, Is.Null, "precondition: session wiped like a relaunch");

            DevTuning.LoadSaved();   // what ApplyOnLaunch runs before any scene's Awake
            Assert.That(DevTuning.PlayerMoveSpeed, Is.EqualTo(11f).Within(0.001f),
                "the saved tuning must come back on the next launch, before any game starts");
        }

        [UnityTest]
        public IEnumerator ResetToDefaultsAlsoClearsTheSavedOverride()
        {
            var canvas = PanelCanvas();
            var sliders = canvas.GetComponentsInChildren<Slider>(true);
            var speed = System.Array.Find(sliders, s => s.transform.parent.name == "Max move speed");
            SetSliderToValue(speed, 11f);
            yield return null;

            FindButton(canvas, "Save settings").onClick.Invoke();
            Assert.That(DevTuning.HasSaved, Is.True, "precondition: a save exists");

            FindButton(canvas, "Reset to defaults").onClick.Invoke();
            yield return null;

            Assert.That(DevTuning.HasSaved, Is.False,
                "Reset to defaults must clear the saved override too, or a relaunch would silently " +
                "bring the old numbers back");

            DevTuning.Reset();   // simulate the relaunch itself
            DevTuning.LoadSaved();
            Assert.That(DevTuning.PlayerMoveSpeed, Is.Null,
                "nothing should reload after Reset to defaults cleared the save");
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
                if (rt.name != "Panel" && rt.name != "Gear" && rt.name != "Save settings" &&
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

        [UnityTest]
        public IEnumerator EveryKnobRowsLabelAndValueFitWithoutOverlapping()
        {
            // YT-190: the label had no width bound and could run rightward under the value's raw
            // number, worst on the panel's narrowest (3-column) rows. This checks both halves of
            // the fix, for every knob on every tab: the label/value rects never overlap (a real
            // gap between their bounded zones), and each text actually fits its own zone at its
            // font size (so it renders on one line, not wrapped into the row below).
            var canvas = PanelCanvas();
            GearButton(canvas).onClick.Invoke();
            yield return null;

            var sliders = canvas.GetComponentsInChildren<Slider>(true);
            Assert.That(sliders.Length, Is.GreaterThan(0));

            foreach (var s in sliders)
            {
                var row = s.transform.parent;
                var texts = row.GetComponentsInChildren<Text>(true);
                Assert.That(texts.Length, Is.EqualTo(2),
                    $"row '{row.name}' should have exactly a label and a value text");

                var a = texts[0];
                var b = texts[1];
                bool aIsLabel = a.rectTransform.anchoredPosition.x <= b.rectTransform.anchoredPosition.x;
                var label = aIsLabel ? a : b;
                var value = aIsLabel ? b : a;

                Assert.That(label.preferredWidth, Is.LessThanOrEqualTo(label.rectTransform.rect.width),
                    $"'{row.name}' label \"{label.text}\" needs {label.preferredWidth:0}px but only " +
                    $"has {label.rectTransform.rect.width:0}px — it would wrap instead of reading as " +
                    "one line.");
                Assert.That(value.preferredWidth, Is.LessThanOrEqualTo(value.rectTransform.rect.width),
                    $"'{row.name}' value \"{value.text}\" needs {value.preferredWidth:0}px but only " +
                    $"has {value.rectTransform.rect.width:0}px.");

                var labelCorners = new Vector3[4];
                var valueCorners = new Vector3[4];
                label.rectTransform.GetWorldCorners(labelCorners);
                value.rectTransform.GetWorldCorners(valueCorners);
                float labelRight = labelCorners[2].x;
                float valueLeft = valueCorners[0].x;
                Assert.That(labelRight, Is.LessThanOrEqualTo(valueLeft + 0.5f),
                    $"'{row.name}' label zone (right edge {labelRight:0}) overlaps the value zone " +
                    $"(left edge {valueLeft:0}).");
            }
        }
    }
}
