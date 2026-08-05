using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;
using MaxWorlds.VFX;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The weapons area (MV-248, v0.5 recut spec §6): a pause-on-enter screen for the RCDA primary's
    /// four upgrade tracks and whichever abilities Max has acquired so far, reimplemented to the
    /// intended design (MV-248) after MV-234 shipped only a bare functional panel — a centred title,
    /// "Lv x/y" text rows and an empty Abilities column, none of the intended layout/colour/copy.
    ///
    /// Layout: a title + CELLS/PARTS/PAUSED cluster up top, a hero column on the left (Max's live
    /// portrait + the primary's name), and on the right the 2x2 primary-track grid followed by the
    /// abilities section (owned abilities only, plus a placeholder naming what's still locked) and an
    /// amber spendbar. Levels render as pip/segment bars, not text — <see cref="BuildGridRow"/> builds
    /// a shared row shape (icon, name, pip bar, + button) for both tracks and abilities so the two
    /// sections can never drift apart in style.
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
        private static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.06f, 0.99f);
        private static readonly Color HeaderAccent = new Color(0.07f, 0.17f, 0.15f, 1f);
        private static readonly Color RowColor = new Color(0.10f, 0.12f, 0.15f, 1f);
        private static readonly Color CardColor = new Color(0.10f, 0.12f, 0.15f, 1f);
        private static readonly Color TextColor = Color.white;
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);

        private static readonly Color CellsColor = new Color(0.35f, 0.85f, 0.95f);
        private static readonly Color PartsColor = new Color(1f, 0.72f, 0.28f);
        private static readonly Color PrimaryAccent = new Color(0.66f, 0.48f, 0.98f);   // primary-weapon pips (spec: purple)
        private static readonly Color AbilityAccent = new Color(0.35f, 0.85f, 0.95f);   // ability pips (spec: cyan)
        private static readonly Color PipEmpty = new Color(1f, 1f, 1f, 0.14f);

        private static readonly Color SpendReady = PartsColor;
        private static readonly Color SpendDisabled = new Color(1f, 1f, 1f, 0.18f);

        private const int TrackCount = 4;        // WeaponCatalog.AllTrackKinds.Length — every track is owned from run start
        private const int MaxAbilityRows = 6;    // WeaponCatalog.AllAbilityKinds.Length — the catalog's fixed pool
        private const int MaxPips = 6;           // largest cap across tracks (Range=6) and abilities (PowerEfficiency/WeaponCooldown=5)

        // Sized so the worst-case ability grid (5 acquired + the placeholder = 4 rows: MaxAbilityRows-1
        // acquired is the most crowded state that still needs the placeholder) fits inside the content
        // budget below the top bar/spendbar with room to spare — see the layout arithmetic in
        // BuildAbilitiesSection's siblings; there's no runtime overflow check, so this is verified by
        // hand rather than measured.
        private const float RowHeight = 100f;
        private const float RowGap = 8f;
        private const float SectionHeaderHeight = 38f;
        private const float SectionHeaderGap = 10f;
        private const float SectionGap = 16f;
        private const float TopBarHeight = 104f;
        private const float SpendbarHeight = 92f;
        private const float ContentMargin = 28f;
        private const float ColGap = 0.04f;      // fraction of column width between the 2 grid cells

        private Canvas _canvas;
        private RectTransform _safeRoot;
        private GameObject _root;
        private MaxPortraitStage _maxStage;

        private Text _cellsText;
        private Text _partsText;
        private Text _abilitiesHeaderText;
        private Text _spendbarText;

        private readonly Text[] _trackName = new Text[TrackCount];
        private readonly Image[][] _trackPips = new Image[TrackCount][];
        private readonly Button[] _trackButton = new Button[TrackCount];
        private readonly Image[] _trackButtonBg = new Image[TrackCount];

        // One row slot per catalog ability; only the acquired ones are shown, in catalog order
        // (WeaponSystemState.Acquired). Which AbilityKind occupies slot i changes as more get acquired,
        // so the kind behind each slot's button is tracked live in _abilityRowKind rather than baked
        // into the click handler at build time.
        private readonly GameObject[] _abilityRow = new GameObject[MaxAbilityRows];
        private readonly Text[] _abilityName = new Text[MaxAbilityRows];
        private readonly Image[][] _abilityPips = new Image[MaxAbilityRows][];
        private readonly Button[] _abilityButton = new Button[MaxAbilityRows];
        private readonly Image[] _abilityButtonBg = new Image[MaxAbilityRows];
        private readonly AbilityKind[] _abilityRowKind = new AbilityKind[MaxAbilityRows];

        private RectTransform _placeholderRow;
        private Text _placeholderText;
        private float _abilitiesGridTop;

        private bool _open;
        private float _prevTimeScale = 1f;

        /// <summary>Is the weapons area currently up (and the game paused)?</summary>
        public bool IsOpen => _open;

        private void Start() => Build();

        private void OnEnable()
        {
            WeaponSystemState.Changed += Refresh;
            PickupWallet.PartsChanged += OnPartsChanged;
            PickupWallet.PowerCellsChanged += OnCellsChanged;
        }

        private void OnDisable()
        {
            WeaponSystemState.Changed -= Refresh;
            PickupWallet.PartsChanged -= OnPartsChanged;
            PickupWallet.PowerCellsChanged -= OnCellsChanged;
        }

        private void OnDestroy()
        {
            // Never leave the world frozen if we're torn down mid-open (a scene swap, a test).
            if (_open) Time.timeScale = _prevTimeScale;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        private void Update()
        {
            if (_open && _maxStage != null) _maxStage.Tick(Time.unscaledTime);
        }

        private void OnPartsChanged(int banked) => Refresh();
        private void OnCellsChanged(int cells) => Refresh();

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
            if (_maxStage != null) _maxStage.Show();
        }

        /// <summary>Close the weapons area and resume at whatever speed it paused from.</summary>
        public void Close()
        {
            if (!_open) return;
            _open = false;
            Time.timeScale = _prevTimeScale;
            _root.SetActive(false);
            if (_maxStage != null) _maxStage.Hide();
        }

        // ------------------------------------------------------------------ live state

        /// <summary>Redraws every row off the live systems — the four tracks, whichever abilities are
        /// currently acquired (in catalog order, WV-230) as pip bars, and the CELLS/PARTS banks — so a
        /// spend, a pickup, or a shed granting a new ability while this screen happens to be open
        /// reflects immediately.</summary>
        private void Refresh()
        {
            if (_root == null) return;

            int banked = PickupWallet.PartsBanked;
            _cellsText.text = $"{PickupWallet.PowerCells} CELLS";
            _partsText.text = $"{banked} PARTS";
            _spendbarText.text = $"You have {banked} parts banked — tap + on a track to spend one. " +
                "Every ability has a cooldown; Weapon Cooldown (not yet owned) shortens them all.";

            for (int i = 0; i < TrackCount; i++)
            {
                var kind = WeaponCatalog.AllTrackKinds[i];
                int level = WeaponSystemState.TrackLevel(kind);
                int cap = WeaponCatalog.MaxLevel(kind);
                _trackName[i].text = WeaponCatalog.TitleCase(WeaponCatalog.DisplayName(kind));
                SetPips(_trackPips[i], level, cap, PrimaryAccent);
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
                _abilityName[shown].text = WeaponCatalog.TitleCase(WeaponCatalog.DisplayName(kind));
                SetPips(_abilityPips[shown], level, cap, AbilityAccent);
                SetSpendable(_abilityButton[shown], _abilityButtonBg[shown], banked > 0 && level < cap);
                shown++;
            }
            // Un-acquired abilities are never shown at all — no locked teasers (spec §6) — so every
            // slot past the acquired count just goes inactive rather than showing a dimmed row.
            for (int i = shown; i < MaxAbilityRows; i++)
                _abilityRow[i].SetActive(false);

            _abilitiesHeaderText.text = $"ABILITIES — acquired ({shown} of {MaxAbilityRows}) · shown only once owned";

            RefreshPlaceholder(shown);
        }

        /// <summary>The dashed "unlock from sheds" row naming whatever abilities Max doesn't own yet —
        /// sits directly under the last populated ability row, never shown once all six are owned.</summary>
        private void RefreshPlaceholder(int shownCount)
        {
            var names = new System.Text.StringBuilder();
            bool any = false;
            foreach (var kind in WeaponSystemState.Unacquired)
            {
                if (any) names.Append("  ·  ");
                names.Append(WeaponCatalog.TitleCase(WeaponCatalog.DisplayName(kind)));
                any = true;
            }

            _placeholderRow.gameObject.SetActive(any);
            if (!any)
            {
                _placeholderText.text = string.Empty;   // don't leave stale copy behind an inactive row
                return;
            }

            int rowsUsed = (shownCount + 1) / 2;   // ceil(shownCount / 2)
            PlaceGridRow(_placeholderRow, rowsUsed, _abilitiesGridTop, RowHeight, RowGap, cols: 1);
            _placeholderText.text = $"{names}  — unlock from sheds —";
        }

        private static void SetPips(Image[] pips, int level, int cap, Color filled)
        {
            for (int i = 0; i < pips.Length; i++)
            {
                bool active = i < cap;
                pips[i].gameObject.SetActive(active);
                if (!active) continue;
                bool isFilled = i < level;
                pips[i].color = isFilled ? filled : PipEmpty;
                pips[i].gameObject.name = isFilled ? "Pip Filled" : "Pip Empty";
            }
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
            scaler.matchWidthOrHeight = 1f;   // match by height (shortest side) — consistent type/target sizes across iPhone aspect ratios

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

            _maxStage = MaxPortraitStage.Create(transform);

            BuildTopBar(rootRt);
            var content = BuildContentRect(rootRt);
            BuildHeroColumn(content);
            var main = BuildMainColumn(content);
            BuildPrimaryGrid(main);
            BuildAbilitiesSection(main);
            BuildSpendbar(rootRt);

            _root.SetActive(false);
        }

        private void BuildTopBar(RectTransform parent)
        {
            var bar = NewRect("Top Bar", parent, new Vector2(0f, 1f), new Vector2(1f, 1f));
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(-2f * ContentMargin, TopBarHeight);
            bar.anchoredPosition = new Vector2(0f, -ContentMargin);

            var accent = AddImage(bar, HudTextures.RoundedBox(32, 0.35f), HeaderAccent, "Title Accent");
            Anchor(accent.rectTransform, new Vector2(0f, 0f), new Vector2(0.34f, 1f), new Vector2(0f, 0.5f));
            accent.rectTransform.offsetMin = Vector2.zero;
            accent.rectTransform.offsetMax = Vector2.zero;
            accent.type = Image.Type.Sliced;

            var title = AddText(bar, 42, TextColor, TextAnchor.MiddleLeft);
            Anchor(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.34f, 0.5f), new Vector2(0f, 0.5f));
            title.rectTransform.offsetMin = new Vector2(28f, -30f);
            title.rectTransform.offsetMax = new Vector2(-12f, 30f);
            title.fontStyle = FontStyle.Bold;
            title.text = "WEAPONS & ABILITIES";

            // Right-hand cluster, laid out from the corner inward: a close affordance (the design has
            // none, but the HUD's WEAPONS button only ever opens — MV-234's OnWeaponsButtonTapped calls
            // Open(), never a toggle — so this screen needs its own way back out), then PAUSED, then
            // the PARTS/CELLS banks.
            float cursor = -16f;
            cursor = BuildCloseButton(bar, cursor) - 16f;

            var paused = AddText(bar, 26, PartsColor, TextAnchor.MiddleRight);
            Anchor(paused.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            paused.rectTransform.sizeDelta = new Vector2(160f, 44f);
            paused.rectTransform.anchoredPosition = new Vector2(cursor, 0f);
            paused.fontStyle = FontStyle.Bold;
            paused.text = "II PAUSED";
            cursor -= 160f + 16f;

            var partsChip = BuildChip(bar, new Vector2(cursor, 0f), PartsColor, out _partsText);
            partsChip.name = "Parts Chip";
            cursor -= 150f + 16f;

            var cellsChip = BuildChip(bar, new Vector2(cursor, 0f), CellsColor, out _cellsText);
            cellsChip.name = "Cells Chip";
        }

        /// <summary>A small round dismiss button pinned at <paramref name="rightEdge"/> from the bar's
        /// right edge. Returns the edge's new cursor position (its left edge) for the next element to
        /// chain from.</summary>
        private float BuildCloseButton(RectTransform bar, float rightEdge)
        {
            const float size = 56f;
            var bg = AddImage(bar, HudTextures.RoundedBox(32, 0.5f), RowColor, "Close Button");
            Anchor(bg.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            bg.rectTransform.anchoredPosition = new Vector2(rightEdge, 0f);
            bg.rectTransform.sizeDelta = new Vector2(size, size);   // >= 44pt tap target
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;

            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Close);

            var label = AddText(bg.rectTransform, 28, TextColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.text = "✕";
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;

            return rightEdge - size;
        }

        /// <summary>A rounded pill: a small tinted dot + a live count/label, right-anchored at
        /// <paramref name="offset"/> from the top bar's right edge (CELLS/PARTS, spec: cyan
        /// diamond/amber dot).</summary>
        private RectTransform BuildChip(RectTransform bar, Vector2 offset, Color accent, out Text label)
        {
            var chip = NewRect("Chip", bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            chip.pivot = new Vector2(1f, 0.5f);
            chip.sizeDelta = new Vector2(150f, 52f);
            chip.anchoredPosition = offset;

            var bg = AddImage(chip, HudTextures.RoundedBox(32, 0.5f), RowColor, "BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;

            var dot = AddImage(chip, HudTextures.Disc(32), accent, "Dot");
            Anchor(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            dot.rectTransform.anchoredPosition = new Vector2(24f, 0f);
            dot.rectTransform.sizeDelta = new Vector2(20f, 20f);

            label = AddText(chip, 24, accent, TextAnchor.MiddleRight);
            Anchor(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f));
            label.rectTransform.offsetMin = new Vector2(42f, -20f);
            label.rectTransform.offsetMax = new Vector2(-14f, 20f);
            label.fontStyle = FontStyle.Bold;
            return chip;
        }

        /// <summary>The area between the top bar and the spendbar, holding the hero column and the
        /// main (primary + abilities) column.</summary>
        private RectTransform BuildContentRect(RectTransform parent)
        {
            var content = NewRect("Content", parent, Vector2.zero, Vector2.one);
            content.offsetMin = new Vector2(ContentMargin, SpendbarHeight + ContentMargin * 1.5f);
            content.offsetMax = new Vector2(-ContentMargin, -(TopBarHeight + ContentMargin * 1.5f));
            return content;
        }

        /// <summary>Left ~33%: Max's live portrait, the "PRIMARY WEAPON" tag, the weapon's full name,
        /// and a short helper line.</summary>
        private RectTransform BuildHeroColumn(RectTransform content)
        {
            var column = NewRect("Hero Column", content, new Vector2(0f, 0f), new Vector2(0.335f, 1f));
            column.offsetMin = Vector2.zero;
            column.offsetMax = new Vector2(-16f, 0f);

            var card = NewRect("Portrait Card", column, new Vector2(0f, 1f), new Vector2(1f, 1f));
            card.pivot = new Vector2(0.5f, 1f);
            card.offsetMin = Vector2.zero; card.offsetMax = Vector2.zero;
            card.sizeDelta = new Vector2(0f, 480f);
            card.anchoredPosition = Vector2.zero;

            var cardBg = AddImage(card, HudTextures.RoundedBox(40, 0.5f), CardColor, "BG");
            Stretch(cardBg.rectTransform); cardBg.type = Image.Type.Sliced;

            var portrait = AddRawImage(card, _maxStage != null ? _maxStage.Texture : null, "Max Portrait");
            Stretch(portrait.rectTransform, -14f);

            var tag = AddText(column, 22, PrimaryAccent, TextAnchor.UpperLeft);
            Anchor(tag.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            tag.rectTransform.sizeDelta = new Vector2(0f, 30f);
            tag.rectTransform.anchoredPosition = new Vector2(0f, -502f);
            tag.fontStyle = FontStyle.Bold;
            tag.text = "PRIMARY WEAPON";

            var name = AddText(column, 30, PrimaryAccent, TextAnchor.UpperLeft);
            Anchor(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            name.rectTransform.sizeDelta = new Vector2(0f, 78f);
            name.rectTransform.anchoredPosition = new Vector2(0f, -534f);
            name.fontStyle = FontStyle.Bold;
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.text = WeaponCatalog.TitleCase(WeaponCatalog.PrimaryName);

            var helper = AddText(column, 20, Dim, TextAnchor.UpperLeft);
            Anchor(helper.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            helper.rectTransform.sizeDelta = new Vector2(0f, 90f);
            helper.rectTransform.anchoredPosition = new Vector2(0f, -616f);
            helper.horizontalOverflow = HorizontalWrapMode.Wrap;
            helper.text = "Spend a part on any owned track to level it up. Cells power everything you fire.";

            return column;
        }

        /// <summary>Right ~67%: the primary-track grid, then the abilities section.</summary>
        private RectTransform BuildMainColumn(RectTransform content)
        {
            var column = NewRect("Main Column", content, new Vector2(0.335f, 0f), new Vector2(1f, 1f));
            column.offsetMin = new Vector2(16f, 0f);
            column.offsetMax = Vector2.zero;
            return column;
        }

        private void BuildPrimaryGrid(RectTransform column)
        {
            var header = AddText(column, 28, TextColor, TextAnchor.UpperLeft);
            Anchor(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            header.rectTransform.sizeDelta = new Vector2(0f, SectionHeaderHeight);
            header.rectTransform.anchoredPosition = Vector2.zero;
            header.fontStyle = FontStyle.Bold;
            header.text = $"PRIMARY — {TrackCount} upgrade tracks";

            float gridTop = -(SectionHeaderHeight + SectionHeaderGap);
            for (int i = 0; i < TrackCount; i++)
            {
                int index = i;   // capture by value, not the loop variable
                BuildGridRow(column, "Track Row", i, gridTop, RowHeight, RowGap,
                    out _trackName[i], out var pips, out _trackButton[i], out _trackButtonBg[i]);
                _trackPips[i] = pips;
                _trackButton[i].onClick.AddListener(() => OnTrackButtonTapped(index));
            }
        }

        private void BuildAbilitiesSection(RectTransform column)
        {
            float primaryRows = (TrackCount + 1) / 2;
            float abilitiesTop = -(SectionHeaderHeight + SectionHeaderGap) - primaryRows * RowHeight
                - (primaryRows - 1) * RowGap - SectionGap;

            _abilitiesHeaderText = AddText(column, 28, TextColor, TextAnchor.UpperLeft);
            Anchor(_abilitiesHeaderText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            _abilitiesHeaderText.rectTransform.sizeDelta = new Vector2(0f, SectionHeaderHeight);
            _abilitiesHeaderText.rectTransform.anchoredPosition = new Vector2(0f, abilitiesTop);
            _abilitiesHeaderText.fontStyle = FontStyle.Bold;

            float gridTop = abilitiesTop - (SectionHeaderHeight + SectionHeaderGap);
            _abilitiesGridTop = gridTop;
            for (int i = 0; i < MaxAbilityRows; i++)
            {
                int row = i;   // capture by value, not the loop variable
                var rowRt = BuildGridRow(column, "Ability Row", i, gridTop, RowHeight, RowGap,
                    out _abilityName[i], out var pips, out _abilityButton[i], out _abilityButtonBg[i]);
                _abilityPips[i] = pips;
                _abilityButton[i].onClick.AddListener(() => OnAbilityButtonTapped(row));
                _abilityRow[i] = rowRt.gameObject;
            }

            // The dashed "unlock from sheds" placeholder — built once here, re-homed each Refresh
            // (RefreshPlaceholder) right after the last populated ability row.
            var ph = NewRect("Placeholder Row", column, new Vector2(0f, 1f), Vector2.one);
            ph.pivot = new Vector2(0.5f, 1f);
            ph.sizeDelta = new Vector2(0f, RowHeight);
            PlaceGridRow(ph, 0, gridTop, RowHeight, RowGap, cols: 1);
            _placeholderRow = ph;

            var phBg = AddImage(ph, HudTextures.RoundedBox(24, 0.35f), new Color(1f, 1f, 1f, 0.05f), "BG");
            Stretch(phBg.rectTransform); phBg.type = Image.Type.Sliced;

            _placeholderText = AddText(ph, 20, Dim, TextAnchor.MiddleCenter);
            Stretch(_placeholderText.rectTransform, -20f);
            _placeholderText.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        /// <summary>Builds one grid slot (icon, name, pip bar, + button) at 2-column/N-row index
        /// <paramref name="slot"/>, anchored under <paramref name="top"/>. Shared by the primary tracks
        /// and the abilities section so the two can never drift apart in style.</summary>
        private RectTransform BuildGridRow(RectTransform column, string name, int slot, float top,
            float rowHeight, float rowGap, out Text nameText, out Image[] pips, out Button plusButton, out Image plusBg)
        {
            var row = NewRect(name, column, new Vector2(0f, 1f), Vector2.one);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, rowHeight);
            PlaceGridRow(row, slot, top, rowHeight, rowGap);

            var bg = AddImage(row, HudTextures.RoundedBox(24, 0.35f), RowColor, "BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;

            var iconBg = AddImage(row, HudTextures.RoundedBox(24, 0.35f), new Color(1f, 1f, 1f, 0.06f), "Icon");
            Anchor(iconBg.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            iconBg.rectTransform.anchoredPosition = new Vector2(16f, 0f);
            iconBg.rectTransform.sizeDelta = new Vector2(60f, 60f);
            iconBg.type = Image.Type.Sliced;

            // Name sits in the row's upper half, pips in the lower half, both spanning the same
            // horizontal band between the icon and the + button (the [0.13, 0.82] fraction of the row).
            nameText = AddText(row, 26, TextColor, TextAnchor.UpperLeft);
            Anchor(nameText.rectTransform, new Vector2(0.13f, 0.5f), new Vector2(0.82f, 1f), new Vector2(0f, 1f));
            nameText.rectTransform.offsetMin = new Vector2(8f, 0f);
            nameText.rectTransform.offsetMax = new Vector2(0f, -10f);
            nameText.fontStyle = FontStyle.Bold;

            var pipRow = NewRect("Pips", row, new Vector2(0.13f, 0f), new Vector2(0.82f, 0.5f));
            pipRow.offsetMin = new Vector2(8f, 12f);
            pipRow.offsetMax = new Vector2(0f, -6f);

            pips = new Image[MaxPips];
            const float pipW = 42f, pipGap = 6f;
            for (int i = 0; i < MaxPips; i++)
            {
                var pip = AddImage(pipRow, HudTextures.RoundedBox(16, 0.5f), PipEmpty, "Pip Empty");
                Anchor(pip.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f));
                pip.rectTransform.anchoredPosition = new Vector2(i * (pipW + pipGap), 0f);
                pip.rectTransform.sizeDelta = new Vector2(pipW, 0f);
                pip.type = Image.Type.Sliced;
                pips[i] = pip;
            }

            var buttonBg = AddImage(row, HudTextures.RoundedBox(20, 0.5f), SpendDisabled, "Plus");
            Anchor(buttonBg.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            buttonBg.rectTransform.anchoredPosition = new Vector2(-16f, 0f);
            buttonBg.rectTransform.sizeDelta = new Vector2(64f, 64f);   // >= 44pt tap target at the height-matched scale
            buttonBg.type = Image.Type.Sliced;
            buttonBg.raycastTarget = true;
            plusBg = buttonBg;

            plusButton = buttonBg.gameObject.AddComponent<Button>();
            plusButton.transition = Selectable.Transition.None;

            var plusLabel = AddText(buttonBg.rectTransform, 32, TextColor, TextAnchor.MiddleCenter);
            Stretch(plusLabel.rectTransform);
            plusLabel.text = "+";
            plusLabel.fontStyle = FontStyle.Bold;
            plusLabel.raycastTarget = false;

            return row;
        }

        /// <summary>Places a row at 2-column/N-row slot index <paramref name="slot"/> under
        /// <paramref name="top"/> — used both for the fixed track/ability pool positions (built once)
        /// and to re-home the "unlock from sheds" placeholder each Refresh as the acquired count
        /// changes. <paramref name="cols"/> = 1 spans the full width (the placeholder row).</summary>
        private static void PlaceGridRow(RectTransform row, int slot, float top, float rowHeight, float rowGap, int cols = 2)
        {
            int r = cols == 1 ? slot : slot / 2;
            int c = cols == 1 ? 0 : slot % 2;
            float y = top - r * (rowHeight + rowGap);
            row.anchoredPosition = new Vector2(0f, y);

            float xMin, xMax;
            if (cols == 1) { xMin = 0f; xMax = 1f; }
            else { xMin = c == 0 ? 0f : 0.5f + ColGap * 0.5f; xMax = c == 0 ? 0.5f - ColGap * 0.5f : 1f; }
            row.anchorMin = new Vector2(xMin, row.anchorMin.y);
            row.anchorMax = new Vector2(xMax, row.anchorMax.y);
        }

        private void BuildSpendbar(RectTransform parent)
        {
            var bar = NewRect("Spendbar", parent, new Vector2(0f, 0f), new Vector2(1f, 0f));
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(-2f * ContentMargin, SpendbarHeight);
            bar.anchoredPosition = new Vector2(0f, ContentMargin);

            var outline = AddImage(bar, HudTextures.RoundedBox(28, 0.4f), PartsColor, "Outline");
            Stretch(outline.rectTransform); outline.type = Image.Type.Sliced;

            var bg = AddImage(bar, HudTextures.RoundedBox(28, 0.4f), PanelColor, "BG");
            Stretch(bg.rectTransform, -4f); bg.type = Image.Type.Sliced;

            var dot = AddImage(bar, HudTextures.Disc(32), PartsColor, "Dot");
            Anchor(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            dot.rectTransform.anchoredPosition = new Vector2(28f, 0f);
            dot.rectTransform.sizeDelta = new Vector2(20f, 20f);

            _spendbarText = AddText(bar, 22, TextColor, TextAnchor.MiddleLeft);
            Anchor(_spendbarText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f));
            _spendbarText.rectTransform.offsetMin = new Vector2(50f, 0f);
            _spendbarText.rectTransform.offsetMax = new Vector2(-24f, 0f);
            _spendbarText.horizontalOverflow = HorizontalWrapMode.Wrap;
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

        private static RawImage AddRawImage(Transform parent, Texture tex, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<RawImage>();
            img.texture = tex;
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
