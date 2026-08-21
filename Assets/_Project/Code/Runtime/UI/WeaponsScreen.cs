using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;
using MaxWorlds.VFX;

namespace MaxWorlds.UI
{
    /// <summary>
    /// THE RIG (MV-423) — Max's ability board, replacing the old ABILITIES screen's primary-track
    /// grid, Water Balloon add-on row and 6-slot abilities grid with a single node-graph rendering of
    /// <c>rig_board.json</c> (MV-422's canonical model, MV-423's own <see cref="RigBoardLayout"/> for
    /// the geometry/colours/icons that model layer deliberately ignores). Every category, ability and
    /// fusion node is placed at the data file's own pixel coordinates on a fixed 1920x1080 board frame
    /// — position/size conformance is asserted against the data file by MV-463's PNG-vs-spec harness
    /// (MV-465 retired the EditMode coordinate assertions), so this class never re-derives a
    /// position; if a layout decision isn't in the JSON, it doesn't belong here.
    ///
    /// Top bar keeps its existing geometry (28/104 inset/height), CLOSE, QUIT TO MENU and the CELLS
    /// chip; SUPERCELLS is a tappable six-socket tray (MV-515: cashes one for 10 cells) and PAUSED is
    /// gone (there's no room and no need — the
    /// screen's own presence already says the game is paused). Self-installing pause-on-open overlay,
    /// same idiom as every other full-screen panel (UpgradeScreen/HomeScreen/ResultScreen).
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

        /// <summary>MV-445 defect 2 lowered 0.9 -> 0.83 to stop SUPPORT clipping down to 1.4:1. MV-472
        /// lowers it again, 0.83 -> 0.70: <see cref="RigBoardLayout"/>'s content-proportional column
        /// layout (item 3 of that ticket) fixed WHICH family ran out of room, but the board's own OUTER
        /// extent is unchanged by redistributing columns inside it — at iPad mini's aspect (the ticket's
        /// own SG1 evidence, 1078x815 = 1.323:1) the un-floored fit-to-width scale is ~0.744, so the OLD
        /// 0.83 floor was still artificially inflating the board past the visible window's own right edge
        /// (scaled x=1920 lands at 1757 vs a visible max of 1678 at that aspect) — the exact clipping
        /// this ticket's screenshot shows. 0.70 clears that with margin and, being a fit-to-width floor,
        /// never engages at 1.4:1-and-wider aspects at all (unchanged there from MV-445). This is NOT the
        /// ticket's own explicitly-forbidden fix for PHONE readability (44pt/11pt) — that's solved
        /// entirely separately by <see cref="RigBoardLayout"/>'s phone-mode radii/fonts, which render at
        /// board scale 1.0 always (phone's aspect is wider than 16:9, never narrower, so this floor never
        /// touches it). See
        /// <c>RigBoardChromeTests.EveryNodeAndRegionPanelFitsInsideTheVisibleWindowAtEveryTestedAspect</c>.
        /// Below the floor (aspects narrower than iPad mini's own), a little edge crop is still accepted
        /// (see <see cref="VisibleRefXWindow"/>) — this ticket's own required coverage stops at iPad mini
        /// and iPhone, not an unbounded narrower hypothetical.</summary>
        private const float BoardScaleFloor = 0.70f;

