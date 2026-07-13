using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The in-run map (YT-72): a HUD button that opens the whole arena at a glance, with Max, the
    /// factory, the gate and the boss plotted live — then closes on a tap.
    ///
    /// It PAUSES while open (Pillar 1: you can check the map at the bus stop without dying). That's
    /// safe alongside the hit-stop, which refuses to start while the game is already frozen and only
    /// hands time back if it still owns it — so a hit-stop landing as the map opens can't un-pause
    /// the map, and the map can't cut a hit-stop short. The one case worth guarding is the run
    /// having already ended: resuming there would un-freeze the game behind the result card.
    /// </summary>
    public sealed class MapScreen : MonoBehaviour
    {
        private static readonly Color Dim = new Color(0.03f, 0.05f, 0.04f, 0.82f);
        private static readonly Color Panel = new Color(0.06f, 0.08f, 0.07f, 0.95f);
        private static readonly Color Ink = new Color(0.96f, 0.94f, 0.86f);
        private static readonly Color ButtonFace = new Color(0.10f, 0.13f, 0.12f, 0.85f);

        private RectTransform _overlay;
        private MapPanel _map;
        private InputAction _toggle;

        public bool IsOpen => _overlay != null && _overlay.gameObject.activeSelf;

        /// <summary>The map's own view of the arena — exposed so tests can assert the dot tracks
        /// Max without reading pixels.</summary>
        public MapPanel Map => _map;

        public void Build(RectTransform root, float refW, float refH)
        {
            EnsureEventSystem();
            BuildButton(root);
            BuildOverlay(root, refW, refH);

            // Desktop convenience; the button is the real control on a phone.
            _toggle = new InputAction("ToggleMap", InputActionType.Button, "<Keyboard>/m");
            _toggle.performed += _ => Toggle();
            _toggle.Enable();
        }

        private void OnDestroy()
        {
            _toggle?.Disable();
            _toggle?.Dispose();
        }

        /// <summary>Top-left. The bottom corners belong to the twin sticks and the top-right to the
        /// ability buttons — this is the one corner a thumb can reach without letting go of the
        /// fight.</summary>
        private void BuildButton(RectTransform root)
        {
            var go = new GameObject("Map Button", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(28f, -28f);
            rt.sizeDelta = new Vector2(96f, 72f);

            var img = go.GetComponent<Image>();
            img.sprite = HudTextures.RoundedBox(24, 0.3f);
            img.type = Image.Type.Sliced;
            img.color = ButtonFace;

            var label = NewText(rt, "MAP", 24, Ink);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.fontStyle = FontStyle.Bold;

            go.GetComponent<Button>().onClick.AddListener(Toggle);
        }

        private void BuildOverlay(RectTransform root, float refW, float refH)
        {
            var overlayGo = new GameObject("Map Overlay", typeof(RectTransform), typeof(Image), typeof(Button));
            _overlay = (RectTransform)overlayGo.transform;
            _overlay.SetParent(root, false);
            _overlay.anchorMin = Vector2.zero;
            _overlay.anchorMax = Vector2.one;
            _overlay.offsetMin = _overlay.offsetMax = Vector2.zero;

            var dim = overlayGo.GetComponent<Image>();
            dim.sprite = HudTextures.Solid();
            dim.color = Dim;
            overlayGo.GetComponent<Button>().onClick.AddListener(Close); // tap anywhere to close

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            var cardRt = (RectTransform)card.transform;
            cardRt.SetParent(_overlay, false);
            cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(refW * 0.52f, refH * 0.78f);
            var cardImg = card.GetComponent<Image>();
            cardImg.sprite = HudTextures.RoundedBox(32, 0.16f);
            cardImg.type = Image.Type.Sliced;
            cardImg.color = Panel;

            var title = NewText(cardRt, "THE BACKYARD", 34, Ink);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(0f, 44f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -14f);
            title.fontStyle = FontStyle.Bold;

            var planHost = new GameObject("Plan Host", typeof(RectTransform));
            var hostRt = (RectTransform)planHost.transform;
            hostRt.SetParent(cardRt, false);
            hostRt.anchorMin = hostRt.anchorMax = hostRt.pivot = new Vector2(0.5f, 0.5f);
            Vector2 hostSize = new Vector2(cardRt.sizeDelta.x - 60f, cardRt.sizeDelta.y - 150f);
            hostRt.sizeDelta = hostSize;
            hostRt.anchoredPosition = new Vector2(0f, -10f);

            var mapGo = new GameObject("Map", typeof(RectTransform));
            _map = mapGo.AddComponent<MapPanel>();
            _map.Build(hostRt, hostSize, opacity: 1f, markerScale: 1f);

            var hint = NewText(cardRt, "TAP TO CLOSE", 20, new Color(0.7f, 0.72f, 0.68f));
            hint.rectTransform.anchorMin = new Vector2(0f, 0f);
            hint.rectTransform.anchorMax = new Vector2(1f, 0f);
            hint.rectTransform.pivot = new Vector2(0.5f, 0f);
            hint.rectTransform.sizeDelta = new Vector2(0f, 30f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, 16f);

            _overlay.gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (IsOpen) return;
            if (RunHasEnded()) return;   // the result card owns the screen (and the pause) by then

            _overlay.gameObject.SetActive(true);
            _map?.Refresh();             // plot Max where he actually is, on the frame it opens
            Time.timeScale = 0f;
        }

        public void Close()
        {
            if (!IsOpen) return;
            _overlay.gameObject.SetActive(false);

            // Never resume a run that has ended while we were open — that would un-freeze the game
            // behind the result card.
            if (!RunHasEnded()) Time.timeScale = 1f;
        }

        private static bool RunHasEnded() => FindFirstObjectByType<ResultScreen>() != null;

        private static Text NewText(Transform parent, string text, int size, Color color)
        {
            var go = new GameObject(text, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = HudFont.Get();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();
        }
    }
}
