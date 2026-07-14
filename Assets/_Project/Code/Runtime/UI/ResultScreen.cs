using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Slice Result screen (YT-31, spec §4.9). Built entirely in code — a dim overlay with a
    /// VICTORY/DEFEAT banner, the run's stat card (time, kills, factory destroyed), and the CTAs
    /// (Replay, Next World — locked in the slice). Shown by <see cref="RunTracker"/> when a run
    /// ends; it pauses the game (timeScale 0) and offers Replay via a clickable button or the R
    /// key. Loads instantly (a code-built canvas), meeting the sub-3-second AC.
    /// </summary>
    public sealed class ResultScreen : MonoBehaviour
    {
        private static readonly Color Dim = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color Panel = new Color(0.08f, 0.10f, 0.14f, 0.96f);
        private static readonly Color Gold = new Color(0.957f, 0.788f, 0.365f);
        private static readonly Color Bone = new Color(0.96f, 0.94f, 0.86f);
        private static readonly Color Green = new Color(0.49f, 0.76f, 0.42f);
        private static readonly Color Red = new Color(0.90f, 0.30f, 0.28f);

        private InputAction _replay;

        /// <summary>Build and show the screen for a finished run. Pauses the game.</summary>
        public void Show(RunStats stats)
        {
            EnsureEventSystem();
            BuildCanvas(stats);

            _replay = new InputAction("ResultReplay", InputActionType.Button);
            _replay.AddBinding("<Keyboard>/r");
            _replay.AddBinding("<Keyboard>/enter");
            _replay.Enable();

            Time.timeScale = 0f; // freeze the run behind the card
        }

        private void Update()
        {
            if (_replay != null && _replay.WasPressedThisFrame()) Replay();
        }

        private void OnDestroy()
        {
            _replay?.Dispose();
        }

        private void Replay()
        {
            Time.timeScale = 1f;
            var scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }

        private void BuildCanvas(RunStats stats)
        {
            var go = new GameObject("Result Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200; // above the HUD (100)
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)go.transform;

            var dim = AddImage(root, HudTextures.Solid(), Dim, "Dim");
            Stretch(dim.rectTransform);

            var panel = AddImage(root, HudTextures.RoundedBox(48, 0.12f), Panel, "Panel");
            panel.type = Image.Type.Sliced;
            Center(panel.rectTransform, ResultLayout.PanelWidth, ResultLayout.PanelHeight);

            bool win = stats.Outcome == RunOutcome.Victory;
            var title = AddText(panel.rectTransform, 78f, win ? Gold : Red, TextAnchor.MiddleCenter, FontStyle.Bold);
            Top(title.rectTransform, 0f, -60f, 680f, 90f);
            title.text = stats.Title;

            var sub = AddText(panel.rectTransform, 26f, Bone, TextAnchor.MiddleCenter, FontStyle.Normal);
            Top(sub.rectTransform, 0f, -140f, 680f, 40f);
            sub.text = win ? "Backyard slice cleared" : "Max was overrun";

            // Stat rows.
            float y = -210f;
            AddStatRow(panel.rectTransform, "TIME", RunStats.FormatTime(stats.Elapsed), ref y);
            AddStatRow(panel.rectTransform, "ROBOTS DESTROYED", stats.Kills.ToString(), ref y);
            AddStatRow(panel.rectTransform, "FACTORIES DESTROYED", stats.FactoriesDestroyed.ToString(), ref y);
            AddStatRow(panel.rectTransform, "DIFFICULTY", "NORMAL", ref y);

            // CTAs. Both sit on the same content column as the stat rows above (YT-81) — REPLAY used
            // to be placed at a bare -300, which with a centred pivot put its left edge 90px outside
            // the panel entirely.
            var replayBtn = AddButton(panel.rectTransform, "REPLAY  (R)", Green, true, Replay);
            Bottom(replayBtn, ResultLayout.LeftButtonX, 40f,
                   ResultLayout.ButtonWidth, ResultLayout.ButtonHeight);

            var nextBtn = AddButton(panel.rectTransform, "NEXT WORLD", new Color(0.3f, 0.34f, 0.4f), false, null);
            Bottom(nextBtn, ResultLayout.RightButtonX, 40f,
                   ResultLayout.ButtonWidth, ResultLayout.ButtonHeight);

            var lockNote = AddText(panel.rectTransform, 16f, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleCenter, FontStyle.Normal);
            Bottom((RectTransform)lockNote.transform, ResultLayout.RightButtonX, 14f,
                   ResultLayout.ButtonWidth, 20f);
            lockNote.text = "locked in the slice";
        }

        private void AddStatRow(RectTransform panel, string label, string value, ref float y)
        {
            var l = AddText(panel, 24f, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleLeft, FontStyle.Normal);
            Top(l.rectTransform, ResultLayout.StatLabelX, y, ResultLayout.StatCellWidth, 34f);
            l.text = label;
            var v = AddText(panel, 26f, Bone, TextAnchor.MiddleRight, FontStyle.Bold);
            Top(v.rectTransform, ResultLayout.StatValueX, y, ResultLayout.StatCellWidth, 34f);
            v.text = value;
            y -= 42f;
        }

        // --- interaction plumbing ---

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            var module = es.AddComponent<InputSystemUIInputModule>();
            // Wire the default point/click/navigate actions so buttons are clickable in a
            // project using the new Input System (no editor setup).
            module.AssignDefaultActions();
        }

        private RectTransform AddButton(RectTransform parent, string label, Color color, bool interactable,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = HudTextures.RoundedBox(32, 0.3f);
            img.type = Image.Type.Sliced;
            img.color = color;
            var btn = go.GetComponent<Button>();
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var t = AddText((RectTransform)go.transform, 26f, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(t.rectTransform);
            t.text = label;
            return (RectTransform)go.transform;
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

        private static Text AddText(Transform parent, float size, Color color, TextAnchor align, FontStyle style)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = HudFont.Get();
            t.fontSize = Mathf.RoundToInt(size);
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // --- layout helpers ---

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }

        private static void Center(RectTransform r, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = Vector2.zero;
        }

        private static void Top(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = new Vector2(x, y);
        }

        private static void Bottom(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = new Vector2(x, y);
        }
    }
}
