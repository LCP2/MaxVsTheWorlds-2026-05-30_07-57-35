using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;

namespace MaxWorlds.UI
{
    /// <summary>
    /// MV-438: the modal Max's death shows before the deferred respawn runs. MV-427 built the whole
    /// death-continues-the-run mechanic but shipped it as an instant, silent teleport — this is the
    /// beat that was missing, per Lee on <c>8cb70d3</c>: "when dying, there must be a popup screen like
    /// before offering quit to main menu or continue."
    ///
    /// CONTINUE resumes <see cref="MaxWorlds.Arena.WorldRunner"/>'s deferred respawn sequence; QUIT TO
    /// MAIN MENU bails out through the same <see cref="RunFlow.QuitToMenu"/> every other pause-style
    /// screen (Weapons, Settings) already shares — reusing that path, rather than inventing a second
    /// quit flow, is what keeps mid-run static-state reset (RigState/WeaponSystemState/PickupWallet/
    /// AbilityCreditBank/DeathRunState, all wired into <c>HomeScreen.StartSlot</c>) correct here too.
    ///
    /// Code-driven, same idiom as <see cref="WeaponsScreen"/>/<see cref="HomeScreen"/>: its own canvas,
    /// built lazily on first <see cref="Show"/>. Unlike those two it never self-installs — WorldRunner
    /// is the only caller and already owns the moment (<c>Time.timeScale = 0</c>,
    /// <c>DeathRunState.RecordDeath</c>) this overlay keys off, so an AfterSceneLoad hook would have
    /// nothing useful to pre-empt.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeathOverlay : MonoBehaviour
    {
        // MV-433's own reasoning, restated in the ticket: a translucent overlay over a lit lawn reads
        // as a glitch, not a pause — opaque enough that the gameplay behind is clearly inert.
        private static readonly Color Scrim = new Color(0.02f, 0.02f, 0.02f, 0.97f);
        private static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.10f, 0.99f);
        private static readonly Color TitleColor = new Color(1f, 0.72f, 0.28f);   // WeaponsScreen's amber, not red — MAX IS DOWN is not DEFEAT
        private static readonly Color TextColor = Color.white;
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.65f);

        // Same two colours WeaponsScreen's CLOSE/QUIT TO MENU already carry (BuildCloseButton/
        // BuildQuitButton) — the ticket asks to reuse them rather than invent a third style.
        private static readonly Color ContinueColor = new Color(1f, 0.72f, 0.28f);
        private static readonly Color QuitColor = new Color(0.85f, 0.20f, 0.20f);

        private const float RefW = 1920f, RefH = 1080f;

        private GameObject _root;
        private Text _titleText, _bodyText, _deathsText;
        private Button _continueButton, _quitButton;
        private Action _onContinue;

        /// <summary>Test-only access, same idiom as <see cref="WeaponsScreen.BoardNode"/>.</summary>
        public Button ContinueButton => _continueButton;
        public Button QuitButton => _quitButton;
        public bool IsOpen => _root != null && _root.activeSelf;
        public string TitleTextValue => _titleText != null ? _titleText.text : null;
        public string BodyTextValue => _bodyText != null ? _bodyText.text : null;
        public string DeathsTextValue => _deathsText != null ? _deathsText.text : null;

        /// <summary>The body copy naming what the death cost — pure so the wording is pinned by an
        /// EditMode test without building a canvas. Only claims the gate re-closed when it actually
        /// did (<paramref name="gateRecloses"/> is false for a boss-room death, RespawnPlanner's own
        /// edge case 2) — saying so anyway would tell the player something false about the boss gate.</summary>
        public static string BodyText(string areaName, bool gateRecloses) => gateRecloses
            ? $"{areaName} has reset. The gate is shut again — break it open."
            : $"{areaName} has reset.";

        /// <summary>The deaths-taken line — pure for the same reason as <see cref="BodyText"/>.</summary>
        public static string DeathsLine(int deathsTaken) =>
            deathsTaken == 1 ? "1 death this run" : $"{deathsTaken} deaths this run";

        /// <summary>Show the overlay for a death in <paramref name="areaName"/>. Idempotent-ish: a
        /// second call while already open just refreshes the copy and callback rather than stacking a
        /// second canvas.</summary>
        public void Show(string areaName, bool gateRecloses, int deathsTaken, Action onContinue)
        {
            if (_root == null) Build();

            _onContinue = onContinue;
            _titleText.text = "MAX IS DOWN";
            _bodyText.text = BodyText(areaName, gateRecloses);
            _deathsText.text = DeathsLine(deathsTaken);

            _root.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root);
        }

        private void OnContinueTapped()
        {
            Hide();
            _onContinue?.Invoke();
        }

        private void OnQuitTapped() => RunFlow.QuitToMenu();

        // ------------------------------------------------------------------ build

        private void Build()
        {
            EnsureEventSystem();

            var go = new GameObject("Death Overlay Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 220;   // above THE RIG (210) — a death mid-Rig-open can't happen (Time.timeScale=0 already), but stay above it regardless

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            var safeRoot = NewRect("Safe Area", go.transform, Vector2.zero, Vector2.one);
            Stretch(safeRoot);
            safeRoot.gameObject.AddComponent<SafeArea>();

            _root = new GameObject("Death Overlay Root", typeof(RectTransform));
            var rootRt = (RectTransform)_root.transform;
            rootRt.SetParent(safeRoot, false);
            Stretch(rootRt);

            var scrim = AddImage(rootRt, HudTextures.Solid(), Scrim, "Scrim");
            Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;   // blocks taps to the frozen gameplay underneath

            const float panelW = 900f, panelH = 480f;
            var panel = AddImage(rootRt, HudTextures.RoundedBox(48, 0.12f), PanelColor, "Panel");
            Anchor(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            panel.rectTransform.sizeDelta = new Vector2(panelW, panelH);
            panel.type = Image.Type.Sliced;
            panel.raycastTarget = true;

            _titleText = AddText(panel.rectTransform, 52, TitleColor, TextAnchor.UpperCenter);
            Anchor(_titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            _titleText.rectTransform.sizeDelta = new Vector2(-80f, 70f);
            _titleText.rectTransform.anchoredPosition = new Vector2(0f, -56f);
            _titleText.fontStyle = FontStyle.Bold;
            _titleText.horizontalOverflow = HorizontalWrapMode.Wrap;

            _bodyText = AddText(panel.rectTransform, 26, TextColor, TextAnchor.UpperCenter);
            Anchor(_bodyText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            _bodyText.rectTransform.sizeDelta = new Vector2(-120f, 100f);
            _bodyText.rectTransform.anchoredPosition = new Vector2(0f, -160f);
            _bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;

            _deathsText = AddText(panel.rectTransform, 20, Dim, TextAnchor.UpperCenter);
            Anchor(_deathsText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            _deathsText.rectTransform.sizeDelta = new Vector2(-120f, 34f);
            _deathsText.rectTransform.anchoredPosition = new Vector2(0f, -280f);
            _deathsText.horizontalOverflow = HorizontalWrapMode.Wrap;

            // QUIT TO MAIN MENU — red, secondary, left. CONTINUE — amber, primary, right. Same
            // left-destructive/right-primary layout WeaponsScreen's top bar already reads (CLOSE then
            // QUIT built right-to-left from the bar's right edge puts QUIT further left of CLOSE; here
            // the two sit on their own row so the convention is spelled out directly instead).
            _quitButton = BuildButton(panel.rectTransform, "QUIT TO MAIN MENU", QuitColor, TextColor,
                new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-10f, 60f),
                new Vector2(360f, 76f), OnQuitTapped);

            _continueButton = BuildButton(panel.rectTransform, "CONTINUE", ContinueColor, PanelColor,
                new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(10f, 60f),
                new Vector2(360f, 76f), OnContinueTapped);

            _root.SetActive(false);
        }

        private Button BuildButton(RectTransform parent, string label, Color bg, Color textColor,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size,
            UnityEngine.Events.UnityAction onClick)
        {
            var bgImg = AddImage(parent, HudTextures.RoundedBox(32, 0.5f), bg, label + " Button");
            Anchor(bgImg.rectTransform, anchorMin, anchorMax, pivot);
            bgImg.rectTransform.anchoredPosition = anchoredPosition;
            bgImg.rectTransform.sizeDelta = size;
            bgImg.type = Image.Type.Sliced;
            bgImg.raycastTarget = true;

            var button = bgImg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(onClick);

            var text = AddText(bgImg.rectTransform, 24, textColor, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            text.text = label;
            text.fontStyle = FontStyle.Bold;
            text.raycastTarget = false;

            return button;
        }

        // ------------------------------------------------------------------ helpers (WeaponsScreen/HomeScreen idiom)

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
        {
            var rgo = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)rgo.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            return rt;
        }

        private static Image AddImage(Transform parent, Sprite sprite, Color color, string name)
        {
            var igo = new GameObject(name, typeof(RectTransform), typeof(Image));
            igo.transform.SetParent(parent, false);
            var img = igo.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        private static Text AddText(Transform parent, int size, Color color, TextAnchor anchor)
        {
            var tgo = new GameObject("Text", typeof(RectTransform));
            tgo.transform.SetParent(parent, false);
            var t = tgo.AddComponent<Text>();
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
