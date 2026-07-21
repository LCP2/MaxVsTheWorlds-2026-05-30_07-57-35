using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Pickups;
using MaxWorlds.Upgrades;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The upgrade moment (YT-132): tap the flashing part chip → the game pauses → a screen shows Max
    /// with his hose and reveals the part you picked up, animating it onto the weapon → tap to resume.
    ///
    /// Dropped-part-decides (confirmed with Lee): there is no menu, you get the component you collected.
    /// The five concrete parts and the effect each applies are YT-133; this builds the flow and drives
    /// it with a generic placeholder part, so the screen, the pause, and the reveal-and-fit animation
    /// are all real and just need their five payloads slotted in.
    ///
    /// Self-installing UI director, same idiom as <see cref="SettingsPanel"/>: its own overlay canvas
    /// above the HUD, an EventSystem if the scene has none, hidden until opened. It pauses with
    /// <see cref="Time.timeScale"/> = 0 and therefore animates on <see cref="Time.unscaledDeltaTime"/>,
    /// which keeps running while the world is frozen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeScreen : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<UpgradeScreen>() != null) return;
            new GameObject("UpgradeScreen").AddComponent<UpgradeScreen>();
        }

        private const float RefW = 1920f, RefH = 1080f;

        private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.10f, 0.98f);
        private static readonly Color CardColor = new Color(0.12f, 0.14f, 0.17f, 1f);
        private static readonly Color BodyColor = new Color(0.90f, 0.45f, 0.16f);   // Max's orange greybox
        private static readonly Color HoseColor = new Color(0.24f, 0.55f, 0.30f);   // the hose
        private static readonly Color TextColor = Color.white;
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);

        // Animation timing (seconds, unscaled). reveal → fit → settle, then the continue prompt.
        private const float RevealTime = 0.45f;
        private const float FitTime = 0.45f;
        private const float SettleTime = 0.35f;

        private Canvas _canvas;
        private RectTransform _safeRoot;
        private GameObject _root;

        private RectTransform _partIcon;      // the part that reveals and flies onto the weapon
        private Text _partLabel;
        private RectTransform _mount;         // where the part fits on the weapon
        private Image _weaponGlow;            // flashes when the part settles
        private Text _title;
        private Text _continueHint;

        private bool _open;
        private float _prevTimeScale = 1f;
        private float _t;                     // unscaled time since Open
        private UpgradePart _part;
        private Vector2 _partStart, _partEnd;

        /// <summary>Is the upgrade screen currently up (and the game paused)?</summary>
        public bool IsOpen => _open;

        /// <summary>The part currently being installed (valid only while open).</summary>
        public UpgradePart Part => _part;

        private void Start() => Build();

        private void OnDestroy()
        {
            // Never leave the world frozen if we're torn down mid-open (a scene swap, a test).
            if (_open) Time.timeScale = _prevTimeScale;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        /// <summary>Open the screen for <paramref name="part"/>, pausing the game. Ignored if already
        /// open (you install one part at a time).</summary>
        public void Open(UpgradePart part)
        {
            if (_open) return;
            if (_canvas == null) Build();

            _part = part.IsValid ? part : UpgradePart.Generic;
            _open = true;
            _t = 0f;
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;   // freeze the fight; we animate on unscaled time

            _title.text = "UPGRADE";
            _partLabel.text = _part.Name;
            _partIcon.GetComponent<Image>().color = _part.Accent;
            _partLabel.color = _part.Accent;
            _continueHint.text = "TAP TO CONTINUE";

            _root.SetActive(true);
            LayoutAnimTargets();
            ApplyAnim(0f);
        }

        /// <summary>Finish the upgrade: bank the install (spend the pending part), resume, hide.</summary>
        public void Continue()
        {
            if (!_open) return;
            _open = false;
            Time.timeScale = _prevTimeScale;
            PickupWallet.SpendPart();   // this part is now installed
            _root.SetActive(false);
        }

        private void Update()
        {
            if (!_open) return;
            _t += Time.unscaledDeltaTime;
            ApplyAnim(_t);
        }

        // ------------------------------------------------------------------ animation

        private void LayoutAnimTargets()
        {
            // The part reveals up and to the right of the weapon, then flies to the mount point on it.
            _partEnd = _mount.anchoredPosition;
            _partStart = _partEnd + new Vector2(210f, 190f);
        }

        private void ApplyAnim(float t)
        {
            // Phase 1 — reveal: part scales up from nothing at its start spot.
            float reveal = Mathf.Clamp01(t / RevealTime);
            float scale = EaseOutBack(reveal);

            // Phase 2 — fit: part slides from start to the mount.
            float fit = Mathf.Clamp01((t - RevealTime) / FitTime);
            float glide = EaseInOut(fit);
            _partIcon.anchoredPosition = Vector2.Lerp(_partStart, _partEnd, glide);
            _partIcon.localScale = Vector3.one * Mathf.Lerp(1f, 0.72f, glide) * scale;

            // Phase 3 — settle: the weapon flashes as the part locks on, then eases off.
            float settle = Mathf.Clamp01((t - RevealTime - FitTime) / SettleTime);
            float flash = Mathf.Sin(settle * Mathf.PI);                 // 0 → 1 → 0
            var g = _part.Accent; g.a = 0.85f * flash;
            _weaponGlow.color = g;

            // The continue hint pulses once the part is on.
            bool done = t >= RevealTime + FitTime;
            _continueHint.gameObject.SetActive(done);
            if (done)
            {
                var c = Dim; c.a = 0.4f + 0.4f * Mathf.Abs(Mathf.Sin(t * 4f));
                _continueHint.color = c;
            }
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float p = x - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }

        private static float EaseInOut(float x) => x * x * (3f - 2f * x);

        // ------------------------------------------------------------------ build

        private void Build()
        {
            if (_canvas != null) return;
            EnsureEventSystem();

            var go = new GameObject("Upgrade Canvas", typeof(Canvas), typeof(CanvasScaler),
                                    typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 210;   // above the HUD (100) and the Settings panel (200)

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _safeRoot = NewRect("Safe Area", _canvas.transform, Vector2.zero, Vector2.one);
            Stretch(_safeRoot);
            _safeRoot.gameObject.AddComponent<SafeArea>();

            _root = new GameObject("Upgrade Root", typeof(RectTransform));
            var rootRt = (RectTransform)_root.transform;
            rootRt.SetParent(_safeRoot, false);
            Stretch(rootRt);

            // Full-screen scrim that both dims the frozen fight and, as a button, makes a tap anywhere
            // continue — the "tap to resume" of the ticket, without hunting for a small button.
            var scrim = AddImage(rootRt, HudTextures.Solid(), Scrim, "Scrim");
            Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;
            var scrimBtn = scrim.gameObject.AddComponent<Button>();
            scrimBtn.transition = Selectable.Transition.None;
            scrimBtn.onClick.AddListener(Continue);

            BuildPanel(rootRt);
            _root.SetActive(false);
        }

        private void BuildPanel(RectTransform parent)
        {
            var panel = NewRect("Panel", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            panel.sizeDelta = new Vector2(1180f, 640f);
            panel.anchoredPosition = Vector2.zero;
            var bg = AddImage(panel, HudTextures.RoundedBox(48, 0.5f), PanelColor, "BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced; bg.raycastTarget = true;

            _title = AddText(panel, 52, TextColor, TextAnchor.UpperCenter);
            Anchor(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            _title.rectTransform.sizeDelta = new Vector2(900f, 70f);
            _title.rectTransform.anchoredPosition = new Vector2(0f, -34f);
            _title.fontStyle = FontStyle.Bold;

            // Max + his hose — a greybox figure with a nozzle out front where the part mounts.
            var stage = NewRect("Stage", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            stage.sizeDelta = new Vector2(1000f, 420f);
            stage.anchoredPosition = new Vector2(0f, -6f);

            // Body (capsule) + head.
            var body = AddImage(stage, HudTextures.RoundedBox(64, 0.5f), BodyColor, "Max Body");
            Anchor(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            body.rectTransform.sizeDelta = new Vector2(150f, 250f);
            body.rectTransform.anchoredPosition = new Vector2(-210f, -10f);
            var head = AddImage(stage, HudTextures.Disc(96), BodyColor, "Max Head");
            Anchor(head.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            head.rectTransform.sizeDelta = new Vector2(120f, 120f);
            head.rectTransform.anchoredPosition = new Vector2(-210f, 150f);
            var mlabel = AddText(stage, 26, Color.white, TextAnchor.MiddleCenter);
            Anchor(mlabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            mlabel.rectTransform.sizeDelta = new Vector2(200f, 40f);
            mlabel.rectTransform.anchoredPosition = new Vector2(-210f, -150f);
            mlabel.text = "MAX";

            // The hose + nozzle (the weapon), reaching right from Max toward the mount.
            var hose = AddImage(stage, HudTextures.RoundedBox(24, 0.5f), HoseColor, "Hose");
            Anchor(hose.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            hose.rectTransform.sizeDelta = new Vector2(260f, 34f);
            hose.rectTransform.anchoredPosition = new Vector2(-20f, 20f);

            // The mount slot — where the part locks on — with a glow that flashes on settle.
            var slot = AddImage(stage, HudTextures.RoundedBox(32, 0.45f), CardColor, "Mount");
            Anchor(slot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            slot.rectTransform.sizeDelta = new Vector2(150f, 150f);
            slot.rectTransform.anchoredPosition = new Vector2(170f, 20f);
            _mount = slot.rectTransform;

            _weaponGlow = AddImage(stage, HudTextures.RoundedBox(40, 0.45f), new Color(0, 0, 0, 0), "Weapon Glow");
            Stretch(_weaponGlow.rectTransform, 14f);   // sits just outside the mount as a rim
            _weaponGlow.rectTransform.SetParent(slot.rectTransform, false);
            _weaponGlow.raycastTarget = false;

            // The part icon that reveals + flies onto the mount. Parented to the stage so its
            // anchoredPosition shares the mount's frame.
            var part = AddImage(stage, HudTextures.RoundedBox(32, 0.4f), BodyColor, "Part");
            Anchor(part.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            part.rectTransform.sizeDelta = new Vector2(130f, 130f);
            part.raycastTarget = false;
            _partIcon = part.rectTransform;

            _partLabel = AddText(stage, 30, TextColor, TextAnchor.MiddleCenter);
            Anchor(_partLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _partLabel.rectTransform.sizeDelta = new Vector2(520f, 48f);
            _partLabel.rectTransform.anchoredPosition = new Vector2(170f, -140f);
            _partLabel.fontStyle = FontStyle.Bold;

            _continueHint = AddText(panel, 30, Dim, TextAnchor.LowerCenter);
            Anchor(_continueHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            _continueHint.rectTransform.sizeDelta = new Vector2(700f, 44f);
            _continueHint.rectTransform.anchoredPosition = new Vector2(0f, 26f);
        }

        // ------------------------------------------------------------------ helpers

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            return rt;
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

        private static Text AddText(Transform parent, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = HudFont.Get();
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static void Anchor(RectTransform r, Vector2 min, Vector2 max, Vector2 pivot)
        {
            r.anchorMin = min; r.anchorMax = max; r.pivot = pivot;
        }

        private static void Stretch(RectTransform r, float padding = 0f)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(-padding, -padding);
            r.offsetMax = new Vector2(padding, padding);
        }
    }
}
