using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Save;
using MaxWorlds.Weapons;

namespace MaxWorlds.UI
{
    /// <summary>
    /// MV-1290: the LOADOUT screen shown on arriving in a world once a slot owns more than one PRIMARY
    /// or more than one SECONDARY, letting it pick this world's pair before Max gets control. Reads what
    /// a slot owns off <see cref="WorldCatalog.OwnedPrimariesThroughWorld"/>/
    /// <see cref="WorldCatalog.OwnedSecondariesThroughWorld"/> (MV-1289) and writes its pick straight
    /// into <see cref="SaveSlotData.ChosenPrimaryForWorldIndex"/>/<see cref="SaveSlotData.ChosenSecondaryForWorldIndex"/>
    /// — the same fields <see cref="WeaponSystemState.ApplyWorldLoadout"/> already resolves through, so
    /// no new apply path is needed once this writes and calls it.
    ///
    /// Self-installing/Open()/Close()/pause-on-open, same idiom as <see cref="MapScreen"/>: a dedicated
    /// overlay canvas, built lazily, <see cref="Time.timeScale"/> 0 while up. <see cref="WorldJoinSequence"/>
    /// is the only production caller (right where it would otherwise hand control back after a corridor
    /// arrival) — a RESUME or a dev world-jump never passes through that class and so never sees this
    /// screen, matching the ticket's own "after the corridor arrival" wording.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("hud")]
    public sealed class LoadoutScreen : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<LoadoutScreen>() != null) return;
            new GameObject("LoadoutScreen").AddComponent<LoadoutScreen>();
        }

        private const float RefW = 1920f, RefH = 1080f;

        private static readonly Color Scrim = new Color(0.03f, 0.04f, 0.05f, 1f);
        private static readonly Color PanelTextColor = Color.white;
        private static readonly Color DimTextColor = new Color(1f, 1f, 1f, 0.6f);
        private static readonly Color CardColor = new Color(0.12f, 0.14f, 0.17f, 1f);
        private static readonly Color SelectedBorderColor = new Color(0.96f, 0.70f, 0.20f);   // amber, matches the design reference's selection ring
        private static readonly Color NewTagColor = new Color(0.96f, 0.70f, 0.20f);
        private static readonly Color StartButtonColor = new Color(0.96f, 0.70f, 0.20f);
        private static readonly Color StartButtonTextColor = new Color(0.08f, 0.07f, 0.03f);
        private static readonly Color WorldLineColor = new Color(0.35f, 0.85f, 0.95f);

        // MV-472 phone floors: every card and START >=44pt tall (>=160 ref-px); every label >=11pt
        // (>=32 ref-px) at this screen's own 1920x1080-match-by-height reference frame.
        private const float CardWidth = 260f;
        private const float CardHeight = 180f;
        private const float CardGap = 20f;
        private const float RowLabelHeight = 28f;
        private const float RowGap = 16f;
        private const float StartButtonWidth = 320f;
        private const float StartButtonHeight = 180f;
        private const float SideMargin = 80f;
        // MV-472 phone floor: every card name must resolve to >=11pt (>=32 ref-px) at best-fit — both
        // ends of the resize range sit above that floor so a shrink-to-fit never crosses it.
        private const float NameFontSize = 38f;
        private const float NameFontMinSize = 33f;

        private Canvas _canvas;
        private RectTransform _safeRoot;
        private RectTransform _screenRoot;
        private Text _worldLineText;
        private Text _worldNameText;
        private RectTransform _primaryLabelRoot;
        private RectTransform _secondaryLabelRoot;
        private RectTransform _startButtonRoot;
        private Button _startButton;

        private readonly List<CardWidgets> _primaryCards = new List<CardWidgets>();
        private readonly List<CardWidgets> _secondaryCards = new List<CardWidgets>();
        private WeaponCatalog.PrimaryKind[] _primaryKinds = Array.Empty<WeaponCatalog.PrimaryKind>();
        private SecondaryKind[] _secondaryKinds = Array.Empty<SecondaryKind>();
        private int _selectedPrimary;
        private int _selectedSecondary;

        private bool _open;
        private float _prevTimeScale = 1f;
        private int _worldIndex;
        private Action _onStart;

        private struct CardWidgets
        {
            public RectTransform Root;
            public Image Border;
            public Text Name;
            public Text Origin;
            public GameObject NewTag;
        }

        /// <summary>Test/capture-only: is the screen currently up (and the game paused)? Same idiom as
        /// <see cref="MapScreen.IsOpen"/>.</summary>
        public bool IsOpen => _open;

        /// <summary>Test/capture-only: how many PRIMARY cards the last <see cref="Open"/>/
        /// <see cref="OpenWithOwnedSets"/> actually built.</summary>
        public int PrimaryCardCount => _primaryKinds.Length;

        /// <summary>Test/capture-only: the SECONDARY twin of <see cref="PrimaryCardCount"/>.</summary>
        public int SecondaryCardCount => _secondaryKinds.Length;

        public RectTransform PrimaryCardRect(int index) => _primaryCards[index].Root;
        public RectTransform SecondaryCardRect(int index) => _secondaryCards[index].Root;
        public Text PrimaryCardNameText(int index) => _primaryCards[index].Name;
        public Text SecondaryCardNameText(int index) => _secondaryCards[index].Name;
        public RectTransform StartButtonRect => _startButtonRoot;

        // ------------------------------------------------------------------ eligibility (MV-1290 step 1)

        /// <summary>Whether this screen should show on arriving at <paramref name="worldIndex"/>: the
        /// active slot owns more than one PRIMARY or more than one SECONDARY by that point
        /// (<see cref="WorldCatalog.OwnedPrimariesThroughWorld"/>/<see cref="WorldCatalog.OwnedSecondariesThroughWorld"/>,
        /// read against <see cref="SaveSlotData.FurthestWorldIndex"/>) AND no loadout has already been
        /// recorded for exactly this world. No active slot (a capture/press-kit run) always reads as "not
        /// yet chosen" — the production caller only ever asks this with a real slot active, so this is
        /// defensive, not a path anything exercises today.</summary>
        public static bool ShouldShow(int worldIndex)
        {
            int furthest = FurthestWorldIndexFor(worldIndex);
            int primaryCount = DistinctPrimaries(WorldCatalog.OwnedPrimariesThroughWorld(furthest)).Count;
            int secondaryCount = DistinctSecondaries(WorldCatalog.OwnedSecondariesThroughWorld(furthest)).Count;
            if (primaryCount <= 1 && secondaryCount <= 1) return false;

            if (SaveSystem.ActiveSlot < 0) return true;
            SaveSlotData data = SaveSystem.Load(SaveSystem.ActiveSlot);
            bool alreadyChosen = data.ChosenPrimaryForWorldIndex == worldIndex || data.ChosenSecondaryForWorldIndex == worldIndex;
            return !alreadyChosen;
        }

        /// <summary>MV-1327: <see cref="Math.Max"/> against <paramref name="worldIndex"/> itself, not
        /// just the slot's recorded <see cref="SaveSlotData.FurthestWorldIndex"/> — a dev world jump
        /// (<see cref="MaxWorlds.UI.HomeScreen.StartDevWorld"/>) deliberately never raises that field (it
        /// would unlock real progress), so a jump straight to a later world on a fresh profile used to
        /// read as having reached only world 0.</summary>
        private static int FurthestWorldIndexFor(int worldIndex) =>
            SaveSystem.ActiveSlot < 0 ? worldIndex : Math.Max(SaveSystem.Load(SaveSystem.ActiveSlot).FurthestWorldIndex, worldIndex);

        // ------------------------------------------------------------------ open / close

        /// <summary>Production entry point: resolves what the active slot owns through
        /// <paramref name="worldIndex"/> itself, then opens. Call only when <see cref="ShouldShow"/> is
        /// true — this does not re-check it, so a caller that always wants the screen (a capture fixture)
        /// can reuse <see cref="OpenWithOwnedSets"/> directly instead.</summary>
        public void Open(int worldIndex, Action onStart)
        {
            int furthest = FurthestWorldIndexFor(worldIndex);
            OpenWithOwnedSets(worldIndex,
                DistinctPrimaries(WorldCatalog.OwnedPrimariesThroughWorld(furthest)),
                DistinctSecondaries(WorldCatalog.OwnedSecondariesThroughWorld(furthest)),
                onStart);
        }

        /// <summary>Capture/test entry point: opens with an explicit owned set instead of reading the
        /// active slot — the "World 5, every weapon owned" conformance fixture
        /// (<see cref="MaxWorlds.Dev.UiScreensDirector"/>) needs a state no real save ever reaches without
        /// playing the whole game.</summary>
        public void OpenWithOwnedSets(int worldIndex, IReadOnlyList<WeaponCatalog.PrimaryKind> primaries,
            IReadOnlyList<SecondaryKind> secondaries, Action onStart)
        {
            if (_open) return;
            if (_canvas == null) Build();

            _worldIndex = worldIndex;
            _onStart = onStart;
            _open = true;
            _prevTimeScale = TimeScaleCapture.ClampForCapture(Time.timeScale);
            Time.timeScale = 0f;
            ModalFrameRateGate.Enter();

            Populate(worldIndex, primaries, secondaries);
            RefitOpeningView(Screen.width, Screen.height);

            _screenRoot.gameObject.SetActive(true);
        }

        /// <summary>Re-runs the row layout against an EXPLICIT viewport pixel size instead of the live
        /// (possibly ambient-batchmode) <see cref="Screen"/> dimensions — same reason and same timing as
        /// <see cref="MapScreen.RefitOpeningView"/>: a capture flips this canvas onto its own camera AFTER
        /// <see cref="OpenWithOwnedSets"/> already laid out against whatever Screen.width/height batchmode
        /// happened to have live at that moment.</summary>
        public void RefitOpeningView(int w, int h)
        {
            if (_primaryLabelRoot == null) return;
            float visibleRefWidth = h > 0 ? RefH * w / h : RefW;
            LayoutRows(visibleRefWidth);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            Time.timeScale = _prevTimeScale;
            ModalFrameRateGate.Exit();
            _onStart = null;
            _screenRoot.gameObject.SetActive(false);
        }

        private void CommitAndStart()
        {
            if (!_open) return;
            WeaponCatalog.PrimaryKind chosenPrimary = _primaryKinds[_selectedPrimary];
            SecondaryKind chosenSecondary = _secondaryKinds[_selectedSecondary];

            if (SaveSystem.ActiveSlot >= 0)
            {
                SaveSlotData data = SaveSystem.Load(SaveSystem.ActiveSlot);
                data.ChosenPrimaryForWorldIndex = _worldIndex;
                data.ChosenPrimaryForWorld = chosenPrimary;
                data.ChosenSecondaryForWorldIndex = _worldIndex;
                data.ChosenSecondaryForWorld = chosenSecondary;
                SaveSystem.Save(SaveSystem.ActiveSlot, data);
            }
            WeaponSystemState.ApplyLoadout(_worldIndex, chosenPrimary, chosenSecondary);

            Action onStart = _onStart;
            Close();
            onStart?.Invoke();
        }

        // ------------------------------------------------------------------ populate

        private void Populate(int worldIndex, IReadOnlyList<WeaponCatalog.PrimaryKind> primaries,
            IReadOnlyList<SecondaryKind> secondaries)
        {
            WorldDefinition world = WorldCatalog.Get(worldIndex);
            (string worldLine, string nameLine) = ResolveTitle(worldIndex);
            _worldLineText.text = worldLine;
            _worldNameText.text = nameLine;

            _primaryKinds = new WeaponCatalog.PrimaryKind[primaries.Count];
            _selectedPrimary = 0;
            for (int i = 0; i < _primaryKinds.Length; i++)
            {
                _primaryKinds[i] = primaries[i];
                bool isNew = WorldCatalog.HomeWorldIndexForPrimary(primaries[i]) == worldIndex;
                int origin = WorldCatalog.HomeWorldIndexForPrimary(primaries[i]) + 1;
                SetCard(_primaryCards[i], WeaponCatalog.ShortName(primaries[i]), $"WORLD {origin}", isNew);
                if (primaries[i] == world.PrimaryWeapon) _selectedPrimary = i;
            }
            for (int i = _primaryKinds.Length; i < _primaryCards.Count; i++) _primaryCards[i].Root.gameObject.SetActive(false);

            _secondaryKinds = new SecondaryKind[secondaries.Count];
            _selectedSecondary = 0;
            for (int i = 0; i < _secondaryKinds.Length; i++)
            {
                _secondaryKinds[i] = secondaries[i];
                bool isNew = WorldCatalog.HomeWorldIndexForSecondary(secondaries[i]) == worldIndex;
                int origin = WorldCatalog.HomeWorldIndexForSecondary(secondaries[i]) + 1;
                SetCard(_secondaryCards[i], WeaponCatalog.ShortName(secondaries[i]), $"WORLD {origin}", isNew);
                if (secondaries[i] == world.SecondaryWeapon) _selectedSecondary = i;
            }
            for (int i = _secondaryKinds.Length; i < _secondaryCards.Count; i++) _secondaryCards[i].Root.gameObject.SetActive(false);

            RefreshSelectionVisuals();
        }

        private static void SetCard(CardWidgets card, string name, string origin, bool isNew)
        {
            card.Root.gameObject.SetActive(true);
            card.Name.text = name;
            card.Origin.text = origin;
            card.NewTag.SetActive(isNew);
        }

        private void SelectPrimary(int index)
        {
            if (index < 0 || index >= _primaryKinds.Length) return;
            _selectedPrimary = index;
            RefreshSelectionVisuals();
        }

        private void SelectSecondary(int index)
        {
            if (index < 0 || index >= _secondaryKinds.Length) return;
            _selectedSecondary = index;
            RefreshSelectionVisuals();
        }

        private void RefreshSelectionVisuals()
        {
            for (int i = 0; i < _primaryKinds.Length; i++) ApplySelected(_primaryCards[i], i == _selectedPrimary);
            for (int i = 0; i < _secondaryKinds.Length; i++) ApplySelected(_secondaryCards[i], i == _selectedSecondary);
        }

        private static void ApplySelected(CardWidgets card, bool selected)
        {
            card.Border.color = selected ? SelectedBorderColor : Color.clear;
        }

        /// <summary>"WORLD n" / name, parsed off the destination world's own authored <c>world</c> field —
        /// the same em-dash split <see cref="WorldJoinSequence"/>'s own <c>ParseTitle</c> and
        /// <see cref="HomeScreen.WorldDisplayName"/> each already do independently; a third small copy
        /// here rather than threading a shared helper through two classes that don't otherwise depend on
        /// each other.</summary>
        private static (string worldLine, string nameLine) ResolveTitle(int worldIndex)
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.KeyForIndex(worldIndex));
            string field = cfg != null ? cfg.world : null;
            if (string.IsNullOrEmpty(field)) return ($"WORLD {worldIndex + 1}", string.Empty);
            int dash = field.IndexOf('—');   // em dash
            if (dash < 0) return (field.Trim().ToUpperInvariant(), string.Empty);
            return (field.Substring(0, dash).Trim().ToUpperInvariant(), field.Substring(dash + 1).Trim().ToUpperInvariant());
        }

        private static List<WeaponCatalog.PrimaryKind> DistinctPrimaries(IEnumerable<WeaponCatalog.PrimaryKind> source)
        {
            var seen = new HashSet<WeaponCatalog.PrimaryKind>();
            var list = new List<WeaponCatalog.PrimaryKind>();
            foreach (WeaponCatalog.PrimaryKind kind in source) if (seen.Add(kind)) list.Add(kind);
            return list;
        }

        private static List<SecondaryKind> DistinctSecondaries(IEnumerable<SecondaryKind> source)
        {
            var seen = new HashSet<SecondaryKind>();
            var list = new List<SecondaryKind>();
            foreach (SecondaryKind kind in source) if (seen.Add(kind)) list.Add(kind);
            return list;
        }

        // ------------------------------------------------------------------ layout

        /// <summary>Lays PRIMARY then SECONDARY out as a centred horizontal flow, wrapping to a new line
        /// whenever the next card would not fit within <paramref name="visibleRefWidth"/> (MV-1290 step
        /// 5: "with 5 primaries the row may scroll horizontally or wrap — no clipping either way"; wrap is
        /// the simpler of the two and needs no ScrollRect). SECONDARY's own top Y follows however many
        /// lines PRIMARY actually wrapped to, so the two rows never overlap regardless of aspect.</summary>
        private void LayoutRows(float visibleRefWidth)
        {
            float usableWidth = Mathf.Max(CardWidth, visibleRefWidth - SideMargin * 2f);
            int columns = Mathf.Max(1, Mathf.FloorToInt((usableWidth + CardGap) / (CardWidth + CardGap)));

            // Starts just under the header (the subtitle's own bottom edge sits at ref-Y 324 — see
            // Build's "Sub Title" block) and stacks downward; the worst case (5 PRIMARY cards wrapping
            // to 2 lines at the ipad-mini aspect, 3 SECONDARY on 1 line, plus START) still clears the
            // frame's own bottom edge at ref-Y -540 with margin to spare.
            const float primaryTop = 300f;
            int primaryLines = LayoutRow(_primaryCards, _primaryKinds.Length, columns, primaryTop, _primaryLabelRoot, "PRIMARY");

            float secondaryTop = primaryTop - RowLabelHeight - RowBlockHeight(primaryLines) - RowGap;
            int secondaryLines = LayoutRow(_secondaryCards, _secondaryKinds.Length, columns, secondaryTop, _secondaryLabelRoot, "SECONDARY");

            float startTop = secondaryTop - RowLabelHeight - RowBlockHeight(secondaryLines) - RowGap;
            _startButtonRoot.anchoredPosition = new Vector2(0f, startTop - StartButtonHeight * 0.5f);
        }

        /// <summary>The vertical span <paramref name="lines"/> of cards actually occupy — gaps only
        /// BETWEEN lines, never trailing one after the last.</summary>
        private static float RowBlockHeight(int lines) =>
            lines <= 0 ? 0f : lines * CardHeight + (lines - 1) * CardGap;

        /// <summary>Positions <paramref name="count"/> active cards out of <paramref name="widgets"/>,
        /// row-major, each line centred independently, and the row's own label above the first line.
        /// Returns how many lines it used, so the caller can stack the next row below it.</summary>
        private int LayoutRow(List<CardWidgets> widgets, int count, int columns, float topY, RectTransform label, string labelText)
        {
            label.anchoredPosition = new Vector2(0f, topY);
            if (count == 0) return 0;

            int lines = Mathf.CeilToInt((float)count / columns);
            for (int i = 0; i < count; i++)
            {
                int line = i / columns;
                int colsThisLine = Mathf.Min(columns, count - line * columns);
                int colInLine = i % columns;
                float lineWidth = colsThisLine * CardWidth + (colsThisLine - 1) * CardGap;
                float startX = -lineWidth * 0.5f + CardWidth * 0.5f;
                float x = startX + colInLine * (CardWidth + CardGap);
                float y = topY - RowLabelHeight - line * (CardHeight + CardGap) - CardHeight * 0.5f;
                widgets[i].Root.anchoredPosition = new Vector2(x, y);
            }
            return lines;
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            EnsureEventSystem();

            var go = new GameObject("Loadout Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 230;   // above every other modal (HomeScreen/UpgradeScreen sit at 220)

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;   // match by height (the design reference's own stated frame)

            _safeRoot = NewRect("Safe Area", _canvas.transform);
            Stretch(_safeRoot);
            _safeRoot.gameObject.AddComponent<SafeArea>();

            _screenRoot = NewRect("Screen Root", _safeRoot);
            Stretch(_screenRoot);

            var scrim = AddImage(_screenRoot, HudTextures.Solid(), Scrim, "Scrim");
            Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;   // blocks input to whatever sits behind, same as every other modal here

            var worldLineRoot = NewRect("World Line", _screenRoot);
            Anchor(worldLineRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            worldLineRoot.sizeDelta = new Vector2(1600f, 56f);
            worldLineRoot.anchoredPosition = new Vector2(0f, -56f);
            _worldLineText = AddText(worldLineRoot, 38f, WorldLineColor, TextAnchor.MiddleCenter);
            Stretch(_worldLineText.rectTransform);
            _worldLineText.fontStyle = FontStyle.Bold;

            var worldNameRoot = NewRect("World Name", _screenRoot);
            Anchor(worldNameRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            worldNameRoot.sizeDelta = new Vector2(1700f, 76f);
            worldNameRoot.anchoredPosition = new Vector2(0f, -108f);
            _worldNameText = AddText(worldNameRoot, 58f, PanelTextColor, TextAnchor.MiddleCenter);
            Stretch(_worldNameText.rectTransform);
            _worldNameText.fontStyle = FontStyle.Bold;

            var subTitleRoot = NewRect("Sub Title", _screenRoot);
            Anchor(subTitleRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            subTitleRoot.sizeDelta = new Vector2(1700f, 56f);
            subTitleRoot.anchoredPosition = new Vector2(0f, -160f);
            var subTitle = AddText(subTitleRoot, 34f, PanelTextColor, TextAnchor.MiddleCenter);
            Stretch(subTitle.rectTransform);
            subTitle.fontStyle = FontStyle.Bold;
            subTitle.text = "CHOOSE YOUR LOADOUT";

            var rowsRoot = NewRect("Rows", _screenRoot);
            Anchor(rowsRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            rowsRoot.sizeDelta = Vector2.zero;
            rowsRoot.anchoredPosition = Vector2.zero;

            _primaryLabelRoot = BuildRowLabel(rowsRoot, "PRIMARY");
            _secondaryLabelRoot = BuildRowLabel(rowsRoot, "SECONDARY");

            int primaryPool = Mathf.Max(1, WorldCatalog.Count);
            int secondaryPool = Mathf.Max(1, Enum.GetValues(typeof(SecondaryKind)).Length);
            for (int i = 0; i < primaryPool; i++) _primaryCards.Add(BuildCard(rowsRoot, i, isPrimary: true));
            for (int i = 0; i < secondaryPool; i++) _secondaryCards.Add(BuildCard(rowsRoot, i, isPrimary: false));

            BuildStartButton(_screenRoot);

            _screenRoot.gameObject.SetActive(false);
        }

        private RectTransform BuildRowLabel(RectTransform parent, string text)
        {
            var root = NewRect($"{text} Label", parent);
            Anchor(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f));
            root.sizeDelta = new Vector2(1700f, RowLabelHeight);
            var label = AddText(root, 28f, DimTextColor, TextAnchor.LowerCenter);
            Stretch(label.rectTransform);
            label.fontStyle = FontStyle.Bold;
            label.text = text;
            return root;
        }

        private CardWidgets BuildCard(RectTransform parent, int index, bool isPrimary)
        {
            string name = (isPrimary ? "Primary" : "Secondary") + " Card " + index;
            var bg = AddImage(parent, HudTextures.RoundedBox(32, 0.5f), CardColor, name);
            Anchor(bg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            bg.rectTransform.sizeDelta = new Vector2(CardWidth, CardHeight);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;

            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            if (isPrimary) button.onClick.AddListener(() => SelectPrimary(index));
            else button.onClick.AddListener(() => SelectSecondary(index));

            var border = AddImage(bg.rectTransform, HudTextures.RoundedBoxOutline(32, 0.5f, 6f), Color.clear, "Border");
            Stretch(border.rectTransform);
            border.type = Image.Type.Sliced;
            border.raycastTarget = false;

            var nameText = AddText(bg.rectTransform, NameFontSize, PanelTextColor, TextAnchor.MiddleCenter);
            Anchor(nameText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            nameText.rectTransform.sizeDelta = new Vector2(CardWidth - 24f, 84f);
            nameText.rectTransform.anchoredPosition = new Vector2(0f, 10f);
            nameText.fontStyle = FontStyle.Bold;
            nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            nameText.resizeTextForBestFit = true;
            nameText.resizeTextMinSize = Mathf.RoundToInt(NameFontMinSize);
            // MV-489 lockstep rule (CLAUDE.md Testing policy): fontSize must track resizeTextMaxSize, or
            // Unity's best-fit search is capped by the stale fontSize instead of the intended max.
            nameText.resizeTextMaxSize = Mathf.RoundToInt(NameFontSize);
            nameText.fontSize = nameText.resizeTextMaxSize;

            var originText = AddText(bg.rectTransform, 18f, DimTextColor, TextAnchor.MiddleCenter);
            Anchor(originText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            originText.rectTransform.sizeDelta = new Vector2(CardWidth - 24f, 28f);
            originText.rectTransform.anchoredPosition = new Vector2(0f, 14f);

            var newTagRoot = NewRect("New Tag", bg.rectTransform);
            Anchor(newTagRoot, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
            newTagRoot.sizeDelta = new Vector2(70f, 26f);
            newTagRoot.anchoredPosition = new Vector2(-12f, 12f);
            var newTag = AddText(newTagRoot, 18f, NewTagColor, TextAnchor.MiddleRight);
            Stretch(newTag.rectTransform);
            newTag.fontStyle = FontStyle.Bold;
            newTag.text = "NEW";

            return new CardWidgets
            {
                Root = bg.rectTransform,
                Border = border,
                Name = nameText,
                Origin = originText,
                NewTag = newTagRoot.gameObject,
            };
        }

        private void BuildStartButton(RectTransform parent)
        {
            var bg = AddImage(parent, HudTextures.RoundedBox(32, 0.5f), StartButtonColor, "Start Button");
            Anchor(bg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            bg.rectTransform.sizeDelta = new Vector2(StartButtonWidth, StartButtonHeight);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;
            _startButtonRoot = bg.rectTransform;

            _startButton = bg.gameObject.AddComponent<Button>();
            _startButton.transition = Selectable.Transition.None;
            _startButton.onClick.AddListener(CommitAndStart);

            var label = AddText(bg.rectTransform, 42f, StartButtonTextColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.fontStyle = FontStyle.Bold;
            label.text = "START";
        }

        // ------------------------------------------------------------------ small UI helpers
        // (same duplicated-per-screen idiom as MapScreen/UpgradeScreen's own private helpers)

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
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

        private static Text AddText(Transform parent, float size, Color color, TextAnchor align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = HudFont.Get();
            t.fontSize = Mathf.Max(4, Mathf.RoundToInt(size));
            t.color = color;
            t.alignment = align;
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
