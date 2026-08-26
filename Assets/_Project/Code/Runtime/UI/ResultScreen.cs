using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Core;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Slice Result screen (YT-31, spec §4.9). Built entirely in code — a dim overlay with the
    /// VICTORY banner, the run's stat card (time, kills, factory destroyed), and NEXT WORLD (locked
    /// in the slice). Shown by <see cref="RunTracker"/> once the boss falls and its payoff finishes;
    /// it pauses the game (timeScale 0). Loads instantly (a code-built canvas), meeting the
    /// sub-3-second AC.
    ///
    /// MV-427: Victory-only now. Death no longer ends the run (it respawns Max instead, handled by
    /// <see cref="MaxWorlds.Arena.WorldRunner"/>), so this screen — and its old REPLAY CTA, which
    /// reloaded the whole scene — never has a Defeat outcome to show any more.
    /// </summary>
    public sealed class ResultScreen : MonoBehaviour
    {
        private static readonly Color Dim = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color Panel = new Color(0.08f, 0.10f, 0.14f, 0.96f);
        private static readonly Color Gold = new Color(0.957f, 0.788f, 0.365f);
        private static readonly Color Bone = new Color(0.96f, 0.94f, 0.86f);

        /// <summary>Build and show the screen for a finished (Victory) run. Pauses the game.</summary>
        public void Show(RunStats stats)
        {
            EnsureEventSystem();
            BuildCanvas(stats);
            Time.timeScale = 0f; // freeze the run behind the card
            ModalFrameRateGate.Enter();   // MV-574: idle the frame rate — this screen never closes in the slice
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

            // MV-427: the only outcome that ever reaches this screen now is Victory — death respawns
            // Max instead of ending the run, so there is no DEFEAT banner/near-miss/REPLAY branch left.
            var title = AddText(panel.rectTransform, 78f, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            Top(title.rectTransform, 0f, -60f, 680f, 90f);
            title.text = stats.Title;

            var sub = AddText(panel.rectTransform, 26f, Bone, TextAnchor.MiddleCenter, FontStyle.Normal);
            Top(sub.rectTransform, 0f, -140f, 680f, 40f);
            sub.text = "Backyard slice cleared";

            // Stat rows.
            float y = -210f;
            AddStatRow(panel.rectTransform, "TIME", RunStats.FormatTime(stats.Elapsed), ref y);
            AddStatRow(panel.rectTransform, "ROBOTS DESTROYED", stats.Kills.ToString(), ref y);
            AddStatRow(panel.rectTransform, "FACTORIES DESTROYED", stats.FactoriesDestroyed.ToString(), ref y);
            AddStatRow(panel.rectTransform, "DIFFICULTY", "NORMAL", ref y);

            // One CTA now that REPLAY is gone (MV-427) — centred on the panel rather than the old
            // two-button RightButtonX slot.
            var nextBtn = AddButton(panel.rectTransform, "NEXT WORLD", new Color(0.3f, 0.34f, 0.4f), false, null);
            Bottom(nextBtn, 0f, 40f, ResultLayout.ButtonWidth, ResultLayout.ButtonHeight);

            var lockNote = AddText(panel.rectTransform, 16f, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleCenter, FontStyle.Normal);
            Bottom((RectTransform)lockNote.transform, 0f, 14f, ResultLayout.ButtonWidth, 20f);
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
