using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using MaxWorlds.CameraRig;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Bosses;
using MaxWorlds.Player;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The in-game Settings panel (YT-120) — a gear button that opens a panel of live tuning sliders.
    ///
    /// It began as a dev-only overlay (YT-105) gated behind a build-time scripting define. That
    /// define was injected by editing ProjectSettings.asset mid-CI, which dirtied the git tree and
    /// tripped the version guard, so the iOS build failed the moment the panel was turned on
    /// (YT-119). The fix Lee asked for is the honest one: make it a real Settings panel that is
    /// ALWAYS compiled into every build. No <c>#if</c>, no define, no build-time file edits — the
    /// gear is simply always there, and a slider a player moves takes effect live through
    /// <see cref="DevTuning"/>.
    ///
    /// The sliders are still the combat-feel numbers: every one is a feel call, and a feel call
    /// costs a guess → build → deploy → play round trip to evaluate. On a phone that round trip is
    /// minutes, so this lets a value be found by sweeping past it and coming back.
    ///
    /// Built in uGUI, NOT IMGUI: the project runs Active Input Handling = "Input System (New)" only,
    /// where IMGUI receiving touch on device is not something to bet an acceptance criterion on.
    /// uGUI rides the EventSystem + InputSystemUIInputModule path the on-screen sticks already prove
    /// works on iOS and WebGL (YT-98). Its canvas sits at sorting order 200, above the HUD's 100, so
    /// its raycasts beat the invisible OnScreenStick pads rather than being swallowed by them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsPanel : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<SettingsPanel>() != null) return;
            new GameObject("SettingsPanel").AddComponent<SettingsPanel>();
        }

        // --- layout, in canvas reference units (1920x1080, match 0.5) ---
        //
        // Sized against the phone, not the monitor: the Craft Bible's non-negotiable is a 6-inch
        // screen. On an iPhone Plus in landscape (932x430pt) the scale factor is
        // sqrt(932/1920)*sqrt(430/1080) = 0.44, so one reference unit is 0.44pt. That makes the
        // smallest font below ~10.5pt (iOS caption is 11-13pt) and the gear a 42pt target.
        private const float RefW = 1920f;
        private const float RefH = 1080f;
        private const float Scale6Inch = 0.44f;   // used by the layout test

        private const float PanelW = 980f;
        private const float PanelH = 900f;
        private const float Pad = 20f;
        private const float ColGap = 20f;
        private const float RowH = 112f;
        private const float SliderH = 72f;
        private const float HandleW = 64f;
        private const float HeaderH = 56f;
        private const float ButtonH = 84f;
        private const float DumpH = 240f;
        private const float GearSize = 96f;

        private const int LabelFont = 30;
        private const int HeaderFont = 40;
        private const int DumpFont = 24;

        // Basement-biome dark panel + bright green accent, from mockups/13-settings.html.
        private static readonly Color PanelColor = new Color(0.055f, 0.075f, 0.062f, 0.96f);
        private static readonly Color Accent = new Color(0.298f, 0.851f, 0.392f);      // #4CD964
        private static readonly Color AccentDeep = new Color(0.165f, 0.616f, 0.204f);  // #2A9D34
        private static readonly Color TrackColor = new Color(1f, 1f, 1f, 0.14f);
        private static readonly Color TextColor = Color.white;
        private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.55f);

        private Canvas _canvas;
        private RectTransform _safeRoot;
        private GameObject _panelRoot;
        private GameObject _scrim;
        private Text _dumpText;
        private bool _open;

        private readonly List<Knob> _knobs = new List<Knob>();

        /// <summary>One tunable value: where its number comes from, where it goes, and what 100%
        /// means. <see cref="Apply"/> aside — most knobs read through <see cref="DevTuning"/> at the
        /// point of use and pick a new number up next frame; only the ones cached into an object at
        /// construction (camera offset, energy pool, health ceiling) have to be pushed.</summary>
        private sealed class Knob
        {
            public string Name;
            public string Unit;
            public float Min;
            public float Max;
            public float Default;
            public Func<float> Get;
            public Action<float> Set;
            public Slider Slider;
            public Text Value;
        }

        // Built once, a frame after the scene loads (the objects it reads defaults from wake in their
        // own Awake first). Always — there is no gate any more.
        private void Start() => Build();

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            if (_canvas != null) return;
            EnsureEventSystem();

            var go = new GameObject("Settings Canvas", typeof(Canvas), typeof(CanvasScaler),
                                    typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;   // above the HUD's 100, so the sticks can't eat our taps

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // Everything hangs off a safe-area root so the gear clears the notch on the exact device
            // the ticket is about.
            _safeRoot = NewRect("Safe Area", _canvas.transform, Vector2.zero, Vector2.one);
            Stretch(_safeRoot);
            _safeRoot.gameObject.AddComponent<SafeArea>();

            BuildKnobs();
            BuildScrim();
            BuildGearButton();
            BuildPanel();
            SetOpen(false);
        }

        /// <summary>The HUD builds one too, but the panel must work in a scene with no HUD (a test
        /// fixture, a stripped scene) or nothing would be clickable.</summary>
        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>
        /// Declare the seven knobs, capturing the authored value of each as its 100% reference.
        /// Defaults come from live objects where serialized (camera, player) and from tuning classes
        /// where const (robot, boss, blaster). If an object isn't in this scene the knob still works —
        /// the override lives in global <see cref="DevTuning"/> — it just falls back to the constant.
        /// </summary>
        private void BuildKnobs()
        {
            _knobs.Clear();

            var rig = FindFirstObjectByType<FixedAngleCameraRig>();
            var player = FindFirstObjectByType<PlayerController>();
            var health = FindFirstObjectByType<PlayerHealth>();

            float camDefault = rig != null ? rig.Distance : 25.1f;
            float playerDefault = player != null ? player.AuthoredMoveSpeed : 6f;
            float healthDefault = health != null ? health.AuthoredMax : 100f;
            float robotDefault = EnemyArchetype.Rusher.MoveSpeed;
            float bossDefault = BossTuning.MoveSpeed;
            float drainDefault = BlasterTuning.EnergyPerSecond;
            float regenDefault = BlasterTuning.RegenPerSec;

            Add("Camera zoom", "m", FixedAngleCameraRig.MinDistance, FixedAngleCameraRig.MaxDistance,
                camDefault,
                () => DevTuning.Or(DevTuning.CameraDistance, camDefault),
                v =>
                {
                    DevTuning.CameraDistance = v;
                    var r = FindFirstObjectByType<FixedAngleCameraRig>();
                    if (r != null) r.SetDistance(v);
                });

            Add("Max move speed", "m/s", 1f, 15f, playerDefault,
                () => DevTuning.Or(DevTuning.PlayerMoveSpeed, playerDefault),
                v => DevTuning.PlayerMoveSpeed = v);

            Add("Robot move speed", "m/s", 0.5f, 12f, robotDefault,
                () => DevTuning.Or(DevTuning.RobotMoveSpeed, robotDefault),
                v => DevTuning.RobotMoveSpeed = v);

            Add("Boss move speed", "m/s", 0.5f, 12f, bossDefault,
                () => DevTuning.Or(DevTuning.BossMoveSpeed, bossDefault),
                v => DevTuning.BossMoveSpeed = v);

            Add("Max max-life", "hp", 25f, 500f, healthDefault,
                () => DevTuning.Or(DevTuning.PlayerMaxHealth, healthDefault),
                v =>
                {
                    DevTuning.PlayerMaxHealth = v;
                    var h = FindFirstObjectByType<PlayerHealth>();
                    if (h != null) h.RefreshMax();
                });

            Add("Water deplete rate", "/s", 0f, 60f, drainDefault,
                () => DevTuning.Or(DevTuning.BlasterDrainPerSecond, drainDefault),
                v => { DevTuning.BlasterDrainPerSecond = v; RefreshBlaster(); });

            Add("Water replenish rate", "/s", 0f, 200f, regenDefault,
                () => DevTuning.Or(DevTuning.BlasterRegenPerSecond, regenDefault),
                v => { DevTuning.BlasterRegenPerSecond = v; RefreshBlaster(); });
        }

        private void Add(string name, string unit, float min, float max, float def,
                         Func<float> get, Action<float> set)
        {
            _knobs.Add(new Knob
            {
                Name = name, Unit = unit, Min = min, Max = max, Default = def, Get = get, Set = set,
            });
        }

        private static void RefreshBlaster()
        {
            var b = FindFirstObjectByType<WaterBlaster>();
            if (b != null) b.RefreshDevTuning();
        }

        // ------------------------------------------------------------------ widgets

        /// <summary>A full-screen dark scrim behind the panel: it dims the game so a small settings
        /// panel reads as a modal, and it swallows every tap outside the panel so tuning can never
        /// drive Max around underneath. Only present while the panel is open.</summary>
        private void BuildScrim()
        {
            var rt = NewRect("Scrim", _safeRoot, Vector2.zero, Vector2.one);
            Stretch(rt);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = HudTextures.Solid();
            img.color = Scrim;
            img.raycastTarget = true;
            // Tap outside the panel to dismiss — the phone-native gesture.
            var btn = rt.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => SetOpen(false));
            _scrim = rt.gameObject;
        }

        private void BuildGearButton()
        {
            // Left edge, vertically centred: the one region nothing else claims. Top-left is the FPS
            // readout and utility icons, top-right the ability slots, both bottom corners the sticks.
            var rt = NewRect("Gear", _safeRoot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(GearSize, GearSize);
            rt.anchoredPosition = new Vector2(Pad + GearSize * 0.5f, 0f);

            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = HudTextures.Disc();
            img.color = new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.78f);
            img.raycastTarget = true;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => SetOpen(!_open));

            // A concentric-ring dial for the icon rather than a ⚙ glyph: the HUD renders in
            // LegacyRuntime.ttf, which doesn't carry the gear codepoint, so a glyph would leave an
            // empty box on device. TechRings is the same icon language the joysticks use, and reads
            // clearly as an adjustable control.
            var icon = AddImage(rt, HudTextures.TechRings(96, 3), Accent, "Icon");
            Stretch(icon.rectTransform);
            icon.raycastTarget = false;
        }

        private void BuildPanel()
        {
            var rt = NewRect("Panel", _safeRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            rt.sizeDelta = new Vector2(PanelW, PanelH);
            // The pivot stays top-left because every child is placed by Place() in top-left space, so
            // centring is done by offsetting half the panel up and left rather than moving the pivot.
            rt.anchoredPosition = new Vector2(-PanelW * 0.5f, PanelH * 0.5f);
            _panelRoot = rt.gameObject;

            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = HudTextures.RoundedBox();
            bg.type = Image.Type.Sliced;
            bg.color = PanelColor;
            bg.raycastTarget = true;

            float y = -Pad;

            var header = AddText(rt, "SETTINGS", HeaderFont, Accent, TextAnchor.MiddleLeft);
            Place(header.rectTransform, Pad, y, PanelW - Pad * 2f, HeaderH);
            y -= HeaderH + 8f;

            // Two columns: seven rows in one column would overrun the safe-area height on a notched
            // phone in landscape.
            float colW = (PanelW - Pad * 2f - ColGap) * 0.5f;
            for (int i = 0; i < _knobs.Count; i++)
            {
                int col = i / 4;
                int row = i % 4;
                float x = Pad + col * (colW + ColGap);
                BuildKnobRow(_knobs[i], rt, x, y - row * RowH, colW);
            }

            float gridH = RowH * 4f;
            float footerY = y - gridH - 12f;

            var copy = BuildButton(rt, "Copy current values", Pad, footerY, 380f, ButtonH, primary: true);
            copy.onClick.AddListener(CopyValues);

            var reset = BuildButton(rt, "Reset to defaults", Pad + 380f + 16f, footerY, 300f, ButtonH);
            reset.onClick.AddListener(ResetValues);

            var close = BuildButton(rt, "Close", Pad + 380f + 16f + 300f + 16f, footerY, 200f, ButtonH);
            close.onClick.AddListener(() => SetOpen(false));

            _dumpText = AddText(rt, "", DumpFont, TextColor, TextAnchor.UpperLeft);
            Place(_dumpText.rectTransform, Pad, footerY - ButtonH - 8f, PanelW - Pad * 2f, DumpH);
            _dumpText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private void BuildKnobRow(Knob k, RectTransform parent, float x, float y, float w)
        {
            var row = NewRect(k.Name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f));
            Place(row, x, y, w, RowH);

            var name = AddText(row, k.Name, LabelFont, TextColor, TextAnchor.MiddleLeft);
            Place(name.rectTransform, 0f, 0f, w * 0.5f, 34f);

            k.Value = AddText(row, "", LabelFont, Accent, TextAnchor.MiddleRight);
            Place(k.Value.rectTransform, w * 0.5f, 0f, w * 0.5f, 34f);

            k.Slider = BuildSlider(row, 0f, -40f, w, SliderH, k);
            UpdateValueText(k);
        }

        private Slider BuildSlider(RectTransform parent, float x, float y, float w, float h, Knob k)
        {
            var rt = NewRect("Slider", parent, new Vector2(0f, 1f), new Vector2(0f, 1f));
            Place(rt, x, y, w, h);
            var slider = rt.gameObject.AddComponent<Slider>();

            var track = NewRect("Track", rt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            track.sizeDelta = new Vector2(0f, 14f);
            track.anchoredPosition = Vector2.zero;
            var trackImg = track.gameObject.AddComponent<Image>();
            trackImg.sprite = HudTextures.RoundedBox(24, 0.5f);
            trackImg.type = Image.Type.Sliced;
            trackImg.color = TrackColor;

            var fillArea = NewRect("Fill Area", rt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            fillArea.sizeDelta = new Vector2(-HandleW, 14f);
            fillArea.anchoredPosition = Vector2.zero;
            var fill = NewRect("Fill", fillArea, new Vector2(0f, 0f), new Vector2(0f, 1f));
            fill.sizeDelta = new Vector2(HandleW, 0f);
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.sprite = HudTextures.RoundedBox(24, 0.5f);
            fillImg.type = Image.Type.Sliced;
            fillImg.color = Accent;

            var handleArea = NewRect("Handle Slide Area", rt, new Vector2(0f, 0f), new Vector2(1f, 1f));
            handleArea.sizeDelta = new Vector2(-HandleW, 0f);
            handleArea.anchoredPosition = Vector2.zero;
            var handle = NewRect("Handle", handleArea, new Vector2(0f, 0f), new Vector2(0f, 1f));
            handle.sizeDelta = new Vector2(HandleW, 0f);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.sprite = HudTextures.Disc();
            handleImg.color = Color.white;

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = k.Min;
            slider.maxValue = k.Max;
            slider.wholeNumbers = false;
            slider.SetValueWithoutNotify(Mathf.Clamp(k.Get(), k.Min, k.Max));
            slider.onValueChanged.AddListener(v => { k.Set(v); UpdateValueText(k); });

            return slider;
        }

        private Button BuildButton(RectTransform parent, string label, float x, float y,
                                   float w, float h, bool primary = false)
        {
            var rt = NewRect(label, parent, new Vector2(0f, 1f), new Vector2(0f, 1f));
            Place(rt, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = HudTextures.RoundedBox();
            img.type = Image.Type.Sliced;
            img.color = primary ? AccentDeep : new Color(1f, 1f, 1f, 0.14f);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var text = AddText(rt, label, LabelFont, TextColor, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            return btn;
        }

        // ------------------------------------------------------------------ behaviour

        private void SetOpen(bool open)
        {
            _open = open;
            if (_panelRoot != null) _panelRoot.SetActive(open);
            if (_scrim != null) _scrim.SetActive(open);
        }

        private void UpdateValueText(Knob k)
        {
            if (k.Value == null) return;
            float v = k.Get();
            // Raw number AND percent of the authored default — the percent makes a value portable
            // ("40% faster"), the raw number is what gets pasted back into the source.
            float pct = k.Default > 0f ? v / k.Default * 100f : 0f;
            k.Value.text = $"{v:0.##} {k.Unit}  ({pct:0}%)";
        }

        /// <summary>
        /// Dump every value as <c>name: value</c> text. Goes three places on purpose: the clipboard
        /// (convenient, but <c>systemCopyBuffer</c> isn't dependable under a WebGL security prompt),
        /// the panel itself (always works — Lee can read or screenshot it), and the log.
        /// </summary>
        private void CopyValues()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# MAX tuning — " + Application.version);
            foreach (var k in _knobs)
            {
                float v = k.Get();
                float pct = k.Default > 0f ? v / k.Default * 100f : 0f;
                sb.AppendLine($"{k.Name}: {v:0.##} {k.Unit}  (default {k.Default:0.##}, {pct:0}%)");
            }

            string dump = sb.ToString();
            GUIUtility.systemCopyBuffer = dump;
            if (_dumpText != null) _dumpText.text = dump;
            Debug.Log("[Settings]\n" + dump);
        }

        private void ResetValues()
        {
            // Push the authored value back through each knob's own setter first, so whatever cached
            // it (camera offset, energy pool) is re-seeded by the same path a slider move uses. Only
            // then drop the overrides — after which Or() returns the authored constant and they agree.
            foreach (var k in _knobs) k.Set(k.Default);
            DevTuning.Reset();

            foreach (var k in _knobs)
            {
                if (k.Slider != null) k.Slider.SetValueWithoutNotify(Mathf.Clamp(k.Default, k.Min, k.Max));
                UpdateValueText(k);
            }
            if (_dumpText != null) _dumpText.text = "";
        }

        // ------------------------------------------------------------------ uGUI helpers

        private static RectTransform NewRect(string name, Transform parent, Vector2 anchorMin,
                                             Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0f, 1f);
            return rt;
        }

        /// <summary>Place a top-left-pivoted rect at (x, y) in its parent's top-left space.</summary>
        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Image AddImage(Transform parent, Sprite sprite, Color color, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        private static Text AddText(Transform parent, string content, int size, Color color,
                                    TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = HudFont.Get();
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Reference-unit → point scale on the 6-inch target, for the layout test.</summary>
        public static float PhoneScale => Scale6Inch;

        /// <summary>Smallest font in the panel, in reference units. The test converts it.</summary>
        public static int SmallestFont => DumpFont;
    }
}
