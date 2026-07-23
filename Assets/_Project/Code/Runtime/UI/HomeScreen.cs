using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Dev;
using MaxWorlds.Intro;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.Upgrades;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The game's Home screen (YT-151): the first thing up on boot, three save slots wide. An empty
    /// slot offers New Game; an occupied one shows its progress and offers Continue (resume exactly
    /// where Max stood) or New Game as an overwrite. Picking a slot hands off to
    /// <see cref="SaveSystem.ActiveSlot"/>, which is also what stops this screen reopening on a
    /// Replay-triggered scene reload — once a slot is live, <see cref="MaxWorlds.Core.SceneInstallers"/>
    /// re-running <see cref="Install"/> after a death/Replay finds the slot already set and leaves the
    /// run alone.
    ///
    /// Code-driven overlay, same idiom as <see cref="ResultScreen"/>/<see cref="UpgradeScreen"/>: its
    /// own canvas above the HUD, built in code, paused via <see cref="Time.timeScale"/> = 0 while a
    /// choice is pending. Skips itself entirely (silently drops into slot 0) when
    /// <see cref="PressKitDirector.Armed"/> — a filming run has nothing to click the modal with, and the
    /// captured shots must not open on a frozen pick-a-slot screen (YT-97).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeScreen : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<HomeScreen>() != null) return;
            new GameObject("HomeScreen").AddComponent<HomeScreen>();
        }

        private const float RefW = 1920f, RefH = 1080f;
        private const float RowHeight = 190f;
        private const float RowGap = 20f;

        private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.82f);
        private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.10f, 0.98f);
        private static readonly Color SlotColor = new Color(0.12f, 0.14f, 0.17f, 1f);
        private static readonly Color Gold = new Color(0.957f, 0.788f, 0.365f);
        private static readonly Color Bone = new Color(0.96f, 0.94f, 0.86f);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color Green = new Color(0.49f, 0.76f, 0.42f);
        private static readonly Color Grey = new Color(0.3f, 0.34f, 0.4f);

        private GameObject _root;
        private float _prevTimeScale = 1f;
        private bool _open;

        /// <summary>Is the pick-a-slot modal currently up (and the game paused)? Tests read this.</summary>
        public bool IsOpen => _open;

        private void Start()
        {
            if (SaveSystem.ActiveSlot >= 0) return;   // a slot is already live (defensive re-add)

            if (PressKitDirector.Armed())
            {
                // Filming has nothing to click the modal with — hand off to slot 0 straight away,
                // resuming it if it has data, without pausing or showing anything (YT-97).
                bool hasSave = SaveSystem.Load(0).HasData;
                StartSlot(0, resume: hasSave, playIntro: false);
                return;
            }

            Open();
        }

        private void OnDestroy()
        {
            // Never leave the world frozen if this is torn down while still open (a scene swap, a
            // test) — same safety net as UpgradeScreen.
            if (_open) Time.timeScale = _prevTimeScale;
        }

        private void Open()
        {
            if (_open) return;
            _open = true;
            EnsureEventSystem();
            Build();
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        private void Close()
        {
            _open = false;
            Time.timeScale = _prevTimeScale;
            if (_root != null) Destroy(_root);
        }

        // ------------------------------------------------------------------ actions

        private void StartSlot(int slot, bool resume, bool playIntro)
        {
            SaveSystem.ActiveSlot = slot;

            if (resume)
            {
                SaveSlotData data = SaveSystem.Load(slot);
                SaveSystem.Apply(data);
                SaveSystem.PlacePlayer(data);
            }
            else
            {
                UpgradeState.Reset();
                PickupWallet.Reset();
                SaveSystem.CaptureAndSave(slot, SaveSystem.DefaultLevelId);   // seed the slot immediately
                if (playIntro) IntroCinematic.TryPlay();
            }
        }

        private void OnContinue(int slot)
        {
            StartSlot(slot, resume: true, playIntro: false);
            Close();
        }

        private void OnNewGame(int slot)
        {
            StartSlot(slot, resume: false, playIntro: true);
            Close();
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            var go = new GameObject("Home Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _root = go;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 220;   // above Upgrade (210) and Result/Settings (200)
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 0.5f;

            var safeRoot = NewRect("Safe Area", go.transform, Vector2.zero, Vector2.one);
            Stretch(safeRoot);
            safeRoot.gameObject.AddComponent<SafeArea>();

            var dim = AddImage(safeRoot, HudTextures.Solid(), Scrim, "Dim");
            Stretch(dim.rectTransform);

            var panel = AddImage(safeRoot, HudTextures.RoundedBox(48, 0.12f), PanelColor, "Panel");
            panel.type = Image.Type.Sliced;
            Center(panel.rectTransform, 1200f, 900f);

            var title = AddText(panel.rectTransform, 56f, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            Top(title.rectTransform, 0f, -46f, 1000f, 70f);
            title.text = "MAX vs THE WORLDS";

            var sub = AddText(panel.rectTransform, 24f, Dim, TextAnchor.MiddleCenter, FontStyle.Normal);
            Top(sub.rectTransform, 0f, -108f, 1000f, 36f);
            sub.text = "SELECT A SAVE";

            const float top = -170f;
            for (int i = 0; i < SaveSystem.SlotCount; i++)
            {
                BuildSlotRow(panel.rectTransform, i, top - i * (RowHeight + RowGap));
            }
        }

        private void BuildSlotRow(RectTransform panel, int slot, float y)
        {
            var row = AddImage(panel, HudTextures.RoundedBox(32, 0.3f), SlotColor, $"Slot {slot + 1}");
            row.type = Image.Type.Sliced;
            Top(row.rectTransform, 0f, y, 1080f, RowHeight);

            SaveSlotData data = SaveSystem.Load(slot);

            var label = AddText(row.rectTransform, 30f, Bone, TextAnchor.UpperLeft, FontStyle.Bold);
            Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            label.rectTransform.sizeDelta = new Vector2(400f, 40f);
            label.rectTransform.anchoredPosition = new Vector2(34f, -20f);
            label.text = $"SLOT {slot + 1}";

            var status = AddText(row.rectTransform, 22f, Dim, TextAnchor.UpperLeft, FontStyle.Normal);
            Anchor(status.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            status.rectTransform.sizeDelta = new Vector2(620f, 100f);
            status.rectTransform.anchoredPosition = new Vector2(34f, -66f);
            status.text = data.HasData ? Summarise(data) : "Empty";

            if (data.HasData)
            {
                var continueBtn = AddButton(row.rectTransform, "CONTINUE", Green, true, () => OnContinue(slot));
                Anchor(continueBtn, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                continueBtn.sizeDelta = new Vector2(280f, 64f);
                continueBtn.anchoredPosition = new Vector2(-190f, 18f);

                var newGameBtn = AddButton(row.rectTransform, "NEW GAME", Grey, true, () => OnNewGame(slot));
                Anchor(newGameBtn, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                newGameBtn.sizeDelta = new Vector2(200f, 44f);
                newGameBtn.anchoredPosition = new Vector2(-40f, -46f);
            }
            else
            {
                var newGameBtn = AddButton(row.rectTransform, "NEW GAME", Green, true, () => OnNewGame(slot));
                Anchor(newGameBtn, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                newGameBtn.sizeDelta = new Vector2(280f, 64f);
                newGameBtn.anchoredPosition = new Vector2(-110f, 0f);
            }
        }

        private static string Summarise(SaveSlotData data)
        {
            string level = data.LevelId == SaveSystem.DefaultLevelId ? "Backyard" : data.LevelId;
            return $"{level}  ·  {data.InstalledParts.Count}/5 parts  ·  {data.PowerCells} cells";
        }

        // ------------------------------------------------------------------ helpers (ResultScreen/UpgradeScreen idiom)

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            var module = es.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            return rt;
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

            var t = AddText((RectTransform)go.transform, 22f, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
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
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static void Anchor(RectTransform r, Vector2 min, Vector2 max, Vector2 pivot)
        {
            r.anchorMin = min; r.anchorMax = max; r.pivot = pivot;
        }

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
    }
}
