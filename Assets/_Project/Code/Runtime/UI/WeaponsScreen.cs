using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The weapons area (WV-232, v0.5 recut spec §6): a pause-on-enter screen for the RCDA primary's
    /// four tracks and whichever abilities Max has acquired so far. Visual glow-up is WV-238's job —
    /// this is the functional container: show levels, show the banked-part count, and spend one banked
    /// part on any owned track/ability through <see cref="MaxWorlds.Weapons.PartSpend"/>.
    ///
    /// Supersedes the legacy <see cref="UpgradeScreen"/> as the WEAPONS button's destination (see the
    /// "replaced wholesale, not renamed in place" note on <see cref="WeaponCatalog"/>) — that screen's
    /// other entry points (the pre-recut part-reveal/draft-pick flow, both already unreachable from
    /// anywhere in Runtime code) are left as-is.
    ///
    /// Self-installing overlay, same pause idiom as UpgradeScreen/HomeScreen/ResultScreen: its own
    /// canvas above the HUD, hidden until opened, freezes with <see cref="Time.timeScale"/> = 0 and
    /// restores whatever speed it paused from.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponsScreen : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<WeaponsScreen>() != null) return;
            new GameObject("WeaponsScreen").AddComponent<WeaponsScreen>();
        }

        private const float RefW = 1920f, RefH = 1080f;

        private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.97f);
        private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.10f, 0.98f);
        private static readonly Color RowColor = new Color(0.12f, 0.14f, 0.17f, 1f);
        private static readonly Color TextColor = Color.white;
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
        private static readonly Color SpendReady = new Color(1f, 0.85f, 0.35f);
        private static readonly Color SpendDisabled = new Color(1f, 1f, 1f, 0.25f);

        private const int TrackCount = 4;        // WeaponCatalog.AllTrackKinds.Length — every track is owned from run start
        private const int MaxAbilityRows = 6;    // WeaponCatalog.AllAbilityKinds.Length — the catalog's fixed pool
        private const float RowHeight = 60f;
        private const float RowStep = 68f;
        private const float ColumnWidth = 620f;

        private Canvas _canvas;
        private RectTransform _safeRoot;
        private GameObject _root;

        private Text _partsLabel;

        private readonly Text[] _trackName = new Text[TrackCount];
        private readonly Text[] _trackLevel = new Text[TrackCount];
        private readonly Button[] _trackButton = new Button[TrackCount];
        private readonly Image[] _trackButtonBg = new Image[TrackCount];

        // One row slot per catalog ability; only the acquired ones are shown, in catalog order
        // (WeaponSystemState.Acquired). Which AbilityKind occupies slot i changes as more get acquired,
        // so the kind behind each slot's button is tracked live in _abilityRowKind rather than baked
        // into the click handler at build time.
        private readonly GameObject[] _abilityRow = new GameObject[MaxAbilityRows];
        private readonly Text[] _abilityName = new Text[MaxAbilityRows];
        private readonly Text[] _abilityLevel = new Text[MaxAbilityRows];
        private readonly Button[] _abilityButton = new Button[MaxAbilityRows];
        private readonly Image[] _abilityButtonBg = new Image[MaxAbilityRows];
        private readonly AbilityKind[] _abilityRowKind = new AbilityKind[MaxAbilityRows];

        private bool _open;
        private float _prevTimeScale = 1f;

        /// <summary>Is the weapons area currently up (and the game paused)?</summary>
        public bool IsOpen => _open;

        private void Start() => Build();

        private void OnEnable()
        {
            WeaponSystemState.Changed += Refresh;
            PickupWallet.PartsChanged += OnPartsChanged;
        }

        private void OnDisable()
        {
            WeaponSystemState.Changed -= Refresh;
            PickupWallet.PartsChanged -= OnPartsChanged;
        }

        private void OnDestroy()
        {
            // Never leave the world frozen if we're torn down mid-open (a scene swap, a test).
            if (_open) Time.timeScale = _prevTimeScale;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        private void OnPartsChanged(int banked) => Refresh();

        /// <summary>Open the weapons area, pausing the game. Ignored if already open.</summary>
        public void Open()
        {
            if (_open) return;
            if (_canvas == null) Build();

            _open = true;
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;   // freeze the fight while the player reads/spends

            Refresh();
            _root.SetActive(true);
        }

        /// <summary>Close the weapons area and resume at whatever speed it paused from.</summary>
        public void Close()
        {
            if (!_open) return;
            _open = false;
            Time.timeScale = _prevTimeScale;
            _root.SetActive(false);
        }

        // ------------------------------------------------------------------ live state

        /// <summary>Redraws every row off the live systems — the four tracks, whichever abilities are
        /// currently acquired (in catalog order, WV-230), and the banked-part count — so a spend, or a
        /// shed granting a new ability while this screen happens to be open, reflects immediately.</summary>
        private void Refresh()
        {
            if (_root == null) return;

            int banked = PickupWallet.PartsBanked;
            _partsLabel.text = $"PARTS BANKED: {banked}";

            for (int i = 0; i < TrackCount; i++)
            {
                var kind = WeaponCatalog.AllTrackKinds[i];
                int level = WeaponSystemState.TrackLevel(kind);
                int cap = WeaponCatalog.MaxLevel(kind);
                _trackName[i].text = WeaponCatalog.DisplayName(kind);
                _trackLevel[i].text = $"Lv {level}/{cap}";
                SetSpendable(_trackButton[i], _trackButtonBg[i], banked > 0 && level < cap);
            }

            int shown = 0;
            foreach (var kind in WeaponSystemState.Acquired)
            {
                if (shown >= MaxAbilityRows) break;
                int level = WeaponSystemState.AbilityLevel(kind);
                int cap = WeaponCatalog.MaxLevel(kind);
                _abilityRowKind[shown] = kind;
                _abilityRow[shown].SetActive(true);
                _abilityName[shown].text = WeaponCatalog.DisplayName(kind);
                _abilityLevel[shown].text = $"Lv {level}/{cap}";
                SetSpendable(_abilityButton[shown], _abilityButtonBg[shown], banked > 0 && level < cap);
                shown++;
            }
            // Un-acquired abilities are never shown at all — no locked teasers (spec §6) — so every
            // slot past the acquired count just goes inactive rather than showing a dimmed row.
            for (int i = shown; i < MaxAbilityRows; i++)
                _abilityRow[i].SetActive(false);
        }

        private static void SetSpendable(Button button, Image bg, bool canSpend)
        {
            button.interactable = canSpend;
            bg.color = canSpend ? SpendReady : SpendDisabled;
        }

        private void OnTrackButtonTapped(int index) => PartSpend.TrySpendOnTrack(WeaponCatalog.AllTrackKinds[index]);

        private void OnAbilityButtonTapped(int row) => PartSpend.TrySpendOnAbility(_abilityRowKind[row]);

        // ------------------------------------------------------------------ build

        private void Build()
        {
            if (_canvas != null) return;
            EnsureEventSystem();

            var go = new GameObject("Weapons Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 210;   // same tier as the legacy UpgradeScreen it supersedes

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _safeRoot = NewRect("Safe Area", _canvas.transform, Vector2.zero, Vector2.one);
            Stretch(_safeRoot);
            _safeRoot.gameObject.AddComponent<SafeArea>();

            _root = new GameObject("Weapons Root", typeof(RectTransform));
            var rootRt = (RectTransform)_root.transform;
            rootRt.SetParent(_safeRoot, false);
            Stretch(rootRt);

            var scrim = AddImage(rootRt, HudTextures.Solid(), Scrim, "Scrim");
            Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;   // blocks taps to whatever's underneath while paused

            BuildPanel(rootRt);

            _root.SetActive(false);
        }

        private void BuildPanel(RectTransform parent)
        {
            var panel = NewRect("Panel", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            panel.sizeDelta = new Vector2(1400f, 760f);
            panel.anchoredPosition = Vector2.zero;
            var bg = AddImage(panel, HudTextures.RoundedBox(48, 0.5f), PanelColor, "BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced; bg.raycastTarget = true;

            var title = AddText(panel, 52, TextColor, TextAnchor.UpperCenter);
            Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            title.rectTransform.sizeDelta = new Vector2(900f, 70f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -34f);
            title.fontStyle = FontStyle.Bold;
            title.text = "WEAPONS";

            _partsLabel = AddText(panel, 30, TextColor, TextAnchor.UpperCenter);
            Anchor(_partsLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            _partsLabel.rectTransform.sizeDelta = new Vector2(900f, 44f);
            _partsLabel.rectTransform.anchoredPosition = new Vector2(0f, -96f);
            _partsLabel.fontStyle = FontStyle.Bold;

            BuildPrimaryColumn(panel);
            BuildAbilitiesColumn(panel);
            BuildCloseButton(panel);
        }

        private void BuildPrimaryColumn(RectTransform panel)
        {
            var column = NewRect("Primary Column", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            column.pivot = new Vector2(0.5f, 1f);
            column.sizeDelta = new Vector2(ColumnWidth, 600f);
            column.anchoredPosition = new Vector2(-(ColumnWidth * 0.5f + 20f), -150f);

            var header = AddText(column, 30, TextColor, TextAnchor.UpperLeft);
            header.rectTransform.anchorMin = new Vector2(0f, 1f);
            header.rectTransform.anchorMax = new Vector2(1f, 1f);
            header.rectTransform.pivot = new Vector2(0f, 1f);
            header.rectTransform.sizeDelta = new Vector2(0f, 40f);
            header.rectTransform.anchoredPosition = Vector2.zero;
            header.fontStyle = FontStyle.Bold;
            header.text = WeaponCatalog.PrimaryShortName;

            for (int i = 0; i < TrackCount; i++)
            {
                float y = -60f - i * RowStep;
                int index = i;   // capture by value, not the loop variable
                BuildRow(column, y, out _trackName[i], out _trackLevel[i], out _trackButton[i], out _trackButtonBg[i]);
                _trackButton[i].onClick.AddListener(() => OnTrackButtonTapped(index));
            }
        }

        private void BuildAbilitiesColumn(RectTransform panel)
        {
            var column = NewRect("Abilities Column", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            column.pivot = new Vector2(0.5f, 1f);
            column.sizeDelta = new Vector2(ColumnWidth, 600f);
            column.anchoredPosition = new Vector2(ColumnWidth * 0.5f + 20f, -150f);

            var header = AddText(column, 30, TextColor, TextAnchor.UpperLeft);
            header.rectTransform.anchorMin = new Vector2(0f, 1f);
            header.rectTransform.anchorMax = new Vector2(1f, 1f);
            header.rectTransform.pivot = new Vector2(0f, 1f);
            header.rectTransform.sizeDelta = new Vector2(0f, 40f);
            header.rectTransform.anchoredPosition = Vector2.zero;
            header.fontStyle = FontStyle.Bold;
            header.text = "ABILITIES";

            for (int i = 0; i < MaxAbilityRows; i++)
            {
                float y = -60f - i * RowStep;
                int row = i;   // capture by value, not the loop variable
                BuildRow(column, y, out _abilityName[i], out _abilityLevel[i], out _abilityButton[i], out _abilityButtonBg[i]);
                _abilityButton[i].onClick.AddListener(() => OnAbilityButtonTapped(row));
                _abilityRow[i] = _abilityButton[i].transform.parent.gameObject;
            }
        }

        private void BuildRow(RectTransform column, float y, out Text nameText, out Text levelText,
            out Button plusButton, out Image plusBg)
        {
            var row = NewRect("Row", column, new Vector2(0f, 1f), new Vector2(1f, 1f));
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, RowHeight);
            row.anchoredPosition = new Vector2(0f, y);

            var bg = AddImage(row, HudTextures.RoundedBox(20, 0.5f), RowColor, "BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;

            nameText = AddText(row, 26, TextColor, TextAnchor.MiddleLeft);
            SetHorizontalSlice(nameText.rectTransform, 0f, 0.6f, 20f, 8f);
            nameText.fontStyle = FontStyle.Bold;

            levelText = AddText(row, 24, Dim, TextAnchor.MiddleRight);
            SetHorizontalSlice(levelText.rectTransform, 0.6f, 0.84f, 8f, 8f);

            var buttonBg = AddImage(row, HudTextures.RoundedBox(16, 0.5f), SpendDisabled, "Plus");
            SetHorizontalSlice(buttonBg.rectTransform, 0.84f, 1f, 4f, 4f);
            buttonBg.type = Image.Type.Sliced;
            buttonBg.raycastTarget = true;
            plusBg = buttonBg;

            plusButton = buttonBg.gameObject.AddComponent<Button>();
            plusButton.transition = Selectable.Transition.None;

            var plusLabel = AddText(buttonBg.rectTransform, 30, TextColor, TextAnchor.MiddleCenter);
            Stretch(plusLabel.rectTransform);
            plusLabel.text = "+";
            plusLabel.fontStyle = FontStyle.Bold;
            plusLabel.raycastTarget = false;
        }

        private void BuildCloseButton(RectTransform panel)
        {
            var root = NewRect("Close Root", panel, new Vector2(1f, 1f), new Vector2(1f, 1f));
            root.sizeDelta = new Vector2(160f, 56f);
            root.anchoredPosition = new Vector2(-100f, -40f);

            var bg = AddImage(root, HudTextures.RoundedBox(28, 0.5f), RowColor, "Close Button");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced; bg.raycastTarget = true;

            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Close);

            var label = AddText(root, 24, TextColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.text = "CLOSE";
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
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

        /// <summary>Stretches a child vertically to fill its parent while occupying only the
        /// [<paramref name="xMin"/>, <paramref name="xMax"/>] horizontal fraction of it, inset by
        /// <paramref name="insetLeft"/>/<paramref name="insetRight"/> px — a row's name/level/button
        /// zones.</summary>
        private static void SetHorizontalSlice(RectTransform r, float xMin, float xMax, float insetLeft, float insetRight)
        {
            r.anchorMin = new Vector2(xMin, 0f);
            r.anchorMax = new Vector2(xMax, 1f);
            r.offsetMin = new Vector2(insetLeft, 0f);
            r.offsetMax = new Vector2(-insetRight, 0f);
        }

        private static void Stretch(RectTransform r, float padding = 0f)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(-padding, -padding);
            r.offsetMax = new Vector2(padding, padding);
        }
    }
}