        private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.97f);
        private static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.06f, 0.99f);
        private static readonly Color HeaderAccent = new Color(0.07f, 0.17f, 0.15f, 1f);
        private static readonly Color RowColor = new Color(0.10f, 0.12f, 0.15f, 1f);
        private static readonly Color TextColor = Color.white;
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);

        private static readonly Color CellsColor = new Color(0.35f, 0.85f, 0.95f);
        private static readonly Color SupercellColor = new Color(1f, 0.72f, 0.28f);
        private static readonly Color SpendDisabled = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color QuitColor = new Color(0.85f, 0.20f, 0.20f);   // MV-257: destructive-red

        // MV-443: the level pill's dark backdrop (rig_board.json's own #0A0B0F, forced opaque-ish at
        // 0.94 per the ticket) — every node's pill fills with this regardless of family, so the state
        // read comes entirely from the pill's border/text colour, not its background.
        private static readonly Color PillBackdrop = new Color(10f / 255f, 11f / 255f, 15f / 255f, 0.94f);

        /// <summary>MV-516: test-only read of <see cref="PillBackdrop"/> — a contrast-ratio test needs
        /// the REAL backdrop colour the pill actually renders against, not a hand-copied duplicate.</summary>
        public static Color PillBackdropColor => PillBackdrop;

        private const float TopBarHeight = 104f;
        private const float ContentMargin = 28f;

        // ------------------------------------------------------------------ THE RIG board (MV-423)

        private const int HexSides = 6;
        private const int FusionSides = 4;
        private const float HexRotationDeg = -90f;   // pointy-top: vertex angles 60*i-90
        // MV-433: diamond (vertex up/down/left/right), not the hex's pointy-top rotation. MV-462: was 45
        // to compensate for HudTextures.PolygonEdge's old off-by-half-segment vertex math (fixed there
        // now) — 0 is what actually puts a vertex at 0/90/180/270 under the corrected formula.
        private const float FusionRotationDeg = 0f;
        private const float Sqrt3 = 1.7320508f;
        // MV-445 defect 6: was 34 — at that size, 6 sockets (socketsWidth 224px) did not fit the
        // socket row's own available half of BuildSupercellTray's old fixed 340px width (154px), so the
        // pips ran past the tray's own edge. Lowered alongside BuildSupercellTray's move to a
        // content-sized width (see that method's own doc comment).
        private const float SupercellsSocketSize = 26f;

        // MV-433: owned/lit-category halo canvas size as a multiple of the node's own hex bounds.
        // MV-446 defect 2 AC: must not exceed 1.25 (the halo's rect vs. the node radius) — headroom for
        // HudTextures.PolygonGlow's blur to fade out in, not a size the shape itself grows to (the glow
        // is drawn hex-tight now; see NodeGlowSprite). Peak alpha and blur width are data-driven
        // (RigBoardLayout.GlowAlphaOwned/GlowBlurOwned, .../Draft) so they're tunable without a code
        // change — was a plain circle sized to the node's SQUARE bounding box at a flat alpha, which
        // both spilled past a hexagon's narrower width and, at MV-443's raised 0.55, bloomed into a
        // lens-flare-looking blowout.
        private const float GlowRadiusMultiplier = 1.15f;

        private Canvas _canvas;
        private RectTransform _topBar;
        private RectTransform _screenRoot;
        private Image _background;
        private Image _screenScrim;
        private RectTransform _safeRoot;
        private GameObject _root;
        private RectTransform _boardScaleRoot;
        private RectTransform _boardRoot;

        /// <summary>MV-472: true once the board has committed to the phone geometry/positions (bigger
        /// radii/fonts, a scrollable content taller than 1080) instead of the standard fixed frame —
        /// decided by <see cref="IsPhoneLayout"/> off the live aspect, and re-decided (triggering a
        /// content rebuild) every time <see cref="ApplyBoardScale(float)"/> runs with a different verdict
        /// than last time. See that method's own doc comment for why a one-time Build()-time decision
        /// isn't enough — the ui-screens capture harness reuses one WeaponsScreen instance across every
        /// registered aspect, phone included.</summary>
        private bool _phoneMode;

        /// <summary>The direct child of <see cref="_boardRoot"/> that owns everything
        /// <see cref="BuildBoardContent"/> builds this pass — a plain full-frame passthrough in standard
        /// mode, the scroll Viewport in phone mode. Torn down and rebuilt whole by
        /// <see cref="DestroyBoardContent"/>/<see cref="BuildBoardContent"/> on a mode change, rather than
        /// _boardRoot itself, so _boardRoot (and every ancestor above it) never has to be re-created.</summary>
        private RectTransform _boardContentHost;

        /// <summary>Where every category/ability/fusion node, panel and connector actually parents to —
        /// <see cref="_boardContentHost"/> itself in standard mode, or its scroll Content child in phone
        /// mode. <see cref="BoardNode"/> searches this, not <see cref="_boardRoot"/>, so it keeps working
        /// after a phone-mode rebuild moves nodes a level deeper.</summary>
        private RectTransform _nodeParent;

        private Text _cellsText;
        private Image _cellsChipBg;
        private Image _cellsBorder;
        private Button _cellsChipButton;

        private Image _supercellsTrayBg;
        private Image _supercellsTrayBorder;
        private Button _supercellsTrayButton;
        private Text _supercellsTrayLabel, _supercellsTraySub;
        private readonly List<Image> _supercellsSockets = new List<Image>();
        private readonly List<Image> _supercellsSocketGlows = new List<Image>();
        private Text _supercellsOverflowText;

        private readonly Dictionary<string, RigNodeVisual> _abilityNodes = new Dictionary<string, RigNodeVisual>();
        private readonly Dictionary<string, RigNodeVisual> _categoryNodes = new Dictionary<string, RigNodeVisual>();
        private readonly Dictionary<string, RigNodeVisual> _fusionNodes = new Dictionary<string, RigNodeVisual>();
        private readonly Dictionary<string, Image> _categoryPanels = new Dictionary<string, Image>();
        private readonly Dictionary<string, Image> _categoryPanelBorders = new Dictionary<string, Image>();
        private readonly Dictionary<string, Image> _connectors = new Dictionary<string, Image>();

        // ------------------------------------------------------------------ Morphing Module draft (MV-424)

        // MV-425: was a hand-copied approximation of rig_board.json's "module" hex (#7FE3FF); now reads
        // the single named constant that data file's own colours block asked for. A property, not a
        // `static readonly` field, so it never bakes in a value ahead of RigBoardLayout's own load.
        private static Color DraftBadgeColor => HudController.ModuleColor;

        private Image _draftScrim;
        private RectTransform _draftBand;
        private Text _draftBandTitle, _draftBandSubtitle, _draftBandReason;
        private bool _draftActive;
        private readonly List<string> _draftCandidateIds = new List<string>();

        private bool _open;
        private float _prevTimeScale = 1f;

        /// <summary>Is THE RIG currently up (and the game paused)?</summary>
        public bool IsOpen => _open;

        /// <summary>A built node's root RectTransform by its <c>rig_board.json</c> id — the layout
        /// test's only way in, so it never has to guess GameObject names.</summary>
        public RectTransform BoardNode(string id)
        {
            if (_nodeParent == null) return null;
            var t = _nodeParent.Find(id);
            return t != null ? (RectTransform)t : null;
        }

        /// <summary>MV-516: the root Canvas — test-only access, same idiom as <see cref="BoardNode"/>, so
        /// a gap-measurement test can pin <c>scaleFactor</c> to a known value (bypassing CanvasScaler's
        /// own ambient-<see cref="Screen"/> read, the same reason <see cref="UiScreensDirector"/>'s own
        /// capture harness does the exact same override) before reading <see cref="RectTransform.GetWorldCorners"/>.</summary>
        public Canvas RootCanvas => _canvas;

        /// <summary>MV-516: the top bar's own root rect — test-only access, so a gap test can read its
        /// REAL built bottom edge via <see cref="RectTransform.GetWorldCorners"/> rather than asserting
        /// the authored <see cref="TopBarHeight"/>/<see cref="ContentMargin"/> constants directly.</summary>
        public RectTransform TopBar => _topBar;

        /// <summary>MV-433: the full-canvas opaque backdrop, first child of <see cref="_screenRoot"/>
        /// (drawn behind the Safe Area, the top bar and the board) — test-only access, same idiom as
        /// <see cref="BoardNode"/>. MV-440: <c>_screenRoot</c> is the single toggle that opens/closes
        /// THE RIG, so the backdrop can never again outlive the screen it belongs to.</summary>
        public Image Background => _background;

        /// <summary>MV-444: the near-opaque (97% alpha) black scrim over the whole root — "blocks taps
        /// to whatever's underneath while paused," and just as much washes out <see cref="Background"/>'s
        /// own colours.base tint everywhere it isn't covered by a node or panel. A pixel probe checking
        /// "outside every node/panel" against raw colours.base ignores this and fails on a perfectly
        /// correct capture — test-only access, same idiom as <see cref="Background"/>, so that check can
        /// composite the two instead of assuming the backdrop shows through unblended.</summary>
        public Image ScreenScrim => _screenScrim;

        /// <summary>MV-433: a category's tinted backdrop column — test-only access, same idiom as
        /// <see cref="BoardNode"/>.</summary>
        public Image CategoryPanel(string id) => _categoryPanels.TryGetValue(id, out var p) ? p : null;

        /// <summary>MV-443: a category panel's 1.5px family-coloured hairline edge — test-only access.</summary>
        public Image CategoryPanelBorder(string id) => _categoryPanelBorders.TryGetValue(id, out var p) ? p : null;

        /// <summary>MV-443: one tree connector by its own build-time id (<c>"conn:cat:ID>ID"</c>,
        /// <c>"conn:ab:ID>ID"</c> or <c>"conn:fusion:ID>ID"</c>) — test-only access, same idiom as
        /// <see cref="BoardNode"/>.</summary>
        public Image Connector(string id) => _connectors.TryGetValue(id, out var c) ? c : null;

        /// <summary>MV-463: the CELLS chip's own family-coloured hairline border — test/conformance-only
        /// access, same idiom as <see cref="CategoryPanelBorder"/>, so the ui-screens conformance pass
        /// can sample a known full-alpha pixel for its named-colour probe.</summary>
        public Image CellsBorder => _cellsBorder;

        /// <summary>MV-463: the SUPERCELL tray's own amber border — see <see cref="CellsBorder"/>.</summary>
        public Image SupercellsBorder => _supercellsTrayBorder;

        /// <summary>MV-433: the board's own scale-to-fit wrapper (never the same object as
        /// <see cref="BoardNode"/>'s parent frame, which stays fixed at 1920x1080 in its own local
        /// space regardless of this wrapper's scale) — test-only access to confirm the clamp applied.</summary>
        public float BoardScale => _boardScaleRoot != null ? _boardScaleRoot.localScale.x : 1f;

        private void Start() => Build();

        private void OnEnable()
        {
            RigState.Changed += Refresh;
            RigFusionState.Changed += Refresh;
            PickupWallet.SupercellsChanged += OnSupercellsChanged;
            PickupWallet.PowerCellsChanged += OnCellsChanged;
            PickupWallet.CapacityChanged += OnCellsChanged;
        }

        private void OnDisable()
        {
            RigState.Changed -= Refresh;
            RigFusionState.Changed -= Refresh;
            PickupWallet.SupercellsChanged -= OnSupercellsChanged;
            PickupWallet.PowerCellsChanged -= OnCellsChanged;
            PickupWallet.CapacityChanged -= OnCellsChanged;
        }

        private void OnDestroy()
        {
            // Never leave the world frozen if we're torn down mid-open (a scene swap, a test).
            if (_open) Time.timeScale = _prevTimeScale;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        /// <summary>The SUPERCELL tray's glow-ring alpha, 0..1 (MV-327, carried over; MV-515: pulses
        /// only while cashing one is actually possible, not merely while one is banked) — a pure
        /// function so the beat is pinned by an EditMode test without building a canvas. Zero unless
        /// <paramref name="cashable"/>; otherwise the same trough/peak sine beat every "something is
        /// waiting" tell on this HUD uses.</summary>
        public static float SupercellsGlowAlpha(float unscaledTime, bool cashable)
        {
            if (!cashable) return 0f;
            float t = 0.5f + 0.5f * Mathf.Sin(unscaledTime * 6f);
            return 0.5f * t;
        }

        /// <summary>MV-469/MV-515: every legal spend a board node's own button can currently perform —
        /// the guard <see cref="Button.interactable"/> is set from. Pure so it's pinned by an EditMode
        /// test without building a canvas. An owned, below-max node only ever needs cells
        /// (<see cref="CellSpend.TryUpgradeNode"/>); an unowned, cell-unlockable node needs cells alone
        /// (<see cref="CellSpend.TryUnlockNode"/>) — MV-515 retired the banked-Supercell unlock
        /// requirement this used to also check.</summary>
        public static bool IsAbilityNodeSpendable(string id, int cellsBanked)
        {
            bool draftable = RigState.IsCellUnlockable(id) && !RigState.IsOwned(id);
            bool canLevelUp = RigState.CanSpendPart(id); // owned, below max — TryUpgradeNode's own gate
            if (canLevelUp) return cellsBanked >= CellSpend.UpgradeCostFor(RigState.Level(id));
            if (draftable) return cellsBanked >= CellSpend.UnlockCostCells;
            return false;
        }

        /// <summary>MV-516: the ability node hex FILL/OUTLINE's own alpha, given the base (state-set)
        /// alpha <paramref name="baseAlpha"/> already carries — "a node the player can act on RIGHT NOW
        /// pulses slowly and unmistakably ... the hexagon FILL and OUTLINE, not just the thin outer
        /// ring." Pure so the cadence is pinned by an EditMode test without building a canvas or
        /// advancing <see cref="Time"/>, same idiom as <see cref="SupercellsGlowAlpha"/>. 1 rad/s — a
        /// third of OuterRing's own 3 rad/s pulse (<see cref="Update"/>) — reads as an invitation, not
        /// an alarm, per the ticket's own "Do not re-raise: whether the pulse should be fast — no, slow,
        /// roughly 1 Hz." <paramref name="spendable"/> MUST be the exact same predicate
        /// <see cref="IsAbilityNodeSpendable"/> drives <c>Button.interactable</c> from — a node that
        /// pulses and then refuses the tap is the bug this ticket exists to kill, so nothing here may
        /// ever compute affordability its own, looser way.</summary>
        public static float NodeActionPulseAlpha(float unscaledTime, float baseAlpha, bool spendable)
        {
            if (!spendable) return baseAlpha;
            float t = 0.55f + 0.45f * Mathf.Sin(unscaledTime * 1f);
            return baseAlpha * t;
        }

        /// <summary>MV-433: the board's scale-to-fit factor for a given screen aspect ratio (width /
        /// height), under the canvas's own <c>matchWidthOrHeight = 1</c> (match-by-height) rule — pure
        /// so the clamp is pinned by an EditMode test without building a canvas or touching
        /// <see cref="Screen"/>. 1.0 at 16:9 and wider (nothing to fit); shrinks below that, floored at
        /// <see cref="BoardScaleFloor"/> so a very narrow window never pushes a tap target smaller than
        /// the floor already costs (see that constant's own doc comment).</summary>
        public static float ComputeBoardScale(float aspect)
        {
            if (aspect <= 0f) return 1f;
            float visibleRefWidth = RefH * aspect;
            float raw = Mathf.Min(1f, visibleRefWidth / RefW);
            return Mathf.Max(raw, BoardScaleFloor);
        }

        /// <summary>MV-433: the board frame's own x-range (in its unscaled 1920x1080 reference space)
        /// that's actually on screen at a given aspect ratio, under match-by-height — independent of
        /// <see cref="ComputeBoardScale"/>'s clamp, this is simply what the device shows. Wider than
        /// 16:9 (e.g. the 932x430 phone target) shows the whole frame and then some (MinX goes
        /// negative); narrower crops both edges symmetrically about the frame's own centre (960).</summary>
        public static (float MinX, float MaxX) VisibleRefXWindow(float aspect)
        {
            float visibleRefWidth = RefH * aspect;
            float minX = (RefW - visibleRefWidth) * 0.5f;
            return (minX, RefW - minX);
        }

        // ------------------------------------------------------------------ phone layout (MV-472)

        /// <summary>A real device's landscape aspect: iPhone lands around ~2.15-2.2:1 (this project's own
        /// registered "phone" captureAspect is 2340x1080 = 2.1667), markedly wider than the tablet-class
        /// aspects THE RIG is already captured at (16:9 = 1.78, 16:10 = 1.6) and wider still than iPad
        /// mini's ~1.33.
        ///
        /// 2.10, not a rounder 1.95: <see cref="RigBoardLayout"/>'s phone content width is a FIXED budget
        /// sized to fit safely at this threshold (see its own <c>PhoneTargetWidth</c> doc comment) — every
        /// aspect phone mode ever actually renders at is >= this threshold, so a wider one only ever has
        /// MORE window to spare, never less. Picking the threshold too low (1.95) left a gap between it
        /// and the width budget's own safe floor where phone mode would select but its own fixed-width
        /// content didn't yet fit that aspect's narrower window — caught by this file's own EditMode
        /// coverage at an aspect of exactly 2.0, not a real device but well within where 1.95 would have
        /// selected phone mode. 2.10 sits with margin below the real registered "phone" aspect (2.1667)
        /// and above every tablet aspect this project targets, so all three still sort unambiguously.</summary>
        private const float PhoneAspectThreshold = 2.10f;

        /// <summary>MV-472: does <paramref name="aspect"/> call for the phone geometry (bigger radii/
        /// fonts, a scrollable content taller than 1080) instead of the standard fixed 1920x1080 frame?
        /// Pure so it's pinned by an EditMode test without building a canvas — same idiom as
        /// <see cref="ComputeBoardScale"/>.</summary>
        public static bool IsPhoneLayout(float aspect) => aspect >= PhoneAspectThreshold;

        private IReadOnlyList<RigCategoryLayout> Categories => _phoneMode ? RigBoardLayout.PhoneCategories : RigBoardLayout.Categories;
        private IReadOnlyList<RigAbilityLayout> Abilities => _phoneMode ? RigBoardLayout.PhoneAbilities : RigBoardLayout.Abilities;
        private IReadOnlyList<RigFusionLayout> Fusions => _phoneMode ? RigBoardLayout.PhoneFusions : RigBoardLayout.Fusions;
        private float RadiusCategory => _phoneMode ? RigBoardLayout.RadiusCategoryPhone : RigBoardLayout.RadiusCategory;
        private float RadiusAbility => _phoneMode ? RigBoardLayout.RadiusAbilityPhone : RigBoardLayout.RadiusAbility;
        private float RadiusFusion => _phoneMode ? RigBoardLayout.RadiusFusionPhone : RigBoardLayout.RadiusFusion;
        private float LabelFontSize => _phoneMode ? RigBoardLayout.LabelFontSizePhone : RigBoardLayout.LabelFontSize;
        private float CategoryLabelFontSize => _phoneMode ? RigBoardLayout.CategoryLabelFontSizePhone : RigBoardLayout.CategoryLabelFontSize;
        private float LevelPillFontSize => _phoneMode ? RigBoardLayout.LevelPillFontSizePhone : RigBoardLayout.LevelPillFontSize;
        private float LevelPillW => _phoneMode ? RigBoardLayout.LevelPillWPhone : RigBoardLayout.LevelPillW;
        private float LevelPillH => _phoneMode ? RigBoardLayout.LevelPillHPhone : RigBoardLayout.LevelPillH;
        private float FusionSubFontSize => _phoneMode ? RigBoardLayout.FusionSubFontSizePhone : RigBoardLayout.FusionSubFontSize;
        private float ForgeCaptionFontSize => _phoneMode ? RigBoardLayout.ForgeCaptionFontSizePhone : RigBoardLayout.ForgeCaptionFontSize;
        private float ForgeDividerY => _phoneMode ? RigBoardLayout.ForgeDividerYPhone : RigBoardLayout.ForgeDividerY;
        private float RegionRectY => _phoneMode ? RigBoardLayout.RegionRectYPhone : RigBoardLayout.RegionRectY;

        private int _lastScreenWidth, _lastScreenHeight;

        private void Update()
        {
            if (!_open) return;
            float dt = Time.unscaledDeltaTime;

            // MV-445 defect 2: ApplyBoardScale otherwise only runs from Build()/Refresh(), both driven
            // by RigState/PickupWallet events — a browser window resized WHILE the screen is open (no
            // ability/parts change in between) never re-fit the board until something else happened to
            // trigger a Refresh(). Cheap to poll every frame; only calls ApplyBoardScale on an actual
            // change.
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
            {
                _lastScreenWidth = Screen.width;
                _lastScreenHeight = Screen.height;
                ApplyBoardScale();
            }

            if (_supercellsTrayBg != null)
            {
                bool cashable = CanCashSupercell();
                var glow = SupercellColor;
                glow.a = 0.25f + SupercellsGlowAlpha(Time.unscaledTime, cashable);
                // Only the tray's own outline breathes; a tray with nothing cashable stays flat.
                if (cashable) _supercellsTrayBg.color = glow;
            }

            // The draftable-capability dashed ring pulses (ticket: "Left to you — the pulse rate on a
            // draftable capability"). Chosen: same family the SUPERCELL glow uses, twice as slow, so the
            // two "something's waiting" tells read as related but distinct.
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
            // MV-470 AC1: "a node the player cannot yet afford reads as inert" — a flat, non-pulsing
            // floor the ring/halo sit at once cellAffordable goes false, instead of Update() still
            // animating a node with nothing tappable behind it.
            const float InertRingAlpha = 0.16f;
            int cellsBanked = PickupWallet.PowerCells;
            foreach (var kv in _abilityNodes)
            {
                var v = kv.Value;
                // MV-516: ONE predicate drives every "you can act on this right now" tell — the ring,
                // the hex body, AND (in RefreshAbilityNode) Button.interactable. MV-470's own
                // CellSpend.IsCellActionAffordable read cells alone; IsAbilityNodeSpendable is the exact
                // gate the button itself is set from, so a node can never again pulse/invite a tap it
                // then refuses.
                bool spendable = IsAbilityNodeSpendable(kv.Key, cellsBanked);

                if (v.OuterRing != null && v.OuterRing.gameObject.activeSelf)
                {
                    // MV-462 defect 3: a draftable node in an unlit family must stay dimmed every frame,
                    // not just at the moment Refresh() ran — Update() drives this pulse independently of
                    // Refresh(), so without this factor the ring/halo would flash back up to full
                    // brightness on every tick regardless of the static dim RefreshAbilityNode applied.
                    float dim = FamilyLitForAbility(kv.Key) ? 1f : RigBoardLayout.FamilyDimFactor;
                    float baseAlpha = spendable ? pulse : InertRingAlpha;

                    var c = v.OuterRing.color;
                    c.a = baseAlpha * dim;
                    v.OuterRing.color = c;

                    // MV-433/MV-443: the draftable node's soft family-tinted halo pulses with the same
                    // ring/cadence. MV-470: restricted to the unowned/draftable case — OuterRing is now
                    // also active while owned-and-upgradeable, but that state's Glow is the flat
                    // GlowAlphaOwned halo (set once in RefreshAbilityNode) and must never be touched here.
                    if (!RigState.IsOwned(kv.Key) && v.Glow != null && v.Glow.gameObject.activeSelf)
                    {
                        var g = v.Glow.color;
                        g.a = baseAlpha * RigBoardLayout.GlowAlphaDraft * dim;
                        v.Glow.color = g;
                    }
                }

                // MV-516 item 2: the hex FILL/OUTLINE — not just the thin ring — is what has to carry
                // "you can act on this right now." NodeActionPulseAlpha is a no-op (returns baseAlpha
                // unchanged) when !spendable, so an inert node's hex never animates.
                if (v.HexFill != null)
                {
                    var fc = v.HexFill.color;
                    fc.a = NodeActionPulseAlpha(Time.unscaledTime, v.FillBaseAlpha, spendable);
                    v.HexFill.color = fc;
                }
                if (v.HexOutline != null)
                {
                    var oc = v.HexOutline.color;
                    oc.a = NodeActionPulseAlpha(Time.unscaledTime, v.OutlineBaseAlpha, spendable);
                    v.HexOutline.color = oc;
                }
            }
            _ = dt;
        }

        private void OnSupercellsChanged(int banked) => Refresh();
        private void OnCellsChanged(int cells) => Refresh();

        /// <summary>MV-515: is cashing a Supercell for <see cref="PickupWallet.SupercellCellValue"/>
        /// cells actually possible right now — at least one banked and room in the reserve for the full
        /// top-up. Thin instance wrapper over <see cref="RigActions.AnySupercellActionAffordable"/> so
        /// <see cref="Update"/> and <see cref="RefreshSupercellTray"/> ask the exact same question.</summary>
        private static bool CanCashSupercell() => RigActions.AnySupercellActionAffordable(
            PickupWallet.SupercellsBanked, PickupWallet.PowerCells, PickupWallet.Capacity);

        /// <summary>Open THE RIG, pausing the game. Ignored if already open. MV-425: if a Morphing
        /// Module draft is banked and waiting (<see cref="PendingMorphingModule"/>), opening here shows
        /// it immediately rather than the plain board — the player asked to open WEAPONS precisely
        /// because the HUD's cyan badge told them one was waiting.</summary>
        public void Open()
        {
            if (_open) return;

            if (PendingMorphingModule.HasPending)
            {
                OpenMorphingModuleDraft(PendingMorphingModule.Take());
                return;
            }

            if (_canvas == null) Build();

            _open = true;
            _prevTimeScale = TimeScaleCapture.ClampForCapture(Time.timeScale);
            Time.timeScale = 0f;   // freeze the fight while the player reads/spends

            Refresh();
            _screenRoot.gameObject.SetActive(true);
        }

        /// <summary>Close THE RIG and resume at whatever speed it paused from.</summary>
        public void Close()
        {
            if (!_open) return;
            _open = false;
            _draftActive = false;
            _draftCandidateIds.Clear();
            Time.timeScale = _prevTimeScale;
            _screenRoot.gameObject.SetActive(false);
        }

        /// <summary>A Morphing Module was collected (MV-424, replacing the old shed → badge → BUILD
        /// ABILITY modal chain): 0 candidates consumes the module with nothing granted, 1 grants it
        /// directly with no screen, 2(-3) opens THE RIG with just those candidates lit on the board —
        /// numbered, TAKE-labelled — and everything else dimmed. One tap takes it and closes the
        /// screen. MV-457: a shed now draws up to 2 locked CATEGORY ids instead of up to 3 ability ids
        /// — <paramref name="candidateIds"/> takes either shape unchanged, since
        /// <see cref="GrantDraftCandidate"/> and the board's own <c>_categoryNodes</c>/<c>_abilityNodes</c>
        /// lookups both key off the same disjoint id namespaces (all-caps category ids vs lowercase
        /// ability ids). Whichever candidate is left behind simply stays locked/unowned for a later
        /// module.</summary>
        public void OpenMorphingModuleDraft(string[] candidateIds)
        {
            if (candidateIds == null || candidateIds.Length == 0)
            {
                Debug.Log("[WeaponsScreen] Morphing Module consumed — nothing left to offer.");
                return;
            }
            if (candidateIds.Length == 1)
            {
                GrantDraftCandidate(candidateIds[0]);
                return;
            }

            if (_canvas == null) Build();

            _draftCandidateIds.Clear();
            _draftCandidateIds.AddRange(candidateIds);
            _draftActive = true;

            if (!_open)
            {
                _open = true;
                _prevTimeScale = TimeScaleCapture.ClampForCapture(Time.timeScale);
                Time.timeScale = 0f;
            }

            Refresh();
            _screenRoot.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------ live state

        /// <summary>Redraws the CELLS/SUPERCELLS banks and every node's visual state off
        /// <see cref="RigState"/> and <see cref="PickupWallet"/> — so a spend, a shed pickup, or a
        /// draft acquire (once MV-424 lands) while this screen happens to be open reflects immediately.</summary>
        private void Refresh()
        {
            if (_root == null) return;

            int banked = PickupWallet.SupercellsBanked;
            _cellsText.text = $"{PickupWallet.PowerCells}/{PickupWallet.Capacity} CELLS";

            // MV-458: e_cel's chip is actionable (and tappable) whenever cells could pay for whichever
            // action (unlock/upgrade) e_cel is currently in. MV-515 dropped the old "or a Supercell is
            // banked" leniency — unlocking no longer accepts a Supercell in place of cells.
            bool cellsOwned = RigState.IsOwned("e_cel");
            bool capacityActionable = cellsOwned
                ? PickupWallet.PowerCellCapacityLevel < PickupWallet.PowerCellCapacityMaxLevel
                : RigState.IsCellUnlockable("e_cel");
            int capacityCostCells = cellsOwned ? CellSpend.UpgradeCostFor(RigState.Level("e_cel")) : CellSpend.UnlockCostCells;
            bool capacitySpendable = capacityActionable && PickupWallet.PowerCells >= capacityCostCells;
            _cellsChipButton.interactable = capacitySpendable;
            // MV-446 defect 1: was tinting the BG SupercellColor/amber when a capacity level-up is
            // affordable — against the border/text's own colours.sec cyan (never touched here) that put
            // two near-equal-luminance colours on top of each other, ~1.15:1 contrast, making the
            // player's own cell count unreadable. The chip stays tappable via .interactable; readability
            // wins over the spend affordance.
            _cellsChipBg.color = RowColor;

            RefreshSupercellTray(banked);
            ApplyBoardScale();
            RefreshBoardState();
        }

        /// <summary>MV-472: the state-repaint tail of <see cref="Refresh"/>, factored out so
        /// <see cref="ApplyBoardScale(float)"/> can re-run it after a phone/standard mode change rebuilds
        /// the board's nodes from scratch — a freshly built node starts in <see cref="BuildAbilityNode"/>'s
        /// raw just-constructed state (label set, but no colour/pill/spend state applied yet), and this
        /// is the same loop that already paints it in every other Refresh() call.</summary>
        private void RefreshBoardState()
        {
            int cellsBanked = PickupWallet.PowerCells;
            foreach (var cat in Categories) RefreshCategoryNode(cat);
            foreach (var ab in Abilities) RefreshAbilityNode(ab);
            foreach (var fusion in Fusions) RefreshFusionNode(fusion, cellsBanked);
            RefreshConnectors();

            RefreshMorphingModuleDraft();
        }

        /// <summary>MV-443/MV-445: all three live-state connector families — ability connectors (family
        /// colour, alphaLive/alphaDim) and fusion connectors (defect 3: supercell/amber colour, gated by
        /// <see cref="RigFusionState.IsEligible"/> between fusionAlpha and the dimmer fusionAlphaLocked,
        /// no longer a static tint set once at build time).</summary>
        private void RefreshConnectors()
        {
            Color module = RigBoardLayout.Colour("module");
            foreach (var ab in RigBoardLayout.Abilities)
            {
                Color family = RigBoardLayout.Colour(RigBoardLayout.CategoryFamily(ab.Category));
                // MV-458: reads the tightened cells-unlock gate, not the looser IsReached the (now
                // production-dead) ability-level Morphing Module draft pool still uses — a connector
                // must not glow "live" toward a node the board can't actually let you tap open yet.
                bool owned = RigState.IsOwned(ab.Id);
                bool draftable = RigState.IsCellUnlockable(ab.Id) && !owned;
                // MV-462 defect 3: a connector is wholly inside one family (both ends share ab.Category),
                // so it dims exactly like every other graphic in an unlit one.
                bool familyLit = CategoryHasOwnedAbility(ab.Category);

                if (string.IsNullOrEmpty(ab.Parent))
                {
                    if (!_connectors.TryGetValue($"conn:cat:{ab.Category}>{ab.Id}", out var img)) continue;
                    bool live = familyLit || draftable;
                    img.color = DimIfUnlit(new Color(family.r, family.g, family.b, live ? RigBoardLayout.ConnectorAlphaLive : RigBoardLayout.ConnectorAlphaDim), familyLit);
                }
                else
                {
                    if (!_connectors.TryGetValue($"conn:ab:{ab.Parent}>{ab.Id}", out var img)) continue;
                    // MV-470: was RigState.IsOwned(ab.Parent) alone — a parent merely owned at level 1
                    // (not yet the level 2 IsCellUnlockable needs) lit this connector "live" even though
                    // the child itself still renders LOCK, the exact mismatch the ticket was filed over.
                    // "Live" now tracks the CHILD's own state; a parent-gated child gets its own distinct
                    // module-cyan tell instead, at the cell economy's colour, pointing back at whichever
                    // parent needs a level.
                    bool parentGated = !owned && !draftable && RigState.IsCategoryUnlocked(ab.Category);
                    bool live = owned || draftable;
                    Color lineColor = parentGated
                        ? new Color(module.r, module.g, module.b, RigBoardLayout.ConnectorAlphaLive)
                        : new Color(family.r, family.g, family.b, live ? RigBoardLayout.ConnectorAlphaLive : RigBoardLayout.ConnectorAlphaDim);
                    img.color = DimIfUnlit(lineColor, familyLit);
                }
            }

            Color supercell = RigBoardLayout.Colour("supercell");
            foreach (var fusion in RigBoardLayout.Fusions)
            {
                bool reachable = RigFusionState.IsEligible(fusion.Id);
                float alpha = reachable ? RigBoardLayout.ConnectorFusionAlpha : RigBoardLayout.ConnectorFusionAlphaLocked;
                foreach (var parentCategoryId in new[] { fusion.ParentA, fusion.ParentB })
                {
                    if (!_connectors.TryGetValue($"conn:fusion:{fusion.Id}>{parentCategoryId}", out var img)) continue;
                    img.color = new Color(supercell.r, supercell.g, supercell.b, alpha);
                }
            }
        }

        private static bool CategoryHasOwnedAbility(string categoryId)
        {
            foreach (var ab in RigBoardLayout.Abilities)
                if (ab.Category == categoryId && RigState.IsOwned(ab.Id)) return true;
            return false;
        }

        /// <summary>MV-462 defect 3: is <paramref name="abilityId"/>'s own category lit (≥1 owned
        /// ability anywhere in it)? <see cref="Update"/>'s per-frame pulse only has the ability id to
        /// hand (from <c>_abilityNodes</c>'s own key), so this re-derives the category the same way
        /// <see cref="RefreshAbilityNode"/> does at refresh time.</summary>
        private static bool FamilyLitForAbility(string abilityId)
        {
            foreach (var ab in RigBoardLayout.Abilities)
                if (ab.Id == abilityId) return CategoryHasOwnedAbility(ab.Category);
            return true;
        }

        /// <summary>Dims the whole board behind a scrim and brings just the candidate nodes back above
        /// it (MV-424) — the design's own "everything else dimmed" (MV-423.png vs MV-424.png). The scrim
        /// also blocks taps to every non-candidate node while a draft is pending, same as the paused
        /// screen already blocks the world underneath it.</summary>
        private void RefreshMorphingModuleDraft()
        {
            if (_draftScrim != null) _draftScrim.gameObject.SetActive(_draftActive);
            if (_draftBand != null) _draftBand.gameObject.SetActive(_draftActive);
            if (!_draftActive) return;

            for (int i = 0; i < _draftCandidateIds.Count; i++)
            {
                string id = _draftCandidateIds[i];
                RigNodeVisual v = _abilityNodes.TryGetValue(id, out var abilityVisual) ? abilityVisual
                    : _categoryNodes.TryGetValue(id, out var categoryVisual) ? categoryVisual : null;
                v?.Root.SetAsLastSibling();   // render above the scrim
            }

            UpdateDraftBandText();
        }

        /// <summary>The bottom band's copy (MV-424): a fixed title/subtitle plus one line naming why
        /// the FIRST numbered candidate is in the pool — showing all three would clutter a single-line
        /// band, and no acceptance criterion pins which one, so the numbered lead candidate is as good
        /// an example as any.</summary>
        private void UpdateDraftBandText()
        {
            if (_draftBandTitle == null || _draftCandidateIds.Count == 0) return;

            // MV-457: a shed's own draft now offers CATEGORY ids, not ability ids — RigBoard.Exists
            // disambiguates the two disjoint id namespaces the same way GrantDraftCandidate does.
            if (!RigBoard.Exists(_draftCandidateIds[0]))
            {
                _draftBandTitle.text = "MORPHING MODULE CAPTURED - CHOOSE A FAMILY";   // ASCII hyphen: LegacyRuntime.ttf has no em-dash coverage
                _draftBandSubtitle.text = "Unlocks every ability in that family for the rest of the run.";
                _draftBandReason.text = "The other family stays locked - the next shed offers it again.";
                return;
            }

            _draftBandTitle.text = "MORPHING MODULE CAPTURED - CHOOSE AN ABILITY";   // ASCII hyphen: LegacyRuntime.ttf has no em-dash coverage
            _draftBandSubtitle.text = "Only capabilities the tree already allows. A shed is the only way to own something new.";
            _draftBandReason.text = DraftReasonLine(_draftCandidateIds[0], _draftCandidateIds.Count - 1);
        }

        /// <summary>"<c>MAGNETO is here because you already have COOLDOWN.</c>" — schema 3 (MV-436)
        /// retired the "you put N parts into X" wording: every parent is a draft grant now, never a
        /// parts-spendable stat, so a candidate's parent is either already owned (having itself been
        /// drafted) or the candidate is a root node, open from the run's start.</summary>
        private static string DraftReasonLine(string candidateId, int leftBehindCount)
        {
            string label = AbilityLabel(candidateId);
            string parent = RigBoard.Parent(candidateId);

            string why = string.IsNullOrEmpty(parent)
                ? $"{label} is here because it was open from the run's start."
                : $"{label} is here because you already have {AbilityLabel(parent)}.";

            string leftBehind = leftBehindCount == 1
                ? "The one you leave goes back in the pool."
                : "The two you leave go back in the pool.";
            return $"{why} {leftBehind}";
        }

        private static string AbilityLabel(string id)
        {
            foreach (var ab in RigBoardLayout.Abilities)
                if (ab.Id == id) return ab.Label;
            return id;
        }

        /// <summary>MV-515: the Supercell tray is THE RIG's cash-in control, not a passive readout —
        /// tapping it (<see cref="OnSupercellTrayTapped"/>) cashes one Supercell for
        /// <see cref="PickupWallet.SupercellCellValue"/> cells. With nothing banked, or nothing banked
        /// AND no room for the full top-up, the tray renders unavailable and names the reason instead
        /// of merely sitting inert (the ticket's own AC3: "renders as unavailable with the reason
        /// visible, not merely inert").</summary>
        private void RefreshSupercellTray(int banked)
        {
            const int socketCount = 6;
            bool any = banked > 0;
            bool roomForCashIn = PickupWallet.Capacity - PickupWallet.PowerCells >= PickupWallet.SupercellCellValue;
            bool cashable = any && roomForCashIn;

            _supercellsTrayLabel.color = any ? SupercellColor : Dim;
            _supercellsTraySub.text = !any ? "none banked"
                : roomForCashIn ? $"{banked} banked · tap to cash"
                : $"{banked} banked · no room";
            _supercellsTraySub.color = cashable ? new Color(TextColor.r, TextColor.g, TextColor.b, 0.7f) : Dim;
            if (!any) _supercellsTrayBg.color = RowColor;

            for (int i = 0; i < socketCount; i++)
            {
                bool filled = i < banked;
                _supercellsSockets[i].sprite = filled
                    ? PolygonFillSprite(HexSides, Mathf.CeilToInt(SupercellsSocketSize * Sqrt3 * 0.5f), Mathf.CeilToInt(SupercellsSocketSize))
                    : SupercellsPipOutlineSprite();
                _supercellsSockets[i].color = filled ? SupercellColor : new Color(SupercellColor.r, SupercellColor.g, SupercellColor.b, 0.40f);
                _supercellsSocketGlows[i].gameObject.SetActive(filled);
                if (filled) _supercellsSocketGlows[i].color = new Color(SupercellColor.r, SupercellColor.g, SupercellColor.b, 0.35f);
            }
            int overflow = Mathf.Max(0, banked - socketCount);
            _supercellsOverflowText.gameObject.SetActive(overflow > 0);
            _supercellsOverflowText.text = $"+{overflow}";

            _supercellsTrayButton.interactable = cashable;
        }

        private void OnSupercellTrayTapped() => PickupWallet.TryCashSupercell();

        /// <summary>MV-443 defect 7: an unfilled SUPERCELL pip's own 2px outline — distinct from
        /// <see cref="SolidHexOutlineSprite"/>'s 4px <c>strokeOwned</c>, which never applied here.</summary>
        private Sprite SupercellsPipOutlineSprite() =>
            HudTextures.PolygonOutline(HexSides, HexRotationDeg,
                Mathf.CeilToInt(SupercellsSocketSize * Sqrt3 * 0.5f), Mathf.CeilToInt(SupercellsSocketSize), 2f);

        /// <summary>MV-443 defect 4: a category is never LOCK/"? ? ?" — it always names itself and its
        /// own owned/total count. "Lit" (has ≥1 owned ability) still gets the stronger owned-style
        /// treatment; "dark" gets its own third, always-legible state, never the ability node's locked
        /// look.</summary>
        private void RefreshCategoryNode(RigCategoryLayout cat)
        {
            if (!_categoryNodes.TryGetValue(cat.Id, out var v)) return;

            int candidateIndex = _draftActive ? _draftCandidateIds.IndexOf(cat.Id) : -1;
            if (candidateIndex >= 0)
            {
                RefreshCandidateNode(v, RigBoardLayout.Colour(cat.Family), cat.Id, candidateIndex);
                return;
            }
            v.DraftBadge.gameObject.SetActive(false);
            v.Button.interactable = false;   // MV-457: a category is only ever tappable as a draft candidate

            int owned = 0, total = 0;
            foreach (var ab in RigBoardLayout.Abilities)
            {
                if (ab.Category != cat.Id) continue;
                total++;
                if (RigState.IsOwned(ab.Id)) owned++;
            }
            // MV-457: "lit" now reads the shed's own unlock, not merely "has an owned ability" — a
            // freshly-unlocked family reads lit immediately, before the player has spent anything into it.
            bool lit = RigState.IsCategoryUnlocked(cat.Id);
            // MV-462 defect 3: the family DIM is its own, older concept — "has an owned ability" — kept
            // distinct from `lit` above so a shed-unlocked-but-still-empty family still dims correctly.
            bool familyLit = CategoryHasOwnedAbility(cat.Id);
            Color family = RigBoardLayout.Colour(cat.Family);
            Color ink = RigBoardLayout.Colour("ink");

            if (_categoryPanels.TryGetValue(cat.Id, out var panel))
                panel.color = DimIfUnlit(new Color(family.r, family.g, family.b, lit ? RigBoardLayout.RegionOpacityLit : RigBoardLayout.RegionOpacityDark), familyLit);
            if (_categoryPanelBorders.TryGetValue(cat.Id, out var border))
                border.color = DimIfUnlit(new Color(family.r, family.g, family.b, lit ? RigBoardLayout.RegionBorderAlphaLit : RigBoardLayout.RegionBorderAlphaDark), familyLit);

            if (lit)
            {
                v.HexFill.color = new Color(family.r, family.g, family.b, 0.30f);
                v.HexOutline.color = family;
                v.Glow.gameObject.SetActive(true);
                v.Glow.sprite = NodeGlowSprite(v.Radius, HexSides, RigBoardLayout.GlowBlurOwned);
                v.Glow.rectTransform.sizeDelta = NodeGlowSize(v.Radius, HexSides);
                v.Glow.color = new Color(family.r, family.g, family.b, RigBoardLayout.GlowAlphaOwned);
                v.OuterRing.gameObject.SetActive(true);
                v.OuterRing.color = new Color(family.r, family.g, family.b, 0.45f);
                v.Icon.color = ink;
            }
            else
            {
                v.HexFill.color = DimIfUnlit(new Color(family.r, family.g, family.b, 0.12f), familyLit);
                v.HexOutline.color = DimIfUnlit(new Color(family.r, family.g, family.b, 0.55f), familyLit);
                v.Glow.gameObject.SetActive(false);
                v.OuterRing.gameObject.SetActive(false);
                v.Icon.color = DimIfUnlit(new Color(family.r, family.g, family.b, 0.85f), familyLit);
            }

            v.PillText.text = $"{owned}/{total}";
            v.PillBg.color = DimIfUnlit(PillBackdrop, familyLit);
            v.PillBorder.color = DimIfUnlit(new Color(family.r, family.g, family.b, lit ? 0.95f : 0.3f), familyLit);
            v.PillText.color = DimIfUnlit(lit ? family : new Color(family.r, family.g, family.b, 0.7f), familyLit);
            v.Label.color = DimIfUnlit(new Color(ink.r, ink.g, ink.b, 0.62f), familyLit);
        }

        /// <summary>MV-462 defect 3: multiplies <paramref name="c"/>'s alpha by
        /// <see cref="RigBoardLayout.FamilyDimFactor"/> when <paramref name="lit"/> is false, on top of
        /// whatever state-specific alpha the caller already chose — an unowned family's locked-node
        /// treatment (say) gets fainter still, it isn't replaced by a flat dim colour. A no-op when
        /// <paramref name="lit"/> is true so a lit family's own per-node states are never touched.</summary>
        private static Color DimIfUnlit(Color c, bool lit) =>
            lit ? c : new Color(c.r, c.g, c.b, c.a * RigBoardLayout.FamilyDimFactor);

        private void RefreshAbilityNode(RigAbilityLayout ab)
        {
            if (!_abilityNodes.TryGetValue(ab.Id, out var v)) return;

            int candidateIndex = _draftActive ? _draftCandidateIds.IndexOf(ab.Id) : -1;
            if (candidateIndex >= 0)
            {
                RefreshCandidateNode(v, RigBoardLayout.Colour(RigBoardLayout.CategoryFamily(ab.Category)), ab.Label, candidateIndex);
                return;
            }
            v.DraftBadge.gameObject.SetActive(false);

            bool owned = RigState.IsOwned(ab.Id);
            bool maxed = owned && RigState.Level(ab.Id) >= ab.MaxLevel;
            // MV-458: "draftable" now means cell-unlockable — its category open and, for a non-root
            // node, its parent at level >= 2 (RigState.IsCellUnlockable, tighter than the IsReached the
            // now production-dead ability-level Morphing Module draft pool still uses).
            bool draftable = RigState.IsCellUnlockable(ab.Id) && !owned;
            // MV-470: the ticket's second lock reason — category already open (so the node IS reached
            // the old way) but the parent hasn't hit level 2 yet, the tighter cell-unlock gate. Distinct
            // from the deeper "family not unlocked" lock below; see RefreshConnectors for the matching
            // connector-line tell.
            bool parentGated = !owned && !draftable && RigState.IsCategoryUnlocked(ab.Category);
            int cellsBanked = PickupWallet.PowerCells;
            bool hasCellCost = draftable || (owned && !maxed);
            bool spendable = IsAbilityNodeSpendable(ab.Id, cellsBanked);
            // MV-470: whether CELLS alone would pay for this node's action right now — drives the
            // afford-dot (CapMarker) and, via Update(), whether the dashed ring pulses live or sits inert.
            bool cellAffordable = CellSpend.IsCellActionAffordable(ab.Id, cellsBanked);
            // MV-462 defect 3: owned==true implies this ability's category already has ≥1 owned ability,
            // so familyLit is always true on the `owned` branch below — DimIfUnlit only ever bites on
            // the draftable/locked branches, which is exactly the "family with nothing owned" case.
            bool familyLit = CategoryHasOwnedAbility(ab.Category);

            Color family = RigBoardLayout.Colour(RigBoardLayout.CategoryFamily(ab.Category));
            Color module = RigBoardLayout.Colour("module");
            Color ink = RigBoardLayout.Colour("ink");

            // MV-470: the dashed affordance ring and its cap-marker dot now mark ANY live cell action —
            // an owned node's upgrade as well as an unowned node's unlock — not just draftable, so a
            // player sitting on affordable cells sees the same "something's waiting" tell everywhere one
            // applies. CapMarker's own presence (rather than a flat colour) IS the "affordable now" read;
            // Update() only ever pulses it while cellAffordable stays true.
            v.OuterRing.gameObject.SetActive(draftable || (owned && !maxed));
            v.CapMarker.gameObject.SetActive(cellAffordable);
            v.HexOutline.sprite = draftable ? DashedHexSprite(v.Radius) : owned ? SolidHexOutlineSprite(v.Radius) : LockedHexOutlineSprite(v.Radius);

            // MV-470: the progress ring + cost tag are shared by both cell-costed branches (draftable's
            // unlock, owned-and-below-max's upgrade) — accumulation and legible cost read the same way
            // regardless of which side of "owned" the node is on.
            if (hasCellCost)
            {
                v.ProgressRing.gameObject.SetActive(true);
                v.ProgressRing.fillAmount = CellSpend.CellCostProgress01(ab.Id, cellsBanked);
                v.ProgressRing.color = DimIfUnlit(new Color(module.r, module.g, module.b, 0.9f), familyLit);
                v.CostIcon.gameObject.SetActive(true);
                v.CostIcon.color = DimIfUnlit(module, familyLit);
                v.CostText.gameObject.SetActive(true);
                v.CostText.text = CellSpend.CurrentCellCost(ab.Id).ToString();
                v.CostText.color = DimIfUnlit(module, familyLit);
            }
            else
            {
                v.ProgressRing.gameObject.SetActive(false);
                v.CostIcon.gameObject.SetActive(false);
                v.CostText.gameObject.SetActive(false);
            }

            if (owned)
            {
                v.HexFill.color = new Color(family.r, family.g, family.b, 0.30f);
                v.HexOutline.color = family;
                v.Glow.gameObject.SetActive(true);
                v.Glow.sprite = NodeGlowSprite(v.Radius, HexSides, RigBoardLayout.GlowBlurOwned);
                v.Glow.rectTransform.sizeDelta = NodeGlowSize(v.Radius, HexSides);
                v.Glow.color = new Color(family.r, family.g, family.b, RigBoardLayout.GlowAlphaOwned);
                v.PillText.text = $"{RigState.Level(ab.Id)}/{ab.MaxLevel}";
                v.PillBg.color = PillBackdrop;
                v.PillBorder.color = new Color(family.r, family.g, family.b, 0.95f);
                // MV-516 item 4: a mid-saturation family hue on PillBackdrop's near-black read as
                // "too faint to see" (Lee, 2000x900 screenshot). Ink (near-white/bone) on that same
                // backdrop clears WCAG's 4.5:1 floor at every family hue, unconditionally — the family
                // identity stays on the border, which never had a legibility job to do.
                v.PillText.color = ink;
                v.Label.text = ab.Label;
                v.Label.color = new Color(TextColor.r, TextColor.g, TextColor.b, 0.95f);
                v.Icon.color = ink;
                // MV-470: an owned-and-upgradeable node's own OuterRing/CapMarker read exactly like a
                // draftable node's — same module cyan, same live/inert rule — so the ring colour is set
                // here too, not left at BuildNodeShell's Color.clear (which only OuterRing's ALPHA gets
                // touched by Update()'s pulse; its RGB has to be set at least once per state, same
                // reasoning as MV-445 defect 4 below).
                v.OuterRing.color = new Color(module.r, module.g, module.b, 0f);
                v.CapMarker.color = module;
            }
            else if (draftable)
            {
                v.HexFill.color = DimIfUnlit(new Color(family.r, family.g, family.b, 0.16f), familyLit);
                v.HexOutline.color = DimIfUnlit(module, familyLit);
                // MV-433 item 3, MV-443 defect 5: the dashed-outer-ring halo is now a SOFT FAMILY glow
                // (was module cyan) — the module tint lives on the border/dot instead so it still reads
                // "draftable", pulsing in Update() alongside the ring itself. MV-446 defect 2: hex-tight
                // (NodeGlowSprite/NodeGlowSize), not a circle sized to the dashed ring's own radius —
                // that circle was wider than the hex on every axis the hex's flat edges don't reach.
                v.Glow.gameObject.SetActive(true);
                v.Glow.sprite = NodeGlowSprite(v.Radius, HexSides, RigBoardLayout.GlowBlurDraft);
                v.Glow.rectTransform.sizeDelta = NodeGlowSize(v.Radius, HexSides);
                v.Glow.color = DimIfUnlit(new Color(family.r, family.g, family.b, RigBoardLayout.GlowAlphaDraft), familyLit);
                // MV-445 defect 4: OuterRing's RGB was never set here — Update()'s pulse only ever
                // touched its alpha, leaving the dashed ring stuck at Color.clear's (0,0,0) RGB from
                // BuildNodeShell, i.e. black dashes instead of the module cyan every other draftable
                // tell uses. MV-462 defect 3: Update() itself applies the family dim to the pulsed alpha
                // (see its own comment) — the alpha set here is only ever the pre-pulse 0f, nothing to
                // dim on this line.
                v.OuterRing.color = new Color(module.r, module.g, module.b, 0f);
                v.CapMarker.color = DimIfUnlit(module, familyLit);
                // MV-458: was "SHED" — a shed now only ever unlocks a whole CATEGORY (MV-457), never an
                // individual node, so a draftable node's own unlock is this cell cost, tapped directly.
                v.PillText.text = CellSpend.UnlockCostCells.ToString();
                v.PillBg.color = DimIfUnlit(PillBackdrop, familyLit);
                v.PillBorder.color = DimIfUnlit(module, familyLit);
                v.PillText.color = DimIfUnlit(module, familyLit);
                v.Label.text = ab.Label;
                v.Label.color = DimIfUnlit(new Color(TextColor.r, TextColor.g, TextColor.b, 0.78f), familyLit);
                v.Icon.color = DimIfUnlit(new Color(family.r, family.g, family.b, 0.95f), familyLit);
            }
            else if (parentGated)
            {
                // MV-470: the trap the ticket was filed over — a node whose family is wide open but
                // whose own PARENT hasn't reached level 2 yet must not read identically to a node whose
                // whole family is still locked. Same LOCK/??? text (no new prose), but tinted the same
                // module cyan the cell economy uses everywhere else on the board, so the eye can trace
                // "this needs a cell spend upstream" straight to the parent's own pulsing cost tag.
                v.HexFill.color = DimIfUnlit(new Color(family.r, family.g, family.b, 0.035f), familyLit);
                v.HexOutline.color = new Color(module.r, module.g, module.b, 0.40f);
                v.Glow.gameObject.SetActive(false);
                v.PillText.text = "LOCK";
                v.PillBg.color = DimIfUnlit(PillBackdrop, familyLit);
                v.PillBorder.color = new Color(module.r, module.g, module.b, 0.35f);
                v.PillText.color = new Color(module.r, module.g, module.b, 0.55f);
                v.Label.text = "? ? ?";
                v.Label.color = DimIfUnlit(new Color(TextColor.r, TextColor.g, TextColor.b, 0.30f), familyLit);
                v.Icon.color = DimIfUnlit(new Color(family.r, family.g, family.b, 0.40f), familyLit);
            }
            else   // family not unlocked — the deepest lock, unchanged from before MV-470
            {
                v.HexFill.color = DimIfUnlit(new Color(family.r, family.g, family.b, 0.035f), familyLit);
                v.HexOutline.color = DimIfUnlit(new Color(family.r, family.g, family.b, 0.24f), familyLit);
                v.Glow.gameObject.SetActive(false);
                v.PillText.text = "LOCK";
                v.PillBg.color = DimIfUnlit(PillBackdrop, familyLit);
                v.PillBorder.color = DimIfUnlit(new Color(family.r, family.g, family.b, 0.22f), familyLit);
                v.PillText.color = DimIfUnlit(new Color(ink.r, ink.g, ink.b, 0.34f), familyLit);
                v.Label.text = "? ? ?";
                v.Label.color = DimIfUnlit(new Color(TextColor.r, TextColor.g, TextColor.b, 0.30f), familyLit);
                v.Icon.color = DimIfUnlit(new Color(family.r, family.g, family.b, 0.40f), familyLit);
            }

            // MV-516: the base (pre-pulse) alpha Update() pulses the hex FILL/OUTLINE around — captured
            // after every branch above has set its own final colour, so this always matches whichever
            // state just rendered, never a stale value from a prior Refresh().
            v.FillBaseAlpha = v.HexFill.color.a;
            v.OutlineBaseAlpha = v.HexOutline.color.a;

            v.Button.interactable = spendable;
        }

        /// <summary>A FORGE fusion diamond (MV-426, 5/5): faint with <c>? ? ?</c> and its two parent
        /// category names until both are lit, then amber with its real name and cost/slot once
        /// eligible — independent of the currently-banked CELLS count (MV-423.png vs -noparts.png) —
        /// and a stronger solid amber once actually forged, matching an owned ability's own "solid,
        /// no longer a prospect" read. MV-515: cost converted from parts to cells.</summary>
        private void RefreshFusionNode(RigFusionLayout fusion, int cellsBanked)
        {
            if (!_fusionNodes.TryGetValue(fusion.Id, out var v)) return;

            bool forged = RigFusionState.IsForged(fusion.Id);
            bool eligible = RigFusionState.IsEligible(fusion.Id);
            Color amber = SupercellColor;

            if (forged)
            {
                v.HexOutline.sprite = SolidPolygonOutlineSprite(FusionSides, v.Radius);
                v.HexFill.color = new Color(amber.r, amber.g, amber.b, 0.28f);
                v.HexOutline.color = amber;
                v.Icon.color = TextColor;
                v.Label.text = fusion.Label;
                v.Label.color = TextColor;
                v.Sub.text = $"FORGED · SLOT {fusion.HudSlot}";
                v.Sub.color = amber;
                v.Sub.fontSize = Mathf.RoundToInt(FusionSubFontSize);
                v.Button.interactable = false;
            }
            else if (eligible)
            {
                v.HexOutline.sprite = SolidPolygonOutlineSprite(FusionSides, v.Radius);
                v.HexFill.color = new Color(amber.r, amber.g, amber.b, 0.14f);
                v.HexOutline.color = amber;
                v.Icon.color = amber;
                v.Label.text = fusion.Label;
                v.Label.color = TextColor;
                v.Sub.text = $"{fusion.CellCost} CELLS · SLOT {fusion.HudSlot}";
                v.Sub.color = amber;
                v.Sub.fontSize = Mathf.RoundToInt(FusionSubFontSize);
                v.Button.interactable = cellsBanked >= fusion.CellCost;
            }
            else   // MV-443 defect 8, MV-445 defect 5: locked fusion diamond
            {
                v.HexOutline.sprite = LockedFusionOutlineSprite(v.Radius);
                v.HexFill.color = new Color(amber.r, amber.g, amber.b, 0.045f);
                v.HexOutline.color = new Color(amber.r, amber.g, amber.b, RigBoardLayout.LockedFusionBorderAlpha);
                v.Icon.color = new Color(amber.r, amber.g, amber.b, RigBoardLayout.LockedFusionIconAlpha);
                v.Label.text = "? ? ?";
                v.Label.color = Dim;
                v.Sub.text = $"{fusion.ParentA} + {fusion.ParentB}";
                var ink = RigBoardLayout.Colour("ink");
                v.Sub.color = new Color(ink.r, ink.g, ink.b, 0.22f);
                v.Sub.fontSize = Mathf.RoundToInt(FusionSubFontSize);
                v.Button.interactable = false;
            }
        }

        /// <summary>MV-443 defect 8: locked fusion diamond border, 2px — distinct from the eligible/
        /// forged states' shared <see cref="SolidPolygonOutlineSprite"/> (strokeOwned, 4px).</summary>
        private Sprite LockedFusionOutlineSprite(float r)
        {
            float w = r * 2f, h = r * 2f;
            return HudTextures.PolygonOutline(FusionSides, FusionRotationDeg, Mathf.CeilToInt(w), Mathf.CeilToInt(h), 2f);
        }

        /// <summary>A Morphing Module draft candidate (MV-424): lit in its family colour with a strong
        /// glow, numbered 1-3 in a badge above the hex, and <c>TAKE</c> in the level pill in place of
        /// the usual level/SHED/LOCK reading. Always tappable — draft candidates ignore the CELLS/
        /// SUPERCELLS banks entirely, a different currency from the amber "+" spend. MV-457: shared by both an ability
        /// node's own candidate render and a category node's — <paramref name="family"/>/<paramref name="label"/>
        /// are passed in rather than re-derived from a <see cref="RigAbilityLayout"/>, since a category
        /// candidate has no such layout to read.</summary>
        private void RefreshCandidateNode(RigNodeVisual v, Color family, string label, int candidateIndex)
        {
            v.OuterRing.gameObject.SetActive(false);
            v.CapMarker.gameObject.SetActive(false);
            v.HexOutline.sprite = SolidHexOutlineSprite(v.Radius);

            v.HexFill.color = new Color(family.r, family.g, family.b, 0.22f);
            v.HexOutline.color = family;
            v.Glow.gameObject.SetActive(true);
            v.Glow.sprite = NodeGlowSprite(v.Radius, HexSides, RigBoardLayout.GlowBlurOwned);
            v.Glow.rectTransform.sizeDelta = NodeGlowSize(v.Radius, HexSides);
            v.Glow.color = new Color(family.r, family.g, family.b, 0.55f);   // MV-424's own stronger draft-candidate glow, unchanged by MV-433/MV-446 (shape/size only)
            v.Icon.color = TextColor;
            v.Label.text = label;
            v.Label.color = TextColor;

            v.PillText.text = "TAKE";
            v.PillBg.color = new Color(DraftBadgeColor.r, DraftBadgeColor.g, DraftBadgeColor.b, 0.30f);
            v.PillBorder.color = DraftBadgeColor;
            v.PillText.color = DraftBadgeColor;

            v.DraftBadge.gameObject.SetActive(true);
            v.DraftBadge.color = DraftBadgeColor;
            v.DraftBadgeText.text = (candidateIndex + 1).ToString();

            // MV-516: a draft candidate's own base alpha, so a stale value from a pre-draft Refresh()
            // never leaks into Update()'s pulse if this node is ever iterated there.
            v.FillBaseAlpha = v.HexFill.color.a;
            v.OutlineBaseAlpha = v.HexOutline.color.a;

            v.Button.interactable = true;
        }

        /// <summary>MV-515: an owned node tries <see cref="CellSpend.TryUpgradeNode"/>; an unowned node
        /// tries <see cref="CellSpend.TryUnlockNode"/> — both cells-only now. A fusion tries
        /// <see cref="PartSpend.TrySpendOnFusion"/>, also cells (converted from parts by this
        /// ticket).</summary>
        private void OnRigNodeTapped(string id)
        {
            if (_draftActive)
            {
                if (_draftCandidateIds.Contains(id))
                {
                    GrantDraftCandidate(id);
                    Close();
                }
                return;   // the scrim already blocks non-candidate taps; belt-and-suspenders here
            }
            if (RigBoard.FusionExists(id)) { PartSpend.TrySpendOnFusion(id); return; }

            if (RigState.IsOwned(id)) CellSpend.TryUpgradeNode(id);
            else CellSpend.TryUnlockNode(id);
        }

        /// <summary>Grants a single draft candidate, whichever shape it is (MV-457): an ability node id
        /// (<see cref="RigBoard.Exists"/>) routes through <see cref="WeaponSystemState.AcquireById"/> as
        /// before; a category id (never a RIG node) unlocks the whole family via
        /// <see cref="RigState.UnlockCategory"/> instead.</summary>
        private static bool GrantDraftCandidate(string id) =>
            RigBoard.Exists(id) ? WeaponSystemState.AcquireById(id) : RigState.UnlockCategory(id);

        /// <summary>MV-458: e_cel is no longer special-cased — tapping the CELLS chip is just a
        /// convenience shortcut to the exact same tap the e_cel hex node itself accepts.</summary>
        private void OnCellsChipTapped() => OnRigNodeTapped("e_cel");

        /// <summary>MV-433 AC1: <c>colours.base</c>, forced fully opaque — the backdrop is meant to
        /// read as "the game is paused behind it; there is nothing to see through to," not a scrim, so
        /// alpha is never anything the data file (or a stray hex-with-alpha) could weaken.</summary>
        private static Color OpaqueBase()
        {
            var c = RigBoardLayout.Colour("base");
            c.a = 1f;
            return c;
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
            _canvas.sortingOrder = 210;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            // MV-440: one screen root, full-canvas, holding both the opaque backdrop and the Safe
            // Area — this is the only GameObject Open()/Close()/OpenMorphingModuleDraft() ever toggle,
            // so the backdrop (built as its first child, below) can never again go up without the rest
            // of the screen, or outlive it. _root stays permanently active from here on.
            _screenRoot = NewRect("Screen Root", _canvas.transform, Vector2.zero, Vector2.one);
            Stretch(_screenRoot);

            // MV-433: opaque, first child of the screen root (not Safe Area) so it sits behind the
            // top bar too and ignores the safe-area inset — the board draws over live gameplay
            // otherwise, which is what washed every family colour out against the lawn.
            _background = AddImage(_screenRoot, HudTextures.Solid(), OpaqueBase(), "Background");
            Stretch(_background.rectTransform);
            _background.raycastTarget = false;

            _safeRoot = NewRect("Safe Area", _screenRoot, Vector2.zero, Vector2.one);
            Stretch(_safeRoot);
            _safeRoot.gameObject.AddComponent<SafeArea>();

            _root = new GameObject("Weapons Root", typeof(RectTransform));
            var rootRt = (RectTransform)_root.transform;
            rootRt.SetParent(_safeRoot, false);
            Stretch(rootRt);

            _screenScrim = AddImage(rootRt, HudTextures.Solid(), Scrim, "Scrim");
            Stretch(_screenScrim.rectTransform);
            _screenScrim.raycastTarget = true;   // blocks taps to whatever's underneath while paused

            // MV-433: a scale-to-fit wrapper.
            //
            // MV-472: anchor is (0.5, 1)-(0.5, 1) — a PROPORTIONAL horizontal-centre point on rootRt's
            // own actual width — not the (0, 1)-(0, 1) top-left point anchor this used to be. That
            // looked equivalent at 16:9 (rootRt is exactly 1920 wide there, so anchoredPosition.x=960
            // WAS the true centre) but silently broke at any narrower aspect: under match-by-height,
            // rootRt's own ref-space width is RefH*aspect, e.g. 1728 at 16:10 — a FIXED offset of 960
            // from its top-left lands 96px right of that canvas's TRUE centre (864), so the whole
            // (correctly scaled) board rendered shifted right by that same 96px, clipping the right edge
            // by exactly the amount VisibleRefXWindow's own symmetric-crop formula assumed was already
            // accounted for. Caught by actually opening rig-16x10.png during this ticket's own cc-screens
            // pass — EditMode coverage (ComputeBoardScale/VisibleRefXWindow are pure functions, never
            // rendered) couldn't have caught it, only a real capture could. A proportional anchor point
            // is always at 50% of whatever rootRt's actual width is, so this now holds at every aspect,
            // not just 16:9 — the exact fix VisibleRefXWindow's formula was already assuming existed.
            //
            // MV-516: pivot's Y is 1 (top), not the 0.5 (centre) MV-433 originally gave it. A CENTRE
            // pivot shrinks top-of-frame content DOWNWARD as scale drops below 1 (every ref-y above the
            // 540 midpoint moves toward it) — harmless for X (that's the whole point of the horizontal
            // centring above) but for Y it fights the very fix this ticket makes: at iPad mini's aspect
            // (scale ~0.744) a centre pivot pushed the category row's own screen position DOWN by ~80
            // ref px versus its unscaled position, opening a ~124px dead band under the top bar where
            // rig_board.json's rowY.category=230 alone only produces 26px at 16:9. Nothing NEEDS Y to
            // shrink toward a centre at all — CanvasScaler's own match-by-height mode already maps 1080
            // ref px to the full screen height losslessly at every aspect; the boardScaleRoot's uniform
            // scale exists purely to squeeze WIDTH at a narrower-than-16:9 aspect. A top pivot means
            // shrinking pulls content toward the frame's own top instead of its centre — the vertical
            // gap now only ever SHRINKS as aspect narrows (verified: -14px, i.e. a hair of overlap, not
            // 124px of empty space, at iPad mini), never grows the dead band the ticket exists to fix.
            _boardScaleRoot = NewRect("Board Scale Root", rootRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            _boardScaleRoot.pivot = new Vector2(0.5f, 1f);
            _boardScaleRoot.sizeDelta = new Vector2(RefW, RefH);
            _boardScaleRoot.anchoredPosition = Vector2.zero;

            // MV-423: the board is a fixed 1920x1080 frame (top-left anchored/pivoted) so every node's
            // json (x,y) maps 1:1 onto anchoredPosition — RigBoardLayoutTests asserts that mapping
            // exactly, in Board Root's own LOCAL space, which the scale wrapper above never touches (a
            // parent's localScale doesn't change a child's anchoredPosition/sizeDelta). It sits directly
            // under the scale wrapper (not a further-inset content rect) because the json's own
            // coordinates (rowY.category=230 etc.) already clear the top bar (28/104).
            _boardRoot = NewRect("Board Root", _boardScaleRoot, new Vector2(0f, 1f), new Vector2(0f, 1f));
            _boardRoot.pivot = new Vector2(0f, 1f);
            _boardRoot.sizeDelta = new Vector2(RefW, RefH);
            _boardRoot.anchoredPosition = Vector2.zero;

            // MV-472: the initial mode guess off the ambient aspect, same signal ApplyBoardScale()'s
            // own paramless overload already reads — good enough for a real device/browser (the common
            // case) since nothing has yet asked for a different aspect. ApplyBoardScale(float) below
            // (called at the end of this method) re-derives the same verdict and finds it unchanged, so
            // this never double-builds; it only matters as the seed a later explicit-aspect call (the
            // ui-screens capture harness driving a shot through several registered aspects on this one
            // instance) can detect a change against.
            float initialAspect = Screen.height > 0 ? (float)Screen.width / Screen.height : RefW / RefH;
            _phoneMode = IsPhoneLayout(initialAspect);
            BuildBoardContent();

            BuildTopBar(rootRt);   // drawn after the board so it sits above it in the hierarchy

            ApplyBoardScale();
            _screenRoot.gameObject.SetActive(false);
        }

        /// <summary>MV-433: recomputes and applies the board's scale-to-fit factor from the AMBIENT
        /// screen aspect. Called from <see cref="Build"/> once and from <see cref="Refresh"/> on every
        /// state change so a resize (or a different device) since the last <see cref="Open"/> is picked
        /// up without needing its own event — cheap enough to just fold into the existing refresh.
        ///
        /// MV-462 defect 2: <c>Screen.width</c>/<c>Screen.height</c> is the ambient display/Game-view
        /// size, which is NOT the same thing as whatever a caller is actually rendering this canvas into
        /// — <c>UiScreensDirector</c>'s capture flips the canvas to <c>ScreenSpaceCamera</c> and renders
        /// into an explicit w x h <c>RenderTexture</c> (already true of <c>ComputeScaleFactor</c>'s own
        /// CanvasScaler override, for exactly the same reason), so a headless capture at 1920x1080 was
        /// still reading whatever the ambient batchmode window happened to report, shrinking and
        /// recentring a board that should have rendered at scale 1. See the explicit-aspect overload
        /// below, which the capture harness now drives directly.</summary>
        private void ApplyBoardScale()
        {
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : RefW / RefH;
            ApplyBoardScale(aspect);
        }

        /// <summary>Applies the scale-to-fit factor for an explicit aspect ratio, bypassing the ambient
        /// <see cref="Screen"/> singleton entirely (MV-462 defect 2) — <c>UiScreensDirector</c> drives
        /// this with its actual capture-target aspect the same way <see cref="Screen"/>-based
        /// <c>ComputeScaleFactor</c> already bypasses <c>Screen</c> for the CanvasScaler. Public so both
        /// the capture harness and an EditMode test can drive it without a real screen/window.</summary>
        public void ApplyBoardScale(float aspect)
        {
            if (_boardScaleRoot == null) return;

            // MV-472: the phone/standard verdict can change on THIS call — the ui-screens capture
            // harness reuses one WeaponsScreen instance across every registered aspect (16:9, 16:10,
            // phone), each shot calling this with its own real aspect well after Build() already ran
            // once at whatever the ambient aspect happened to be. A verdict change means the board's
            // nodes were built at the wrong radii/fonts/positions for this aspect, so rebuild them from
            // scratch before applying the scale-to-fit factor below.
            bool wantPhoneMode = IsPhoneLayout(aspect);
            if (wantPhoneMode != _phoneMode || _nodeParent == null)
            {
                _phoneMode = wantPhoneMode;
                DestroyBoardContent();
                BuildBoardContent();
                RefreshBoardState();
            }

            float scale = ComputeBoardScale(aspect);
            _boardScaleRoot.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>MV-472: builds everything under <see cref="_boardContentHost"/> — the category
        /// panels/connectors/FORGE section/nodes/draft chrome, off whichever geometry <see cref="_phoneMode"/>
        /// currently selects. Called once from <see cref="Build"/> and again from
        /// <see cref="ApplyBoardScale(float)"/> whenever the phone/standard verdict changes.</summary>
        private void BuildBoardContent()
        {
            _nodeParent = _phoneMode ? BuildPhoneScrollViewport(_boardRoot, out _boardContentHost) : BuildStandardBoardContent(_boardRoot);

            BuildCategoryPanels(_nodeParent);
            BuildConnectors(_nodeParent);   // MV-443: behind the panels' contents, but above the panel fill
            BuildForgeSection(_nodeParent);
            foreach (var cat in Categories) _categoryNodes[cat.Id] = BuildCategoryNode(_nodeParent, cat);
            foreach (var ab in Abilities) _abilityNodes[ab.Id] = BuildAbilityNode(_nodeParent, ab);

            BuildDraftScrim(_nodeParent);   // MV-424: last board child so it dims everything built above,
            BuildDraftBand(_nodeParent);    // then the draft nodes come back on top of it (RefreshMorphingModuleDraft)
        }

        /// <summary>Standard mode's node parent (MV-472, current spec, defect 3): a masked vertical
        /// ScrollRect, the same pattern <see cref="BuildPhoneScrollViewport"/> already uses for phone —
        /// there was previously no mask or scroll here at all, so the FORGE section's own fusion
        /// sub-caption (the deepest content, node Y 910 + label offset 86 + 22 + half its own 24-tall
        /// box ≈ y=1030 in the SAME 1920x1080 frame every node's authored (x, y) already assumes) sat
        /// within ~50px of the 1080 bottom edge with no room for error and no way to reach it if it
        /// didn't fit — this is what Lee saw clipped on iPad mini. Unlike phone's viewport (offset below
        /// its own taller top bar), this one spans the FULL boardRoot rect at (0,0) so every existing
        /// node position — already authored assuming placement directly in the full frame — renders
        /// exactly where it always has when unscrolled; only content past the fold becomes reachable by
        /// scrolling instead of invisible. A distinct child of <see cref="_boardRoot"/> (not _boardRoot
        /// itself) purely so <see cref="DestroyBoardContent"/> has something of its own to destroy on a
        /// mode change without ever touching _boardRoot or any of its ancestors.</summary>
        private RectTransform BuildStandardBoardContent(RectTransform boardRoot)
        {
            var viewport = NewRect("Board Viewport", boardRoot, Vector2.zero, Vector2.one);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = NewRect("Board Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f));
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            float contentHeight = Mathf.Max(RefH, RigBoardLayout.StandardContentHeight);
            content.sizeDelta = new Vector2(0f, contentHeight);

            var scrollRect = viewport.gameObject.AddComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;

            BuildScrollHint(viewport, RefH, contentHeight);

            _boardContentHost = viewport;
            return content;
        }

        /// <summary>Phone mode's node parent: a vertical ScrollRect (MV-472 item 2 — "reflow ... rather
        /// than overflow"). The phone row schedule (<see cref="RigBoardLayout.PhoneContentHeight"/>) is
        /// taller than the 1080-tall reference frame can show at once once nodes/fonts are big enough to
        /// clear Apple's 44pt/11pt floors, so content scrolls under a masked viewport instead of clipping
        /// or cramming. The viewport sits between the top bar and the board's own bottom margin, in the
        /// SAME fixed 1920x1080 board frame every other measurement on this screen already uses.</summary>
        private RectTransform BuildPhoneScrollViewport(RectTransform boardRoot, out RectTransform host)
        {
            const float viewportTop = 140f, viewportBottom = 1050f;

            // MV-472: RigBoardLayout's phone column layout deliberately extends outside the standard
            // [0, RefW] frame (its own centred-content math — see BuildColumnLayout's own cursor
            // comment) since a wide phone aspect's real visible window is wider than 1920. A viewport
            // sized to boardRoot's own 1920 width would RectMask2D-clip that overflow on both edges —
            // caught live: a first pass sized the viewport to boardRoot's own width and PRIMARY clipped
            // off the left edge of rig-phone.png despite fitting VisibleRefXWindow's own check (the mask
            // was the thing clipping it, not the actual visible window). horizontalOverflow (700, each
            // side) comfortably exceeds PhoneTargetWidth's own worst-case span with room to spare.
            const float horizontalOverflow = 700f;

            var viewport = NewRect("Board Viewport", boardRoot, new Vector2(0f, 1f), new Vector2(1f, 1f));
            viewport.pivot = new Vector2(0.5f, 1f);
            viewport.anchoredPosition = new Vector2(0f, -viewportTop);
            viewport.sizeDelta = new Vector2(horizontalOverflow * 2f, viewportBottom - viewportTop);
            viewport.gameObject.AddComponent<RectMask2D>();

            // Content's own local origin (its top-left, where a node's anchoredPosition.x=0 would land)
            // must still land on boardRoot-frame x=0 — the same reference every node's own RigBoardLayout
            // x assumes — so it's offset by horizontalOverflow from viewport's own (now wider) left edge,
            // not stretched to match viewport like the standard-mode content host is.
            var content = NewRect("Board Content", viewport, new Vector2(0f, 1f), new Vector2(0f, 1f));
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = new Vector2(horizontalOverflow, 0f);
            content.sizeDelta = new Vector2(RefW + horizontalOverflow * 2f, RigBoardLayout.PhoneContentHeight);

            var scrollRect = viewport.gameObject.AddComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;

            BuildScrollHint(viewport, viewportBottom - viewportTop, RigBoardLayout.PhoneContentHeight);

            host = viewport;
            return content;
        }

        /// <summary>MV-472 (current spec, defect 4): a small downward chevron pinned at a scrollable
        /// viewport's own bottom edge — content running past the fold with nothing on screen signalling
        /// it scrolls read as broken/clipped to Lee ("I read it as the board being cut off"). Parented to
        /// <paramref name="viewport"/> itself (a SIBLING of its ScrollRect <c>content</c>, not a child of
        /// it), so the ScrollRect's own translation of <c>content</c> never moves it and it always reads
        /// against the viewport's own fixed bottom edge; destroyed for free whenever
        /// <see cref="DestroyBoardContent"/> tears down <c>_boardContentHost</c> (the viewport, in both
        /// modes) since it lives inside that same subtree. Built only when there is genuinely something
        /// below the fold — a board that fits needs no hint, and phone mode's content is always taller
        /// than its viewport so it always gets one.</summary>
        private static void BuildScrollHint(RectTransform viewport, float viewportHeight, float contentHeight)
        {
            if (contentHeight <= viewportHeight + 0.5f) return;

            var hint = AddImage(viewport, HudTextures.Arrow(64), Dim, "Scroll Hint");
            Anchor(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            hint.rectTransform.anchoredPosition = new Vector2(0f, 20f);
            hint.rectTransform.sizeDelta = new Vector2(40f, 24f);
            hint.rectTransform.localScale = new Vector3(1f, -1f, 1f);   // Arrow() points up; flip to point down ("more below")
            hint.raycastTarget = false;
        }

        /// <summary>Tears down whatever <see cref="BuildBoardContent"/> built last pass — the whole
        /// <see cref="_boardContentHost"/> subtree (nodes, panels, connectors, FORGE section, draft
        /// chrome, and in phone mode the scroll viewport itself) — and clears every dictionary that
        /// indexed into it, so a rebuild starts from the same clean state <see cref="Build"/> does.
        /// <c>DestroyImmediate</c> outside Play mode, same idiom as every other rebuild-in-place in this
        /// project (e.g. <c>Sentinel.cs</c>), since an EditMode test calling <see cref="ApplyBoardScale(float)"/>
        /// synchronously must not find stale, about-to-be-destroyed objects still answering <c>Find</c>.</summary>
        private void DestroyBoardContent()
        {
            if (_boardContentHost != null)
            {
                if (Application.isPlaying) Destroy(_boardContentHost.gameObject);
                else DestroyImmediate(_boardContentHost.gameObject);
            }
            _boardContentHost = null;
            _nodeParent = null;

            _categoryNodes.Clear();
            _abilityNodes.Clear();
            _fusionNodes.Clear();
            _categoryPanels.Clear();
            _categoryPanelBorders.Clear();
            _connectors.Clear();
            _draftScrim = null;
            _draftBand = null;
            _draftBandTitle = null;
            _draftBandSubtitle = null;
            _draftBandReason = null;
        }

        /// <summary>The five tinted backdrop columns behind each category's tree (MV-423.png) — one
        /// per category, spanning from the midpoint with its left neighbour to the midpoint with its
        /// right one (the fusion row's diamonds sit exactly on these boundaries: <c>f_del</c> at
        /// x=430 is the PRIMARY/SECONDARY midpoint, etc. — confirmed against the design file rather
        /// than guessed). Drawn before the nodes so they sit behind everything.</summary>
        private void BuildCategoryPanels(RectTransform boardRoot)
        {
            var categories = Categories;
            int n = categories.Count;
            if (n == 0) return;
            float y = RegionRectY, h = RigBoardLayout.RegionRectH, radius = RigBoardLayout.RegionRectRadius;

            for (int i = 0; i < n; i++)
            {
                // MV-472: each family's own column is no longer a uniform 1/5 share of the board — its
                // half-width is sized to its actual content (RigBoardLayout.ColumnHalfWidth). An interior
                // boundary still splits the gap with its neighbour at the midpoint (works unchanged for
                // non-uniform spacing); only the outer edges (first/last) needed their own column's own
                // half-width instead of a shared "spacing" borrowed from the 0-1 gap.
                float left = i == 0 ? categories[i].X - categories[i].ColumnHalfWidth : (categories[i - 1].X + categories[i].X) * 0.5f;
                float right = i == n - 1 ? categories[i].X + categories[i].ColumnHalfWidth : (categories[i].X + categories[i + 1].X) * 0.5f;
                float w = right - left;

                float cornerFraction = Mathf.Clamp(radius / (Mathf.Min(w, h) * 0.5f), 0.05f, 0.5f);
                var panel = AddImage(boardRoot, HudTextures.RoundedBox(64, cornerFraction),
                    new Color(1f, 1f, 1f, RigBoardLayout.RegionOpacityDark), $"{categories[i].Id} Panel");
                Anchor(panel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
                panel.rectTransform.sizeDelta = new Vector2(w, h);
                panel.rectTransform.anchoredPosition = new Vector2(left, -y);
                panel.type = Image.Type.Sliced;
                panel.raycastTarget = false;
                _categoryPanels[categories[i].Id] = panel;

                // MV-443 defect 1: a 1.5px family-coloured hairline so the columns read as an edge
                // without shouting — same rect as the panel fill, stacked on top.
                var border = AddImage(boardRoot, HudTextures.RoundedBoxOutline(64, cornerFraction, 1.5f),
                    Color.clear, $"{categories[i].Id} Panel Border");
                Anchor(border.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
                border.rectTransform.sizeDelta = new Vector2(w, h);
                border.rectTransform.anchoredPosition = new Vector2(left, -y);
                border.type = Image.Type.Sliced;
                border.raycastTarget = false;
                _categoryPanelBorders[categories[i].Id] = border;
            }
        }

        /// <summary>MV-443 defect 2: the tree's connector lines — category-to-parentless-ability,
        /// parent-ability-to-child-ability, and category-to-fusion — built once here as static cubic-
        /// bezier strokes (<see cref="HudTextures.BezierStroke"/>); <see cref="RefreshConnectors"/>
        /// only ever recolours them. Built right after the panels (behind every node, above the panel
        /// fill) so nothing drawn later needs to know connectors exist.</summary>
        private void BuildConnectors(RectTransform boardRoot)
        {
            float catR = RadiusCategory, abR = RadiusAbility, fuR = RadiusFusion;
            float bias = RigBoardLayout.ConnectorControlBias, width = RigBoardLayout.ConnectorWidth;

            var categoryById = new Dictionary<string, RigCategoryLayout>();
            foreach (var cat in Categories) categoryById[cat.Id] = cat;
            var abilityById = new Dictionary<string, RigAbilityLayout>();
            foreach (var ab in Abilities) abilityById[ab.Id] = ab;

            foreach (var ab in Abilities)
            {
                var end = new Vector2(ab.X, ab.Y + RigBoardLayout.ConnectorEndOffset(abR));
                if (string.IsNullOrEmpty(ab.Parent))
                {
                    if (!categoryById.TryGetValue(ab.Category, out var cat)) continue;
                    var start = new Vector2(cat.X, cat.Y + catR + RigBoardLayout.ConnectorStartOffsetCategory);
                    BuildConnector(boardRoot, $"conn:cat:{cat.Id}>{ab.Id}", start, end, bias, width);
                }
                else
                {
                    if (!abilityById.TryGetValue(ab.Parent, out var parent)) continue;
                    var start = new Vector2(parent.X, parent.Y + RigBoardLayout.ConnectorStartOffsetAbility(abR));
                    BuildConnector(boardRoot, $"conn:ab:{parent.Id}>{ab.Id}", start, end, bias, width);
                }
            }

            // MV-445 defect 3: geometry only here — colour is now live (RefreshConnectors), gated by
            // RigFusionState.IsEligible so an unreachable fusion (not both parent categories lit) draws
            // at the dimmer fusionAlphaLocked, not the old always-on fusionAlpha.
            float fusionBias = RigBoardLayout.ConnectorFusionControlBias, fusionWidth = RigBoardLayout.ConnectorFusionWidth;
            foreach (var fusion in Fusions)
            {
                var end = new Vector2(fusion.X, fusion.Y + RigBoardLayout.ConnectorEndOffset(fuR));
                foreach (var parentCategoryId in new[] { fusion.ParentA, fusion.ParentB })
                {
                    if (!categoryById.TryGetValue(parentCategoryId, out var cat)) continue;
                    var start = new Vector2(cat.X, cat.Y + catR + RigBoardLayout.ConnectorStartOffsetCategory);
                    BuildConnector(boardRoot, $"conn:fusion:{fusion.Id}>{parentCategoryId}", start, end, fusionBias, fusionWidth);
                }
            }
        }

        /// <summary>One cubic-bezier connector: control points sit on the vertical between
        /// <paramref name="start"/> and <paramref name="end"/>, at <paramref name="controlBias"/> of the
        /// gap from each end — a smooth mostly-vertical S-curve regardless of how far apart the two
        /// points sit on x.</summary>
        private Image BuildConnector(RectTransform boardRoot, string id, Vector2 start, Vector2 end, float controlBias, float strokeWidth)
        {
            float gap = end.y - start.y;
            var c1 = new Vector2(start.x, start.y + gap * controlBias);
            var c2 = new Vector2(end.x, start.y + gap * (1f - controlBias));

            float pad = strokeWidth * 0.5f + 2f;
            float minX = Mathf.Min(Mathf.Min(start.x, c1.x), Mathf.Min(c2.x, end.x)) - pad;
            float maxX = Mathf.Max(Mathf.Max(start.x, c1.x), Mathf.Max(c2.x, end.x)) + pad;
            float minY = Mathf.Min(Mathf.Min(start.y, c1.y), Mathf.Min(c2.y, end.y)) - pad;
            float maxY = Mathf.Max(Mathf.Max(start.y, c1.y), Mathf.Max(c2.y, end.y)) + pad;
            int w = Mathf.Max(2, Mathf.CeilToInt(maxX - minX));
            int h = Mathf.Max(2, Mathf.CeilToInt(maxY - minY));

            Vector2 Local(Vector2 p) => new Vector2(p.x - minX, p.y - minY);
            var sprite = HudTextures.BezierStroke(Local(start), Local(c1), Local(c2), Local(end), strokeWidth, w, h);

            var img = AddImage(boardRoot, sprite, Color.clear, id);
            Anchor(img.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            img.rectTransform.sizeDelta = new Vector2(w, h);
            img.rectTransform.anchoredPosition = new Vector2(minX, -minY);
            img.raycastTarget = false;
            _connectors[id] = img;
            return img;
        }

        /// <summary>The Morphing Module draft's dimming scrim (MV-424) — one full-board rect, built
        /// last among the board's normal children so it renders above every category/ability/fusion
        /// node, and toggled active only while a draft is pending. Also the reason non-candidate nodes
        /// stop receiving taps during a draft: a raycast target this high in the hierarchy eats them
        /// before they reach anything drawn underneath it. <see cref="RefreshMorphingModuleDraft"/>
        /// brings the candidate nodes themselves back above this via <c>SetAsLastSibling</c>.</summary>
        private void BuildDraftScrim(RectTransform boardRoot)
        {
            var scrim = AddImage(boardRoot, HudTextures.Solid(), new Color(0f, 0f, 0f, 0.82f), "Draft Scrim");
            Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;
            scrim.gameObject.SetActive(false);
            _draftScrim = scrim;
        }

        /// <summary>The bottom band (MV-424): full width, anchored to the board's own bottom edge so
        /// its position is pinned in the fixed 1920x1080 board frame regardless of Safe Area.
        /// Deliberately at the BOTTOM, not the top: a top banner would cover the category row, and the
        /// whole value of drafting on the board is seeing the current build while choosing (ticket,
        /// non-negotiable).</summary>
        private void BuildDraftBand(RectTransform boardRoot)
        {
            const float bandHeight = 170f;
            var band = NewRect("draft_band", boardRoot, new Vector2(0f, 0f), new Vector2(1f, 0f));
            band.pivot = new Vector2(0.5f, 0f);
            band.sizeDelta = new Vector2(0f, bandHeight);
            band.anchoredPosition = Vector2.zero;
            _draftBand = band;

            var bg = AddImage(band, HudTextures.Solid(), new Color(0.02f, 0.05f, 0.07f, 0.96f), "Band BG");
            Stretch(bg.rectTransform);
            bg.raycastTarget = true;   // swallow taps under the band too, same as the scrim

            var hairline = AddImage(band, HudTextures.Solid(), DraftBadgeColor, "Band Hairline");
            Anchor(hairline.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            hairline.rectTransform.sizeDelta = new Vector2(0f, 2f);
            hairline.rectTransform.anchoredPosition = Vector2.zero;
            hairline.raycastTarget = false;

            _draftBandTitle = AddText(band, 34, DraftBadgeColor, TextAnchor.UpperCenter);
            Anchor(_draftBandTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            _draftBandTitle.rectTransform.sizeDelta = new Vector2(-2f * ContentMargin, 44f);
            _draftBandTitle.rectTransform.anchoredPosition = new Vector2(0f, -22f);
            _draftBandTitle.fontStyle = FontStyle.Bold;

            _draftBandSubtitle = AddText(band, 18, Dim, TextAnchor.UpperCenter);
            Anchor(_draftBandSubtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            _draftBandSubtitle.rectTransform.sizeDelta = new Vector2(-2f * ContentMargin, 30f);
            _draftBandSubtitle.rectTransform.anchoredPosition = new Vector2(0f, -66f);

            _draftBandReason = AddText(band, 16, TextColor, TextAnchor.UpperCenter);
            Anchor(_draftBandReason.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            _draftBandReason.rectTransform.sizeDelta = new Vector2(-2f * ContentMargin, 30f);
            _draftBandReason.rectTransform.anchoredPosition = new Vector2(0f, -100f);

            band.gameObject.SetActive(false);
        }

        /// <summary>FORGE row — divider, caption, and the four fusion diamonds. MV-423 (2/5) placed and
        /// labelled these (RigBoardLayoutTests covers position/size); MV-426 (5/5) gives them their
        /// real state machine via <see cref="RefreshFusionNode"/>, called once by <see cref="Refresh"/>
        /// right after <see cref="Build"/> — this method only builds the static shell each starts
        /// from.</summary>
        private void BuildForgeSection(RectTransform boardRoot)
        {
            float dividerY = ForgeDividerY;
            var divider = AddImage(boardRoot, HudTextures.Solid(), new Color(1f, 1f, 1f, 0.12f), "Forge Divider");
            Anchor(divider.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            divider.rectTransform.offsetMin = new Vector2(ContentMargin, 0f);
            divider.rectTransform.offsetMax = new Vector2(-ContentMargin, 0f);
            divider.rectTransform.anchoredPosition = new Vector2(0f, -dividerY);
            divider.rectTransform.sizeDelta = new Vector2(0f, 1.5f);

            var forgeLabel = AddText(boardRoot, 22, SupercellColor, TextAnchor.UpperLeft);
            Anchor(forgeLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            forgeLabel.rectTransform.anchoredPosition = new Vector2(ContentMargin, -(dividerY + 24f));
            forgeLabel.rectTransform.sizeDelta = new Vector2(200f, 28f);
            forgeLabel.fontStyle = FontStyle.Bold;
            forgeLabel.text = "FORGE";

            // MV-443 defect 8: two short lines under the FORGE label (not one long line beside it,
            // which used to run under the first diamond) — left of x=380, per MV-442.png. MV-446
            // defect 3: fontSize now off rig_board.json (was a hardcoded 16, under the 16px readability
            // floor once actually measured against the design's own 1920x1080 reference frame) — box
            // height grown to match so two lines still clear it.
            var caption = AddText(boardRoot, Mathf.RoundToInt(ForgeCaptionFontSize), Dim, TextAnchor.UpperLeft);
            Anchor(caption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            caption.rectTransform.anchoredPosition = new Vector2(ContentMargin, -(dividerY + 58f));
            caption.rectTransform.sizeDelta = new Vector2(380f - ContentMargin, 52f);
            caption.lineSpacing = 1.2f;
            caption.text = "two lit categories · costs cells\nnever a shed · lands in B / U";

            foreach (var fusion in Fusions) _fusionNodes[fusion.Id] = BuildFusionNode(boardRoot, fusion);
        }

        private RigNodeVisual BuildFusionNode(RectTransform boardRoot, RigFusionLayout fusion)
        {
            float r = RadiusFusion;
            var node = BuildNodeShell(boardRoot, fusion.Id, fusion.X, fusion.Y, r, FusionSides, out var shell);

            Color amber = SupercellColor;
            shell.HexFill.color = new Color(amber.r, amber.g, amber.b, 0.03f);
            shell.HexOutline.sprite = SolidPolygonOutlineSprite(FusionSides, r);
            shell.HexOutline.color = new Color(1f, 1f, 1f, 0.14f);

            int fuseIconSize = Mathf.RoundToInt(r * RigBoardLayout.IconScaleFusion);
            shell.Icon.sprite = HudTextures.VectorIcon(RigBoardLayout.Icon("fuse"), fuseIconSize);
            shell.Icon.rectTransform.sizeDelta = new Vector2(fuseIconSize, fuseIconSize);
            shell.Icon.color = Dim;

            shell.Label.text = "? ? ?";
            shell.Label.color = Dim;

            // MV-446 defect 3: fontSize off rig_board.json (was a hardcoded 13, dropping to 12 in the
            // locked state — both under the 16px readability floor); box grown to match.
            var sub = AddText(node, Mathf.RoundToInt(FusionSubFontSize), Dim, TextAnchor.UpperCenter);
            Anchor(sub.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            sub.rectTransform.sizeDelta = new Vector2(280f, 24f);
            sub.rectTransform.anchoredPosition = new Vector2(0f, -(RigBoardLayout.LabelOffsetY(r) + 22f));
            sub.text = $"{fusion.ParentA} + {fusion.ParentB}";
            shell.Sub = sub;

            shell.PillBg.gameObject.SetActive(false);   // fusions carry no level pill
            shell.PillBorder.gameObject.SetActive(false);
            shell.OuterRing.gameObject.SetActive(false);
            shell.CapMarker.gameObject.SetActive(false);
            shell.Button.interactable = false;   // RefreshFusionNode (MV-426) turns this on once eligible

            string id = fusion.Id;   // capture by value, not the loop variable
            shell.Button.onClick.AddListener(() => OnRigNodeTapped(id));
            return shell;
        }

        private RigNodeVisual BuildCategoryNode(RectTransform boardRoot, RigCategoryLayout cat)
        {
            float r = RadiusCategory;
            BuildNodeShell(boardRoot, cat.Id, cat.X, cat.Y, r, HexSides, out var shell);

            shell.HexOutline.sprite = SolidHexOutlineSprite(r);
            int catIconSize = Mathf.RoundToInt(r * RigBoardLayout.IconScaleCategory);
            shell.Icon.sprite = HudTextures.VectorIcon(RigBoardLayout.Icon(cat.Icon), catIconSize);
            shell.Icon.rectTransform.sizeDelta = new Vector2(catIconSize, catIconSize);

            shell.Label.rectTransform.anchoredPosition = new Vector2(0f, -RigBoardLayout.CategoryLabelOffsetY(r));
            shell.Label.text = cat.Id;

            // MV-472 (current spec, defects 1+2): BuildNodeShell's label box/wrap/best-fit above is
            // sized and configured for an ABILITY label sharing a tight column with siblings — a
            // category label is one word alone above its hex, with the real column to itself
            // (cat.ColumnHalfWidth, MV-472's own content-proportional layout data, never consulted here
            // before). Two bugs shared one root cause:
            //   1. MV-489: MV-472 set resizeTextMinSize/resizeTextMaxSize off CategoryLabelFontSize (36
            //      in phone mode) but left the base Text.fontSize field at whatever BuildNodeShell had
            //      already authored it to — the ABILITY size (32) — on the assumption that best-fit
            //      ignores the bare fontSize field entirely. Verified false by direct measurement
            //      (TextGenerator.Populate/fontSizeUsedForBestFit, the same call OnPopulateMesh makes):
            //      Unity's best-fit search is bounded ABOVE by fontSize as well as resizeTextMaxSize, so
            //      a stale fontSize below the intended cap silently re-imposes it — the label rendered
            //      at 32 even with resizeTextMaxSize correctly set to 36. fontSize must be kept in sync
            //      with resizeTextMaxSize, not left at whatever the shared shell happened to author.
            //   2. The inherited box (phone: 190px/Wrap) is an ability-column width, not this category's
            //      own — narrower than "SECONDARY" needs, so it broke mid-word ("SECONDAR"/"Y").
            // Deriving the box from the real column width and using best-fit (Overflow, never Wrap) to
            // shrink rather than break fixes both: the box is now wide enough that best-fit lands at
            // CategoryLabelFontSize (its intended max) in the normal case, and a still-too-long word
            // shrinks gracefully instead of breaking or spilling into a neighbouring column.
            float categoryLabelBoxW = Mathf.Max(2f * cat.ColumnHalfWidth - 16f, 120f);
            shell.Label.rectTransform.sizeDelta = new Vector2(categoryLabelBoxW, _phoneMode ? 60f : 28f);
            shell.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
            shell.Label.resizeTextForBestFit = true;
            shell.Label.resizeTextMinSize = Mathf.Max(1, Mathf.RoundToInt(CategoryLabelFontSize * 0.75f));
            shell.Label.resizeTextMaxSize = Mathf.RoundToInt(CategoryLabelFontSize);
            shell.Label.fontSize = shell.Label.resizeTextMaxSize;   // MV-489: keep in lockstep with the cap above — see note 1

            // MV-443 defect 5: a lit category additionally gets a solid outer ring at
            // capOuterRingOffset — reusing the shared Outer Ring image, but with a SOLID (not dashed)
            // sprite of its own; RefreshCategoryNode only ever toggles/tints it.
            // MV-481: was HudTextures.Ring — a plain circle, sized to a square canvas regardless of the
            // hex's own r*sqrt(3):2r proportions. At RadiusCategory=72 that circle (radius 81) reaches
            // further out than the hex-following Glow does in the edge-midpoint direction (apothem+blur
            // = 76px) while sitting inside it in the vertex direction (r+blur=86px) — an real, visible
            // "ring doesn't match the hex" defect, and the actual cause of MV-481's hex-orientation FAIL
            // (confirmed live: the harness's own ray-march read this circle, not the glow, as the node's
            // outer edge in the left/right direction). Matches HexOutline's own hex-silhouette sizing.
            float ringR = r + RigBoardLayout.CapOuterRingOffset;
            float ringW = ringR * Sqrt3, ringH = ringR * 2f;
            shell.OuterRing.rectTransform.sizeDelta = new Vector2(ringW, ringH);
            shell.OuterRing.sprite = HudTextures.PolygonOutline(HexSides, HexRotationDeg, Mathf.CeilToInt(ringW), Mathf.CeilToInt(ringH), 2f);
            shell.OuterRing.gameObject.SetActive(false);

            shell.CapMarker.gameObject.SetActive(false);
            shell.Button.interactable = false;   // MV-457: only tappable while it's a shed draft candidate — see RefreshCategoryNode

            string catId = cat.Id;   // capture by value, not the loop variable
            shell.Button.onClick.AddListener(() => OnRigNodeTapped(catId));
            return shell;
        }

        private RigNodeVisual BuildAbilityNode(RectTransform boardRoot, RigAbilityLayout ab)
        {
            float r = RadiusAbility;
            BuildNodeShell(boardRoot, ab.Id, ab.X, ab.Y, r, HexSides, out var shell);

            shell.HexOutline.sprite = SolidHexOutlineSprite(r);
            int abIconSize = Mathf.RoundToInt(r * RigBoardLayout.IconScaleAbility);
            shell.Icon.sprite = HudTextures.VectorIcon(RigBoardLayout.Icon(ab.Icon), abIconSize);
            shell.Icon.rectTransform.sizeDelta = new Vector2(abIconSize, abIconSize);

            // Outer dashed ring (capability draftable) — a hex at r + capOuterRingOffset, independent
            // of the node's own hex outline so it can toggle without disturbing it. MV-481: was a plain
            // circle (HudTextures.Ring) — see the category ring's own comment (BuildCategoryNode) for
            // why that doesn't match the hex-following glow underneath it.
            float ringR = r + RigBoardLayout.CapOuterRingOffset;
            float ringW = ringR * Sqrt3, ringH = ringR * 2f;
            shell.OuterRing.rectTransform.sizeDelta = new Vector2(ringW, ringH);
            shell.OuterRing.sprite = HudTextures.PolygonOutline(HexSides, HexRotationDeg, Mathf.CeilToInt(ringW), Mathf.CeilToInt(ringH), RigBoardLayout.StrokeActive, true);

            float markerR = RigBoardLayout.CapMarkerRadius;
            Vector2 markerOffset = RigBoardLayout.CapMarkerOffset(r);
            shell.CapMarker.rectTransform.sizeDelta = new Vector2(markerR * 2f, markerR * 2f);
            shell.CapMarker.rectTransform.anchoredPosition = markerOffset;
            shell.CapMarker.sprite = HudTextures.Disc(32);

            // MV-470: the accumulation ring — a plain (non-dashed) ring just inside the dashed
            // OuterRing/CapOuterRingOffset radius, revealed by Image.Type.Filled/Radial360 as
            // cellsBanked climbs toward whichever cost currently applies (RefreshAbilityNode sets
            // fillAmount off CellSpend.CellCostProgress01). Inactive until a cell cost applies.
            float progressRingR = r + 4f;
            shell.ProgressRing = AddImage(shell.Root, HudTextures.Ring(96, RigBoardLayout.StrokeActive, false), Color.clear, "Progress Ring");
            Anchor(shell.ProgressRing.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            shell.ProgressRing.rectTransform.sizeDelta = new Vector2(progressRingR * 2f, progressRingR * 2f);
            shell.ProgressRing.raycastTarget = false;
            shell.ProgressRing.type = Image.Type.Filled;
            shell.ProgressRing.fillMethod = Image.FillMethod.Radial360;
            shell.ProgressRing.fillOrigin = (int)Image.Origin360.Top;
            shell.ProgressRing.fillClockwise = true;
            shell.ProgressRing.fillAmount = 0f;
            shell.ProgressRing.gameObject.SetActive(false);

            // MV-470: the cost tag — a small cell-glyph icon + number sitting in the real vertical gap
            // between the level pill (bottom edge at LevelPillOffsetY - h/2) and the label (top edge at
            // LabelOffsetY - 12), so it never fights the pill's own "{level}/{max}" text or the label's
            // ability name. Same PowerCell glyph the CELLS header chip and the world HUD's own counter
            // use, so a node's cost reads as CELLS on sight, not a naked integer.
            float pillBottom = -RigBoardLayout.LevelPillOffsetY(r) - LevelPillH * 0.5f;
            float labelTop = -RigBoardLayout.LabelOffsetY(r) + 12f;
            float costTagY = (pillBottom + labelTop) * 0.5f;
            Color moduleColour = RigBoardLayout.Colour("module");

            shell.CostIcon = AddImage(shell.Root, WeaponHudIcons.PowerCell(24), Color.clear, "Cost Icon");
            Anchor(shell.CostIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            shell.CostIcon.rectTransform.sizeDelta = new Vector2(14f, 14f);
            shell.CostIcon.rectTransform.anchoredPosition = new Vector2(-9f, costTagY);
            shell.CostIcon.raycastTarget = false;
            shell.CostIcon.gameObject.SetActive(false);

            shell.CostText = AddText(shell.Root, 14, moduleColour, TextAnchor.MiddleLeft);
            Anchor(shell.CostText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            shell.CostText.rectTransform.sizeDelta = new Vector2(30f, 18f);
            shell.CostText.rectTransform.anchoredPosition = new Vector2(9f, costTagY);
            shell.CostText.fontStyle = FontStyle.Bold;
            shell.CostText.raycastTarget = false;
            shell.CostText.gameObject.SetActive(false);

            shell.Label.text = ab.Label;

            string id = ab.Id;   // capture by value, not the loop variable
            shell.Button.onClick.AddListener(() => OnRigNodeTapped(id));
            return shell;
        }

        /// <summary>The shared shell every node (category/ability/fusion) is built from: a
        /// <paramref name="sides"/>-gon of circumradius <paramref name="r"/> centred at
        /// (<paramref name="x"/>, <paramref name="y"/>) in the board's own frame, plus the pieces every
        /// state needs — fill, outline, glow, outer ring, cap marker, level pill, label,
        /// icon and a full-hit-rect button. The ROOT rect is a <c>2r x 2r</c> square (not the hex's own
        /// narrower bounding box) so every node gets a full hit rect regardless of shape — the AC's own
        /// wording ("do not shrink any radius").</summary>
        private RectTransform BuildNodeShell(RectTransform boardRoot, string id, float x, float y, float r,
            int sides, out RigNodeVisual shell)
        {
            var root = NewRect(id, boardRoot, new Vector2(0f, 1f), new Vector2(0f, 1f));
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(r * 2f, r * 2f);
            root.anchoredPosition = new Vector2(x, -y);

            float hexW = sides == HexSides ? r * Sqrt3 : r * 2f;
            float hexH = r * 2f;

            // MV-433/MV-446: a soft halo behind the node plate (drawn first among this shell's children,
            // so it renders behind Fill/Outline/Icon), not the old flat-alpha hex-shaped fill — a
            // hex-silhouette-following HudTextures.PolygonGlow, sprite/size/tint set per state in
            // Refresh*Node below (owned/draftable each pick their own blur+alpha off RigBoardLayout).
            // Built once here at the owned blur just so the Image has a valid sprite before its first
            // Refresh; every activation below reassigns it for the actual state.
            var glow = AddImage(root, NodeGlowSprite(r, sides, RigBoardLayout.GlowBlurOwned), Color.clear, "Glow");
            Anchor(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            glow.rectTransform.sizeDelta = NodeGlowSize(r, sides);
            glow.raycastTarget = false;
            glow.gameObject.SetActive(false);

            var fill = AddImage(root, PolygonFillSprite(sides, Mathf.CeilToInt(hexW), Mathf.CeilToInt(hexH)), Color.clear, "Fill");
            Anchor(fill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            fill.rectTransform.sizeDelta = new Vector2(hexW, hexH);
            fill.raycastTarget = false;

            var outline = AddImage(root, null, Color.white, "Outline");
            Anchor(outline.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            outline.rectTransform.sizeDelta = new Vector2(hexW, hexH);
            outline.raycastTarget = false;

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(root, false);
            Anchor(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            icon.rectTransform.anchoredPosition = new Vector2(0f, RigBoardLayout.IconOffsetY);
            icon.raycastTarget = false;

            var outerRing = AddImage(root, HudTextures.Ring(96, RigBoardLayout.StrokeActive, true, 16), Color.clear, "Outer Ring");
            Anchor(outerRing.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            outerRing.raycastTarget = false;

            var capMarker = AddImage(root, HudTextures.Disc(32), Color.clear, "Cap Marker");
            Anchor(capMarker.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            capMarker.raycastTarget = false;

            float pillW = LevelPillW, pillH = LevelPillH;
            var pillBg = AddImage(root, HudTextures.RoundedBox(32, 0.5f), PillBackdrop, "Pill");
            Anchor(pillBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            pillBg.rectTransform.sizeDelta = new Vector2(pillW, pillH);
            pillBg.rectTransform.anchoredPosition = new Vector2(0f, -RigBoardLayout.LevelPillOffsetY(r));
            pillBg.type = Image.Type.Sliced;
            pillBg.raycastTarget = false;

            // MV-443 defect 3: the level pill's own 2px border — a separate stacked image so its
            // colour (the state read: family/module/ink) never has to fight the backdrop's fixed
            // #0A0B0F fill.
            var pillBorder = AddImage(root, HudTextures.RoundedBoxOutline(32, 0.5f, 2f), Color.clear, "Pill Border");
            Anchor(pillBorder.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            pillBorder.rectTransform.sizeDelta = new Vector2(pillW, pillH);
            pillBorder.rectTransform.anchoredPosition = pillBg.rectTransform.anchoredPosition;
            pillBorder.type = Image.Type.Sliced;
            pillBorder.raycastTarget = false;

            var pillText = AddText(pillBg.rectTransform, Mathf.RoundToInt(LevelPillFontSize), TextColor, TextAnchor.MiddleCenter);
            Stretch(pillText.rectTransform);
            pillText.fontStyle = FontStyle.Bold;
            pillText.raycastTarget = false;

            var label = AddText(root, Mathf.RoundToInt(LabelFontSize), TextColor, TextAnchor.UpperCenter);
            Anchor(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            label.rectTransform.anchoredPosition = new Vector2(0f, -RigBoardLayout.LabelOffsetY(r));
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
            if (_phoneMode)
            {
                // MV-472: phone mode's own bigger 32pt labels ("FORCE FIELD", "CELL STORAGE", ...) can
                // run wider than the tighter sibling spacing phone-mode columns actually have room for —
                // best-fit shrinks a long label down (never below the 11pt-clearing floor
                // PhoneLabelFontSizeMin already sits above) instead of letting it bleed into a neighbour.
                // Caught live: "FORCE FIELDSTORAGE" — ENERGY's own two tier-1 labels overlapping each
                // other, not bleeding into a different family, so tighter spacing alone couldn't fix it
                // without either cramming hexes together or capping label width somehow.
                label.rectTransform.sizeDelta = new Vector2(RigBoardLayout.PhoneLabelBoxWidth, 52f);
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = Mathf.RoundToInt(RigBoardLayout.PhoneLabelFontSizeMin);
                label.resizeTextMaxSize = Mathf.RoundToInt(RigBoardLayout.LabelFontSizePhone);
            }
            else
            {
                label.rectTransform.sizeDelta = new Vector2(r * 3f, 24f);
            }

            // MV-424: the Morphing Module draft's numbered badge (1-3), centred above the hex — a
            // different corner language from the SHED/spend markers so a candidate reads unmistakably
            // as "tap to take", not another passive state.
            var draftBadge = AddImage(root, HudTextures.Disc(40), Color.clear, "Draft Badge");
            Anchor(draftBadge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            draftBadge.rectTransform.sizeDelta = new Vector2(36f, 36f);
            draftBadge.rectTransform.anchoredPosition = new Vector2(0f, r + 34f);
            draftBadge.raycastTarget = false;
            draftBadge.gameObject.SetActive(false);

            var draftBadgeText = AddText(draftBadge.rectTransform, 18, PanelColor, TextAnchor.MiddleCenter);
            Stretch(draftBadgeText.rectTransform);
            draftBadgeText.fontStyle = FontStyle.Bold;
            draftBadgeText.raycastTarget = false;

            var hit = AddImage(root, HudTextures.Solid(), Color.clear, "Hit");
            Stretch(hit.rectTransform);
            hit.raycastTarget = true;
            var button = hit.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;

            shell = new RigNodeVisual
            {
                Root = root, Glow = glow, HexFill = fill, HexOutline = outline, Icon = icon,
                OuterRing = outerRing, CapMarker = capMarker,
                PillBg = pillBg, PillBorder = pillBorder, PillText = pillText, Label = label, Button = button, Radius = r,
                DraftBadge = draftBadge, DraftBadgeText = draftBadgeText
            };
            return root;
        }

        // MV-433: FORGE's fusion nodes (sides == FusionSides) render as diamonds — Polygon(4, 45) per
        // geometry.radius.fusion — not squares; every other caller (hex nodes, the supercell tray's hex
        // sockets) keeps the pointy-top hex rotation. A single shared rotation constant for both shapes
        // was the bug (fusion squares in MV-423's build).
        private static float RotationFor(int sides) => sides == FusionSides ? FusionRotationDeg : HexRotationDeg;

        private static Sprite PolygonFillSprite(int sides, int w, int h) => HudTextures.Polygon(sides, RotationFor(sides), w, h);

        /// <summary>A <paramref name="sides"/>-gon's own (width, height) bounding box at circumradius
        /// <paramref name="r"/> — the same <c>hexW</c>/<c>hexH</c> pair <see cref="BuildNodeShell"/>'s
        /// Fill/Outline already size themselves to, shared here so the glow halo (MV-446) follows the
        /// SAME aspect instead of the square bounding box <see cref="HudTextures.Glow"/> used.</summary>
        private static Vector2 HexBounds(float r, int sides) => new Vector2(sides == HexSides ? r * Sqrt3 : r * 2f, r * 2f);

        /// <summary>The owned/draftable node halo's rect size (MV-446 defect 2 AC: at most 1.25x the
        /// node's own radius) — the hex's own bounds scaled up by <see cref="GlowRadiusMultiplier"/> for
        /// the blur's headroom, not a flat square.</summary>
        private static Vector2 NodeGlowSize(float r, int sides) => HexBounds(r, sides) * GlowRadiusMultiplier;

        /// <summary>The hex-silhouette-following glow sprite for a node of radius <paramref name="r"/>
        /// (MV-446 defect 2) — <paramref name="blurPx"/> comes from <see cref="RigBoardLayout.GlowBlurOwned"/>/
        /// <see cref="RigBoardLayout.GlowBlurDraft"/> so callers pick the state's own falloff width.</summary>
        private static Sprite NodeGlowSprite(float r, int sides, float blurPx)
        {
            Vector2 size = NodeGlowSize(r, sides);
            return HudTextures.PolygonGlow(sides, RotationFor(sides), Mathf.CeilToInt(size.x), Mathf.CeilToInt(size.y), r, blurPx);
        }

        private Sprite SolidHexOutlineSprite(float r) => SolidPolygonOutlineSprite(HexSides, r);

        private Sprite SolidPolygonOutlineSprite(int sides, float r)
        {
            float w = sides == HexSides ? r * Sqrt3 : r * 2f, h = r * 2f;
            return HudTextures.PolygonOutline(sides, RotationFor(sides), Mathf.CeilToInt(w), Mathf.CeilToInt(h), RigBoardLayout.StrokeOwned);
        }

        // MV-445 defect 4: dash 13 / gap 9, walked as arc length around the closed hexagon (see
        // HudTextures.PolygonOutline's own doc comment for why the old angle-based dash broke).
        private const float DraftableDashLength = 13f;
        private const float DraftableGapLength = 9f;

        private Sprite DashedHexSprite(float r)
        {
            float w = r * Sqrt3, h = r * 2f;
            return HudTextures.PolygonOutline(HexSides, HexRotationDeg, Mathf.CeilToInt(w), Mathf.CeilToInt(h),
                RigBoardLayout.StrokeActive, true, DraftableDashLength, DraftableGapLength);
        }

        /// <summary>MV-443 defect 5: a locked ability's hex outline draws at <c>strokeLocked</c> (2px),
        /// not the owned state's <c>strokeOwned</c> (4px) <see cref="SolidHexOutlineSprite"/> uses.</summary>
        private Sprite LockedHexOutlineSprite(float r)
        {
            float w = r * Sqrt3, h = r * 2f;
            return HudTextures.PolygonOutline(HexSides, HexRotationDeg, Mathf.CeilToInt(w), Mathf.CeilToInt(h), RigBoardLayout.StrokeLocked);
        }

        /// <summary>The refs a built node hands back to <see cref="Refresh"/> — one shared shape for
        /// categories, abilities and fusions so all three can only ever drift apart in DATA (their
        /// json entry), never in code structure.</summary>
        private sealed class RigNodeVisual
        {
            public RectTransform Root;
            public Image Glow, HexFill, HexOutline, Icon, OuterRing, CapMarker, PillBg, PillBorder, DraftBadge;
            public Text PillText, Label, DraftBadgeText;
            public Button Button;
            public float Radius;

            /// <summary>MV-516: the hex FILL/OUTLINE alpha <see cref="WeaponsScreen.RefreshAbilityNode"/>
            /// (or <see cref="WeaponsScreen.RefreshCandidateNode"/>) most recently set for this node's
            /// current state — <see cref="WeaponsScreen.Update"/> reads these as the pulse's OWN peak
            /// rather than re-deriving the state's alpha itself, so the animated tell can never drift
            /// out of sync with whatever RefreshAbilityNode's own branch decided this state looks like.</summary>
            public float FillBaseAlpha, OutlineBaseAlpha;

            /// <summary>Fusion nodes only (MV-426): the sub-label beneath the name — parent category
            /// names while unforgeable, "<c>N CELLS · SLOT B</c>" once eligible (MV-515: was parts),
            /// "<c>FORGED · SLOT B</c>" once forged. Null for category/ability nodes.</summary>
            public Text Sub;

            /// <summary>MV-470: ability nodes only (null for category/fusion) — the accumulation ring
            /// (<see cref="Image.Type.Filled"/>/<see cref="Image.FillMethod.Radial360"/>, fillAmount =
            /// cells-banked / cost) and the small cost-tag icon+number pair sitting between the level
            /// pill and the label, where THE RIG's row spacing leaves real clearance. All three null
            /// unless a cell cost currently applies to the node (draftable or owned-and-below-max).</summary>
            public Image ProgressRing, CostIcon;
            public Text CostText;
        }

        // ------------------------------------------------------------------ top bar

        private void BuildTopBar(RectTransform parent)
        {
            var bar = NewRect("Top Bar", parent, new Vector2(0f, 1f), new Vector2(1f, 1f));
            _topBar = bar;
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(-2f * ContentMargin, TopBarHeight);
            bar.anchoredPosition = new Vector2(0f, -ContentMargin);

            // MV-433: inset (not flush to the bar's own edges) so it reads as a soft-cornered panel
            // INSIDE the top bar, per MV-423.png — flush-to-edge plus a shallow corner radius is what
            // made it read as a hard rectangle overlapping the debug FPS readout (Bootstrap.cs OnGUI,
            // screen pixels 12,8-652,68 — squarely under the old flush plate).
            var accent = AddImage(bar, HudTextures.RoundedBox(32, 0.5f), HeaderAccent, "Title Accent");
            Anchor(accent.rectTransform, new Vector2(0f, 0f), new Vector2(0.34f, 1f), new Vector2(0f, 0.5f));
            accent.rectTransform.offsetMin = new Vector2(0f, 12f);
            accent.rectTransform.offsetMax = new Vector2(-6f, -12f);
            accent.type = Image.Type.Sliced;

            var title = AddText(bar, 38, TextColor, TextAnchor.MiddleLeft);
            Anchor(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.34f, 0.5f), new Vector2(0f, 0.5f));
            title.rectTransform.offsetMin = new Vector2(28f, -30f);
            title.rectTransform.offsetMax = new Vector2(-160f, 30f);
            title.fontStyle = FontStyle.Bold;
            title.text = "THE RIG";

            var subtitle = AddText(bar, 18, Dim, TextAnchor.MiddleLeft);
            Anchor(subtitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.34f, 0.5f), new Vector2(0f, 0.5f));
            // MV-443 defect 7: was 190 — "THE RIG" at 38pt Bold runs past that, colliding with this
            // subtitle at the design's own scale; pushed right enough to clear it with a real gap.
            subtitle.rectTransform.offsetMin = new Vector2(230f, -30f);
            subtitle.rectTransform.offsetMax = new Vector2(-12f, 30f);
            subtitle.fontStyle = FontStyle.Bold;
            subtitle.text = "MAX'S WORKBENCH";

            float cursor = -16f;
            cursor = BuildCloseButton(bar, cursor) - 16f;
            cursor = BuildQuitButton(bar, cursor) - 16f;

            cursor = BuildSupercellTray(bar, cursor) - 16f;

            // MV-433: widened from 170 — "28 / 30 CELLS" at the chip's own min best-fit size (14pt)
            // was clipping its leading digit against the icon at the old width.
            const float cellsChipWidth = 190f;
            var cellsChip = BuildChip(bar, new Vector2(cursor, 0f), cellsChipWidth, CellsColor, out _cellsText);
            cellsChip.name = "Cells Chip";

            _cellsChipBg = cellsChip.Find("BG").GetComponent<Image>();
            _cellsChipButton = _cellsChipBg.gameObject.AddComponent<Button>();
            _cellsChipButton.transition = Selectable.Transition.None;
            _cellsChipButton.onClick.AddListener(OnCellsChipTapped);

            // MV-443 defect 7, MV-445 defect 6: a real rounded pill (radius 25, matching BuildChip's own
            // background corner) with a 2.5px colours.sec border — was a flat, borderless chip.
            float cellsCornerFraction = Mathf.Clamp(25f / (Mathf.Min(cellsChipWidth, 52f) * 0.5f), 0.05f, 0.5f);
            var cellsBorder = AddImage(cellsChip, HudTextures.RoundedBoxOutline(32, cellsCornerFraction, 2.5f), CellsColor, "Cells Border");
            Stretch(cellsBorder.rectTransform);
            cellsBorder.type = Image.Type.Sliced;
            cellsBorder.raycastTarget = false;
            _cellsBorder = cellsBorder;
        }

        /// <summary>MV-423's replacement for the old spinning-gear PARTS chip: six hex sockets (filled
        /// amber up to the banked count, a <c>+N</c> overflow past six). MV-515: the tray is now a
        /// tappable control, not a passive readout — tapping it cashes one Supercell
        /// (<see cref="OnSupercellTrayTapped"/>); see <see cref="RefreshSupercellTray"/> for its
        /// captions and unavailable-with-a-reason states.
        ///
        /// MV-445 defect 6: width is now computed from actual content (label column + socket row +
        /// paddings) instead of a hardcoded 340 — at that fixed width the socket row's old 50%-of-tray
        /// anchor gave the six pips only 154px to share (170 half-width minus a 16px pad) against the
        /// 224px <see cref="SupercellsSocketSize"/>(34) actually needed, so they overran the row's own
        /// bounds. Both the label column and the socket row are now fixed-size blocks pinned to their
        /// own edge, so the tray's width is simply their sum — it can never again under-provision one
        /// side chasing a magic total.</summary>
        private float BuildSupercellTray(RectTransform bar, float rightEdge)
        {
            const int socketCount = 6;
            const float socketGap = 4f;
            const float leftColumnWidth = 100f;
            const float midGap = 14f;
            const float rightPad = 16f;
            float socketsWidth = socketCount * SupercellsSocketSize + (socketCount - 1) * socketGap;
            float width = leftColumnWidth + midGap + socketsWidth + rightPad;

            var tray = NewRect("Supercell Tray", bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            tray.pivot = new Vector2(1f, 0.5f);
            // MV-446 defect 3: grown from 68 -> 76 to give the sub-label's raised font floor (below)
            // headroom for a two-line wrap without crowding the "SUPERCELLS" label above it.
            tray.sizeDelta = new Vector2(width, 76f);
            tray.anchoredPosition = new Vector2(rightEdge, 0f);

            var bg = AddImage(tray, HudTextures.RoundedBox(32, 0.3f), RowColor, "Tray BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;
            _supercellsTrayBg = bg;
            // MV-515: the tray itself is the cash-in control — the BG's own raycastable Image is what
            // the Button attaches to, same idiom as the CELLS chip (_cellsChipBg.gameObject.AddComponent<Button>()).
            _supercellsTrayButton = bg.gameObject.AddComponent<Button>();
            _supercellsTrayButton.transition = Selectable.Transition.None;
            _supercellsTrayButton.onClick.AddListener(OnSupercellTrayTapped);

            var inner = AddImage(tray, HudTextures.RoundedBox(32, 0.3f), PanelColor, "Tray Inner");
            Stretch(inner.rectTransform, -2.5f); inner.type = Image.Type.Sliced;

            // MV-443 defect 7: "a bordered box in colours.supercell" — the tray had a flat fill but no
            // amber border of its own.
            var border = AddImage(tray, HudTextures.RoundedBoxOutline(32, 0.3f, 2f), SupercellColor, "Tray Border");
            Stretch(border.rectTransform);
            border.type = Image.Type.Sliced;
            border.raycastTarget = false;
            _supercellsTrayBorder = border;

            _supercellsTrayLabel = AddText(tray, 18, SupercellColor, TextAnchor.UpperLeft);
            Anchor(_supercellsTrayLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            _supercellsTrayLabel.rectTransform.anchoredPosition = new Vector2(14f, -6f);
            _supercellsTrayLabel.rectTransform.sizeDelta = new Vector2(leftColumnWidth - 14f, 24f);
            _supercellsTrayLabel.fontStyle = FontStyle.Bold;
            _supercellsTrayLabel.text = "SUPERCELLS";

            // MV-443 defect 7: was clipping "...one banked" mid-word — best-fit shrink instead of a
            // fixed 13pt, same idiom the CELLS chip's own label already uses for a tight box. MV-446
            // defect 3: min/max raised off rig_board.json — the old 10-13pt range shrank well under the
            // 16px readability floor for anything longer than "0 banked"; box height grown to match.
            _supercellsTraySub = AddText(tray, Mathf.RoundToInt(RigBoardLayout.SupercellTraySubFontSizeMax), Dim, TextAnchor.LowerLeft);
            Anchor(_supercellsTraySub.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f));
            _supercellsTraySub.rectTransform.anchoredPosition = new Vector2(14f, 8f);
            _supercellsTraySub.rectTransform.sizeDelta = new Vector2(leftColumnWidth - 14f, 30f);
            _supercellsTraySub.horizontalOverflow = HorizontalWrapMode.Wrap;
            _supercellsTraySub.resizeTextForBestFit = true;
            _supercellsTraySub.resizeTextMinSize = Mathf.RoundToInt(RigBoardLayout.SupercellTraySubFontSizeMin);
            _supercellsTraySub.resizeTextMaxSize = Mathf.RoundToInt(RigBoardLayout.SupercellTraySubFontSizeMax);

            var socketRow = NewRect("Sockets", tray, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            socketRow.pivot = new Vector2(1f, 0.5f);
            socketRow.sizeDelta = new Vector2(socketsWidth, SupercellsSocketSize);
            socketRow.anchoredPosition = new Vector2(-rightPad, 0f);

            _supercellsSockets.Clear();
            _supercellsSocketGlows.Clear();
            for (int i = 0; i < socketCount; i++)
            {
                var pos = new Vector2(-(socketsWidth - (i + 0.5f) * (SupercellsSocketSize + socketGap)), 0f);

                // MV-443 defect 7: a filled pip gets a soft glow behind it — built once here, toggled
                // in RefreshSupercellTray, same shared-texture idiom the node halos already use.
                var glow = AddImage(socketRow, HudTextures.Glow(64), Color.clear, $"Socket {i} Glow");
                Anchor(glow.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                glow.rectTransform.sizeDelta = new Vector2(SupercellsSocketSize * 1.6f, SupercellsSocketSize * 1.6f);
                glow.rectTransform.anchoredPosition = pos;
                glow.raycastTarget = false;
                glow.gameObject.SetActive(false);
                _supercellsSocketGlows.Add(glow);

                var socket = AddImage(socketRow, SupercellsPipOutlineSprite(), new Color(1f, 1f, 1f, 0.1f), $"Socket {i}");
                Anchor(socket.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                socket.rectTransform.sizeDelta = new Vector2(SupercellsSocketSize * Sqrt3 * 0.5f, SupercellsSocketSize);
                socket.rectTransform.anchoredPosition = pos;
                _supercellsSockets.Add(socket);
            }

            _supercellsOverflowText = AddText(socketRow, 14, SupercellColor, TextAnchor.MiddleRight);
            Anchor(_supercellsOverflowText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            _supercellsOverflowText.rectTransform.sizeDelta = new Vector2(50f, 30f);
            _supercellsOverflowText.rectTransform.anchoredPosition = new Vector2(6f, 0f);
            _supercellsOverflowText.fontStyle = FontStyle.Bold;
            _supercellsOverflowText.gameObject.SetActive(false);

            return rightEdge - width;
        }

        /// <summary>A dismiss pill pinned at <paramref name="rightEdge"/> from the bar's right edge.</summary>
        private float BuildCloseButton(RectTransform bar, float rightEdge)
        {
            const float w = 104f, h = 56f;
            var bg = AddImage(bar, HudTextures.RoundedBox(32, 0.5f), SupercellColor, "Close Button");
            Anchor(bg.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            bg.rectTransform.anchoredPosition = new Vector2(rightEdge, 0f);
            bg.rectTransform.sizeDelta = new Vector2(w, h);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;

            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Close);

            var label = AddText(bg.rectTransform, 24, PanelColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            // MV-445 defect 6: was U+2715 (heavy multiplication X, a dingbat) — HudFont's LegacyRuntime.ttf
            // has no coverage for it (same class of gap as the draft band's own ASCII-hyphen note), so it
            // rendered as a dropped/missing glyph. U+00D7 (the plain Latin-1 multiplication sign) is.
            label.text = "× CLOSE";
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;

            return rightEdge - w;
        }

        /// <summary>MV-257: abandons the run and returns to Home via <see cref="RunFlow.QuitToMenu"/>.</summary>
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

        /// <summary>A rounded pill: a live count/label, right-anchored at <paramref name="offset"/>
        /// from the top bar's right edge (CELLS). MV-445 defect 6: dropped the leading icon dot —
        /// neither design image carries one, "0 / 20 CELLS" reads on its own — and switched the
        /// background to a true stadium radius (25, matching the ticket's own spec against this chip's
        /// fixed 52px height) over a dark fill, not the lighter RowColor every other row uses.</summary>
        private RectTransform BuildChip(RectTransform bar, Vector2 offset, float width, Color accent,
            out Text label)
        {
            const float h = 52f;
            const float radius = 25f;
            float cornerFraction = Mathf.Clamp(radius / (Mathf.Min(width, h) * 0.5f), 0.05f, 0.5f);

            var chip = NewRect("Chip", bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            chip.pivot = new Vector2(1f, 0.5f);
            chip.sizeDelta = new Vector2(width, h);
            chip.anchoredPosition = offset;

            var bg = AddImage(chip, HudTextures.RoundedBox(32, cornerFraction), PanelColor, "BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;

            label = AddText(chip, 24, accent, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(14f, -20f);
            label.rectTransform.offsetMax = new Vector2(-14f, 20f);
            label.fontStyle = FontStyle.Bold;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 14;
            label.resizeTextMaxSize = 24;
            return chip;
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
