using System.Collections.Generic;
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
    /// Layout: a title + CELLS/PARTS/PAUSED cluster up top (MV-262: PARTS carries a spinning gear
    /// glyph; MV-327: it's a wider chip with its own pulsing glow + unit suffix so it reads as a
    /// bank at a glance rather than a bare number, and CELLS' label shrinks to fit instead of
    /// overflowing over its icon), a narrow hero column on the left (Max's own key art
    /// above the RCDA's live render with a glowing tech-ring "loadout" treatment), and on the right
    /// a big hero-sized primary-weapon name followed by the 2x2 primary-track grid and the abilities
    /// grid. The abilities grid always shows all six slots (MV-262): owned ones by name, the rest as
    /// greyed, unnamed placeholder tiles, so the count of "more to find" reads at a glance instead of
    /// needing a text list. There is no bottom spendbar/instructional line any more — the parts count
    /// lives solely in the top-bar chip. Levels render as pip/segment bars, not text —
    /// <see cref="BuildGridRow"/> builds a shared row shape (highlight ring, icon + glyph, name, pip
    /// bar, + button) for both tracks and abilities so the two sections can never drift apart in style.
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
        private static readonly Color NewBadgeColor = new Color(0.45f, 0.95f, 0.55f);   // MV-250: newly-acquired flag
        private static readonly Color QuitColor = new Color(0.85f, 0.20f, 0.20f);       // MV-257: destructive-red
        private static readonly Color LockedIconBg = new Color(1f, 1f, 1f, 0.05f);      // MV-262: unowned ability tile

        // MV-251: every row's icon tile now tints by its OWN section accent (instead of a near-invisible
        // neutral box) and carries a short glyph, so a track/ability reads as a distinct tile at a glance
        // instead of a blank square next to a name.
        private static readonly Color TrackIconBg = new Color(PrimaryAccent.r, PrimaryAccent.g, PrimaryAccent.b, 0.22f);
        private static readonly Color AbilityIconBg = new Color(AbilityAccent.r, AbilityAccent.g, AbilityAccent.b, 0.22f);

        /// <summary>Where <c>Max.png</c> (MV-251, Lee's call for the hero panel) lives under a
        /// Resources folder — loaded by stable key per <c>docs/CODE_DRIVEN_SCENES.md</c> rule 4, not an
        /// Inspector-wired reference.</summary>
        private const string MaxPortraitResourcePath = "Art/Max";

        private const int TrackCount = 4;        // WeaponCatalog.AllTrackKinds.Length — every track is owned from run start (MV-291 added Damage, MV-299 added Depletion Rate)
        private const int MaxAbilityRows = 5;    // WeaponCatalog.AllAbilityKinds.Length — the catalog's fixed pool
        private const int MaxPips = 6;           // largest cap across tracks (MV-291: all now cap at 6) and abilities (WeaponCooldown=5)

        // MV-262: the abilities grid is a fixed 6-slot (3-row) grid regardless of how many are owned,
        // so unlike the old dynamic "shown + placeholder" layout there's a single worst case to
        // verify: primary header + primary grid (2 rows, MV-299: 4 tracks at 2 cols, unchanged from
        // MV-291's 2 rows) + abilities header + abilities grid (3 rows) all fit inside the content budget below the top bar
        // (there's no bottom spendbar any more) with room to spare for the hero-sized primary-name
        // block above the grids — see the arithmetic in
        // BuildPrimaryNameHeader/BuildPrimaryGrid/BuildAbilitiesSection; there's no runtime overflow
        // check, so this is verified by hand rather than measured. MV-291's third track pushes the
        // primary grid from 1 row to 2, costing one RowHeight+RowGap (116px) of the margin that left —
        // still fits inside RefH's 1080 budget, just with less spare room below the abilities grid.
        private const float RowHeight = 108f;
        private const float RowGap = 8f;
        private const float SectionHeaderHeight = 38f;
        private const float SectionHeaderGap = 10f;
        private const float SectionGap = 16f;
        private const float TopBarHeight = 104f;
        private const float ContentMargin = 28f;
        private const float ColGap = 0.04f;      // fraction of column width between the 2 grid cells

        private const float HeroColumnFraction = 0.27f;   // MV-262: narrowed from ~0.335 to give the weapons UI more room
        private const float HeroNameTagH = 28f;
        private const int HeroNameFontSize = 42;           // MV-262: the hero label — much bigger than the old 26pt name
        private const float HeroNameH = 150f;
        private const float HeroNameGap = 20f;

        private Canvas _canvas;
        private RectTransform _safeRoot;
        private GameObject _root;
        private UpgradeWeaponStage _weaponStage;   // MV-251: the RCDA's own live render — the hero panel used to mislabel Max's bust as this

        private Text _cellsText;
        private Text _partsText;
        private Image _partsIcon;          // MV-262: the gear glyph next to the parts count — spins while open
        private Image _partsGlow;          // MV-327: pulses while a part is banked — the "prominent, readable at a glance" tell
        private Image _weaponGlowRing;     // MV-262: the "cooler under Max" tech-ring behind the RCDA render — spins while open
        private Text _abilitiesHeaderText;
        private RectTransform _buildAbilityRoot;   // MV-358: BUILD ABILITY — spends a banked shed credit
        private Image _buildAbilityBg;
        private Text _buildAbilityLabel;
        private Button _buildAbilityButton;

        private readonly Text[] _trackName = new Text[TrackCount];
        private readonly Image[][] _trackPips = new Image[TrackCount][];
        private readonly Button[] _trackButton = new Button[TrackCount];
        private readonly Image[] _trackButtonBg = new Image[TrackCount];

        // One row slot per catalog ability, always active (MV-262): the leading slots are populated
        // from WeaponSystemState.Acquired in acquisition order (MV-333), the rest greyed via
        // SetAbilityLocked. A slot, once filled, keeps its AbilityKind forever — only the trailing
        // locked slots shift as more get acquired — but the kind behind each slot's button is still
        // tracked live in _abilityRowKind rather than baked into the click handler at build time
        // (locked slots' buttons are inactive, so their stale _abilityRowKind is never read).
        private readonly GameObject[] _abilityRow = new GameObject[MaxAbilityRows];
        private readonly Text[] _abilityName = new Text[MaxAbilityRows];
        private readonly Image[][] _abilityPips = new Image[MaxAbilityRows][];
        private readonly Button[] _abilityButton = new Button[MaxAbilityRows];
        private readonly Image[] _abilityButtonBg = new Image[MaxAbilityRows];
        private readonly AbilityKind[] _abilityRowKind = new AbilityKind[MaxAbilityRows];
        private readonly Image[] _abilityIcon = new Image[MaxAbilityRows];

        // MV-250: "clear feedback when something is picked up". An ability that's acquired since the
        // player last actually looked at this screen gets a NEW badge for the whole session it's first
        // shown in, then never again — _newThisOpen is a snapshot taken at Open() (so it doesn't churn
        // if something else triggers a Refresh while already up), folded into _seenAbilities at Close().
        private readonly HashSet<AbilityKind> _seenAbilities = new HashSet<AbilityKind>();
        private readonly HashSet<AbilityKind> _newThisOpen = new HashSet<AbilityKind>();

        // MV-251: per-row glyph label and highlight ring, alongside the existing icon-tint tell.
        private readonly Text[] _abilityIconGlyph = new Text[MaxAbilityRows];
        private readonly Image[] _abilityOutline = new Image[MaxAbilityRows];

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
            AbilityCreditBank.Changed += OnAbilityCreditsChanged;
        }

        private void OnDisable()
        {
            WeaponSystemState.Changed -= Refresh;
            PickupWallet.PartsChanged -= OnPartsChanged;
            PickupWallet.PowerCellsChanged -= OnCellsChanged;
            AbilityCreditBank.Changed -= OnAbilityCreditsChanged;
        }

        private void OnDestroy()
        {
            // Never leave the world frozen if we're torn down mid-open (a scene swap, a test).
            if (_open) Time.timeScale = _prevTimeScale;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        private void Update()
        {
            if (!_open) return;
            if (_weaponStage != null) _weaponStage.Tick(Time.unscaledTime, 0f, 0f);

            // MV-262: "playful/animated, not a sentence" — the parts gear and the RCDA's glow ring
            // spin while the screen is up. Time is frozen (Time.timeScale = 0 for the pause), so this
            // has to ride unscaledDeltaTime rather than Update's usual scaled clock.
            float dt = Time.unscaledDeltaTime;
            if (_partsIcon != null) _partsIcon.rectTransform.Rotate(0f, 0f, -dt * 90f);
            if (_weaponGlowRing != null) _weaponGlowRing.rectTransform.Rotate(0f, 0f, dt * 18f);

            // MV-327: the PARTS chip's outer glow only beats while a part is actually banked and
            // spendable — otherwise it stays fully transparent, same "only alert when actionable"
            // rule HudController's own part-alert badge follows.
            if (_partsGlow != null)
            {
                var c = PartsColor;
                c.a = PartsGlowAlpha(Time.unscaledTime, PickupWallet.PartsBanked);
                _partsGlow.color = c;
            }
        }

        /// <summary>The PARTS chip's glow-ring alpha, 0..1 (MV-327) — a pure function so the beat is
        /// pinned by an EditMode test without building a canvas. Zero whenever nothing is banked (no
        /// upgrade to flag); otherwise the same trough/peak sine beat <see cref="HudController"/>'s own
        /// part-alert badge uses, so every "a part is waiting" tell on screen pulses at one rate.</summary>
        public static float PartsGlowAlpha(float unscaledTime, int partsBanked)
        {
            if (partsBanked <= 0) return 0f;
            float t = 0.5f + 0.5f * Mathf.Sin(unscaledTime * 6f);
            return 0.5f * t;
        }

        private void OnPartsChanged(int banked) => Refresh();
        private void OnCellsChanged(int cells) => Refresh();
        private void OnAbilityCreditsChanged(int banked) => Refresh();

        /// <summary>Open the weapons area, pausing the game. Ignored if already open.</summary>
        public void Open()
        {
            if (_open) return;
            if (_canvas == null) Build();

            _open = true;
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;   // freeze the fight while the player reads/spends

            _newThisOpen.Clear();
            foreach (var kind in WeaponSystemState.Acquired)
                if (!_seenAbilities.Contains(kind)) _newThisOpen.Add(kind);

            Refresh();
            _root.SetActive(true);
            if (_weaponStage != null) _weaponStage.ShowInstalled();
        }

        /// <summary>Close the weapons area and resume at whatever speed it paused from.</summary>
        public void Close()
        {
            if (!_open) return;
            _open = false;
            foreach (var kind in _newThisOpen) _seenAbilities.Add(kind);   // NEW badges don't return
            _newThisOpen.Clear();
            Time.timeScale = _prevTimeScale;
            _root.SetActive(false);
            if (_weaponStage != null) _weaponStage.Hide();
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
            _partsText.text = $"{banked} PARTS";   // MV-327: a bare number read as decoration, not a bank

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
                _abilityButton[shown].gameObject.SetActive(true);   // a slot that was locked last Refresh needs its + back
                _abilityName[shown].color = TextColor;
                _abilityName[shown].text = WeaponCatalog.TitleCase(WeaponCatalog.DisplayName(kind));
                _abilityIconGlyph[shown].color = TextColor;
                _abilityIconGlyph[shown].text = AbilityGlyph(kind);
                SetPips(_abilityPips[shown], level, cap, AbilityAccent);
                SetSpendable(_abilityButton[shown], _abilityButtonBg[shown], banked > 0 && level < cap);
                // MV-250/MV-251: "clear feedback when something is picked up" — an ability first seen
                // this Open() gets its icon lit and the whole card ringed in the same colour, not just a
                // faint icon-tint change that's easy to miss.
                bool isNew = _newThisOpen.Contains(kind);
                _abilityIcon[shown].color = isNew ? NewBadgeColor : AbilityIconBg;
                _abilityOutline[shown].color = isNew ? NewBadgeColor : Color.clear;
                shown++;
            }
            // MV-262: every slot past the acquired count is now a greyed, unnamed placeholder tile
            // (not hidden, and not a text list) — the fixed 6-slot grid itself communicates "there are
            // more to find" at a glance.
            for (int i = shown; i < MaxAbilityRows; i++)
                SetAbilityLocked(i);

            _abilitiesHeaderText.text = $"ABILITIES — {shown} of {MaxAbilityRows} unlocked";

            // MV-358: the BUILD ABILITY button only exists while a shed credit is banked — no dead
            // button otherwise — and its label carries the count so spending several in a row is legible.
            int credits = AbilityCreditBank.Banked;
            if (_buildAbilityRoot != null) _buildAbilityRoot.gameObject.SetActive(credits > 0);
            if (_buildAbilityLabel != null)
                _buildAbilityLabel.text = $"BUILD ABILITY ({credits})";
        }

        /// <summary>Greys out ability slot <paramref name="i"/> into an unnamed "still to find" tile —
        /// no name, no pips, no spend button, just a dim "?" glyph.</summary>
        private void SetAbilityLocked(int i)
        {
            _abilityRow[i].SetActive(true);
            _abilityButton[i].gameObject.SetActive(false);
            _abilityName[i].text = string.Empty;
            _abilityIconGlyph[i].color = Dim;
            _abilityIconGlyph[i].text = "?";
            _abilityIcon[i].color = LockedIconBg;
            _abilityOutline[i].color = Color.clear;
            foreach (var pip in _abilityPips[i]) pip.gameObject.SetActive(false);
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

        /// <summary>BUILD ABILITY (MV-358): draws candidates the same way the old mid-fight modal did,
        /// but on demand from this screen instead of the instant a shed dies. Two or three candidates
        /// open the draft-pick card screen so the player chooses (<see cref="UpgradeScreen.OpenAbilityChoice"/>,
        /// which itself spends the credit once a card is tapped); exactly one candidate builds directly —
        /// a one-card choice would be a pointless tap, same rationale MV-357 used. Every ability already
        /// owned (credits outlived the pool, e.g. two credits banked with only one ability left) spends
        /// the credit with nothing to show for it rather than leaving a dead button.</summary>
        private void OnBuildAbilityTapped()
        {
            if (AbilityCreditBank.Banked <= 0) return;

            AbilityKind[] candidates = AbilityDraft.DrawCandidates();
            switch (candidates.Length)
            {
                case 0:
                    AbilityCreditBank.TrySpend();
                    return;
                case 1:
                    WeaponSystemState.Acquire(candidates[0]);
                    AbilityCreditBank.TrySpend();
                    return;
                default:
                    var screen = FindFirstObjectByType<UpgradeScreen>();
                    if (screen != null) screen.OpenAbilityChoice(candidates);
                    return;
            }
        }

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

            _weaponStage = UpgradeWeaponStage.Create(transform);

            BuildTopBar(rootRt);
            var content = BuildContentRect(rootRt);
            BuildHeroColumn(content);
            var main = BuildMainColumn(content);
            BuildMainColumnContent(main);

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
            title.text = "ABILITIES";

            // Right-hand cluster, laid out from the corner inward: a close affordance (the design has
            // none, but the HUD's WEAPONS button only ever opens — MV-234's OnWeaponsButtonTapped calls
            // Open(), never a toggle — so this screen needs its own way back out), then QUIT TO MENU
            // (MV-257 — this screen's opaque scrim hides the HUD's own HOME button underneath it, so
            // the only way back to the main menu while this is open used to be none at all), then
            // PAUSED, then the PARTS/CELLS banks.
            float cursor = -16f;
            cursor = BuildCloseButton(bar, cursor) - 16f;
            cursor = BuildQuitButton(bar, cursor) - 16f;

            var paused = AddText(bar, 26, PartsColor, TextAnchor.MiddleRight);
            Anchor(paused.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            paused.rectTransform.sizeDelta = new Vector2(160f, 44f);
            paused.rectTransform.anchoredPosition = new Vector2(cursor, 0f);
            paused.fontStyle = FontStyle.Bold;
            paused.text = "II PAUSED";
            cursor -= 160f + 16f;

            // MV-262: the parts chip's dot becomes a spinning gear glyph (see Update()). MV-327: it's
            // also wider, spells out PARTS instead of a bare number, and grows a pulsing glow ring
            // while a part is banked — "just a number" didn't read as a bank at a glance.
            const float partsChipWidth = 190f;
            var partsChip = BuildChip(bar, new Vector2(cursor, 0f), partsChipWidth, PartsColor,
                HudTextures.Gear(48, 8), 34f, out _partsText, out _partsIcon);
            partsChip.name = "Parts Chip";

            // Outer glow ring, same "halo behind an opaque pill" technique as HudController's own
            // part-alert badge (BuildPartAlert): sits behind the chip's BG so only the 10px overflow
            // beyond the pill's edge ever shows, driven by Update()'s pulse above.
            _partsGlow = AddImage(partsChip, HudTextures.RoundedBox(64, 0.5f), Color.clear, "Glow Ring");
            Stretch(_partsGlow.rectTransform, 10f);
            _partsGlow.type = Image.Type.Sliced;
            _partsGlow.raycastTarget = false;
            _partsGlow.transform.SetAsFirstSibling();
            cursor -= partsChipWidth + 16f;

            // MV-327: the label now shrink-to-fits its box (see BuildChip) instead of overflowing past
            // it, which used to draw wide strings like "224 CELLS" back over this dot icon.
            const float cellsChipWidth = 150f;
            var cellsChip = BuildChip(bar, new Vector2(cursor, 0f), cellsChipWidth, CellsColor,
                HudTextures.Disc(32), 20f, out _cellsText, out _);
            cellsChip.name = "Cells Chip";
        }

        /// <summary>A dismiss pill pinned at <paramref name="rightEdge"/> from the bar's right edge.
        /// MV-250: the original dark-on-dark square (same colour as every other row) read as
        /// invisible in playtest — nobody could tell it was a button, let alone the only way out.
        /// Filled bright amber with a dark bold label so it reads as an obvious, unmistakable
        /// call-to-action against the screen's near-black palette. Returns the edge's new cursor
        /// position (its left edge) for the next element to chain from.</summary>
        private float BuildCloseButton(RectTransform bar, float rightEdge)
        {
            const float w = 104f, h = 56f;
            var bg = AddImage(bar, HudTextures.RoundedBox(32, 0.5f), PartsColor, "Close Button");
            Anchor(bg.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            bg.rectTransform.anchoredPosition = new Vector2(rightEdge, 0f);
            bg.rectTransform.sizeDelta = new Vector2(w, h);   // wide pill, well above the 44pt tap-target floor
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;

            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Close);

            var label = AddText(bg.rectTransform, 24, PanelColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.text = "✕ CLOSE";
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;

            return rightEdge - w;
        }

        /// <summary>MV-257: a second, distinctly-coloured pill next to CLOSE — abandons the run and
        /// returns to the Home/save-slot screen via <see cref="RunFlow.QuitToMenu"/>. Red rather than
        /// CLOSE's amber so it never reads as "close this screen" by mistake: it's the destructive
        /// one. Same right-edge-cursor chaining as <see cref="BuildCloseButton"/>.</summary>
        private float BuildQuitButton(RectTransform bar, float rightEdge)
        {
            const float w = 200f, h = 56f;
            var bg = AddImage(bar, HudTextures.RoundedBox(32, 0.5f), QuitColor, "Quit Button");
            Anchor(bg.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            bg.rectTransform.anchoredPosition = new Vector2(rightEdge, 0f);
            bg.rectTransform.sizeDelta = new Vector2(w, h);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;

            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(RunFlow.QuitToMenu);

            var label = AddText(bg.rectTransform, 22, TextColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.text = "QUIT TO MENU";
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;

            return rightEdge - w;
        }

        /// <summary>A rounded pill: a tinted icon + a live count/label, right-anchored at
        /// <paramref name="offset"/> from the top bar's right edge (CELLS/PARTS). MV-262: the icon
        /// sprite/size is now caller-supplied (and handed back via <paramref name="icon"/>) so the
        /// PARTS chip can carry the spinning gear glyph while CELLS keeps the plain dot. MV-327: width
        /// is caller-supplied too (PARTS needs more room for its glow + unit suffix), and the label
        /// shrinks to fit its box instead of overflowing past it — the old fixed 24pt + Overflow wrap
        /// let a wide string ("224 CELLS") draw back over the icon at the box's left edge.</summary>
        private RectTransform BuildChip(RectTransform bar, Vector2 offset, float width, Color accent,
            Sprite iconSprite, float iconSize, out Text label, out Image icon)
        {
            var chip = NewRect("Chip", bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            chip.pivot = new Vector2(1f, 0.5f);
            chip.sizeDelta = new Vector2(width, 52f);
            chip.anchoredPosition = offset;

            var bg = AddImage(chip, HudTextures.RoundedBox(32, 0.5f), RowColor, "BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;

            icon = AddImage(chip, iconSprite, accent, "Icon");
            Anchor(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            icon.rectTransform.anchoredPosition = new Vector2(24f, 0f);
            icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
            icon.raycastTarget = false;

            label = AddText(chip, 24, accent, TextAnchor.MiddleRight);
            Anchor(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f));
            label.rectTransform.offsetMin = new Vector2(42f, -20f);
            label.rectTransform.offsetMax = new Vector2(-14f, 20f);
            label.fontStyle = FontStyle.Bold;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 14;
            label.resizeTextMaxSize = 24;
            return chip;
        }

        /// <summary>The area below the top bar, holding the hero column and the main (primary +
        /// abilities) column. MV-262: there's no bottom spendbar reserving space any more — the
        /// content rect now runs all the way down to the margin.</summary>
        private RectTransform BuildContentRect(RectTransform parent)
        {
            var content = NewRect("Content", parent, Vector2.zero, Vector2.one);
            content.offsetMin = new Vector2(ContentMargin, ContentMargin);
            content.offsetMax = new Vector2(-ContentMargin, -(TopBarHeight + ContentMargin * 1.5f));
            return content;
        }

        /// <summary>Left strip (MV-262: narrowed to <see cref="HeroColumnFraction"/> so the weapons UI
        /// on the right gets the reclaimed width): MAX's own key-art portrait, then the RCDA's live
        /// render under a "LOADOUT" tag and a rotating tech-ring glow. The primary weapon's own name
        /// now lives in the main column as the hero label (<see cref="BuildPrimaryNameHeader"/>) —
        /// this column is purely Max + his gear.
        ///
        /// MV-251: the card here used to show a RawImage of <c>MaxPortraitStage</c> — Max's own bust —
        /// under a "PRIMARY WEAPON" tag; the screen never rendered the weapon at all. Split honestly in
        /// two: <see cref="MaxPortraitResourcePath"/> (Lee's call) as Max's own showcase, and a small
        /// live render of the actual RCDA (<see cref="UpgradeWeaponStage"/>, already built and tested
        /// for the legacy pickup-reveal screen) under its own card.</summary>
        private RectTransform BuildHeroColumn(RectTransform content)
        {
            var column = NewRect("Hero Column", content, Vector2.zero, new Vector2(HeroColumnFraction, 1f));
            column.offsetMin = Vector2.zero;
            column.offsetMax = new Vector2(-16f, 0f);

            // The hero column is a narrow strip, and the content width itself is only ever a phone's
            // short edge (matchWidthOrHeight=1 keeps everything matched to height, not width). Every
            // piece here stacks full-width, top to bottom — a side-by-side icon+text row has no room
            // to breathe at this width.
            const float portraitH = 460f, gap = 18f;
            var card = NewRect("Portrait Card", column, new Vector2(0f, 1f), new Vector2(1f, 1f));
            card.pivot = new Vector2(0.5f, 1f);
            card.offsetMin = Vector2.zero; card.offsetMax = Vector2.zero;
            card.sizeDelta = new Vector2(0f, portraitH);
            card.anchoredPosition = Vector2.zero;

            // A thin rim in Max's own hoodie colour, peeking out from behind the card — the same
            // framing trick the legacy UpgradeScreen used (YT-176), so the two screens' Max art still
            // reads as belonging to the same identity even though this one is a different medium.
            var rim = AddImage(card, HudTextures.RoundedBox(40, 0.5f),
                                CharacterSkin.BaseColorFor(CharacterRole.Player), "Rim");
            Stretch(rim.rectTransform); rim.type = Image.Type.Sliced;

            var cardBg = AddImage(card, HudTextures.RoundedBox(40, 0.5f), CardColor, "BG");
            Stretch(cardBg.rectTransform, -4f); cardBg.type = Image.Type.Sliced;

            // MAX's key art (MV-251, Lee's call): preserveAspect keeps the portrait's own proportions
            // intact regardless of the card's actual on-device width, so it never stretches/distorts.
            var portrait = new GameObject("Max Portrait", typeof(RectTransform), typeof(Image));
            portrait.transform.SetParent(cardBg.rectTransform, false);
            var portraitImg = portrait.GetComponent<Image>();
            Stretch((RectTransform)portrait.transform, -14f);
            portraitImg.sprite = Resources.Load<Sprite>(MaxPortraitResourcePath);
            portraitImg.preserveAspect = true;
            portraitImg.raycastTarget = false;

            var mlabel = AddText(card, 24, TextColor, TextAnchor.LowerCenter);
            Anchor(mlabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            mlabel.rectTransform.sizeDelta = new Vector2(300f, 36f);
            mlabel.rectTransform.anchoredPosition = new Vector2(0f, 14f);
            mlabel.fontStyle = FontStyle.Bold;
            mlabel.text = "MAX";

            // MV-262: "the area under Max much cooler, not empty/plain" — a LOADOUT tag and a
            // rotating tech-ring glow (same idiom as the joystick base) behind the RCDA's live render,
            // instead of the old flat, static card.
            const float captionH = 30f;
            var caption = AddText(column, 20, PrimaryAccent, TextAnchor.UpperLeft);
            Anchor(caption.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            caption.rectTransform.sizeDelta = new Vector2(0f, captionH);
            caption.rectTransform.anchoredPosition = new Vector2(0f, -(portraitH + gap));
            caption.fontStyle = FontStyle.Bold;
            caption.text = "LOADOUT";

            const float weaponCardH = 210f;
            var weaponCard = NewRect("Weapon Card", column, new Vector2(0f, 1f), Vector2.one);
            weaponCard.pivot = new Vector2(0.5f, 1f);
            weaponCard.sizeDelta = new Vector2(0f, weaponCardH);
            weaponCard.anchoredPosition = new Vector2(0f, -(portraitH + gap + captionH + 6f));

            var ring = AddImage(weaponCard, HudTextures.TechRings(220, 3),
                new Color(PrimaryAccent.r, PrimaryAccent.g, PrimaryAccent.b, 0.55f), "Glow Ring");
            Anchor(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            ring.rectTransform.sizeDelta = new Vector2(weaponCardH * 1.15f, weaponCardH * 1.15f);
            ring.raycastTarget = false;
            _weaponGlowRing = ring;

            var weaponBg = AddImage(weaponCard, HudTextures.RoundedBox(28, 0.4f), CardColor, "BG");
            Stretch(weaponBg.rectTransform); weaponBg.type = Image.Type.Sliced;

            var weaponRender = AddRawImage(weaponCard, _weaponStage != null ? _weaponStage.Texture : null, "Weapon Render");
            Stretch(weaponRender.rectTransform, -12f);

            return column;
        }

        /// <summary>Right side (MV-262: widened via <see cref="HeroColumnFraction"/>): the hero-sized
        /// primary-weapon name, the primary-track grid, then the abilities grid.</summary>
        private RectTransform BuildMainColumn(RectTransform content)
        {
            var column = NewRect("Main Column", content, new Vector2(HeroColumnFraction, 0f), new Vector2(1f, 1f));
            column.offsetMin = new Vector2(16f, 0f);
            column.offsetMax = Vector2.zero;
            return column;
        }

        /// <summary>Threads a single top-down "cursor" (y, 0 at the column's top edge, growing more
        /// negative downward) through the primary-name header, the primary grid, and the abilities
        /// grid — so each section's own height determines where the next one starts instead of the
        /// three of them being laid out from independently hand-computed offsets.</summary>
        private void BuildMainColumnContent(RectTransform main)
        {
            float y = BuildPrimaryNameHeader(main, 0f);
            y = BuildPrimaryGrid(main, y);
            BuildAbilitiesSection(main, y);
        }

        /// <summary>MV-262: the primary weapon's name, hoisted out of the cramped hero column into the
        /// main column as a genuine hero label — "MUCH bigger" per the ticket, not a 26pt line under a
        /// weapon-render card.</summary>
        private float BuildPrimaryNameHeader(RectTransform column, float y)
        {
            var tag = AddText(column, 22, PrimaryAccent, TextAnchor.UpperLeft);
            Anchor(tag.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            tag.rectTransform.sizeDelta = new Vector2(0f, HeroNameTagH);
            tag.rectTransform.anchoredPosition = new Vector2(0f, y);
            tag.fontStyle = FontStyle.Bold;
            tag.text = "PRIMARY WEAPON";
            y -= HeroNameTagH + 6f;

            var name = AddText(column, HeroNameFontSize, TextColor, TextAnchor.UpperLeft);
            Anchor(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            name.rectTransform.sizeDelta = new Vector2(0f, HeroNameH);
            name.rectTransform.anchoredPosition = new Vector2(0f, y);
            name.fontStyle = FontStyle.Bold;
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.verticalOverflow = VerticalWrapMode.Overflow;
            name.text = WeaponCatalog.TitleCase(WeaponCatalog.PrimaryName);
            y -= HeroNameH + HeroNameGap;

            return y;
        }

        private float BuildPrimaryGrid(RectTransform column, float y)
        {
            var header = AddText(column, 28, TextColor, TextAnchor.UpperLeft);
            Anchor(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            header.rectTransform.sizeDelta = new Vector2(0f, SectionHeaderHeight);
            header.rectTransform.anchoredPosition = new Vector2(0f, y);
            header.fontStyle = FontStyle.Bold;
            header.text = $"PRIMARY — {TrackCount} upgrade tracks";

            float gridTop = y - (SectionHeaderHeight + SectionHeaderGap);
            for (int i = 0; i < TrackCount; i++)
            {
                int index = i;   // capture by value, not the loop variable
                var kind = WeaponCatalog.AllTrackKinds[i];
                var r = BuildGridRow(column, "Track Row", i, gridTop, RowHeight, RowGap, TrackIconBg);
                _trackName[i] = r.Name;
                _trackPips[i] = r.Pips;
                _trackButton[i] = r.PlusButton;
                _trackButtonBg[i] = r.PlusBg;
                r.IconGlyph.text = TrackGlyph(kind);   // static per track — never revisited by Refresh
                _trackButton[i].onClick.AddListener(() => OnTrackButtonTapped(index));
            }

            float primaryRows = (TrackCount + 1) / 2;
            return gridTop - (primaryRows * RowHeight + (primaryRows - 1) * RowGap) - SectionGap;
        }

        /// <summary>MV-262: always builds all six ability slots as active rows (never SetActive(false)
        /// past the acquired count) — <see cref="Refresh"/>/<see cref="SetAbilityLocked"/> then decide
        /// per-slot whether a row shows real data or a greyed, unnamed placeholder. There is no
        /// separate placeholder row any more; the grid itself is the "more to find" tell.</summary>
        private void BuildAbilitiesSection(RectTransform column, float y)
        {
            _abilitiesHeaderText = AddText(column, 28, TextColor, TextAnchor.UpperLeft);
            Anchor(_abilitiesHeaderText.rectTransform, new Vector2(0f, 1f), new Vector2(0.6f, 1f), new Vector2(0f, 1f));
            _abilitiesHeaderText.rectTransform.sizeDelta = new Vector2(0f, SectionHeaderHeight);
            _abilitiesHeaderText.rectTransform.anchoredPosition = new Vector2(0f, y);
            _abilitiesHeaderText.fontStyle = FontStyle.Bold;

            BuildBuildAbilityButton(column, y);

            float gridTop = y - (SectionHeaderHeight + SectionHeaderGap);
            for (int i = 0; i < MaxAbilityRows; i++)
            {
                int row = i;   // capture by value, not the loop variable
                var r = BuildGridRow(column, "Ability Row", i, gridTop, RowHeight, RowGap, AbilityIconBg);
                _abilityName[i] = r.Name;
                _abilityPips[i] = r.Pips;
                _abilityButton[i] = r.PlusButton;
                _abilityButtonBg[i] = r.PlusBg;
                _abilityIcon[i] = r.Icon;
                _abilityIconGlyph[i] = r.IconGlyph;
                _abilityOutline[i] = r.Outline;
                _abilityButton[i].onClick.AddListener(() => OnAbilityButtonTapped(row));
                _abilityRow[i] = r.Row.gameObject;
            }
        }

        /// <summary>MV-358: a pill on the abilities header row, right of the "X of Y unlocked" text —
        /// shown only while a shed credit is banked (see <see cref="Refresh"/>). Amber like the other
        /// spend affordances (PARTS chip, +buttons), since it's the same "you have something to spend"
        /// family of action.</summary>
        private void BuildBuildAbilityButton(RectTransform column, float y)
        {
            _buildAbilityRoot = NewRect("Build Ability Root", column, new Vector2(0.6f, 1f), new Vector2(1f, 1f));
            _buildAbilityRoot.pivot = new Vector2(1f, 1f);
            _buildAbilityRoot.sizeDelta = new Vector2(0f, SectionHeaderHeight);
            _buildAbilityRoot.anchoredPosition = new Vector2(0f, y);

            _buildAbilityBg = AddImage(_buildAbilityRoot, HudTextures.RoundedBox(20, 0.5f), PartsColor, "Build Ability Button");
            Stretch(_buildAbilityBg.rectTransform); _buildAbilityBg.type = Image.Type.Sliced;
            _buildAbilityBg.raycastTarget = true;

            _buildAbilityButton = _buildAbilityBg.gameObject.AddComponent<Button>();
            _buildAbilityButton.transition = Selectable.Transition.None;
            _buildAbilityButton.onClick.AddListener(OnBuildAbilityTapped);

            _buildAbilityLabel = AddText(_buildAbilityRoot, 22, PanelColor, TextAnchor.MiddleCenter);
            Stretch(_buildAbilityLabel.rectTransform);
            _buildAbilityLabel.fontStyle = FontStyle.Bold;
            _buildAbilityLabel.raycastTarget = false;

            _buildAbilityRoot.gameObject.SetActive(false);   // Refresh() turns it on once a credit is banked
        }

        /// <summary>The refs a built grid row hands back — <see cref="BuildGridRow"/> grew a couple more
        /// per-row pieces for MV-251 (an icon glyph, a highlight-ring "Outline"), enough that named
        /// fields on a small struct read better than yet another <c>out</c> parameter.</summary>
        private readonly struct GridRowRefs
        {
            public readonly RectTransform Row;
            public readonly Text Name;
            public readonly Image[] Pips;
            public readonly Button PlusButton;
            public readonly Image PlusBg;
            public readonly Image Icon;
            public readonly Text IconGlyph;
            public readonly Image Outline;

            public GridRowRefs(RectTransform row, Text name, Image[] pips, Button plusButton, Image plusBg,
                Image icon, Text iconGlyph, Image outline)
            {
                Row = row; Name = name; Pips = pips; PlusButton = plusButton; PlusBg = plusBg;
                Icon = icon; IconGlyph = iconGlyph; Outline = outline;
            }
        }

        /// <summary>Builds one grid slot (highlight ring, icon + glyph, name, pip bar, + button) at
        /// 2-column/N-row index <paramref name="slot"/>, anchored under <paramref name="top"/>. Shared
        /// by the primary tracks and the abilities section so the two can never drift apart in style.
        /// <paramref name="iconColor"/> is the row's baseline icon tint — its own section accent
        /// (MV-251: <c>TrackIconBg</c>/<c>AbilityIconBg</c>), not a shared neutral, so a tile reads as
        /// belonging to its section before you even read its name.</summary>
        private GridRowRefs BuildGridRow(RectTransform column, string name, int slot, float top,
            float rowHeight, float rowGap, Color iconColor)
        {
            var row = NewRect(name, column, new Vector2(0f, 1f), Vector2.one);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, rowHeight);
            PlaceGridRow(row, slot, top, rowHeight, rowGap);

            // A ring behind the card, only ever visible as a thin border once BG (inset 3px) sits over
            // it. Transparent by default; MV-251's "newly-acquired" tell lights this to NewBadgeColor
            // instead of relying on the icon tint alone.
            var outline = AddImage(row, HudTextures.RoundedBox(24, 0.35f), Color.clear, "Outline");
            Stretch(outline.rectTransform); outline.type = Image.Type.Sliced;

            var bg = AddImage(row, HudTextures.RoundedBox(24, 0.35f), RowColor, "BG");
            Stretch(bg.rectTransform, -3f); bg.type = Image.Type.Sliced;

            var iconBg = AddImage(row, HudTextures.RoundedBox(24, 0.35f), iconColor, "Icon");
            Anchor(iconBg.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            iconBg.rectTransform.anchoredPosition = new Vector2(18f, 0f);
            iconBg.rectTransform.sizeDelta = new Vector2(66f, 66f);
            iconBg.type = Image.Type.Sliced;

            var glyph = AddText(iconBg.rectTransform, 21, TextColor, TextAnchor.MiddleCenter);
            Stretch(glyph.rectTransform);
            glyph.fontStyle = FontStyle.Bold;
            glyph.raycastTarget = false;

            // Name sits in the row's upper half, pips in the lower half, both spanning the same
            // horizontal band between the icon and the + button (the [0.13, 0.82] fraction of the row).
            var nameText = AddText(row, 32, TextColor, TextAnchor.UpperLeft);
            Anchor(nameText.rectTransform, new Vector2(0.13f, 0.5f), new Vector2(0.82f, 1f), new Vector2(0f, 1f));
            nameText.rectTransform.offsetMin = new Vector2(8f, 0f);
            nameText.rectTransform.offsetMax = new Vector2(0f, -10f);
            nameText.fontStyle = FontStyle.Bold;

            var pipRow = NewRect("Pips", row, new Vector2(0.13f, 0f), new Vector2(0.82f, 0.5f));
            pipRow.offsetMin = new Vector2(8f, 12f);
            pipRow.offsetMax = new Vector2(0f, -6f);

            var pips = new Image[MaxPips];
            const float pipW = 46f, pipGap = 7f;
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
            buttonBg.rectTransform.anchoredPosition = new Vector2(-18f, 0f);
            buttonBg.rectTransform.sizeDelta = new Vector2(70f, 70f);   // >= 44pt tap target at the height-matched scale
            buttonBg.type = Image.Type.Sliced;
            buttonBg.raycastTarget = true;

            var plusButton = buttonBg.gameObject.AddComponent<Button>();
            plusButton.transition = Selectable.Transition.None;

            var plusLabel = AddText(buttonBg.rectTransform, 35, TextColor, TextAnchor.MiddleCenter);
            Stretch(plusLabel.rectTransform);
            plusLabel.text = "+";
            plusLabel.fontStyle = FontStyle.Bold;
            plusLabel.raycastTarget = false;

            return new GridRowRefs(row, nameText, pips, plusButton, buttonBg, iconBg, glyph, outline);
        }

        /// <summary>Short glyphs for the RCDA tracks' icon tiles (MV-251) — abbreviations, not
        /// gameplay identity, so kept local to this screen rather than added to <see cref="WeaponCatalog"/>.</summary>
        private static string TrackGlyph(WeaponTrackKind kind)
        {
            switch (kind)
            {
                case WeaponTrackKind.Range: return "RNG";
                case WeaponTrackKind.Spread: return "SPR";
                case WeaponTrackKind.Damage: return "DMG";
                case WeaponTrackKind.DepletionRate: return "DEP";
                default: return "?";
            }
        }

        /// <summary>Short glyphs for the abilities' icon tiles (MV-251) — same rationale as
        /// <see cref="TrackGlyph"/>.</summary>
        private static string AbilityGlyph(AbilityKind kind)
        {
            switch (kind)
            {
                case AbilityKind.WaterBalloon: return "H2O";
                case AbilityKind.Speed: return "SPD";
                case AbilityKind.Dash: return "DSH";
                case AbilityKind.Teleport: return "TP";
                case AbilityKind.WeaponCooldown: return "CD";
                default: return "?";
            }
        }

        /// <summary>Places a row at 2-column/N-row slot index <paramref name="slot"/> under
        /// <paramref name="top"/> — the fixed track/ability grid positions, built once.</summary>
        private static void PlaceGridRow(RectTransform row, int slot, float top, float rowHeight, float rowGap)
        {
            int r = slot / 2;
            int c = slot % 2;
            float y = top - r * (rowHeight + rowGap);
            row.anchoredPosition = new Vector2(0f, y);

            float xMin = c == 0 ? 0f : 0.5f + ColGap * 0.5f;
            float xMax = c == 0 ? 0.5f - ColGap * 0.5f : 1f;
            row.anchorMin = new Vector2(xMin, row.anchorMin.y);
            row.anchorMax = new Vector2(xMax, row.anchorMax.y);
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
