using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
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
    /// Top bar keeps its existing geometry (28/104 inset/height), CLOSE and QUIT TO MENU; PAUSED is
    /// gone (there's no room and no need — the screen's own presence already says the game is paused).
    /// MV-519: the SUPERCELLS tray is gone too — a Supercell grants its cells the instant it's picked
    /// up, never banked or cashed here — leaving the CELLS chip as the bar's one, deliberately dominant
    /// currency readout. Self-installing pause-on-open overlay, same idiom as every other full-screen
    /// panel (UpgradeScreen/HomeScreen/ResultScreen).
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("hud")]
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
        /// Below the floor (aspects narrower than iPad mini's own — never phone mode, whose aspect is
        /// always wider than 16:9), a little edge crop is still accepted (see
        /// <see cref="VisibleRefXWindow"/>) — required coverage stops at iPad mini and iPhone, not an
        /// unbounded narrower hypothetical.
        ///
        /// MV-549: nothing crops at either of those two supported aspects any more, on any device. iPhone
        /// used to be the exception — a real notch/Dynamic Island narrows the actual safe window below
        /// what this floor and <see cref="VisibleRefXWindow"/> (both full-canvas-only) ever knew about,
        /// so THE RIG's outer columns could still run past the safe area despite already being correctly
        /// centred on it. <see cref="ComputePhoneFitScale"/> closes that gap for phone mode specifically
        /// (standard mode, including iPad mini, was never affected — its insets are negligible and
        /// symmetric).</summary>
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

        private const float TopBarHeight = 40f;   // MV-1408: canvas units at the 852x393 reference
        private const float ContentMargin = 28f;

        // MV-1408: the RIG canvas is a phone-first 852x393 design, ScreenMatchMode.Expand (the whole
        // design always visible; extra screen centres it). The board stays authored in its own 1920x1080
        // frame and is scaled to fit the area below the top bar (see ApplyBoardScale).
        private const float CanvasRefW = 852f, CanvasRefH = 393f;
        private const float StandardViewportTop = 100f;   // frame y where standard mode's content starts (nothing above it)
        private const float PhoneViewportTop = 140f;      // frame y where phone mode's content starts
        private const float PhoneBoardBottomPad = 200f;   // phone FORGE captions hang below PhoneContentHeight

        // ------------------------------------------------------------------ THE RIG board (MV-423)

        private const int HexSides = 6;
        private const int FusionSides = 4;
        private const float HexRotationDeg = -90f;   // pointy-top: vertex angles 60*i-90
        // MV-433: diamond (vertex up/down/left/right), not the hex's pointy-top rotation. MV-462: was 45
        // to compensate for HudTextures.PolygonEdge's old off-by-half-segment vertex math (fixed there
        // now) — 0 is what actually puts a vertex at 0/90/180/270 under the corrected formula.
        private const float FusionRotationDeg = 0f;
        private const float Sqrt3 = 1.7320508f;

        // MV-1117: u_slt's own enlarged pill floors — "never smaller than 28 px"/"at least 22 px at
        // the reference resolution" — see BuildAbilityNode's u_slt special-case.
        private const float SentinelSlotsPillMinFontSize = 28f;
        private const float SentinelPriceLineMinFontSize = 22f;

        // MV-731: the sub-label box under a FORGE fusion diamond used to be a flat 280px regardless of
        // how close the column-layout pass (RigBoardLayout.BuildColumnLayout) happened to place that
        // fusion's neighbours — measured as close as ~139px apart at standard aspect, so two 280px boxes
        // overlapped by ~141px. FusionSubLabelWidth below shrinks the box to clear the resolved gap to
        // the nearest neighbouring fusion, with GapMargin as clear space between adjacent boxes and
        // MinWidth as a sanity floor (not expected to bind against real rig_board.json data).
        private const float FusionSubLabelMaxWidth = 280f;
        private const float FusionSubLabelGapMargin = 16f;
        private const float FusionSubLabelMinWidth = 96f;

        // MV-433: owned/lit-category halo canvas size as a multiple of the node's own hex bounds.
        // MV-446 defect 2 AC: must not exceed 1.25 (the halo's rect vs. the node radius) — headroom for
        // HudTextures.PolygonGlow's blur to fade out in, not a size the shape itself grows to (the glow
        // is drawn hex-tight now; see NodeGlowSprite). Peak alpha and blur width are data-driven
        // (RigBoardLayout.GlowAlphaOwned/GlowBlurOwned, .../Draft) so they're tunable without a code
        // change — was a plain circle sized to the node's SQUARE bounding box at a flat alpha, which
        // both spilled past a hexagon's narrower width and, at MV-443's raised 0.55, bloomed into a
        // lens-flare-looking blowout.
        private const float GlowRadiusMultiplier = 1.15f;

        // MV-538 AC4: the progress ring's own empty TRACK — a dim full-circle drawn behind the fill
        // so a partial arc reads as "part of a whole," not a decorative stray arc. Below the ring's
        // own 0.9 fill alpha so the fill always reads as the foreground.
        private const float ProgressTrackAlpha = 0.22f;

        private Canvas _canvas;
        /// <summary>MV-1398: the <see cref="CrispUi.Resolution"/> bucket this screen's shapes were built at.</summary>
        private int _shapeRes;
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

        /// <summary>MV-753: the world <see cref="RigBoardLayout"/> was last synced to — -1 until
        /// <see cref="Build"/> seeds it, so the first <see cref="ApplyBoardScale(float)"/> call doesn't
        /// redundantly rebuild content <see cref="Build"/> just built. Re-checked on every
        /// <see cref="ApplyBoardScale(float)"/> call against <see cref="RigBoard.ActiveWorldIndex"/>,
        /// same idiom as <see cref="_phoneMode"/> above — a Weapon Core morph landing while this screen
        /// is already built must force the exact same rebuild a phone/standard verdict change does, or
        /// the board keeps drawing the OLD world's ids against the NEW model (every one of them then
        /// reads as a permanently parent-gated LOCK — the root cause MV-753 fixes).</summary>
        private int _lastBoardWorldIndex = -1;

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

        private TextMeshProUGUI _cellsText;
        private Image _cellsChipBg;
        private Image _cellsBorder;
        private Button _cellsChipButton;

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
        private TextMeshProUGUI _draftBandTitle, _draftBandSubtitle, _draftBandReason;
        private bool _draftActive;
        private readonly List<string> _draftCandidateIds = new List<string>();

        // MV-605: supersedes MV-521's near-instant (0.6s) reveal glow with a staged ~3s ceremony that
        // plays when the player OPENS THE RIG with a module pending, not on pickup and not on close.
        // The family is still granted immediately, at the same call sites MV-521 already granted from
        // (see GrantDraftCandidate's own callers) — every existing grant-timing test keeps holding. The
        // ceremony only ever gates two things while it plays: what the player SEES (the rest of the
        // board dims back, the category node lights and its label resolves from a locked placeholder,
        // its children light in sequence top row down) and what they can SPEND (RefreshAbilityNode/
        // Update() suppress a node's own spendable state for the category the ceremony is still playing
        // over, via IsCeremonySuppressing — see AC4, "not spendable until it settles"). Skippable — a
        // tap anywhere fast-forwards straight to the settled state through the exact same EndCeremony()
        // a natural timeout takes, so "let it finish" and "tap through it" can never land in different
        // end states (AC5). Built once per BuildBoardContent() pass (rebuilt/cleared exactly like
        // _draftScrim). Every timing constant lives here so retiming later — Lee's own "starting value,
        // put the timings in one place" note — is a one-line change.
        private const float CeremonyDuration = 3.0f;
        private const float CeremonyPreUnlockEnd = CeremonyDuration * 0.12f;      // a: locked column, untouched
        private const float CeremonyDimEnd = CeremonyDuration * 0.28f;            // b: rest of the board dims back
        private const float CeremonyCategoryLightEnd = CeremonyDuration * 0.55f;  // c: category lights, label resolves
        private const float CeremonyChildLightEnd = CeremonyDuration * 0.92f;     // d: children light in sequence
        private const int CeremonyMaxChildGlows = 6;   // generous cap — no board family has this many children

        private bool _ceremonyActive;
        private string _ceremonyCategoryId;
        private float _ceremonyStartUnscaledTime = float.NegativeInfinity;
        private readonly List<string> _ceremonyChildIds = new List<string>();
        private Image _ceremonyScrim;                  // stage b: dims every other category back
        private RectTransform _ceremonyCategoryGlow;
        private Image _ceremonyCategoryGlowImage;       // stage c: highlight over the lit category's own panel
        private TextMeshProUGUI _ceremonyCategoryLabelOverlay;     // stage c: locked placeholder, fades out to reveal the real label beneath
        private readonly List<Image> _ceremonyChildGlows = new List<Image>();   // stage d: one per child, pooled
        private Button _ceremonySkipCatcher;            // full-board invisible tap target while a ceremony plays

        private bool _openField;
        // MV-1444: every path that opens/closes THE RIG goes through here, so the RIG soundscape follows.
        private bool _open
        {
            get => _openField;
            set { _openField = value; MaxWorlds.Audio.RigAudioMix.RigOpen = value; }
        }
        private float _prevTimeScale = 1f;

        /// <summary>Is THE RIG currently up (and the game paused)?</summary>
        public bool IsOpen => _open;

        /// <summary>MV-521: is a Morphing Module draft currently up on the board? — test-only access,
        /// same idiom as <see cref="IsOpen"/>.</summary>
        public bool IsDraftActive => _draftActive;

        /// <summary>MV-521: the draft's current candidate ids, in draw order — test-only access, same
        /// idiom as <see cref="BoardNode"/>.</summary>
        public IReadOnlyList<string> DraftCandidateIds => _draftCandidateIds;

        /// <summary>MV-521: the screen's own show/hide root — test-only access, same idiom as
        /// <see cref="Background"/>, so a test can assert directly on what <see cref="Close"/> toggles
        /// rather than inferring it through a child's own activeInHierarchy.</summary>
        public GameObject ScreenRoot => _screenRoot != null ? _screenRoot.gameObject : null;

        /// <summary>MV-605: is the just-unlocked-family reveal ceremony currently playing? — test-only
        /// access, same idiom as <see cref="IsOpen"/>. Superseded MV-521's identically-named glow-only
        /// check; kept the same property name since the question a caller asks ("is a reveal up right
        /// now?") hasn't changed, only what answers it.</summary>
        public bool IsCeremonyActive => _ceremonyActive;

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

        /// <summary>MV-549: the Safe Area rect itself — test-only access, so a crop test can compare a
        /// node's real built <see cref="RectTransform.GetWorldCorners"/> against the ACTUAL safe rect
        /// (post <see cref="SafeArea"/> inset) instead of re-deriving it from <see cref="Screen"/>.</summary>
        public RectTransform SafeRoot => _safeRoot;

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

        /// <summary>MV-520: an ability node's own cost-tag text — test-only access, same idiom as
        /// <see cref="CategoryPanel"/>, so a resolved-value test can assert the built Text component
        /// directly rather than guessing GameObject names among a node's several Text children.</summary>
        public TextMeshProUGUI NodeCostText(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.CostText : null;

        /// <summary>MV-520: an ability node's own cost-tag glyph — test-only access, same idiom as
        /// <see cref="NodeCostText"/>.</summary>
        public Image NodeCostIcon(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.CostIcon : null;

        /// <summary>MV-520: an ability node's own name label — test-only access, so a test can confirm
        /// the "? ? ?" name-hidden placeholder never coincides with a spendable cost.</summary>
        public TextMeshProUGUI NodeLabel(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.Label : null;

        /// <summary>MV-538: an ability node's own hex fill — test-only access, same idiom as
        /// <see cref="NodeLabel"/>, so a dim-scope test can compare alpha across nodes without
        /// guessing GameObject names among a node's several Image children.</summary>
        public Image NodeHexFill(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.HexFill : null;

        /// <summary>MV-538: an ability node's own tap button — test-only access, same idiom as
        /// <see cref="NodeHexFill"/>.</summary>
        public Button NodeButton(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.Button : null;

        /// <summary>MV-470/MV-538: an ability node's own accumulation ring — test-only access, same
        /// idiom as <see cref="NodeHexFill"/>.</summary>
        public Image NodeProgressRing(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.ProgressRing : null;

        /// <summary>MV-538: the dim full-circle track drawn behind <see cref="NodeProgressRing"/> so a
        /// partial fill reads as "part of a whole" instead of a stray arc (AC4) — test-only access,
        /// same idiom as <see cref="NodeHexFill"/>.</summary>
        public Image NodeProgressTrack(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.ProgressTrack : null;

        /// <summary>MV-620: an ability node's own level-pill backdrop — test-only access, same idiom as
        /// <see cref="NodeHexFill"/>, so a legibility test can read the pill's real bottom edge rather
        /// than re-deriving it from <see cref="RigBoardLayout"/> formulas independently.</summary>
        public Image NodePillBg(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.PillBg : null;

        /// <summary>MV-654: an ability node's own level-pill text — test-only access, same idiom as
        /// <see cref="NodePillBg"/>, so a test can read the RESOLVED pill text (e.g. <c>u_slt</c>'s
        /// deployable-sentinels read) rather than re-deriving it from the refresh formulas independently.</summary>
        public TextMeshProUGUI NodePillText(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.PillText : null;

        /// <summary>MV-1117: <c>u_slt</c>'s own deploy-price line, stacked beneath its (doubled-size)
        /// slots pill text — test-only access, same idiom as <see cref="NodePillText"/>. Null for every
        /// other node (see <see cref="RigNodeVisual.SentinelPriceText"/>'s own doc comment).</summary>
        public TextMeshProUGUI NodeSentinelPriceText(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.SentinelPriceText : null;

        /// <summary>MV-1117: <c>u_slt</c>'s own deploy-price glyph — test-only access, same idiom as
        /// <see cref="NodeSentinelPriceText"/>.</summary>
        public Image NodeSentinelPriceIcon(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.SentinelPriceIcon : null;

        /// <summary>MV-729: a FORGE fusion node's own name label — test-only access, same idiom as
        /// <see cref="NodeLabel"/>, so a test can confirm the real fusion name is shown even while
        /// locked, never the old "? ? ?" placeholder.</summary>
        public TextMeshProUGUI FusionNodeLabel(string id) => _fusionNodes.TryGetValue(id, out var v) ? v.Label : null;

        /// <summary>MV-729: a FORGE fusion node's own sub-label — test-only access, same idiom as
        /// <see cref="FusionNodeLabel"/>, so a test can read the RESOLVED unlock-requirement/cost/slot
        /// text rather than re-deriving it from the refresh formulas independently.</summary>
        public TextMeshProUGUI FusionNodeSub(string id) => _fusionNodes.TryGetValue(id, out var v) ? v.Sub : null;

        /// <summary>MV-433: the board's own scale-to-fit wrapper (never the same object as
        /// <see cref="BoardNode"/>'s parent frame, which stays fixed at 1920x1080 in its own local
        /// space regardless of this wrapper's scale) — test-only access to confirm the clamp applied.</summary>
        public float BoardScale => _boardScaleRoot != null ? _boardScaleRoot.localScale.x : 1f;

        private void Start() => Build();

        private void OnEnable()
        {
            RigState.Changed += Refresh;
            RigFusionState.Changed += Refresh;
            PickupWallet.PowerCellsChanged += OnCellsChanged;
            PickupWallet.CapacityChanged += OnCellsChanged;
        }

        private void OnDisable()
        {
            RigState.Changed -= Refresh;
            RigFusionState.Changed -= Refresh;
            PickupWallet.PowerCellsChanged -= OnCellsChanged;
            PickupWallet.CapacityChanged -= OnCellsChanged;
        }

        private void OnDestroy()
        {
            // Never leave the world frozen if we're torn down mid-open (a scene swap, a test).
            if (_open)
            {
                Time.timeScale = _prevTimeScale;
                ModalFrameRateGate.Exit();
                MaxWorlds.Audio.RigAudioMix.RigOpen = false;
            }
            if (_canvas != null) Destroy(_canvas.gameObject);
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
            if (canLevelUp) return cellsBanked >= CellSpend.UpgradeCostFor(id, RigState.Level(id));
            if (draftable) return cellsBanked >= CellSpend.UnlockCostFor(id);
            return false;
        }

        /// <summary>MV-516: the ability node hex FILL/OUTLINE's own alpha, given the base (state-set)
        /// alpha <paramref name="baseAlpha"/> already carries — "a node the player can act on RIGHT NOW
        /// pulses slowly and unmistakably ... the hexagon FILL and OUTLINE, not just the thin outer
        /// ring." Pure so the cadence is pinned by an EditMode test without building a canvas or
        /// advancing <see cref="Time"/>. 1 rad/s — a
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

        /// <summary>MV-605 AC4: is <paramref name="categoryId"/>'s family still mid-ceremony — i.e. must
        /// its nodes read as NOT spendable regardless of what <see cref="IsAbilityNodeSpendable"/> alone
        /// would say? The family is granted immediately (unchanged since MV-424/521), so this is the
        /// ONE gate standing between "granted" and "the player can actually spend into it" while the
        /// reveal is still playing.</summary>
        private bool IsCeremonySuppressing(string categoryId) =>
            _ceremonyActive && _ceremonyCategoryId == categoryId;

        /// <summary>Same gate as <see cref="IsCeremonySuppressing"/>, keyed off an ABILITY id instead of
        /// its category — the two call sites (<see cref="Update"/>'s per-frame pulse,
        /// <see cref="RefreshAbilityNode"/>'s own spendable computation) only ever have the ability id to
        /// hand, same reasoning as the existing <see cref="CategoryUnlockedForAbility"/> helper.</summary>
        private bool IsCeremonySuppressingAbility(string abilityId)
        {
            if (!_ceremonyActive) return false;
            foreach (var ab in RigBoardLayout.Abilities)
                if (ab.Id == abilityId) return ab.Category == _ceremonyCategoryId;
            return false;
        }

        /// <summary>Starts the reveal ceremony over <paramref name="categoryId"/>'s own panel — staged,
        /// skippable, ~<see cref="CeremonyDuration"/>s (see the field block's own doc comment for the
        /// full stage breakdown). A no-op if the category has no built panel (nothing to highlight).</summary>
        private void StartCeremony(string categoryId)
        {
            SelectCategory(categoryId);   // MV-1410: show the tree the reveal is about
            if (_ceremonyScrim == null || string.IsNullOrEmpty(categoryId)) return;

            _ceremonyActive = true;
            _ceremonyCategoryId = categoryId;
            _ceremonyStartUnscaledTime = Time.unscaledTime;

            // d: "children light in sequence, top row down" — RigBoardLayout's own Y is image-like
            // (top of board = smallest Y; see BuildNodeShell's own `anchoredPosition = (x, -y)`), so
            // ascending Y is top-to-bottom on screen.
            _ceremonyChildIds.Clear();
            var children = new List<RigAbilityLayout>();
            foreach (var ab in Abilities)
                if (ab.Category == categoryId && _treeSpots.TryGetValue(ab.Id, out var spot))
                    children.Add(new RigAbilityLayout(ab.Id, ab.Category, ab.Icon, ab.Label, ab.Kind, ab.Parent, spot.X, spot.Y, ab.MaxLevel, ab.Effect));
            children.Sort((a, b) => a.Y.CompareTo(b.Y));
            foreach (var ab in children) _ceremonyChildIds.Add(ab.Id);

            if (_ceremonySkipCatcher != null) _ceremonySkipCatcher.gameObject.SetActive(true);
            if (_ceremonyScrim != null) _ceremonyScrim.gameObject.SetActive(true);   // b's dim fade needs the scrim actually on to show
            ApplyCeremonyTiming(0f);
            Refresh();   // AC4: the newly-granted family must render inert until IsCeremonySuppressing clears
        }

        /// <summary>Applies the ceremony's staged visuals for a given elapsed unscaled time since it
        /// started — called every frame from <see cref="Update"/> with the real elapsed time, and
        /// callable directly (test-only) with a fabricated value, same idiom the old MV-521 reveal glow
        /// established (<c>ApplyRevealTiming</c>/<c>RevealStillActive</c>), so "the ceremony has ended N
        /// seconds later" can be asserted without <see cref="Update"/> ever running.</summary>
        public void ApplyCeremonyTiming(float elapsedUnscaledSeconds)
        {
            if (!_ceremonyActive) return;
            if (elapsedUnscaledSeconds >= CeremonyDuration) { EndCeremony(); return; }

            // b: the rest of the board dims back.
            float dimT = Mathf.Clamp01(Mathf.InverseLerp(CeremonyPreUnlockEnd, CeremonyDimEnd, elapsedUnscaledSeconds));
            if (_ceremonyScrim != null)
            {
                var c = _ceremonyScrim.color;
                c.a = 0.72f * dimT;
                _ceremonyScrim.color = c;
            }

            // c: the category node's own highlight glow, and its label resolving from a locked
            // placeholder to the real family name.
            float catT = Mathf.Clamp01(Mathf.InverseLerp(CeremonyDimEnd, CeremonyCategoryLightEnd, elapsedUnscaledSeconds));
            if (_ceremonyCategoryGlow != null && _treePanel != null)
            {
                RectTransform panelRect = _treePanel.rectTransform;
                _ceremonyCategoryGlow.anchorMin = panelRect.anchorMin;
                _ceremonyCategoryGlow.anchorMax = panelRect.anchorMax;
                _ceremonyCategoryGlow.pivot = panelRect.pivot;
                _ceremonyCategoryGlow.sizeDelta = panelRect.sizeDelta;
                _ceremonyCategoryGlow.anchoredPosition = panelRect.anchoredPosition;
                _ceremonyCategoryGlow.SetAsLastSibling();
                _ceremonyCategoryGlow.gameObject.SetActive(catT > 0f);

                Color family = RigBoardLayout.Colour(RigBoardLayout.CategoryFamily(_ceremonyCategoryId));
                _ceremonyCategoryGlowImage.color = new Color(family.r, family.g, family.b, 0.55f * catT);
            }
            if (_ceremonyCategoryLabelOverlay != null && _treeNameText != null)
            {
                bool resolved = elapsedUnscaledSeconds >= CeremonyCategoryLightEnd;
                RectTransform realLabelRect = _treeNameText.rectTransform;
                RectTransform overlayRect = _ceremonyCategoryLabelOverlay.rectTransform;
                overlayRect.SetParent(realLabelRect.parent, false);
                overlayRect.anchorMin = realLabelRect.anchorMin;
                overlayRect.anchorMax = realLabelRect.anchorMax;
                overlayRect.pivot = realLabelRect.pivot;
                overlayRect.sizeDelta = realLabelRect.sizeDelta;
                overlayRect.anchoredPosition = realLabelRect.anchoredPosition;
                overlayRect.SetAsLastSibling();
                _ceremonyCategoryLabelOverlay.gameObject.SetActive(!resolved);
                var oc = _ceremonyCategoryLabelOverlay.color;
                oc.a = 1f - catT;
                _ceremonyCategoryLabelOverlay.color = oc;
            }

            // d: child nodes light in sequence, one short beat apart.
            if (_ceremonyChildIds.Count > 0)
            {
                float span = Mathf.Max(0.01f, CeremonyChildLightEnd - CeremonyCategoryLightEnd);
                float perChild = span / _ceremonyChildIds.Count;
                Color family = RigBoardLayout.Colour(RigBoardLayout.CategoryFamily(_ceremonyCategoryId));
                for (int i = 0; i < _ceremonyChildIds.Count && i < _ceremonyChildGlows.Count; i++)
                {
                    var img = _ceremonyChildGlows[i];
                    if (!_abilityNodes.TryGetValue(_ceremonyChildIds[i], out var childVisual)) { img.gameObject.SetActive(false); continue; }

                    float childStart = CeremonyCategoryLightEnd + perChild * i;
                    float t = Mathf.Clamp01(Mathf.InverseLerp(childStart, childStart + perChild * 0.6f, elapsedUnscaledSeconds));

                    RectTransform nodeRect = childVisual.Root;
                    var glowRect = img.rectTransform;
                    glowRect.SetParent(nodeRect.parent, false);
                    glowRect.anchorMin = nodeRect.anchorMin;
                    glowRect.anchorMax = nodeRect.anchorMax;
                    glowRect.pivot = nodeRect.pivot;
                    glowRect.anchoredPosition = nodeRect.anchoredPosition;
                    glowRect.sizeDelta = nodeRect.sizeDelta * 1.3f;
                    glowRect.SetAsLastSibling();

                    img.gameObject.SetActive(t > 0f);
                    img.color = new Color(family.r, family.g, family.b, 0.6f * t);
                }
            }
        }

        /// <summary>Ends the ceremony — the SAME code path whether it ran out its full
        /// <see cref="CeremonyDuration"/> (<see cref="Update"/>/<see cref="ApplyCeremonyTiming"/> calling
        /// this once elapsed time runs out) or the player tapped to skip it
        /// (<see cref="_ceremonySkipCatcher"/>'s own listener), so "let it finish" and "tap through it"
        /// (AC5) can never land in different end states. AC7/8: chains straight into the next banked
        /// module, back to back, within this same rig visit — never from <see cref="Close"/>, which
        /// cancels the visuals instead (see <see cref="CancelCeremony"/>) and leaves anything still
        /// pending banked for the player's next open.</summary>
        private void EndCeremony()
        {
            if (!_ceremonyActive) return;
            CancelCeremony();
            Refresh();   // the suppression this ceremony was applying is gone now — spendable/pulse read live

            if (_open && PendingMorphingModule.HasPending)
                OpenMorphingModuleDraft(PendingMorphingModule.Take());
        }

        /// <summary>Clears the ceremony's own state and hides every visual it was driving, without
        /// granting anything (the family was already granted at <see cref="StartCeremony"/> time — see
        /// its callers) and without chaining into whatever else might still be pending — that's
        /// <see cref="EndCeremony"/>'s job, not this one's. Used both by a normal completion and by
        /// <see cref="Close"/> catching one mid-flight (MV-605 AC7's "does not replay" — a cancelled
        /// ceremony leaves nothing of itself behind to resume).</summary>
        private void CancelCeremony()
        {
            if (!_ceremonyActive) return;
            _ceremonyActive = false;
            _ceremonyCategoryId = null;
            _ceremonyChildIds.Clear();

            if (_ceremonyScrim != null) _ceremonyScrim.gameObject.SetActive(false);
            if (_ceremonyCategoryGlow != null) _ceremonyCategoryGlow.gameObject.SetActive(false);
            if (_ceremonyCategoryLabelOverlay != null) _ceremonyCategoryLabelOverlay.gameObject.SetActive(false);
            if (_ceremonySkipCatcher != null) _ceremonySkipCatcher.gameObject.SetActive(false);
            foreach (var img in _ceremonyChildGlows) img.gameObject.SetActive(false);
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
        // MV-1410: always true. THE RIG is phone-first at every aspect now - a category rail and one
        // tree at a time - so the big phone radii/fonts apply everywhere (the old standard 1920x1080
        // board is gone).
        public static bool IsPhoneLayout(float aspect) => true;

        // ------------------------------------------------------------------ MV-1410: rail + one tree

        /// <summary>Board frame units to canvas units. The tree is authored in frame units (the node
        /// shell's radii, fonts and pill sizes all are) and drawn at half size, so a 60-radius child is a
        /// 60-canvas-unit tap rect.</summary>
        public const float TreeScale = 0.5f;
        private const float RootRadius = 72f, ChildRadius = 60f, FusionTreeRadius = 72f;   // frame units: rects 144 / 120 / 144
        private const float TreeMargin = 8f, TreeGap = 6f, RailWidth = 440f, DetailReserve = 360f, TreeHeaderH = 100f;
        private const float TreeLabelHang = 56f, TreeMaxGapY = 110f, TreeMaxPitchX = 240f;
        // MV-1415: node labels hang UNDER the hex (name, then the level / price line) and are never wider than
        // their own column minus a 12 pt (24 frame unit) gap, so no label or hex rect can overlap a neighbour.
        private const float QuitBtnH = 76f;   // 38 pt
        private const float LabelNameH = 30f, LabelSubH = 28f, LabelBlockH = 62f, LabelColGap = 24f, LabelMaxW = 220f, RootLabelW = 200f;
        private static readonly Color GoldColor = new Color32(0xFF, 0xC0, 0x4A, 0xFF);
        private static readonly Color SubGrey = new Color(0.62f, 0.65f, 0.72f, 1f);
        private const float ViewHeight = 1400f, ViewLeft = -240f, ViewWidth = 2400f;
        private const string ForgeTabId = "FORGE";
        // MV-1410: a padlock in the same 30-unit centred stroke style as rig_board.json's icons.
        private const string PadlockSvg = "<path d=\"M-10,-1 L10,-1 L10,14 L-10,14 Z\" fill=\"none\" stroke=\"#ICON#\" stroke-width=\"3\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/><path d=\"M-6,-1 L-6,-7 L-3,-12 L3,-12 L6,-7 L6,-1\" fill=\"none\" stroke=\"#ICON#\" stroke-width=\"3\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>";

        /// <summary>Where everything in the rail + tree layout sits, in board frame units (x right, y down
        /// from the top of the area below the top bar).</summary>
        private struct TreeFrame { public Rect Panel, Area, Detail, Quit; public Rect[] Tabs; }

        private static TreeFrame ComputeTreeFrame(float safeW, float safeH, int tabCount)
        {
            float w = safeW / TreeScale, h = (safeH - TopBarHeight) / TreeScale;
            float x0 = RefW * 0.5f - w * 0.5f;
            var f = new TreeFrame { Tabs = new Rect[tabCount] };
            float tabH = tabCount > 0 ? Mathf.Min(150f, (h - 2f * TreeMargin - QuitBtnH - TreeGap - (tabCount - 1) * TreeGap) / tabCount) : 0f;
            f.Quit = new Rect(x0 + TreeMargin, h - TreeMargin - QuitBtnH, RailWidth, QuitBtnH);   // MV-1415: QUIT TO MENU lives at the rail's foot, far from CLOSE
            for (int i = 0; i < tabCount; i++)
                f.Tabs[i] = new Rect(x0 + TreeMargin, TreeMargin + i * (tabH + TreeGap), RailWidth, tabH);
            float px = x0 + TreeMargin + RailWidth + TreeMargin;
            f.Panel = new Rect(px, TreeMargin, Mathf.Max(200f, w - (px - x0) - DetailReserve - TreeMargin), h - 2f * TreeMargin);
            f.Detail = new Rect(f.Panel.xMax + TreeMargin, TreeMargin, DetailReserve - 2f * TreeMargin, f.Panel.height);   // MV-1411: right of the tree panel, same height
            f.Area = new Rect(f.Panel.x + 24f, f.Panel.y + TreeHeaderH, f.Panel.width - 48f, f.Panel.height - TreeHeaderH - 16f - LegendH);
            return f;
        }

        /// <summary>The tree-layout result for one node, in board frame units.</summary>
        private struct TreeSpot { public float X, Y, R, Pitch; public int Depth; public bool HasChildren, RootAlone; public string Category; }
        private readonly Dictionary<string, TreeSpot> _treeSpots = new Dictionary<string, TreeSpot>();
        private readonly Dictionary<string, Rect> _treeLabelRects = new Dictionary<string, Rect>();

        /// <summary>MV-1415: height of the label block under <paramref name="id"/>'s hex. u_slt carries its
        /// enlarged slots/price pill (MV-1117) between its name and the next row.</summary>
        private float LabelBlockHeight(string id) => id == "u_slt" ? 4f + LabelNameH + 2f + SentinelPillHeight() + 2f : LabelBlockH;

        private float SentinelPillHeight()
        {
            float slotsFontSize = Mathf.Max(SentinelSlotsPillMinFontSize, LevelPillFontSize * 2f);
            float priceFontSize = Mathf.Max(SentinelPriceLineMinFontSize, CostFontSize);
            return slotsFontSize * 1.3f + priceFontSize * 1.3f + 6f;
        }

        /// <summary>MV-1415: the label block's rect (board frame units): under the hex, centred, as wide as the
        /// node's column minus the gap; a root that is alone on its row carries it to the right instead.</summary>
        private Rect ComputeLabelRect(TreeSpot s, string id)
        {
            if (s.RootAlone) return new Rect(s.X + s.R * Sqrt3 * 0.5f + 28f, s.Y - 30f, RootLabelW, 60f);
            float w = Mathf.Clamp(s.Pitch - LabelColGap, 60f, LabelMaxW);
            return new Rect(s.X - w * 0.5f, s.Y + s.R + 2f, w, LabelBlockHeight(id) - 2f);
        }

        /// <summary>MV-1415: a tree node's label block rect in board frame units (see <see cref="TreeScale"/>),
        /// or default if unknown. Counted together with <see cref="TreeNodeRect"/> for overlap.</summary>
        public Rect TreeLabelRect(string id) => _treeLabelRects.TryGetValue(id, out var r) ? r : default;
        private TreeFrame _treeFrame;
        private string _selectedCategory;
        private string _selectedNodeId;

        /// <summary>The category ids that get a rail tab, in board order (FORGE last, only when the world has fusions).</summary>
        private List<string> RailIds()
        {
            var ids = new List<string>();
            foreach (var c in Categories) ids.Add(c.Id);
            if (Fusions.Count > 0) ids.Add(ForgeTabId);
            return ids;
        }

        private void ComputeTreeLayouts()
        {
            _treeSpots.Clear();
            _treeLabelRects.Clear();
            var ids = RailIds();
            _treeFrame = ComputeTreeFrame(_safeW, _safeH, ids.Count);
            if (_selectedCategory == null || !ids.Contains(_selectedCategory)) _selectedCategory = ids.Count > 0 ? ids[0] : null;

            var area = _treeFrame.Area;
            foreach (var cat in Categories)
            {
                var pairs = new List<(string, string)>();
                foreach (var ab in Abilities) if (ab.Category == cat.Id) pairs.Add((ab.Id, ab.Parent));
                var placed = RigTreeLayout.Compute(pairs, area.width, area.height, RootRadius * 2f, ChildRadius * 2f, LabelBlockHeight, 0f, TreeMaxGapY, TreeMaxPitchX);
                int rootCount = 0;
                foreach (var p in placed) if (p.IsRoot) rootCount++;
                foreach (var p in placed)
                {
                    var spot = new TreeSpot { X = area.x + p.X, Y = area.y + p.Y, R = p.Size * 0.5f, Pitch = p.Pitch, Depth = p.Depth, HasChildren = p.HasChildren, RootAlone = p.Depth == 0 && rootCount == 1, Category = cat.Id };
                    _treeSpots[p.Id] = spot;
                    _treeLabelRects[p.Id] = ComputeLabelRect(spot, p.Id);
                }
            }
            if (Fusions.Count > 0)
            {
                var pairs = new List<(string, string)>();
                foreach (var fu in Fusions) pairs.Add((fu.Id, null));
                var placed = RigTreeLayout.Compute(pairs, area.width, area.height, FusionTreeRadius * 2f, FusionTreeRadius * 2f, null, TreeLabelHang, TreeMaxGapY, TreeMaxPitchX);
                foreach (var p in placed)
                    _treeSpots[p.Id] = new TreeSpot { X = area.x + p.X, Y = area.y + p.Y, R = p.Size * 0.5f, HasChildren = false, Category = ForgeTabId };
            }
            // MV-1411: keep the selected node only while it is still in the selected category's tree.
            if (_selectedNodeId == null || !_treeSpots.TryGetValue(_selectedNodeId, out var keep) || keep.Category != _selectedCategory)
                _selectedNodeId = RootNodeOf(_selectedCategory);
        }

        /// <summary>MV-549: half the total ref-px span phone mode's own category panels actually occupy,
        /// centred on the board's own x=960 midpoint — the same envelope
        /// <c>RigBoardChromeTests.EveryNodeAndRegionPanelFitsInsideTheVisibleWindowAtEveryTestedAspect</c>
        /// already checks against a window, computed here instead of hard-coding
        /// <c>RigBoardLayout.PhoneTargetWidth</c> so this tracks that constant (or any future column
        /// re-tuning) without needing its own update.</summary>
        private static float PhoneContentHalfWidth()
        {
            const float centreX = RefW * 0.5f;
            float half = 0f;
            foreach (var cat in RigBoardLayout.PhoneCategories)
                half = Mathf.Max(half, Mathf.Abs(cat.X - centreX) + cat.ColumnHalfWidth);
            return half;
        }

        /// <summary>MV-549: phone mode never runs <see cref="VisibleRefXWindow"/>'s crop maths (that's
        /// standard/tablet mode only — see this file's own class doc history) and never shrinks via
        /// <see cref="ComputeBoardScale"/> either (a phone aspect is always wider than 16:9). Both of
        /// those assume the board's available width is the FULL screen; a real notch/Dynamic Island
        /// narrows the actual SAFE window below that without phone mode ever finding out, which is what
        /// let THE RIG's outer columns run past the safe area on iPhone despite already being correctly
        /// CENTRED on it (<c>_boardScaleRoot</c>'s own anchor already does that part right). This is the
        /// missing other half: shrink — by the smallest amount that closes the gap, never more — only
        /// when the actual safe window is narrower than the content's own fixed-width envelope. On every
        /// device with room to spare (<paramref name="safeAreaWidthFraction"/> at or near 1, true of
        /// every EditMode test that never simulates a notch, and most real phones) this returns exactly
        /// 1f, so <see cref="BoardScale"/> is unchanged from before this ticket. Pure and aspect-driven
        /// (not read off <c>_safeRoot</c>'s own rendered rect) so it's pinned without a live canvas, same
        /// idiom as <see cref="ComputeBoardScale"/>.</summary>
        public static float ComputePhoneFitScale(float aspect, float safeAreaWidthFraction)
        {
            if (aspect <= 0f || safeAreaWidthFraction <= 0f) return 1f;
            float safeRefWidth = RefH * aspect * safeAreaWidthFraction;
            float neededWidth = PhoneContentHalfWidth() * 2f;
            if (neededWidth <= 0f) return 1f;
            return Mathf.Min(1f, safeRefWidth / neededWidth);
        }

        private IReadOnlyList<RigCategoryLayout> Categories => _phoneMode ? RigBoardLayout.PhoneCategories : RigBoardLayout.Categories;
        private IReadOnlyList<RigAbilityLayout> Abilities => _phoneMode ? RigBoardLayout.PhoneAbilities : RigBoardLayout.Abilities;
        /// <summary>MV-850: empty in World 2+ — FORGE is World 1 only until the four fusions are
        /// redesigned for the LPPE/Shoulder Rack kit. Every reader of this property (node building,
        /// connector building, live refresh) goes through it rather than <see cref="RigBoardLayout"/>
        /// directly, so gating it here is the one switch that hides the whole row.</summary>
        private IReadOnlyList<RigFusionLayout> Fusions =>
            !RigFusionState.EnabledInWorld(RigBoard.ActiveWorldIndex) ? Array.Empty<RigFusionLayout>() :
            _phoneMode ? RigBoardLayout.PhoneFusions : RigBoardLayout.Fusions;
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
        private float CostFontSize => _phoneMode ? RigBoardLayout.CostFontSizePhone : RigBoardLayout.CostFontSize;
        private float CostIconSize => _phoneMode ? RigBoardLayout.CostIconSizePhone : RigBoardLayout.CostIconSize;
        private float ForgeDividerY => _phoneMode ? RigBoardLayout.ForgeDividerYPhone : RigBoardLayout.ForgeDividerY;

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

            // MV-605: the just-unlocked-family reveal ceremony — self-terminating, so this only ever
            // does anything while one is actually playing.
            if (_ceremonyActive)
                ApplyCeremonyTiming(Time.unscaledTime - _ceremonyStartUnscaledTime);

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
                // then refuses. MV-605 AC4: a node whose family is still mid-ceremony must stay inert
                // too — the same suppression RefreshAbilityNode's own Button.interactable applies.
                bool spendable = IsAbilityNodeSpendable(kv.Key, cellsBanked) && !IsCeremonySuppressingAbility(kv.Key);

                if (v.OuterRing != null && v.OuterRing.gameObject.activeSelf)
                {
                    // MV-462 defect 3: a draftable node in an unlit family must stay dimmed every frame,
                    // not just at the moment Refresh() ran — Update() drives this pulse independently of
                    // Refresh(), so without this factor the ring/halo would flash back up to full
                    // brightness on every tick regardless of the static dim RefreshAbilityNode applied.
                    float dim = CategoryUnlockedForAbility(kv.Key) ? 1f : RigBoardLayout.FamilyDimFactor;
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
                    fc.a = v.FillBaseAlpha;
                    v.HexFill.color = fc;
                }
                if (v.HexOutline != null)
                {
                    var oc = v.HexOutline.color;
                    oc.a = v.OutlineBaseAlpha;
                    v.HexOutline.color = oc;
                }
            }
            _ = dt;
        }

        private void OnCellsChanged(int cells) => Refresh();

        /// <summary>Open THE RIG, pausing the game. Ignored if already open. MV-425: if a Morphing
        /// Module draft is banked and waiting (<see cref="PendingMorphingModule"/>), opening here shows
        /// it immediately rather than the plain board — the player asked to open WEAPONS precisely
        /// because the HUD's cyan badge told them one was waiting. MV-698: a banked Weapon Core
        /// (<see cref="PendingMorphingModule.WeaponCorePending"/>) resolves first and silently — unlike
        /// a module draft there is nothing to pick between, so the morph just applies and the board
        /// opens straight into its new PRIMARY/SECONDARY rather than a separate ceremony screen.</summary>
        public void Open()
        {
            if (_open) return;

            WeaponSystemState.OpenWeaponCoreMorphIfPending(CurrentWorldIndex());

            if (PendingMorphingModule.RackModulePending)
            {
                OpenRackModuleReveal();
                return;
            }

            // MV-1248: World 5's cradle-destroyed unlock schedule — same priority tier as the Rack
            // Module reveal above (resolved before any ordinary draft), Secondary (2nd cradle) ahead of
            // Sentinel (4th cradle) since that is the order the schedule itself banks them in.
            if (PendingMorphingModule.SecondaryFamilyPending)
            {
                OpenSecondaryFamilyReveal();
                return;
            }

            if (PendingMorphingModule.SentinelFamilyPending)
            {
                OpenSentinelFamilyReveal();
                return;
            }

            if (PendingMorphingModule.HasPending)
            {
                OpenMorphingModuleDraft(PendingMorphingModule.Take());
                return;
            }

            if (_canvas == null) Build();

            _open = true;
            _prevTimeScale = TimeScaleCapture.ClampForCapture(Time.timeScale);
            Time.timeScale = 0f;   // freeze the fight while the player reads/spends
            ModalFrameRateGate.Enter();   // MV-574: idle the frame rate while this modal is up

            // MV-1411: opening the RIG lands on the PRIMARY tab with its root node selected.
            if (_railOrder.Contains("PRIMARY")) _selectedCategory = "PRIMARY";
            _selectedNodeId = RootNodeOf(_selectedCategory);
            ApplyTreeSelection();

            Refresh();
            _screenRoot.gameObject.SetActive(true);
        }

        /// <summary>MV-698: the world whose board a pending Weapon Core morph should switch to — the
        /// active profile's current <c>WorldIndex</c>. MV-1074 corrects this doc's own prior false
        /// assumption: between collecting a finale's Weapon Core and actually crossing the finale gate,
        /// <c>WorldIndex</c> has NOT yet advanced — <c>SaveSystem.RecordResult</c> only advances it once
        /// the run seals, which needs Max to walk out through the gate. So opening THE RIG in the finale
        /// world itself still reads the OLD (current) world here; that is exactly why
        /// <see cref="WeaponSystemState.OpenWeaponCoreMorphIfPending"/> compares this value against
        /// <see cref="RigBoard.ActiveWorldIndex"/> rather than trusting it to already mean "the next
        /// world" — only once the run seals and the NEXT world's own <see cref="MaxWorlds.Arena.BackyardPath.Awake"/>
        /// calls back in with the advanced index does the morph actually apply. Falls back to World 1
        /// with no active profile (a capture/press-kit run, or a test) — never reached for real, since
        /// nothing sets <see cref="PendingMorphingModule.WeaponCorePending"/> without an active slot.</summary>
        private static int CurrentWorldIndex() =>
            SaveSystem.ActiveSlot >= 0 ? SaveSystem.Load(SaveSystem.ActiveSlot).WorldIndex : 0;

        /// <summary>Close THE RIG and resume at whatever speed it paused from.</summary>
        public void Close()
        {
            if (!_open) return;
            _open = false;
            _draftActive = false;
            _draftCandidateIds.Clear();
            // MV-605 AC7: a ceremony caught mid-flight by the player closing early must not linger, must
            // not replay on the next open, and must not grant anything a second time — CancelCeremony
            // only ever clears/hides; the grant itself already happened at StartCeremony time.
            CancelCeremony();
            CloseQuitDialog();
            Time.timeScale = _prevTimeScale;
            ModalFrameRateGate.Exit();
            _screenRoot.gameObject.SetActive(false);
        }

        /// <summary>A Morphing Module was collected (MV-424, replacing the old shed → badge → BUILD
        /// ABILITY modal chain): 0 candidates consumes the module with nothing granted; 1 grants it
        /// outright (there is nothing to pick between) but still opens THE RIG with the family reveal
        /// playing, exactly as a multi-candidate pick does once resolved (MV-521, MV-595) — the reveal
        /// is the reward, so a single candidate is never a silent grant; 2(-3) opens THE RIG with just
        /// those candidates lit on the board — numbered, TAKE-labelled — and everything else dimmed.
        /// MV-521: a multi-candidate tap resolves the pick IN PLACE (<see cref="ResolveDraftPick"/>) —
        /// it no longer closes the screen; the player does that themselves once they're done looking at
        /// what just unlocked. A shed's own draw is a locked CATEGORY id (MV-457, now MV-595's single
        /// next-in-order id, not a 2-of-N sample) — <paramref name="candidateIds"/> takes either a
        /// category or ability id shape unchanged, since <see cref="GrantDraftCandidate"/> and the
        /// board's own <c>_categoryNodes</c>/<c>_abilityNodes</c> lookups both key off the same disjoint
        /// id namespaces (all-caps category ids vs lowercase ability ids). Whichever candidate is left
        /// behind in a 2+ draw simply stays locked/unowned for a later module.</summary>
        public void OpenMorphingModuleDraft(string[] candidateIds)
        {
            if (candidateIds == null || candidateIds.Length == 0)
            {
                Debug.Log("[WeaponsScreen] Morphing Module consumed — nothing left to offer.");
                return;
            }
            if (candidateIds.Length == 1)
            {
                string id = candidateIds[0];
                string categoryId = RigBoard.Exists(id) ? RigBoard.Category(id) : id;
                GrantDraftCandidate(id);

                if (_canvas == null) Build();
                if (!_open)
                {
                    _open = true;
                    _prevTimeScale = TimeScaleCapture.ClampForCapture(Time.timeScale);
                    Time.timeScale = 0f;
                    ModalFrameRateGate.Enter();   // MV-574: idle the frame rate while this modal is up
                }

                Refresh();
                _screenRoot.gameObject.SetActive(true);
                StartCeremony(categoryId);
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
                ModalFrameRateGate.Enter();   // MV-574: idle the frame rate while this modal is up
            }

            Refresh();
            _screenRoot.gameObject.SetActive(true);
        }

        /// <summary>MV-1090: resolves a banked Rack Module (<see cref="PendingMorphingModule.RackModulePending"/>)
        /// the moment THE RIG opens — unlocks SECONDARY and grants its root at level 1, both at once
        /// (unlike <see cref="GrantDraftCandidate"/>, which only ever does one or the other), then plays
        /// the exact same family reveal <see cref="StartCeremony"/> gives a Device's 1-candidate grant.
        /// The grant happens immediately, same as every other draft path — the ceremony only gates what
        /// the player can SEE/SPEND while it plays, never when the grant itself lands.
        ///
        /// MV-1289: grants whichever SECONDARY is actually equipped, not always the Shoulder Rack's own
        /// <c>s_rkt</c> — a chosen loadout can equip a different SECONDARY than World 2's native row.</summary>
        private void OpenRackModuleReveal()
        {
            PendingMorphingModule.TakeRackModule();
            RigState.UnlockCategory("SECONDARY");
            // MV-435: every RIG grant routes through WeaponSystemState so Changed fires — granting
            // straight off the model layer here would leave the HUD never told to refresh.
            WeaponSystemState.AcquireEquippedSecondaryDefaults();

            if (_canvas == null) Build();
            if (!_open)
            {
                _open = true;
                _prevTimeScale = TimeScaleCapture.ClampForCapture(Time.timeScale);
                Time.timeScale = 0f;
                ModalFrameRateGate.Enter();   // MV-574: idle the frame rate while this modal is up
            }

            Refresh();
            _screenRoot.gameObject.SetActive(true);
            StartCeremony("SECONDARY");
        }

        /// <summary>MV-1248: resolves a banked Secondary family reveal
        /// (<see cref="PendingMorphingModule.SecondaryFamilyPending"/>, World 5's 2nd Assembly Cradle
        /// destroyed) — unlocks SECONDARY and grants every <c>s_orb</c> node (ORBS/REACH/POWER/SPIN) at
        /// level 1, since Orbitals is "always on once owned, nothing to press" (<c>OrbitalsTrackKind</c>'s
        /// own class doc) rather than a single root grant the player levels up themselves the way
        /// <see cref="OpenRackModuleReveal"/>'s default grant is. Same ceremony shape otherwise.
        ///
        /// MV-1289: grants whichever SECONDARY is actually equipped, not always Orbitals — a chosen
        /// loadout can equip a different SECONDARY than World 5's native row.</summary>
        private void OpenSecondaryFamilyReveal()
        {
            PendingMorphingModule.TakeSecondaryFamily();
            RigState.UnlockCategory("SECONDARY");
            WeaponSystemState.AcquireEquippedSecondaryDefaults();

            if (_canvas == null) Build();
            if (!_open)
            {
                _open = true;
                _prevTimeScale = TimeScaleCapture.ClampForCapture(Time.timeScale);
                Time.timeScale = 0f;
                ModalFrameRateGate.Enter();
            }

            Refresh();
            _screenRoot.gameObject.SetActive(true);
            StartCeremony("SECONDARY");
        }

        /// <summary>MV-1248: resolves a banked Sentinel family reveal
        /// (<see cref="PendingMorphingModule.SentinelFamilyPending"/>, World 5's 4th Assembly Cradle
        /// destroyed) — unlocks SUPPORT and grants <c>u_sen</c> at level 1 for free, the same
        /// "unlock + root grant" shape <see cref="OpenRackModuleReveal"/> uses for SECONDARY/<c>s_rkt</c>.
        /// MV-1095's own remembered-levels restore fires for free inside <see cref="RigState.UnlockCategory"/>
        /// itself if this family was ever owned before.</summary>
        private void OpenSentinelFamilyReveal()
        {
            PendingMorphingModule.TakeSentinelFamily();
            RigState.UnlockCategory("SUPPORT");
            WeaponSystemState.AcquireById("u_sen");

            if (_canvas == null) Build();
            if (!_open)
            {
                _open = true;
                _prevTimeScale = TimeScaleCapture.ClampForCapture(Time.timeScale);
                Time.timeScale = 0f;
                ModalFrameRateGate.Enter();
            }

            Refresh();
            _screenRoot.gameObject.SetActive(true);
            StartCeremony("SUPPORT");
        }

        // ------------------------------------------------------------------ live state

        /// <summary>Redraws the CELLS bank and every node's visual state off <see cref="RigState"/> and
        /// <see cref="PickupWallet"/> — so a spend, a shed pickup, or a draft acquire (once MV-424 lands)
        /// while this screen happens to be open reflects immediately. MV-519: cells are now the only
        /// currency — the old SUPERCELLS tray is gone (a Supercell grants its cells on pickup, never
        /// banked here) — so this only ever redraws the one chip.</summary>
        private void Refresh()
        {
            if (_root == null) return;

            RefreshTopBarState();
            ApplyBoardScale();
            RefreshBoardState();
        }

        /// <summary>The PARTS chip's live text/colour/interactable state (MV-1408: split out of
        /// <see cref="Refresh"/> so a top-bar rebuild at a new resolution can repaint it).</summary>
        private void RefreshTopBarState()
        {
            if (_cellsText == null) return;
            _cellsText.text = $"{PickupWallet.PowerCells}/{PickupWallet.Capacity} PARTS";
            // MV-519 Change item 5: an over-cap balance must read as a deliberate bonus, not a bug —
            // tinted the same amber a Supercell glows, mirroring HudController.SetCellCountDisplay's
            // treatment of the gameplay HUD's own cell readout, so the two never disagree.
            _cellsText.color = PickupWallet.IsOverCapacity ? SupercellColor : CellsColor;

            // MV-458: e_cel's chip is actionable (and tappable) whenever cells could pay for whichever
            // action (unlock/upgrade) e_cel is currently in. MV-515 dropped the old "or a Supercell is
            // banked" leniency — unlocking no longer accepts a Supercell in place of cells.
            bool cellsOwned = RigState.IsOwned("e_cel");
            bool capacityActionable = cellsOwned
                ? PickupWallet.PowerCellCapacityLevel < PickupWallet.PowerCellCapacityMaxLevel
                : RigState.IsCellUnlockable("e_cel");
            int capacityCostCells = cellsOwned ? CellSpend.UpgradeCostFor("e_cel", RigState.Level("e_cel")) : CellSpend.UnlockCostFor("e_cel");
            bool capacitySpendable = capacityActionable && PickupWallet.PowerCells >= capacityCostCells;
            _cellsChipButton.interactable = capacitySpendable;
            // MV-446 defect 1: was tinting the BG SupercellColor/amber when a capacity level-up is
            // affordable — against the border/text's own colours.sec cyan (never touched here) that put
            // two near-equal-luminance colours on top of each other, ~1.15:1 contrast, making the
            // player's own cell count unreadable. The chip stays tappable via .interactable; readability
            // wins over the spend affordance.
            _cellsChipBg.color = RowColor;
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
            RefreshRail();
            RefreshDetail();

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
                // MV-462 defect 3: a connector is wholly inside one family (both ends share ab.Category)
                // — familyLit (has >=1 owned ability) still drives which of the two LIVE/DIM alphas a
                // connector picks (a genuinely different question — "is there real traffic here" —
                // from whether the family is dimmed at all).
                bool familyLit = CategoryHasOwnedAbility(ab.Category);
                // MV-538: the DIM SCOPE itself (as opposed to the live/dim alpha choice above) must
                // track the category's own unlock, not ownership — see RefreshAbilityNode's own
                // categoryUnlocked comment for the full defect. A shed-unlocked-but-still-empty family's
                // connectors must render at full strength, same as RefreshCategoryNode/RefreshAbilityNode.
                bool categoryUnlocked = RigState.IsCategoryUnlocked(ab.Category);

                if (string.IsNullOrEmpty(ab.Parent))
                {
                    if (!_connectors.TryGetValue($"conn:cat:{ab.Category}>{ab.Id}", out var img)) continue;
                    bool live = familyLit || draftable;
                    img.color = DimIfUnlit(new Color(family.r, family.g, family.b, live ? RigBoardLayout.ConnectorAlphaLive : RigBoardLayout.ConnectorAlphaDim), categoryUnlocked);
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
                    bool parentGated = !owned && !draftable && categoryUnlocked;
                    bool live = owned || draftable;
                    Color lineColor = parentGated
                        ? new Color(module.r, module.g, module.b, RigBoardLayout.ConnectorAlphaLive)
                        : new Color(family.r, family.g, family.b, live ? RigBoardLayout.ConnectorAlphaLive : RigBoardLayout.ConnectorAlphaDim);
                    img.color = DimIfUnlit(lineColor, categoryUnlocked);
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
            SyncConnectorExtras();
        }

        private static bool CategoryHasOwnedAbility(string categoryId)
        {
            foreach (var ab in RigBoardLayout.Abilities)
                if (ab.Category == categoryId && RigState.IsOwned(ab.Id)) return true;
            return false;
        }

        /// <summary>MV-538 (was MV-462 defect 3's "FamilyLitForAbility"): is <paramref name="abilityId"/>'s
        /// own category shed-unlocked? <see cref="Update"/>'s per-frame pulse only has the ability id
        /// to hand (from <c>_abilityNodes</c>'s own key), so this re-derives the category the same way
        /// <see cref="RefreshAbilityNode"/> does at refresh time. Renamed from the old
        /// CategoryHasOwnedAbility-backed version: the dim must track whether a shed has opened the
        /// category, not whether anything in it is owned yet — see RefreshAbilityNode's own
        /// categoryUnlocked comment for the full defect.</summary>
        private static bool CategoryUnlockedForAbility(string abilityId)
        {
            foreach (var ab in RigBoardLayout.Abilities)
                if (ab.Id == abilityId) return RigState.IsCategoryUnlocked(ab.Category);
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

            int n = Mathf.Min(_draftCandidateIds.Count, _draftChips.Count);
            const float gap = 0.03f, pad = 0.05f;
            float chipW = (1f - 2f * pad - gap * (n - 1)) / Mathf.Max(1, n);
            for (int i = 0; i < _draftChips.Count; i++)
            {
                var chip = _draftChips[i];
                chip.Root.gameObject.SetActive(i < n);
                if (i >= n) continue;
                string id = _draftCandidateIds[i];
                chip.Root.name = "Draft Candidate " + id;
                float x0 = pad + i * (chipW + gap);
                Anchor(chip.Root, new Vector2(x0, 0.08f), new Vector2(x0 + chipW, 0.40f), new Vector2(0.5f, 0.5f));
                chip.Root.offsetMin = Vector2.zero; chip.Root.offsetMax = Vector2.zero;
                string cat = RigBoard.Exists(id) ? RigBoard.Category(id) : id;
                Color family = RigBoardLayout.Colour(RigBoardLayout.CategoryFamily(cat));
                chip.Bg.color = new Color(family.r * 0.55f, family.g * 0.55f, family.b * 0.55f, 1f);
                chip.Label.text = $"{i + 1}  " + (RigBoard.Exists(id) ? AbilityLabel(id) : id);
                chip.Button.onClick.RemoveAllListeners();
                string pick = id;
                chip.Button.onClick.AddListener(() => OnRigNodeTapped(pick));
            }
            _draftBand.SetAsLastSibling();

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
            // MV-538: every graphic below now dims off this SAME `lit` flag — MV-462's separate "has an
            // owned ability" dim (CategoryHasOwnedAbility) was the actual bug: a shed-unlocked-but-still-
            // empty family rendered its whole column at FamilyDimFactor even though its root nodes were
            // already unlockable and affordable. A family is only ever dim while genuinely LOCKED now.
            bool lit = RigState.IsCategoryUnlocked(cat.Id);
            Color family = RigBoardLayout.Colour(cat.Family);
            Color ink = RigBoardLayout.Colour("ink");

            if (_categoryPanels.TryGetValue(cat.Id, out var panel))
                panel.color = DimIfUnlit(new Color(family.r, family.g, family.b, lit ? RigBoardLayout.RegionOpacityLit : RigBoardLayout.RegionOpacityDark), lit);
            if (_categoryPanelBorders.TryGetValue(cat.Id, out var border))
                border.color = DimIfUnlit(new Color(family.r, family.g, family.b, lit ? RigBoardLayout.RegionBorderAlphaLit : RigBoardLayout.RegionBorderAlphaDark), lit);

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
                v.HexFill.color = DimIfUnlit(new Color(family.r, family.g, family.b, 0.12f), lit);
                v.HexOutline.color = DimIfUnlit(new Color(family.r, family.g, family.b, 0.55f), lit);
                v.Glow.gameObject.SetActive(false);
                v.OuterRing.gameObject.SetActive(false);
                v.Icon.color = DimIfUnlit(new Color(family.r, family.g, family.b, 0.85f), lit);
            }

            v.PillText.text = $"{owned}/{total}";
            v.PillBg.color = DimIfUnlit(PillBackdrop, lit);
            v.PillBorder.color = DimIfUnlit(new Color(family.r, family.g, family.b, lit ? 0.95f : 0.3f), lit);
            v.PillText.color = DimIfUnlit(lit ? family : new Color(family.r, family.g, family.b, 0.7f), lit);
            v.Label.color = DimIfUnlit(new Color(ink.r, ink.g, ink.b, 0.62f), lit);
            v.Label.text = CategoryHeaderText(cat.Id, out string slotLine);
            if (v.SlotLabel != null)
            {
                v.SlotLabel.gameObject.SetActive(slotLine != null);
                if (slotLine != null)
                {
                    v.SlotLabel.text = slotLine;
                    v.SlotLabel.color = v.Label.color;
                }
            }
        }

        /// <summary>MV-1391: the weapon column headers (PRIMARY / SECONDARY) are two lines — a small slot
        /// line and a larger line naming the equipped weapon, read from <see cref="WeaponCatalog"/>
        /// (never a literal). SECONDARY reads "?" while <see cref="RigState.SecondaryLocked"/>. Every
        /// other category keeps its plain id and no slot line (<paramref name="slotLine"/> null).</summary>
        private static string CategoryHeaderText(string categoryId, out string slotLine)
        {
            slotLine = null;
            if (categoryId == "PRIMARY")
            {
                slotLine = categoryId;
                return WeaponCatalog.DisplayName(WeaponSystemState.ActivePrimary);
            }
            if (categoryId == "SECONDARY")
            {
                slotLine = categoryId;
                return RigState.SecondaryLocked ? "?" : WeaponCatalog.DisplayName(WeaponSystemState.SecondaryKind);
            }
            return categoryId;
        }

        /// <summary>MV-1391: test-only access to a category header's slot line (null if it has none).</summary>
        public TextMeshProUGUI CategorySlotLabel(string id) =>
            _categoryNodes.TryGetValue(id, out var v) && v.SlotLabel != null && v.SlotLabel.gameObject.activeSelf ? v.SlotLabel : null;

        /// <summary>MV-1391: test-only access to a category header's name line.</summary>
        public TextMeshProUGUI CategoryNameLabel(string id) => _categoryNodes.TryGetValue(id, out var v) ? v.Label : null;

        /// <summary>MV-689: rebuilds the whole board from scratch against whichever world
        /// <see cref="RigBoard.ActiveWorldIndex"/> currently reads — needed because a Weapon Core morph
        /// changes PRIMARY/SECONDARY's own ability ids (not just their levels), so the node graph
        /// <see cref="BuildBoardContent"/> already built for the old board has no entries for the new
        /// ids at all. Mirrors the exact rebuild <see cref="ApplyBoardScale(float)"/> already does on a
        /// phone/standard verdict change. Public: the ui-screens capture harness drives this directly
        /// (no scene-level "world changed" event exists yet to call it automatically).</summary>
        public void RebuildBoard()
        {
            RigBoardLayout.UseWorld(RigBoard.ActiveWorldIndex);
            _lastBoardWorldIndex = RigBoard.ActiveWorldIndex;
            DestroyBoardContent();
            BuildBoardContent();
            RefreshBoardState();
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
            v.PillBg.gameObject.SetActive(true);   // MV-1410: a locked node hides its pill, so every refresh starts from visible
            v.PillBorder.gameObject.SetActive(true);
            if (v.NormalIcon != null) v.Icon.sprite = v.NormalIcon;

            bool owned = RigState.IsOwned(ab.Id);
            bool maxed = owned && RigState.Level(ab.Id) >= ab.MaxLevel;
            // MV-458: "draftable" now means cell-unlockable — its category open and, for a non-root
            // node, its parent at level >= 2 (RigState.IsCellUnlockable, tighter than the IsReached the
            // now production-dead ability-level Morphing Module draft pool still uses).
            bool draftable = RigState.IsCellUnlockable(ab.Id) && !owned;
            // MV-538: the dim scope for every DimIfUnlit call below — was CategoryHasOwnedAbility (has
            // >=1 owned ability), which meant a shed-unlocked-but-still-empty family rendered its own
            // draftable/parent-gated/locked nodes at FamilyDimFactor even though the root was already
            // unlockable and affordable. A node's family is only ever dim while genuinely LOCKED now.
            bool categoryUnlocked = RigState.IsCategoryUnlocked(ab.Category);
            // MV-470: the ticket's second lock reason — category already open (so the node IS reached
            // the old way) but the parent hasn't hit level 2 yet, the tighter cell-unlock gate. Distinct
            // from the deeper "family not unlocked" lock below; see RefreshConnectors for the matching
            // connector-line tell.
            bool parentGated = !owned && !draftable && categoryUnlocked;
            int cellsBanked = PickupWallet.PowerCells;
            // MV-520: the live ring/progress read stays scoped to a node you can actually act on right
            // now (draftable, or owned-and-below-max) — unchanged from before this ticket.
            bool hasLiveCellAction = draftable || (owned && !maxed);
            // MV-520: the PRICE, unlike the ring, is never gated — every node that will ever cost cells
            // shows one, including family-locked and parent-gated nodes. Only owned-and-maxed has
            // nothing left to buy.
            bool hasCostToShow = !(owned && maxed);
            // MV-605 AC4: a family still mid-ceremony must not be spendable yet, even though the grant
            // itself already landed — see IsCeremonySuppressing's own doc comment.
            bool spendable = IsAbilityNodeSpendable(ab.Id, cellsBanked) && !IsCeremonySuppressing(ab.Category);
            // MV-470: whether CELLS alone would pay for this node's action right now — drives the
            // afford-dot (CapMarker) and, via Update(), whether the dashed ring pulses live or sits inert.
            bool cellAffordable = CellSpend.IsCellActionAffordable(ab.Id, cellsBanked);

            Color family = RigBoardLayout.Colour(RigBoardLayout.CategoryFamily(ab.Category));
            Color module = RigBoardLayout.Colour("module");
            Color ink = RigBoardLayout.Colour("ink");

            // MV-470: the dashed affordance ring and its cap-marker dot now mark ANY live cell action —
            // an owned node's upgrade as well as an unowned node's unlock — not just draftable, so a
            // player sitting on affordable cells sees the same "something's waiting" tell everywhere one
            // applies. CapMarker's own presence (rather than a flat colour) IS the "affordable now" read;
            // Update() only ever pulses it while cellAffordable stays true.
            // MV-1440: three independent signals. STATE = fill / outline / icon (locked, available = hollow,
            // owned = solid, maxed = solid + gold outline); AFFORDABILITY = green badge + green price on available /
            // owned nodes only; SELECTION = the corner brackets (BuildSelectionRing) + a white name. No dashed
            // outline, glow, ring, dot or teal outline remains for state (MV-1424 already removed the rings).
            v.OuterRing.gameObject.SetActive(false);
            v.CapMarker.gameObject.SetActive(false);
            v.ProgressRing.gameObject.SetActive(false);
            v.ProgressTrack.gameObject.SetActive(false);
            v.CostIcon.gameObject.SetActive(false);
            v.CostText.gameObject.SetActive(false);
            v.Glow.gameObject.SetActive(false);

            bool selected = ab.Id == _selectedNodeId;
            bool afford = NodeAffordable(ab.Id) && !IsCeremonySuppressing(ab.Category);
            Color light = Color.Lerp(family, Color.white, 0.45f);
            string priceHex = HexStr(afford ? AffordGreen : PriceGrey);
            Color nameColour = selected ? Color.white : new Color(0.70f, 0.73f, 0.80f, 1f);   // white only on the selected node
            bool isSlt = ab.Id == "u_slt";

            if (owned)
            {
                v.Symbol = maxed ? RigNodeSymbol.Maxed : RigNodeSymbol.Owned;
                v.HexFill.color = family;
                v.HexOutline.sprite = HexOutlineSprite(v.Radius, maxed ? OutlineMaxed : OutlineAvailable);
                v.HexOutline.color = maxed ? GoldColor : family;
                int lvl = RigState.Level(ab.Id);
                // MV-654: u_slt's own pill reads deployable sentinels, not the raw node level.
                v.PillText.text = isSlt
                    ? $"{AbilityTuning.SentinelDeploymentSlots(lvl)}/{AbilityTuning.SentinelDeploymentSlots(RigBoard.MaxLevel(ab.Id))}"
                    : maxed ? $"<color=#{HexStr(GoldColor)}>MAX</color>"
                    : $"{lvl} / {ab.MaxLevel} \u00B7 <color=#{priceHex}>{CellSpend.UpgradeCostFor(ab.Id, lvl)}</color>";
                if (isSlt) RefreshSentinelPriceLine(v);
                v.PillBg.color = PillBackdrop;
                v.PillBorder.color = new Color(family.r, family.g, family.b, 0.95f);
                v.PillText.color = isSlt ? ink : SubGrey;
                v.Label.text = ab.Label;
                v.Label.color = nameColour;
                v.Icon.color = Color.white;
            }
            else if (draftable)
            {
                v.Symbol = RigNodeSymbol.Available;
                v.HexFill.color = new Color(0.05f, 0.04f, 0.09f, 1f);
                v.HexOutline.sprite = HexOutlineSprite(v.Radius, OutlineAvailable);
                v.HexOutline.color = light;
                v.PillText.text = isSlt
                    ? $"{AbilityTuning.SentinelDeploymentSlots(RigState.Level(ab.Id))}/{AbilityTuning.SentinelDeploymentSlots(RigBoard.MaxLevel(ab.Id))}"
                    : $"NEW \u00B7 <color=#{priceHex}>{CellSpend.PotentialCellCost(ab.Id)}</color>";
                if (isSlt) RefreshSentinelPriceLine(v);
                v.PillBg.color = PillBackdrop;
                v.PillBorder.color = light;
                v.PillText.color = isSlt ? light : SubGrey;
                v.Label.text = ab.Label;
                v.Label.color = nameColour;
                v.Icon.color = light;
            }
            else   // LOCKED - its parent is not bought yet (or its whole family is still closed)
            {
                v.Symbol = RigNodeSymbol.Locked;
                v.HexFill.color = LockedFill;
                v.HexOutline.sprite = HexOutlineSprite(v.Radius, OutlineLocked);
                v.HexOutline.color = LockedOutline;
                v.PillText.text = "";
                v.PillBg.gameObject.SetActive(false);   // MV-1410: locked = a padlock, no pill, no price
                v.PillBorder.gameObject.SetActive(false);
                v.Icon.sprite = v.LockIcon;
                v.Label.text = "";
                v.Label.color = nameColour;
                v.Icon.color = new Color(LockedOutline.r * 1.6f, LockedOutline.g * 1.6f, LockedOutline.b * 1.6f, 1f);
            }
            v.AffordBadge.gameObject.SetActive(afford);


            // MV-516: the base (pre-pulse) alpha Update() pulses the hex FILL/OUTLINE around — captured
            // after every branch above has set its own final colour, so this always matches whichever
            // state just rendered, never a stale value from a prior Refresh().
            v.FillBaseAlpha = v.HexFill.color.a;
            v.OutlineBaseAlpha = v.HexOutline.color.a;

            v.Button.interactable = spendable;
        }

        /// <summary>MV-1117 (Lee, 6 Oct 2026 device playtest): u_slt is "the add circle" where Lee reads
        /// how many sentinels he can field, so the deploy PRICE belongs right there too — a number that
        /// never had anywhere clear to live before (the HUD button only ever showed "NO PARTS", never a
        /// figure). Parts colour (<see cref="CellsColor"/>) when <see cref="PickupWallet.PowerCells"/>
        /// covers <see cref="PlayerAbilities.SentinelCost"/> right now, destructive-red
        /// (<see cref="QuitColor"/>) when it doesn't — called from both the owned and draftable branches
        /// above (u_slt's price never goes away just because the node itself is maxed or not yet
        /// bought).</summary>
        private void RefreshSentinelPriceLine(RigNodeVisual v)
        {
            int price = PlayerAbilities.SentinelCost;
            Color priceColor = PickupWallet.PowerCells >= price ? CellsColor : QuitColor;

            v.SentinelPriceIcon.gameObject.SetActive(true);
            v.SentinelPriceIcon.color = priceColor;
            v.SentinelPriceText.gameObject.SetActive(true);
            v.SentinelPriceText.text = price.ToString();
            v.SentinelPriceText.color = priceColor;
        }

        /// <summary>A FORGE fusion diamond: faint, but always naming the real fusion and stating its
        /// two required categories (flagging whichever the player already has) and its part cost
        /// while locked (MV-729 — the four bare <c>? ? ?</c> diamonds read as broken furniture, not a
        /// legible goal), then amber with its real name and cost/slot once eligible — independent of
        /// the currently-banked CELLS count (MV-423.png vs -noparts.png) — and a stronger solid amber
        /// once actually forged, matching an owned ability's own "solid, no longer a prospect" read.
        /// MV-515: cost converted from parts to cells.</summary>
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
                v.Sub.text = $"{fusion.CellCost} PARTS · SLOT {fusion.HudSlot}";
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
                // MV-729: the real name, not "? ? ?" — a player who hasn't met the unlock condition
                // still deserves to know what they're working toward, same as the requirement/cost below.
                v.Label.text = fusion.Label;
                v.Label.color = Dim;
                v.Sub.text = LockedFusionRequirementText(fusion);
                var ink = RigBoardLayout.Colour("ink");
                v.Sub.color = new Color(ink.r, ink.g, ink.b, 0.22f);
                v.Sub.fontSize = Mathf.RoundToInt(FusionSubFontSize);
                v.Button.interactable = false;
            }
        }

        /// <summary>MV-729: a locked fusion's plain-words unlock requirement — which two categories
        /// must be lit (flagging whichever the player already has, per <see cref="RigFusionState.CategoryLit"/>)
        /// and the part cost — replacing the old bare "PARENTA + PARENTB" line that named the
        /// categories but never said which was already met or what forging would cost. Two lines,
        /// not one: the worst-case category pairing (PRIMARY + SECONDARY) plus a HAVE flag and the
        /// cost measured 344px against the sub-label's 280px box on a single line, bleeding into the
        /// neighbouring diamond's own text — categories and cost read separately instead.</summary>
        private static string LockedFusionRequirementText(RigFusionLayout fusion)
        {
            string a = RigFusionState.CategoryLit(fusion.ParentA) ? $"{fusion.ParentA} (HAVE)" : fusion.ParentA;
            string b = RigFusionState.CategoryLit(fusion.ParentB) ? $"{fusion.ParentB} (HAVE)" : fusion.ParentB;
            return $"{a} + {b}\n{fusion.CellCost} PARTS";
        }

        /// <summary>MV-731: <see cref="RigBoardLayout"/>'s column-layout pass places each fusion at the
        /// midpoint of its two parent categories, which can land two adjacent fusions' X far closer
        /// together than <c>rig_board.json</c>'s raw authored x values suggest (their raw x is only a
        /// fallback for a fusion with an unrecognised parent — see <c>BuildColumnLayout</c>). Root cause:
        /// the sub-label box was a flat <see cref="FusionSubLabelMaxWidth"/> regardless of that resolved
        /// spacing. This clamps the box to the resolved nearest-neighbour gap (minus <see
        /// cref="FusionSubLabelGapMargin"/> of clear space) whenever that is tighter than the design
        /// width, so two adjacent boxes can never overlap no matter how the layout pass happens to space
        /// that particular pair. <paramref name="allFusions"/> must be the same aspect-correct list
        /// (<see cref="Fusions"/>) the caller is iterating, not the unconditional standard-aspect one.</summary>
        private float FusionSubLabelWidth(RigFusionLayout fusion, IReadOnlyList<RigFusionLayout> allFusions)
        {
            // MV-1410: gaps are read off the tree layout's real positions, not the board JSON's x.
            if (!_treeSpots.TryGetValue(fusion.Id, out var me)) return FusionSubLabelMaxWidth;
            float nearestGap = float.MaxValue;
            foreach (var other in allFusions)
            {
                if (other.Id == fusion.Id || !_treeSpots.TryGetValue(other.Id, out var o)) continue;
                float dx = Mathf.Abs(o.X - me.X);
                if (dx < nearestGap) nearestGap = dx;
            }
            if (nearestGap == float.MaxValue) return FusionSubLabelMaxWidth;
            return Mathf.Clamp(nearestGap - FusionSubLabelGapMargin, FusionSubLabelMinWidth, FusionSubLabelMaxWidth);
        }

        /// <summary>MV-443 defect 8: locked fusion diamond border, 2px — distinct from the eligible/
        /// forged states' shared <see cref="SolidPolygonOutlineSprite"/> (strokeOwned, 4px).</summary>
        private Sprite LockedFusionOutlineSprite(float r)
        {
            float w = r * 2f, h = r * 2f;
            return CrispUi.PolygonOutline(FusionSides, FusionRotationDeg, Mathf.CeilToInt(w), Mathf.CeilToInt(h), 2f);
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

        /// <summary>MV-1411: a node tap SELECTS the node and nothing else - buying moved to the detail
        /// panel's UPGRADE/UNLOCK button (<see cref="OnDetailUpgradePressed"/>). The one exception is a
        /// live Morphing Module draft, where a candidate tap is the pick itself.</summary>
        private void OnNodeTapped(string id)
        {
            if (_draftActive) { OnRigNodeTapped(id); return; }
            SelectNode(id);
        }

        /// <summary>MV-1411: a transparent always-live tap target over a node. The node's own button is
        /// only interactable while the node is spendable (MV-516), but selecting must work on every node.</summary>
        private void AddSelectHit(RigNodeVisual shell, string id)
        {
            var hit = AddImage(shell.Root, CrispUi.Solid(), Color.clear, "Select Hit");
            Stretch(hit.rectTransform);
            hit.raycastTarget = true;
            var b = hit.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => OnNodeTapped(id));
        }

        /// <summary>MV-515: an owned node tries <see cref="CellSpend.TryUpgradeNode"/>; an unowned node
        /// tries <see cref="CellSpend.TryUnlockNode"/> — both cells-only now. A fusion tries
        /// <see cref="PartSpend.TrySpendOnFusion"/>, also cells (converted from parts by this
        /// ticket).</summary>
        private void OnRigNodeTapped(string id)
        {
            if (_draftActive)
            {
                if (_draftCandidateIds.Contains(id)) ResolveDraftPick(id);
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

        /// <summary>MV-521: resolves a draft pick IN PLACE — the screen used to <see cref="Close"/>
        /// here, throwing the player back into the fight at exactly the moment there was most new
        /// material to look at (a category pick unlocks a whole family, MV-457). Now: grant, drop the
        /// draft state, start the just-unlocked family's reveal, and either chain straight into another
        /// banked module (<see cref="PendingMorphingModule"/>) or fall back to the board's ordinary
        /// spendable state — never closing the screen ourselves. The player's own CLOSE/QUIT controls
        /// are the only things that still dismiss it.</summary>
        private void ResolveDraftPick(string id)
        {
            string categoryId = RigBoard.Exists(id) ? RigBoard.Category(id) : id;

            GrantDraftCandidate(id);
            _draftActive = false;
            _draftCandidateIds.Clear();

            StartCeremony(categoryId);

            // OpenMorphingModuleDraft already does exactly "arm the next draft in place" when the screen
            // is already open (its own !_open guard skips the pause/timescale work) — reusing it here is
            // what makes chaining work instead of duplicating its 0/1/2+ candidate handling.
            if (PendingMorphingModule.HasPending) OpenMorphingModuleDraft(PendingMorphingModule.Take());

            Refresh();
        }

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
            scaler.referenceResolution = new Vector2(CanvasRefW, CanvasRefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;   // MV-1408

            // MV-440: one screen root, full-canvas, holding both the opaque backdrop and the Safe
            // Area — this is the only GameObject Open()/Close()/OpenMorphingModuleDraft() ever toggle,
            // so the backdrop (built as its first child, below) can never again go up without the rest
            // of the screen, or outlive it. _root stays permanently active from here on.
            _screenRoot = NewRect("Screen Root", _canvas.transform, Vector2.zero, Vector2.one);
            Stretch(_screenRoot);

            // MV-433: opaque, first child of the screen root (not Safe Area) so it sits behind the
            // top bar too and ignores the safe-area inset — the board draws over live gameplay
            // otherwise, which is what washed every family colour out against the lawn.
            _background = AddImage(_screenRoot, CrispUi.Solid(), OpaqueBase(), "Background");
            Stretch(_background.rectTransform);
            _background.raycastTarget = false;

            _safeRoot = NewRect("Safe Area", _screenRoot, Vector2.zero, Vector2.one);
            Stretch(_safeRoot);
            _safeRoot.gameObject.AddComponent<SafeArea>();

            _root = new GameObject("Weapons Root", typeof(RectTransform));
            var rootRt = (RectTransform)_root.transform;
            rootRt.SetParent(_safeRoot, false);
            Stretch(rootRt);

            _screenScrim = AddImage(rootRt, CrispUi.Solid(), Scrim, "Scrim");
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
            // MV-1408: centre-anchored (not top-anchored) so its position never depends on the live
            // canvas size — ApplyBoardScale places it from the computed canvas size.
            _boardScaleRoot = NewRect("Board Scale Root", rootRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
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
            float initialAspect = Screen.height > 0 ? (float)Screen.width / Screen.height : CanvasRefW / CanvasRefH;
            _phoneMode = IsPhoneLayout(initialAspect);
            // MV-1398/MV-1408: seed the shape resolution from the same ambient guess, so the first build
            // already lands in the bucket ApplyBoardScale will re-derive (no throwaway double build).
            UpdateSafeSize(initialAspect);
            int initialPixelH = Screen.height > 0 ? Screen.height : (int)CanvasRefH;
            ComputeBoardMetrics(initialAspect, initialPixelH, out _, out int seedRes, out int seedBarRes);
            _shapeRes = seedRes;
            _barRes = seedBarRes;
            CrispUi.Resolution = _shapeRes;
            // MV-753: BuildBoardContent below reads straight off RigBoardLayout, which must already
            // match whatever world RigBoard is on — e.g. a Weapon Core morph already applied at run
            // start, before THE RIG was ever opened — same sync ApplyBoardScale(float) re-checks below.
            RigBoardLayout.UseWorld(RigBoard.ActiveWorldIndex);
            _lastBoardWorldIndex = RigBoard.ActiveWorldIndex;
            BuildBoardContent();

            CrispUi.Resolution = _barRes;
            BuildTopBar(rootRt);   // drawn after the board so it sits above it in the hierarchy
            CrispUi.Resolution = _shapeRes;

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
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : CanvasRefW / CanvasRefH;
            ApplyBoardScale(aspect, Screen.height);
        }

        /// <summary>Applies the scale-to-fit factor for an explicit aspect ratio, bypassing the ambient
        /// <see cref="Screen"/> singleton entirely (MV-462 defect 2) — <c>UiScreensDirector</c> drives
        /// this with its actual capture-target aspect the same way <see cref="Screen"/>-based
        /// <c>ComputeScaleFactor</c> already bypasses <c>Screen</c> for the CanvasScaler. Public so both
        /// the capture harness and an EditMode test can drive it without a real screen/window.</summary>
        public void ApplyBoardScale(float aspect) => ApplyBoardScale(aspect, Screen.height);

        /// <summary>MV-1408: the RIG canvas is 852x393 with <c>ScreenMatchMode.Expand</c>, so its size in
        /// canvas units for a screen of <paramref name="aspect"/> is 393 tall (wider screens) or 852 wide
        /// (taller screens). Pure.</summary>
        public static void ComputeCanvasSize(float aspect, out float width, out float height)
        {
            float refAspect = CanvasRefW / CanvasRefH;
            if (aspect <= 0f) aspect = refAspect;
            if (aspect >= refAspect) { height = CanvasRefH; width = CanvasRefH * aspect; }
            else { width = CanvasRefW; height = CanvasRefW / aspect; }
        }

        /// <summary>MV-1408: the live safe area (canvas units) the top bar and board are laid out in,
        /// computed from the aspect rather than read off a rendered rect (an un-laid-out canvas reports
        /// ~0 under the EditMode runner). Everything in THE RIG is centre-anchored inside the safe root
        /// and sized from these two numbers, so the layout is the same wherever it is measured.</summary>
        private float _safeW = CanvasRefW, _safeH = CanvasRefH;
        private int _barRes = 1;

        /// <summary>MV-1408: the safe area size last laid out, test/conformance-only.</summary>
        public Vector2 SafeSize => new Vector2(_safeW, _safeH);

        private void UpdateSafeSize(float aspect)
        {
            ComputeCanvasSize(aspect, out float cw, out float ch);
            _safeW = cw * SafeArea.CurrentSafeWidthFraction();
            _safeH = ch * SafeArea.CurrentSafeHeightFraction();
        }

        /// <summary>MV-1408: where the board content (the masked viewport the category columns live in)
        /// sits in the 1920x1080 board frame, and how big it is. Nothing scrolls: the viewport is exactly
        /// as tall as the content, and the whole thing is scaled to fit by <see cref="ComputeBoardFit"/>.
        /// <paramref name="top"/> is the frame y of the viewport's top edge; <paramref name="contentOffsetY"/>
        /// is how far above that edge the content's own origin (authored y = 0) sits.</summary>
        private static void ComputeBoardViewport(bool phone, out float left, out float top, out float width, out float height, out float contentHeight, out float contentOffsetY)
        {
            // MV-1410: one fixed viewport, wide and tall enough for any aspect; the rail and tree are laid
            // out in frame units from the live safe size (ComputeTreeFrame), so nothing here depends on mode.
            left = ViewLeft; top = 0f; width = ViewWidth; height = ViewHeight;
            contentHeight = ViewHeight; contentOffsetY = 0f;
        }

        /// <summary>MV-1408: uniform scale that fits the whole board viewport into
        /// <paramref name="availW"/> x <paramref name="availH"/> canvas units (the area below the top bar).</summary>
        private static float ComputeBoardFit(bool phone, float availW, float availH) => TreeScale;

        /// <summary>MV-1408: where board-content coordinates (the authored json x, y) land on the canvas for a
        /// screen of <paramref name="aspect"/>, in canvas units measured from the canvas's top-left:
        /// <c>(originX + x * scale, originY + y * scale)</c>. Pure; the conformance harness builds its
        /// json-to-pixel transform from this so its probes follow the layout instead of re-deriving it.</summary>
        public static void ComputeBoardMapping(float aspect, out float scale, out float originX, out float originY)
        {
            bool phone = IsPhoneLayout(aspect);
            ComputeCanvasSize(aspect, out float cw, out float ch);
            float safeW = cw * SafeArea.CurrentSafeWidthFraction();
            float safeH = ch * SafeArea.CurrentSafeHeightFraction();
            scale = TreeScale;
            // The frame's x = 960 (centre) sits on the canvas centre; its y = 0 on the top bar's bottom edge.
            originX = cw * 0.5f - RefW * 0.5f * scale;
            originY = (ch - safeH) * 0.5f + TopBarHeight;
        }

        /// <summary>MV-1408/MV-1398: board fit scale plus the two shape-resolution buckets — the board's
        /// (canvas scale x fit) and the top bar's (canvas scale alone) — for a render target of
        /// <paramref name="aspect"/> and <paramref name="pixelHeight"/>.</summary>
        private void ComputeBoardMetrics(float aspect, int pixelHeight, out float fit, out int boardRes, out int barRes)
        {
            bool phone = IsPhoneLayout(aspect);
            fit = ComputeBoardFit(phone, _safeW, _safeH - TopBarHeight);
            float canvasScale = pixelHeight > 0
                ? Mathf.Min(aspect * pixelHeight / CanvasRefW, pixelHeight / CanvasRefH)
                : 1f;
            boardRes = CrispUi.BucketFor(canvasScale * fit);
            barRes = CrispUi.BucketFor(canvasScale);
        }

        /// <summary>MV-1398: <see cref="ApplyBoardScale(float)"/> with the render target's real pixel
        /// height, so the shape sprites are generated at the true device-pixel scale (the canvas scale is
        /// <c>min(width/852, height/393)</c>) instead of whatever the ambient <see cref="Screen"/>
        /// reports. The capture harness and tests pass their own.
        ///
        /// MV-1408: also lays out the top bar and fits the board below it (see
        /// <see cref="ComputeBoardFit"/>) — no scrolling, nothing hidden.</summary>
        public void ApplyBoardScale(float aspect, int pixelHeight)
        {
            if (_boardScaleRoot == null) return;

            bool wantPhoneMode = IsPhoneLayout(aspect);
            // MV-753: RigBoard is THE RIG's single source of truth for which world's ids even exist —
            // a change here (a Weapon Core morph landing) must force the exact same rebuild a
            // phone/standard verdict change does, checked on every call.
            bool worldChanged = _lastBoardWorldIndex != RigBoard.ActiveWorldIndex;

            UpdateSafeSize(aspect);
            ComputeBoardMetrics(aspect, pixelHeight, out float fit, out int wantRes, out int wantBarRes);
            bool resChanged = wantRes != _shapeRes;
            bool barResChanged = wantBarRes != _barRes || _topBar == null;
            _shapeRes = wantRes;
            _barRes = wantBarRes;

            if (barResChanged)
            {
                if (_topBar != null)
                {
                    if (Application.isPlaying) Destroy(_topBar.gameObject);
                    else DestroyImmediate(_topBar.gameObject);
                }
                CrispUi.Resolution = wantBarRes;
                BuildTopBar((RectTransform)_root.transform);
                RefreshTopBarState();
            }
            CrispUi.Resolution = wantRes;

            // MV-1410: the rail and tree are laid out from the live safe size, so a new size re-lays them out.
            bool safeChanged = !Mathf.Approximately(_builtSafeW, _safeW) || !Mathf.Approximately(_builtSafeH, _safeH);
            if (wantPhoneMode != _phoneMode || worldChanged || resChanged || safeChanged || _nodeParent == null)
            {
                _phoneMode = wantPhoneMode;
                RigBoardLayout.UseWorld(RigBoard.ActiveWorldIndex);
                _lastBoardWorldIndex = RigBoard.ActiveWorldIndex;
                DestroyBoardContent();
                BuildBoardContent();
                RefreshBoardState();
            }

            LayoutTopBar();

            // The board's viewport top edge lands on the top bar's bottom edge; the fit scale keeps the
            // rest of it inside the safe area. The wrapper is centre-anchored, so only its own offset
            // from the safe area's centre matters.
            ComputeBoardViewport(_phoneMode, out _, out float vTop, out _, out _, out _, out _);
            _boardScaleRoot.localScale = new Vector3(fit, fit, 1f);
            _boardScaleRoot.anchoredPosition = new Vector2(0f, _safeH * 0.5f - TopBarHeight + vTop * fit);
        }

        /// <summary>MV-472: builds everything under <see cref="_boardContentHost"/> — the category
        /// panels/connectors/FORGE section/nodes/draft chrome, off whichever geometry <see cref="_phoneMode"/>
        /// currently selects. Called once from <see cref="Build"/> and again from
        /// <see cref="ApplyBoardScale(float)"/> whenever the phone/standard verdict changes.</summary>
        private void BuildBoardContent()
        {
            _nodeParent = BuildBoardViewport(_boardRoot, _phoneMode, out _boardContentHost);
            _builtSafeW = _safeW; _builtSafeH = _safeH;

            ComputeTreeLayouts();                // MV-1410: every category's tree, laid out from parent links
            BuildTreePanel(_nodeParent);         // the tree panel, its header and the category rail
            BuildConnectors(_nodeParent);
            BuildForgeSection(_nodeParent);
            foreach (var cat in Categories) _categoryNodes[cat.Id] = BuildCategoryNode(_nodeParent, cat);
            foreach (var ab in Abilities) _abilityNodes[ab.Id] = BuildAbilityNode(_nodeParent, ab);
            BuildSelectionRing(_nodeParent);     // MV-1411: above the nodes, below the draft chrome

            BuildDraftScrim(_nodeParent);   // MV-424: last board child so it dims everything built above,
            BuildDraftBand(_nodeParent);    // then the draft nodes come back on top of it (RefreshMorphingModuleDraft)
            BuildCeremonyVisuals(_nodeParent);   // MV-605: built last too, so it renders above everything else
            BuildQuitDialog();                   // MV-1415: over the whole RIG, top bar included
            ApplyTreeSelection();
        }

        private float _builtSafeW = -1f, _builtSafeH = -1f;

        /// <summary>MV-1408: the node parent for both modes — a masked, NON-scrolling viewport exactly as
        /// big as the content (see <see cref="ComputeBoardViewport"/>), so every node is reachable without
        /// a ScrollRect and there is no scroll hint. The viewport starts below the empty band the old top
        /// bar used to occupy; its child "Board Content" keeps the authored coordinate origin, so every
        /// node's json (x, y) still maps 1:1 onto its anchoredPosition. A distinct child of
        /// <see cref="_boardRoot"/> purely so <see cref="DestroyBoardContent"/> has something of its own to
        /// destroy on a rebuild without touching _boardRoot or any ancestor.</summary>
        private RectTransform BuildBoardViewport(RectTransform boardRoot, bool phone, out RectTransform host)
        {
            ComputeBoardViewport(phone, out float vx, out float vy, out float vw, out float vh, out float contentH, out float contentOffsetY);

            var viewport = NewRect("Board Viewport", boardRoot, new Vector2(0f, 1f), new Vector2(0f, 1f));
            viewport.pivot = new Vector2(0f, 1f);
            viewport.anchoredPosition = new Vector2(vx, -vy);
            viewport.sizeDelta = new Vector2(vw, vh);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = NewRect("Board Content", viewport, new Vector2(0f, 1f), new Vector2(0f, 1f));
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = new Vector2(-vx, contentOffsetY);
            content.sizeDelta = new Vector2(RefW, contentH);

            host = viewport;
            return content;
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
            _connectorExtras.Clear();
            _railTabs.Clear();
            _railOrder.Clear();
            _treePanel = null; _treePanelBorder = null; _treeSlotText = null; _treeNameText = null;
            _detailPanel = null; _detailPath = null; _detailName = null; _detailEffect = null; _detailValue = null; _detailLevel = null;
            _detailUpgrade = null; _detailUpgradeBg = null; _detailUpgradeText = null; _selectionRing = null; _detailPips.Clear();
            for (int i = 0; i < 3; i++) { _costLabel[i] = null; _costValue[i] = null; _costIcon[i] = null; }
            if (_quitDialog != null)
            {
                if (Application.isPlaying) Destroy(_quitDialog.gameObject);
                else DestroyImmediate(_quitDialog.gameObject);
            }
            _quitDialog = null; _quitRailButton = null; _quitConfirmButton = null; _quitCancelButton = null; _quitScrimButton = null;
            _draftScrim = null;
            _draftBand = null;
            _draftBandTitle = null;
            _draftBandSubtitle = null;
            _draftBandReason = null;
            _ceremonyActive = false;
            _ceremonyCategoryId = null;
            _ceremonyChildIds.Clear();
            _ceremonyStartUnscaledTime = float.NegativeInfinity;
            _ceremonyScrim = null;
            _ceremonyCategoryGlow = null;
            _ceremonyCategoryGlowImage = null;
            _ceremonyCategoryLabelOverlay = null;
            _ceremonyChildGlows.Clear();
            _ceremonySkipCatcher = null;
        }

        // ------------------------------------------------------------------ MV-1410: tree panel + rail

        private sealed class RailTab
        {
            public string Id, Family;
            public RectTransform Root;
            public Image Bg, Border, Hex, Icon, BarTrack, BarFill, Padlock, Badge;   // MV-1440: Badge = green can-afford arrow
            public TextMeshProUGUI Top, Name, Count;
            public Button Button;
            public CanvasGroup Group;
        }

        /// <summary>MV-1415: what a rail tab shows, resolved from live state - the bar fraction is owned nodes over
        /// the category's nodes; a category is MAX once every node is at its cap; a locked one has no bar.</summary>
        public readonly struct RailTabState
        {
            public readonly bool Locked, Maxed;
            public readonly float Fraction;
            public readonly int Owned, Total;
            public RailTabState(bool locked, bool maxed, float fraction, int owned, int total)
            { Locked = locked; Maxed = maxed; Fraction = fraction; Owned = owned; Total = total; }
        }

        /// <summary>MV-1415: the state a rail tab is showing for <paramref name="id"/>.</summary>
        public RailTabState ComputeRailTabState(string id)
        {
            bool forge = id == ForgeTabId;
            int owned = 0, total = 0;
            bool allMax = true;
            if (forge)
            {
                foreach (var fu in Fusions) { total++; if (RigFusionState.IsForged(fu.Id)) owned++; else allMax = false; }
            }
            else
            {
                foreach (var ab in Abilities)
                {
                    if (ab.Category != id) continue;
                    total++;
                    if (RigState.IsOwned(ab.Id)) owned++;
                    int max = Mathf.Max(1, RigBoard.Exists(ab.Id) ? RigBoard.MaxLevel(ab.Id) : ab.MaxLevel);
                    if (RigState.Level(ab.Id) < max) allMax = false;
                }
            }
            bool locked = !forge && !RigState.IsCategoryUnlocked(id);
            bool maxed = !locked && total > 0 && allMax;
            return new RailTabState(locked, maxed, total > 0 ? Mathf.Clamp01((float)owned / total) : 0f, owned, total);
        }

        /// <summary>MV-1415: test access to a rail tab's resolved parts.</summary>
        public Image RailTabBarTrack(string id) => _railTabs.TryGetValue(id, out var t) ? t.BarTrack : null;
        public Image RailTabBarFill(string id) => _railTabs.TryGetValue(id, out var t) ? t.BarFill : null;
        public Image RailTabPadlock(string id) => _railTabs.TryGetValue(id, out var t) ? t.Padlock : null;
        public TextMeshProUGUI RailTabTopText(string id) => _railTabs.TryGetValue(id, out var t) ? t.Top : null;
        public TextMeshProUGUI RailTabNameText(string id) => _railTabs.TryGetValue(id, out var t) ? t.Name : null;
        public TextMeshProUGUI RailTabCountText(string id) => _railTabs.TryGetValue(id, out var t) ? t.Count : null;
        public float RailTabAlpha(string id) => _railTabs.TryGetValue(id, out var t) && t.Group != null ? t.Group.alpha : 1f;

        private readonly Dictionary<string, RailTab> _railTabs = new Dictionary<string, RailTab>();
        private readonly List<string> _railOrder = new List<string>();
        private Image _treePanel, _treePanelBorder;
        private TextMeshProUGUI _treeSlotText, _treeNameText;
        private readonly Dictionary<string, List<Image>> _connectorExtras = new Dictionary<string, List<Image>>();

        /// <summary>MV-1410: the tree panel's rect in board frame units - test/conformance access.</summary>
        public Rect TreePanelRect => _treeFrame.Panel;

        /// <summary>MV-1410: the category currently shown in the tree panel (a rail tab id, or FORGE).</summary>
        public string SelectedCategoryId => _selectedCategory;

        /// <summary>MV-1410: the rail's tab ids, in board order.</summary>
        public IReadOnlyList<string> RailTabIds => _railOrder;

        /// <summary>MV-1410: a rail tab's rect in board frame units (see <see cref="TreeScale"/>).</summary>
        public Rect RailTabRect(string id)
        {
            int i = _railOrder.IndexOf(id);
            return i >= 0 && _treeFrame.Tabs != null && i < _treeFrame.Tabs.Length ? _treeFrame.Tabs[i] : default;
        }

        /// <summary>MV-1410: a rail tab's tap button.</summary>
        public Button RailTabButton(string id) => _railTabs.TryGetValue(id, out var t) ? t.Button : null;

        /// <summary>MV-1410: the ability/fusion ids shown in the tree right now (the selected category's nodes).</summary>
        public List<string> VisibleTreeNodeIds()
        {
            var ids = new List<string>();
            foreach (var kv in _treeSpots) if (kv.Value.Category == _selectedCategory) ids.Add(kv.Key);
            return ids;
        }

        /// <summary>MV-1410: an ability's parent id in its tree (null for a root, and for a fusion).</summary>
        public string NodeParentId(string id)
        {
            foreach (var ab in Abilities) if (ab.Id == id) return ab.Parent;
            return null;
        }

        /// <summary>MV-1410: a tree node's tap rect in board frame units, or default if unknown.</summary>
        public Rect TreeNodeRect(string id) =>
            _treeSpots.TryGetValue(id, out var s) ? new Rect(s.X - s.R, s.Y - s.R, s.R * 2f, s.R * 2f) : default;

        /// <summary>MV-1410: the visible tree's abilities with their tree-layout positions (board frame units) -
        /// what the capture harness probes, in place of the board JSON's x/y.</summary>
        public List<RigAbilityLayout> TreeAbilityLayouts()
        {
            var list = new List<RigAbilityLayout>();
            foreach (var ab in Abilities)
                if (_treeSpots.TryGetValue(ab.Id, out var s) && s.Category == _selectedCategory)
                    list.Add(new RigAbilityLayout(ab.Id, ab.Category, ab.Icon, ab.Label, ab.Kind, ab.Parent, s.X, s.Y, ab.MaxLevel, ab.Effect));
            return list;
        }

        /// <summary>MV-1410: a child node's radius in frame units (a root is <see cref="TreeRootRadiusValue"/>).</summary>
        public static float TreeChildRadiusValue => ChildRadius;
        public static float TreeRootRadiusValue => RootRadius;

        /// <summary>MV-1410: shows <paramref name="categoryId"/>'s tree (a rail tab id, or FORGE).</summary>
        public void SelectCategory(string categoryId)
        {
            if (!_railOrder.Contains(categoryId) || categoryId == _selectedCategory) return;
            if (_draftActive || _ceremonyActive) return;   // MV-1407: the modal / reveal owns the tree until it ends
            _selectedCategory = categoryId;
            _selectedNodeId = RootNodeOf(categoryId);
            foreach (var nb in Abilities) RefreshAbilityNode(nb);   // MV-1440: the selected node's name turns white
            ApplyTreeSelection();
            RefreshRail();
            RefreshDetail();
        }

        /// <summary>MV-1411: the first parentless node of <paramref name="categoryId"/>'s tree (its root),
        /// or the first fusion for FORGE; null if the tree is empty.</summary>
        private string RootNodeOf(string categoryId)
        {
            if (categoryId == null) return null;
            if (categoryId == ForgeTabId) { foreach (var fu in Fusions) return fu.Id; return null; }
            foreach (var ab in Abilities) if (ab.Category == categoryId && string.IsNullOrEmpty(ab.Parent)) return ab.Id;
            return null;
        }

        /// <summary>MV-1411: the node whose details the detail panel shows (null if none).</summary>
        public string SelectedNodeId => _selectedNodeId;

        /// <summary>MV-1411: selects a node of the currently shown tree. Never spends anything.</summary>
        public void SelectNode(string id)
        {
            if (id == null || !_treeSpots.TryGetValue(id, out var spot) || spot.Category != _selectedCategory) return;
            _selectedNodeId = id;
            foreach (var nb in Abilities) RefreshAbilityNode(nb);   // MV-1440: the selected node's name turns white
            RefreshRail();
            RefreshDetail();
        }

        private const float LegendH = 44f;

        /// <summary>MV-1440: one line along the bottom of the tree panel - SELECTED, CAN AFFORD, OWNED, AVAILABLE,
        /// MAXED, LOCKED - each with its icon drawn exactly as on the nodes (same sprites, colours, strokes).</summary>
        private void BuildLegend(RectTransform parent)
        {
            var p = _treeFrame.Panel;
            string[] names = { "SELECTED", "CAN AFFORD", "OWNED", "AVAILABLE", "MAXED", "LOCKED" };
            int chars = 0; foreach (var n in names) chars += n.Length;
            float avail = p.width - 56f;
            float font = Mathf.Clamp((avail - 5f * 12f - 6f * 12f) / (0.78f * chars + 6f * 1.5f), 10f, 24f);
            float icon = font * 1.5f, gap = 12f, cy = p.yMax - LegendH * 0.5f - 6f;
            Color fam = new Color(0.58f, 0.38f, 0.95f);
            Color light = Color.Lerp(fam, Color.white, 0.45f);
            float x = p.x + 28f;
            for (int i = 0; i < names.Length; i++)
            {
                var cell = new Rect(x, cy - icon * 0.5f, icon, icon);
                int hw = Mathf.CeilToInt(icon * 0.866f), hh = Mathf.CeilToInt(icon);
                var holder = NewRect("Legend " + names[i], parent, new Vector2(0f, 1f), new Vector2(0f, 1f));
                PlaceTopLeft(holder, cell);
                RectTransform at(string n, float w, float h) { var im = AddImage(holder, null, Color.white, n); Anchor(im.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)); im.rectTransform.sizeDelta = new Vector2(w, h); im.raycastTarget = false; return im.rectTransform; }
                switch (i)
                {
                    case 0:
                        for (int k = 0; k < 4; k++)
                        {
                            var corner = new Vector2(k & 1, (k >> 1) & 1);
                            foreach (var size in new[] { new Vector2(icon * 0.4f, 3f), new Vector2(3f, icon * 0.4f) })
                            {
                                var bar = AddImage(holder, CrispUi.Solid(), Color.white, "Bar");
                                Anchor(bar.rectTransform, corner, corner, corner);
                                bar.rectTransform.sizeDelta = size; bar.rectTransform.anchoredPosition = Vector2.zero; bar.raycastTarget = false;
                            }
                        }
                        break;
                    case 1:
                        var b = AddAffordBadge(holder, Vector2.zero, icon);
                        break;
                    default:
                        Color fill = i == 2 || i == 4 ? fam : i == 3 ? new Color(0.05f, 0.04f, 0.09f, 1f) : LockedFill;
                        Color line = i == 2 ? fam : i == 3 ? light : i == 4 ? GoldColor : LockedOutline;
                        float stroke = i == 4 ? 3f : 2.5f;
                        var f = at("Fill", hw, hh).GetComponent<Image>(); f.sprite = CrispUi.Polygon(HexSides, HexRotationDeg, hw, hh); f.color = fill;
                        var o = at("Outline", hw, hh).GetComponent<Image>(); o.sprite = CrispUi.PolygonOutline(HexSides, HexRotationDeg, hw, hh, stroke); o.color = line;
                        break;
                }
                var t = AddText(parent, Mathf.RoundToInt(font), Color.white, TextAnchor.MiddleLeft);
                t.text = names[i]; t.fontStyle = FontStyles.Bold; t.raycastTarget = false;
                t.textWrappingMode = TextWrappingModes.NoWrap; t.color = new Color(0.78f, 0.80f, 0.86f, 1f);
                float tw = names[i].Length * font * 0.78f + 6f;
                PlaceTopLeft(t.rectTransform, new Rect(x + icon + 6f, cy - font, tw, font * 2f));
                x += icon + 6f + tw + gap;
            }
        }

        private void BuildTreePanel(RectTransform parent)
        {
            _railTabs.Clear();
            _railOrder.Clear();
            var ids = RailIds();
            _railOrder.AddRange(ids);

            var p = _treeFrame.Panel;
            float cornerFraction = Mathf.Clamp(RigBoardLayout.RegionRectRadius * 2f / (Mathf.Min(p.width, p.height) * 0.5f), 0.05f, 0.5f);
            _treePanel = AddImage(parent, CrispUi.RoundedBox(64, cornerFraction), Color.white, "Tree Panel");
            PlaceTopLeft(_treePanel.rectTransform, p);
            _treePanel.type = Image.Type.Sliced;
            _treePanel.raycastTarget = false;
            _treePanelBorder = AddImage(parent, CrispUi.RoundedBoxOutline(64, cornerFraction, 3f), Color.white, "Tree Panel Border");
            PlaceTopLeft(_treePanelBorder.rectTransform, p);
            _treePanelBorder.type = Image.Type.Sliced;
            _treePanelBorder.raycastTarget = false;

            _treeSlotText = AddText(parent, 26, Color.white, TextAnchor.MiddleLeft);
            PlaceTopLeft(_treeSlotText.rectTransform, new Rect(p.x + 28f, p.y + 10f, p.width - 56f, 30f));
            _treeSlotText.fontStyle = FontStyles.Bold;
            _treeSlotText.raycastTarget = false;
            _treeNameText = AddText(parent, 44, TextColor, TextAnchor.MiddleLeft);
            PlaceTopLeft(_treeNameText.rectTransform, new Rect(p.x + 28f, p.y + 38f, p.width - 56f, 50f));
            _treeNameText.fontStyle = FontStyles.Bold;
            _treeNameText.enableAutoSizing = true;
            _treeNameText.fontSizeMin = 22f; _treeNameText.fontSizeMax = 44f;
            _treeNameText.textWrappingMode = TextWrappingModes.Normal;
            _treeNameText.overflowMode = TextOverflowModes.Truncate;
            _treeNameText.raycastTarget = false;

            for (int i = 0; i < ids.Count; i++) _railTabs[ids[i]] = BuildRailTab(parent, ids[i], _treeFrame.Tabs[i]);
            BuildLegend(parent);   // MV-1440
            BuildRailQuit(parent);
            BuildDetailPanel(parent);
        }

        // ------------------------------------------------------------------ MV-1415: QUIT TO MENU + confirm

        private Button _quitRailButton, _quitConfirmButton, _quitCancelButton, _quitScrimButton;
        private RectTransform _quitDialog;

        /// <summary>MV-1415: the rail's QUIT TO MENU button, its rect in board frame units, and the confirm dialog.</summary>
        public Button QuitRailButton => _quitRailButton;
        public Rect QuitRailRect => _treeFrame.Quit;
        public bool QuitDialogOpen => _quitDialog != null && _quitDialog.gameObject.activeSelf;
        public Button QuitConfirmButton => _quitConfirmButton;
        public Button QuitCancelButton => _quitCancelButton;
        public Button QuitScrimButton => _quitScrimButton;

        public void OpenQuitDialog()
        {
            if (_quitDialog == null) return;
            _quitDialog.gameObject.SetActive(true);
            _quitDialog.SetAsLastSibling();
        }

        public void CloseQuitDialog()
        {
            if (_quitDialog != null) _quitDialog.gameObject.SetActive(false);
        }

        private void ConfirmQuit()
        {
            CloseQuitDialog();
            RunFlow.QuitToMenu();
        }

        private static readonly Color QuitText = new Color(1f, 0.38f, 0.40f);

        private void BuildRailQuit(RectTransform parent)
        {
            var r = _treeFrame.Quit;
            float corner = Mathf.Clamp(24f / (r.height * 0.5f), 0.05f, 0.5f);
            var bg = AddImage(parent, CrispUi.RoundedBox(64, corner), new Color(0.07f, 0.04f, 0.05f, 1f), "Quit Rail Button");
            PlaceTopLeft(bg.rectTransform, r);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;
            _quitRailButton = bg.gameObject.AddComponent<Button>();
            _quitRailButton.transition = Selectable.Transition.None;
            _quitRailButton.onClick.AddListener(OpenQuitDialog);   // never quits on its own: the dialog's QUIT does

            var border = AddImage(bg.rectTransform, CrispUi.RoundedBoxOutline(64, corner, 3f), QuitColor, "Border");
            Stretch(border.rectTransform);
            border.type = Image.Type.Sliced;
            border.raycastTarget = false;

            // power icon: a broken ring with a bar through the gap, in the same centred-stroke style as the other icons
            var arc = new System.Text.StringBuilder("<path d=\"M");
            for (int i = 0; i <= 10; i++)
            {
                float a = Mathf.Deg2Rad * (-55f + 290f * i / 10f);
                string x = (Mathf.Sin(a) * 11f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                string y = (-Mathf.Cos(a) * 11f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                arc.Append(i == 0 ? $"{x},{y}" : $" L{x},{y}");
            }
            arc.Append("\" fill=\"none\" stroke=\"#ICON#\" stroke-width=\"3\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/><path d=\"M0,-14 L0,-2\" stroke=\"#ICON#\" stroke-width=\"3\" stroke-linecap=\"round\"/>");
            int iconSize = 40;
            var icon = AddImage(bg.rectTransform, CrispUi.VectorIcon(arc.ToString(), iconSize), QuitText, "Power Icon");
            Anchor(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
            icon.rectTransform.anchoredPosition = new Vector2(34f, 0f);
            icon.raycastTarget = false;

            var label = AddText(bg.rectTransform, 30, QuitText, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(34f + iconSize + 8f, 4f);
            label.rectTransform.offsetMax = new Vector2(-12f, -4f);
            label.text = QuitLabel;
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = true;
            label.fontSizeMin = 14f; label.fontSizeMax = 30f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
        }

        /// <summary>MV-1415: the small "Quit to menu?" dialog over the RIG, in canvas units under the safe root
        /// (above the top bar). QUIT is red and small; CANCEL is the default - larger and more prominent. A tap on the
        /// scrim outside the dialog cancels.</summary>
        private void BuildQuitDialog()
        {
            var parent = (RectTransform)_root.transform;
            _quitDialog = NewRect("Quit Dialog", parent, Vector2.zero, Vector2.one);
            Stretch(_quitDialog);

            var scrim = AddImage(_quitDialog, CrispUi.Solid(), new Color(0f, 0f, 0f, 0.62f), "Quit Scrim");
            Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;
            _quitScrimButton = scrim.gameObject.AddComponent<Button>();
            _quitScrimButton.transition = Selectable.Transition.None;
            _quitScrimButton.onClick.AddListener(CloseQuitDialog);

            var panel = AddImage(_quitDialog, CrispUi.RoundedBox(64, 0.12f), new Color(0.075f, 0.085f, 0.11f, 1f), "Quit Panel");
            Anchor(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            panel.rectTransform.sizeDelta = new Vector2(300f, 130f);
            panel.rectTransform.anchoredPosition = Vector2.zero;
            panel.type = Image.Type.Sliced;
            panel.raycastTarget = true;   // taps on the dialog itself are absorbed, never read as "outside"
            var panelBorder = AddImage(panel.rectTransform, CrispUi.RoundedBoxOutline(64, 0.12f, 2f), new Color(1f, 1f, 1f, 0.18f), "Border");
            Stretch(panelBorder.rectTransform);
            panelBorder.type = Image.Type.Sliced;
            panelBorder.raycastTarget = false;

            var title = AddText(panel.rectTransform, 20, TextColor, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            title.rectTransform.sizeDelta = new Vector2(280f, 36f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -16f);
            title.text = "Quit to menu?";
            title.fontStyle = FontStyles.Bold;
            title.raycastTarget = false;

            _quitConfirmButton = DialogButton(panel.rectTransform, "Quit Confirm Button", "QUIT", new Vector2(-72f, -26f), new Vector2(100f, 34f),
                QuitColor, Color.white, 16);
            _quitConfirmButton.onClick.AddListener(ConfirmQuit);
            _quitCancelButton = DialogButton(panel.rectTransform, "Quit Cancel Button", "CANCEL", new Vector2(68f, -26f), new Vector2(136f, 44f),
                new Color(0.86f, 0.89f, 0.94f, 1f), new Color(0.07f, 0.08f, 0.11f, 1f), 20);
            _quitCancelButton.onClick.AddListener(CloseQuitDialog);

            _quitDialog.gameObject.SetActive(false);
        }

        private static Button DialogButton(RectTransform parent, string name, string text, Vector2 pos, Vector2 size, Color fill, Color ink, int font)
        {
            var bg = AddImage(parent, CrispUi.RoundedBox(32, 0.3f), fill, name);
            Anchor(bg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            bg.rectTransform.sizeDelta = size;
            bg.rectTransform.anchoredPosition = pos;
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;
            var b = bg.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            var t = AddText(bg.rectTransform, font, ink, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform);
            t.text = text;
            t.fontStyle = FontStyles.Bold;
            t.raycastTarget = false;
            return b;
        }

        // ------------------------------------------------------------------ MV-1411: detail panel

        private Image _detailPanel, _detailUpgradeBg, _selectionRing;
        private TextMeshProUGUI _detailPath, _detailName, _detailEffect, _detailValue, _detailLevel, _detailUpgradeText;
        private Button _detailUpgrade;
        private readonly List<Image> _detailPips = new List<Image>();
        // MV-1424: the cost block - three rows (Cost / You have / Need), label left, parts icon + value right.
        private readonly TextMeshProUGUI[] _costLabel = new TextMeshProUGUI[3], _costValue = new TextMeshProUGUI[3];
        private readonly Image[] _costIcon = new Image[3];
        private const float DetailPad = 20f, DetailButtonH = 110f, CostRowH = 40f;

        /// <summary>MV-1411: test access - the detail panel's name line, level line, effect line (empty
        /// until MV-1400), UPGRADE/UNLOCK/MAXED button and its label.</summary>
        public TextMeshProUGUI DetailNameText => _detailName;
        public TextMeshProUGUI DetailLevelText => _detailLevel;
        public TextMeshProUGUI DetailEffectText => _detailEffect;
        /// <summary>MV-1415: the effect's value line - current -> next in a larger weight, on its own line.</summary>
        public TextMeshProUGUI DetailValueText => _detailValue;
        public TextMeshProUGUI DetailPathText => _detailPath;
        public Button DetailUpgradeButton => _detailUpgrade;
        /// <summary>MV-1424: test access - cost block row <paramref name="row"/> (0 Cost, 1 You have, 2 Need): its label and value text, or null while the row is hidden.</summary>
        public string DetailCostRowLabel(int row) => _costLabel[row] != null && _costLabel[row].gameObject.activeSelf ? _costLabel[row].text : null;
        public string DetailCostRowValue(int row) => _costValue[row] != null && _costValue[row].gameObject.activeSelf ? _costValue[row].text : null;
        /// <summary>MV-1424: the selected node's white outline, null when nothing is selected.</summary>
        public Image SelectionOutline => _selectionRing != null && _selectionRing.gameObject.activeSelf ? _selectionRing : null;
        /// <summary>MV-1424: the active ring / circle / dot images under tree nodes (progress track, progress ring, outer ring, cap marker) - empty when every node has one outline only.</summary>
        public List<string> NodeStackedOutlineNames()
        {
            var found = new List<string>();
            foreach (var kv in _abilityNodes)
                foreach (var img in kv.Value.Root.GetComponentsInChildren<Image>(false))
                    if (img.name == "Progress Track" || img.name == "Progress Ring" || img.name == "Outer Ring" || img.name == "Cap Marker")
                        found.Add(kv.Key + "/" + img.name);
            return found;
        }
        public TextMeshProUGUI DetailUpgradeText => _detailUpgradeText;

        private void BuildDetailPanel(RectTransform parent)
        {
            var d = _treeFrame.Detail;
            float cornerFraction = Mathf.Clamp(RigBoardLayout.RegionRectRadius * 2f / (Mathf.Min(d.width, d.height) * 0.5f), 0.05f, 0.5f);
            _detailPanel = AddImage(parent, CrispUi.RoundedBox(64, cornerFraction), new Color(0.075f, 0.085f, 0.11f, 1f), "Detail Panel");
            PlaceTopLeft(_detailPanel.rectTransform, d);
            _detailPanel.type = Image.Type.Sliced;
            _detailPanel.raycastTarget = false;
            var border = AddImage(_detailPanel.rectTransform, CrispUi.RoundedBoxOutline(64, cornerFraction, 3f), new Color(1f, 1f, 1f, 0.16f), "Border");
            Stretch(border.rectTransform);
            border.type = Image.Type.Sliced;
            border.raycastTarget = false;

            float inner = d.width - 2f * DetailPad;
            var tr = _detailPanel.rectTransform;
            _detailPath = AddText(tr, 24, Dim, TextAnchor.MiddleLeft);
            PlaceTopLeft(_detailPath.rectTransform, new Rect(DetailPad, 16f, inner, 30f));
            _detailPath.fontStyle = FontStyles.Bold;
            _detailPath.enableAutoSizing = true;
            _detailPath.fontSizeMin = 14f; _detailPath.fontSizeMax = 24f;
            _detailPath.textWrappingMode = TextWrappingModes.NoWrap;
            _detailPath.overflowMode = TextOverflowModes.Ellipsis;   // a long weapon name shrinks, then truncates, never runs off the panel
            _detailPath.raycastTarget = false;

            _detailName = AddText(tr, 44, TextColor, TextAnchor.MiddleLeft);
            PlaceTopLeft(_detailName.rectTransform, new Rect(DetailPad, 48f, inner, 56f));
            _detailName.fontStyle = FontStyles.Bold;
            _detailName.enableAutoSizing = true;
            _detailName.fontSizeMin = 24f; _detailName.fontSizeMax = 44f;
            _detailName.textWrappingMode = TextWrappingModes.Normal;
            _detailName.overflowMode = TextOverflowModes.Truncate;
            _detailName.raycastTarget = false;

            // The effect line (MV-1400 fills it): its space is reserved, its text stays empty here.
            // MV-1415: the effect NAME on one line, then "current -> next" on its own line in a larger weight
            // (18 pt = 36 frame units). Neither wraps: each shrinks to fit the panel instead.
            _detailEffect = AddText(tr, 26, Dim, TextAnchor.MiddleLeft);
            PlaceTopLeft(_detailEffect.rectTransform, new Rect(DetailPad, 108f, inner, 34f));
            _detailEffect.textWrappingMode = TextWrappingModes.NoWrap;
            _detailEffect.enableAutoSizing = true;
            _detailEffect.fontSizeMin = 14f; _detailEffect.fontSizeMax = 26f;
            _detailEffect.overflowMode = TextOverflowModes.Ellipsis;
            _detailEffect.text = "";
            _detailEffect.raycastTarget = false;

            _detailValue = AddText(tr, 36, TextColor, TextAnchor.MiddleLeft);
            PlaceTopLeft(_detailValue.rectTransform, new Rect(DetailPad, 144f, inner, 48f));
            _detailValue.fontStyle = FontStyles.Bold;
            _detailValue.textWrappingMode = TextWrappingModes.NoWrap;
            _detailValue.enableAutoSizing = true;
            _detailValue.fontSizeMin = 18f; _detailValue.fontSizeMax = 36f;
            _detailValue.overflowMode = TextOverflowModes.Ellipsis;
            _detailValue.richText = true;
            _detailValue.text = "";
            _detailValue.raycastTarget = false;

            int maxPips = 1;
            foreach (var ab in Abilities) maxPips = Mathf.Max(maxPips, RigBoard.Exists(ab.Id) ? RigBoard.MaxLevel(ab.Id) : ab.MaxLevel);
            for (int i = 0; i < maxPips; i++)
            {
                var pip = AddImage(tr, CrispUi.RoundedBox(32, 0.5f), Color.white, "Pip " + (i + 1));
                pip.type = Image.Type.Sliced;
                pip.raycastTarget = false;
                _detailPips.Add(pip);
            }

            _detailLevel = AddText(tr, 28, Dim, TextAnchor.MiddleLeft);
            PlaceTopLeft(_detailLevel.rectTransform, new Rect(DetailPad, 240f, inner, 36f));
            _detailLevel.textWrappingMode = TextWrappingModes.NoWrap;   // MV-1424: "Level n of m · k more to MAX" shrinks to fit, never clips
            _detailLevel.enableAutoSizing = true;
            _detailLevel.fontSizeMin = 14f; _detailLevel.fontSizeMax = 28f;
            _detailLevel.overflowMode = TextOverflowModes.Ellipsis;
            _detailLevel.raycastTarget = false;

            // MV-1424: cost block above the button - 3 rows of CostRowH, label left, parts icon + value right-aligned.
            float rowsTop = d.height - DetailPad - DetailButtonH - 14f - 3f * CostRowH;
            for (int i = 0; i < 3; i++)
            {
                float ry = rowsTop + i * CostRowH;
                _costLabel[i] = AddText(tr, 28, Dim, TextAnchor.MiddleLeft);
                PlaceTopLeft(_costLabel[i].rectTransform, new Rect(DetailPad, ry, inner * 0.5f, CostRowH));
                _costLabel[i].raycastTarget = false;
                _costValue[i] = AddText(tr, 32, GoldColor, TextAnchor.MiddleRight);
                PlaceTopLeft(_costValue[i].rectTransform, new Rect(DetailPad + inner - 150f, ry, 150f, CostRowH));
                _costValue[i].fontStyle = FontStyles.Bold;
                _costValue[i].raycastTarget = false;
                _costIcon[i] = AddImage(tr, WeaponHudIcons.PowerCell(32 * CrispUi.Resolution), GoldColor, "Cost Icon " + (i + 1));
                PlaceTopLeft(_costIcon[i].rectTransform, new Rect(DetailPad + inner - 150f - 34f, ry + (CostRowH - 28f) * 0.5f, 28f, 28f));
                _costIcon[i].raycastTarget = false;
            }

            var btnRect = new Rect(DetailPad, d.height - DetailPad - DetailButtonH, inner, DetailButtonH);
            _detailUpgradeBg = AddImage(tr, CrispUi.RoundedBox(64, 0.3f), Color.white, "Detail Upgrade");
            _detailUpgradeBg.type = Image.Type.Sliced;
            PlaceTopLeft(_detailUpgradeBg.rectTransform, btnRect);
            _detailUpgradeBg.raycastTarget = true;
            _detailUpgrade = _detailUpgradeBg.gameObject.AddComponent<Button>();
            _detailUpgrade.transition = Selectable.Transition.None;
            _detailUpgrade.onClick.AddListener(OnDetailUpgradePressed);
            _detailUpgradeText = AddText(_detailUpgradeBg.rectTransform, 36, new Color(0.07f, 0.05f, 0.12f, 1f), TextAnchor.MiddleCenter);
            Stretch(_detailUpgradeText.rectTransform);
            _detailUpgradeText.fontStyle = FontStyles.Bold;
            _detailUpgradeText.enableAutoSizing = true;
            _detailUpgradeText.fontSizeMin = 20f; _detailUpgradeText.fontSizeMax = 36f;
            _detailUpgradeText.raycastTarget = false;
        }

        private void BuildSelectionRing(RectTransform parent)
        {
            // MV-1440: the selection reticle - four white corner brackets (3 pt stroke) drawn OUTSIDE the hex.
            // The ring itself is an invisible rect sized to the hex's box plus the gap (see RefreshDetail).
            _selectionRing = AddImage(parent, CrispUi.Solid(), Color.clear, "Selection Ring");
            _selectionRing.raycastTarget = false;
            for (int i = 0; i < 4; i++)
            {
                float cx = (i & 1) == 0 ? 0f : 1f, cy = (i & 2) == 0 ? 0f : 1f;
                var corner = new Vector2(cx, cy);
                var group = NewRect("Bracket " + i, _selectionRing.transform, corner, corner);
                group.pivot = corner; group.sizeDelta = new Vector2(BracketLen, BracketLen); group.anchoredPosition = Vector2.zero;
                foreach (var size in new[] { new Vector2(BracketLen, BracketStroke), new Vector2(BracketStroke, BracketLen) })
                {
                    var bar = AddImage(group, CrispUi.Solid(), Color.white, "Bar");
                    Anchor(bar.rectTransform, corner, corner, corner);
                    bar.rectTransform.sizeDelta = size;
                    bar.rectTransform.anchoredPosition = Vector2.zero;
                    bar.raycastTarget = false;
                }
            }
            _selectionRing.gameObject.SetActive(false);
        }

        /// <summary>The cell price the detail button advertises for <paramref name="id"/>: the upgrade
        /// price while owned, else the unlock price (for a node in a locked category, that category's
        /// root node's unlock price - the category itself has no price of its own).</summary>
        private static int DetailPrice(string id, string category)
        {
            if (RigState.IsOwned(id)) return CellSpend.PotentialCellCost(id);
            if (!RigState.IsCategoryUnlocked(category))
                foreach (var ab in RigBoardLayout.PhoneAbilities)
                    if (ab.Category == category && string.IsNullOrEmpty(ab.Parent)) return CellSpend.UnlockCostFor(ab.Id);
            return CellSpend.UnlockCostFor(id);
        }

        /// <summary>MV-1400: the detail panel's effect line - "Damage per disc  12 → 15" (name, then the
        /// gameplay value now and at the next level, MAX at the cap), or the category's unlock text while
        /// its category is still locked. Empty when the node has no effect name or no gameplay read.</summary>
        public static string DetailEffectLine(RigAbilityLayout node)
        {
            if (node == null) return "";
            if (!RigState.IsCategoryUnlocked(node.Category)) return CategoryUnlockText(node.Category);
            if (string.IsNullOrEmpty(node.Effect)) return "";
            int max = Mathf.Max(1, RigBoard.Exists(node.Id) ? RigBoard.MaxLevel(node.Id) : node.MaxLevel);
            string v = RigEffectLines.ValueText(WeaponSystemState.ActivePrimary, WeaponSystemState.SecondaryKind,
                node.Id, RigState.Level(node.Id), max);
            return v == null ? "" : node.Effect + "  " + v;
        }

        /// <summary>The unlock effect text a locked category's nodes show instead of a value.</summary>
        public static string CategoryUnlockText(string category)
        {
            switch (category)
            {
                case "PRIMARY": return "Unlocks your main weapon upgrades";
                case "SECONDARY": return "Unlocks the secondary weapon";
                case "ENERGY": return "Unlocks shield and pickup upgrades";
                case "MOVE": return "Unlocks speed and teleport";
                case "SUPPORT": return "Unlocks the sentinel";
                default: return "";
            }
        }

        private static readonly Color ValueNextGreen = new Color32(0x7C, 0xF5, 0x9A, 0xFF);

        /// <summary>MV-1415: splits <see cref="DetailEffectLine"/>'s "name  now -> next" (or "name  now MAX", or a
        /// bare unlock text) into the name line and the value line - now in white, next in green, MAX in gold.</summary>
        private void SetDetailEffect(string line)
        {
            _detailEffect.text = "";
            _detailValue.text = "";
            if (string.IsNullOrEmpty(line)) return;
            int split = line.IndexOf("  ", StringComparison.Ordinal);
            if (split < 0) { _detailEffect.text = line; return; }
            _detailEffect.text = line.Substring(0, split);
            string value = line.Substring(split + 2);
            string white = ColorUtility.ToHtmlStringRGB(TextColor), green = ColorUtility.ToHtmlStringRGB(ValueNextGreen), gold = ColorUtility.ToHtmlStringRGB(GoldColor);
            int arrow = value.IndexOf('\u2192');
            if (arrow >= 0)
                _detailValue.text = $"<color=#{white}>{value.Substring(0, arrow).TrimEnd()}</color> \u2192 <color=#{green}>{value.Substring(arrow + 1).TrimStart()}</color>";
            else if (value.EndsWith(RigEffectLines.Max, StringComparison.Ordinal))
                _detailValue.text = $"<color=#{white}>{value.Substring(0, value.Length - RigEffectLines.Max.Length).TrimEnd()}</color>  <color=#{gold}>{RigEffectLines.Max}</color>";
            else _detailValue.text = $"<color=#{white}>{value}</color>";
        }

        /// <summary>Repaints the detail panel from the selected node's live state.</summary>
        private void RefreshDetail()
        {
            if (_detailPanel == null) return;
            string id = _selectedNodeId;
            RigAbilityLayout node = null; RigFusionLayout fusion = null;
            if (id != null)
            {
                foreach (var ab in Abilities) if (ab.Id == id) node = ab;
                foreach (var fu in Fusions) if (fu.Id == id) fusion = fu;
            }
            bool has = node != null || fusion != null;
            _detailPath.gameObject.SetActive(has); _detailName.gameObject.SetActive(has);
            _detailLevel.gameObject.SetActive(has); _detailUpgradeBg.gameObject.SetActive(has); _detailEffect.gameObject.SetActive(has); _detailValue.gameObject.SetActive(has);
            if (_selectionRing != null) _selectionRing.gameObject.SetActive(has);
            foreach (var pip in _detailPips) pip.gameObject.SetActive(false);
            for (int i = 0; i < 3; i++)
            {
                _costLabel[i].gameObject.SetActive(false); _costValue[i].gameObject.SetActive(false); _costIcon[i].gameObject.SetActive(false);
            }
            if (!has) return;

            string category = node != null ? node.Category : ForgeTabId;
            string family = category == ForgeTabId ? "supercell" : RigBoardLayout.CategoryFamily(category);
            Color fam = RigBoardLayout.Colour(family);
            string slot = category == ForgeTabId ? "FORGE" : category == "PRIMARY" ? "PRIMARY" : category == "SECONDARY" ? "SECONDARY" : "SYSTEM";
            string catName = category == ForgeTabId ? "FUSIONS" : CategoryHeaderText(category, out _);
            _detailPath.text = (slot == catName ? slot : slot + " · " + catName).ToUpperInvariant();
            _detailPath.color = fam;
            _detailName.text = node != null ? node.Label : fusion.Label;
            SetDetailEffect(node != null ? DetailEffectLine(node) : "");   // MV-1400 / MV-1415

            string buttonText; bool live;
            int costShown = -1;   // MV-1424: the price the cost block shows (-1 = no block: maxed / forged)
            if (node != null)
            {
                int max = Mathf.Max(1, RigBoard.Exists(id) ? RigBoard.MaxLevel(id) : node.MaxLevel), level = RigState.Level(id);
                _detailLevel.text = (RigState.IsOwned(id) && !RigState.CanSpendPart(id)) ? "MAXED" : $"Level {level} of {max} \u00B7 {Mathf.Max(0, max - level)} more to MAX";
                var d = _treeFrame.Detail;
                float inner = d.width - 2f * DetailPad, gap = 8f;
                float w = (inner - gap * (max - 1)) / max;
                for (int i = 0; i < _detailPips.Count; i++)
                {
                    var pip = _detailPips[i];
                    bool show = i < max;
                    pip.gameObject.SetActive(show);
                    if (!show) continue;
                    PlaceTopLeft(pip.rectTransform, new Rect(DetailPad + i * (w + gap), 206f, w, 16f));
                    pip.color = i < level ? fam : new Color(0.17f, 0.19f, 0.26f, 1f);
                }
                bool maxed = RigState.IsOwned(id) && !RigState.CanSpendPart(id);
                int price = DetailPrice(id, category);
                if (maxed) { buttonText = "MAXED"; live = false; }
                else
                {
                    costShown = price;
                    live = !_draftActive && !IsCeremonySuppressingAbility(id) && CellSpend.IsCellActionAffordable(id, PickupWallet.PowerCells);
                    int short_ = price - PickupWallet.PowerCells;
                    buttonText = !live && short_ > 0 ? $"NEED {short_} MORE PARTS" : (RigState.IsOwned(id) ? "UPGRADE   " : "UNLOCK   ") + price;
                }
            }
            else
            {
                bool forged = RigFusionState.IsForged(id);
                _detailLevel.text = forged ? "Forged" : "Not forged";
                if (forged) { buttonText = "FORGED"; live = false; }
                else
                {
                    costShown = fusion.CellCost;
                    live = !_draftActive && PickupWallet.PowerCells >= fusion.CellCost;
                    int short_ = fusion.CellCost - PickupWallet.PowerCells;
                    buttonText = !live && short_ > 0 ? $"NEED {short_} MORE PARTS" : "FORGE   " + fusion.CellCost;
                }
            }
            _detailLevel.color = Dim;
            _detailUpgradeText.text = buttonText;
            // MV-1424: affordable = family colour with dark text; otherwise dark grey with light text (#E6E8EF on #2A2D38,
            // well over 4.5:1). OnDetailUpgradePressed still re-checks, so a refused press spends nothing.
            _detailUpgradeBg.color = live ? fam : new Color32(0x2A, 0x2D, 0x38, 0xFF);
            _detailUpgradeText.color = live ? new Color(0.07f, 0.05f, 0.12f, 1f) : new Color32(0xE6, 0xE8, 0xEF, 0xFF);
            _detailUpgrade.interactable = live;
            if (costShown >= 0)
            {
                int wallet = PickupWallet.PowerCells, need = costShown - wallet;
                SetCostRow(0, "Cost", costShown.ToString(), GoldColor, Dim);
                SetCostRow(1, "You have", wallet.ToString(), GoldColor, Dim);
                if (need > 0) SetCostRow(2, "Need", need + " more", NeedRed, NeedRed);
            }

            if (_treeSpots.TryGetValue(id, out var spot) && _selectionRing != null)
            {
                float rr = spot.R;
                float ringW = rr * Sqrt3 + 2f * BracketGap, ringH = rr * 2f + 2f * BracketGap;
                _selectionRing.color = Color.clear;
                PlaceTopLeft(_selectionRing.rectTransform, new Rect(spot.X - ringW * 0.5f, spot.Y - ringH * 0.5f, ringW, ringH));
            }
        }

        private static readonly Color NeedRed = new Color32(0xFF, 0x6B, 0x6B, 0xFF);

        private void SetCostRow(int row, string label, string value, Color valueColor, Color labelColor)
        {
            _costLabel[row].text = label; _costLabel[row].color = labelColor; _costLabel[row].gameObject.SetActive(true);
            _costValue[row].text = value; _costValue[row].color = valueColor; _costValue[row].gameObject.SetActive(true);
            _costIcon[row].color = valueColor; _costIcon[row].gameObject.SetActive(true);
        }

        /// <summary>MV-1411: the only place a RIG node is bought. Price, affordability and spending are
        /// the existing <see cref="OnRigNodeTapped"/> / <see cref="CellSpend"/> logic, unchanged; a maxed
        /// node, a live draft or a mid-ceremony family spends nothing.</summary>
        private void OnDetailUpgradePressed()
        {
            string id = _selectedNodeId;
            if (id == null || _draftActive) return;
            if (RigState.IsOwned(id) && !RigState.CanSpendPart(id)) return;   // MAXED
            if (IsCeremonySuppressingAbility(id)) return;
            OnRigNodeTapped(id);
            Refresh();   // the wallet/level events repaint too; this keeps the panel right even when they have not fired yet
        }

        private static void PlaceTopLeft(RectTransform r, Rect rect)
        {
            Anchor(r, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            r.sizeDelta = rect.size;
            r.anchoredPosition = new Vector2(rect.x, -rect.y);
        }

        private RailTab BuildRailTab(RectTransform parent, string id, Rect rect)
        {
            string family = id == ForgeTabId ? "supercell" : RigBoardLayout.CategoryFamily(id);
            string iconKey = id == ForgeTabId ? "fuse" : null;
            if (iconKey == null) foreach (var c in Categories) if (c.Id == id) iconKey = c.Icon;
            var tab = new RailTab { Id = id, Family = family };

            float corner = Mathf.Clamp(24f / (rect.height * 0.5f), 0.05f, 0.5f);
            tab.Bg = AddImage(parent, CrispUi.RoundedBox(64, corner), Color.white, "Tab " + id);
            tab.Root = tab.Bg.rectTransform;
            PlaceTopLeft(tab.Root, rect);
            tab.Bg.type = Image.Type.Sliced;
            tab.Bg.raycastTarget = true;
            tab.Button = tab.Bg.gameObject.AddComponent<Button>();
            tab.Button.transition = Selectable.Transition.None;
            string tabId = id;
            tab.Button.onClick.AddListener(() => SelectCategory(tabId));

            tab.Border = AddImage(tab.Root, CrispUi.RoundedBoxOutline(64, corner, 3f), Color.clear, "Border");
            Stretch(tab.Border.rectTransform);
            tab.Border.type = Image.Type.Sliced;
            tab.Border.raycastTarget = false;

            float hexH = Mathf.Min(rect.height - 24f, 96f), hexW = hexH * Sqrt3 * 0.5f;
            tab.Hex = AddImage(tab.Root, CrispUi.Polygon(HexSides, HexRotationDeg, Mathf.CeilToInt(hexW), Mathf.CeilToInt(hexH)), Color.white, "Hex");
            Anchor(tab.Hex.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f));
            tab.Hex.rectTransform.sizeDelta = new Vector2(hexW, hexH);
            tab.Hex.rectTransform.anchoredPosition = new Vector2(20f + hexW * 0.5f, 0f);
            tab.Hex.raycastTarget = false;
            int iconSize = Mathf.RoundToInt(hexH * 0.62f);
            tab.Icon = AddImage(tab.Hex.rectTransform, CrispUi.VectorIcon(RigBoardLayout.Icon(iconKey), iconSize), Color.white, "Icon");
            Anchor(tab.Icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            tab.Icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
            tab.Icon.raycastTarget = false;

            float textLeft = 20f + hexW + 18f;
            float countW = 96f;
            tab.Top = AddText(tab.Root, 22, Dim, TextAnchor.LowerLeft);
            Anchor(tab.Top.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f));
            tab.Top.rectTransform.offsetMin = new Vector2(textLeft, 0f);
            tab.Top.rectTransform.offsetMax = new Vector2(-countW - 8f, 30f);
            tab.Top.fontStyle = FontStyles.Bold;
            tab.Top.raycastTarget = false;

            tab.Name = AddText(tab.Root, 32, TextColor, TextAnchor.UpperLeft);
            Anchor(tab.Name.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 1f));
            tab.Name.rectTransform.offsetMin = new Vector2(textLeft, -Mathf.Min(60f, rect.height * 0.5f - 10f));
            tab.Name.rectTransform.offsetMax = new Vector2(-countW - 8f, 0f);
            tab.Name.fontStyle = FontStyles.Bold;
            tab.Name.enableAutoSizing = true;
            tab.Name.fontSizeMin = 12f; tab.Name.fontSizeMax = 32f;
            tab.Name.textWrappingMode = TextWrappingModes.NoWrap;   // MV-1415: a tab name stays on ONE line and shrinks to fit ("SHOULDER RACK")
            tab.Name.overflowMode = TextOverflowModes.Ellipsis;
            tab.Name.raycastTarget = false;

            tab.Count = AddText(tab.Root, 32, Dim, TextAnchor.MiddleRight);
            Anchor(tab.Count.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            tab.Count.rectTransform.sizeDelta = new Vector2(countW, 44f);
            tab.Count.rectTransform.anchoredPosition = new Vector2(-14f, 0f);
            tab.Count.fontStyle = FontStyles.Bold;
            tab.Count.raycastTarget = false;

            // MV-1415: 3 pt (6 frame unit) bar along the bottom edge - family colour filled to owned/total, gold
            // and full when maxed, absent when locked - plus a padlock that replaces the count on a locked tab.
            tab.BarTrack = AddImage(tab.Root, CrispUi.Solid(), new Color(1f, 1f, 1f, 0.12f), "Bar Track");
            Anchor(tab.BarTrack.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
            tab.BarTrack.rectTransform.offsetMin = new Vector2(20f, 8f);
            tab.BarTrack.rectTransform.offsetMax = new Vector2(-20f, 14f);
            tab.BarTrack.raycastTarget = false;
            tab.BarFill = AddImage(tab.BarTrack.rectTransform, CrispUi.Solid(), Color.white, "Bar Fill");
            Anchor(tab.BarFill.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f));
            tab.BarFill.rectTransform.offsetMin = Vector2.zero; tab.BarFill.rectTransform.offsetMax = Vector2.zero;
            tab.BarFill.raycastTarget = false;
            tab.Padlock = AddImage(tab.Root, CrispUi.VectorIcon(PadlockSvg, 40), new Color(0.80f, 0.84f, 0.90f, 1f), "Padlock");
            Anchor(tab.Padlock.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            tab.Padlock.rectTransform.sizeDelta = new Vector2(40f, 40f);
            tab.Padlock.rectTransform.anchoredPosition = new Vector2(-18f, 0f);
            tab.Padlock.raycastTarget = false;
            tab.Badge = AddAffordBadge(tab.Root, Vector2.zero, 30f);   // MV-1440: top-right corner, beside the level count
            Anchor(tab.Badge.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
            tab.Badge.rectTransform.anchoredPosition = new Vector2(-24f, -22f);
            tab.Badge.gameObject.SetActive(false);
            tab.Group = tab.Root.gameObject.AddComponent<CanvasGroup>();
            return tab;
        }

        /// <summary>Repaints the rail tabs and the tree header from live state: the selected tab gets its
        /// family outline and fill, a locked category is dimmed but stays tappable.</summary>
        private void RefreshRail()
        {
            foreach (var kv in _railTabs)
            {
                var tab = kv.Value;
                bool forge = tab.Id == ForgeTabId;
                bool selected = tab.Id == _selectedCategory;
                bool unlocked = forge || RigState.IsCategoryUnlocked(tab.Id);
                Color fam = RigBoardLayout.Colour(tab.Family);
                Color ink = RigBoardLayout.Colour("ink");
                float dim = 1f;   // MV-1415: a locked tab is dimmed as a whole (CanvasGroup), not element by element

                tab.Bg.color = selected ? new Color(fam.r * 0.28f, fam.g * 0.28f, fam.b * 0.28f, 1f) : new Color(0.075f, 0.085f, 0.11f, 1f);
                tab.Border.color = selected ? fam : Color.clear;
                tab.Hex.color = new Color(fam.r, fam.g, fam.b, unlocked ? 1f : 0.5f);   // a locked hex sits a step below the tab's own 45% (the project is in linear colour space, where 45% alpha reads brighter than in the mock)
                tab.Icon.color = new Color(ink.r, ink.g, ink.b, dim);

                string name, top;
                if (forge) { name = "FORGE"; top = "FUSIONS"; }
                else
                {
                    name = CategoryHeaderText(tab.Id, out _);
                    top = !unlocked ? "LOCKED" : (tab.Id == "PRIMARY" || tab.Id == "SECONDARY") ? tab.Id : "SYSTEMS";
                }
                tab.Top.text = top;
                tab.Top.color = new Color(fam.r, fam.g, fam.b, 0.95f);
                tab.Name.text = name;
                tab.Name.color = TextColor;

                var st = ComputeRailTabState(tab.Id);
                tab.Group.alpha = st.Locked ? 0.45f : 1f;
                tab.Padlock.gameObject.SetActive(st.Locked);
                tab.Count.gameObject.SetActive(!st.Locked);
                tab.Badge.gameObject.SetActive(!st.Locked && !IsCeremonySuppressing(tab.Id) && CategoryHasAffordableNode(tab.Id));   // MV-1440
                tab.BarTrack.gameObject.SetActive(!st.Locked);
                tab.Count.text = st.Maxed ? "MAX" : $"{st.Owned}/{st.Total}";
                tab.Count.color = st.Maxed ? GoldColor : new Color(ink.r, ink.g, ink.b, 0.8f);
                tab.BarFill.color = st.Maxed ? GoldColor : fam;
                tab.BarFill.rectTransform.anchorMax = new Vector2(st.Maxed ? 1f : st.Fraction, 1f);
                tab.BarFill.rectTransform.offsetMin = Vector2.zero; tab.BarFill.rectTransform.offsetMax = Vector2.zero;
            }

            if (_treePanel == null || _selectedCategory == null || !_railTabs.TryGetValue(_selectedCategory, out var sel)) return;
            Color f = RigBoardLayout.Colour(sel.Family);
            _treePanel.color = new Color(f.r * 0.16f, f.g * 0.16f, f.b * 0.16f, 0.55f);
            _treePanelBorder.color = new Color(f.r, f.g, f.b, 0.55f);
            bool isForge = _selectedCategory == ForgeTabId;
            _treeSlotText.text = isForge ? "FUSIONS" : _selectedCategory == "PRIMARY" ? "PRIMARY WEAPON" : _selectedCategory == "SECONDARY" ? "SECONDARY WEAPON" : "SYSTEM";
            _treeSlotText.color = f;
            _treeNameText.text = isForge ? "FORGE" : CategoryHeaderText(_selectedCategory, out _);
        }

        /// <summary>Shows only the selected category's nodes and connectors. The others stay built (just
        /// inactive) so every node id still resolves through <see cref="BoardNode"/>.</summary>
        private void ApplyTreeSelection()
        {
            foreach (var kv in _abilityNodes)
                kv.Value.Root.gameObject.SetActive(_treeSpots.TryGetValue(kv.Key, out var s) && s.Category == _selectedCategory);
            foreach (var kv in _fusionNodes)
                kv.Value.Root.gameObject.SetActive(_selectedCategory == ForgeTabId);
            foreach (var kv in _categoryNodes) kv.Value.Root.gameObject.SetActive(false);   // the rail tab IS the category now
            foreach (var kv in _connectors)
            {
                bool show = false;
                if (kv.Key.StartsWith("conn:ab:", StringComparison.Ordinal))
                {
                    string child = kv.Key.Substring(kv.Key.IndexOf('>') + 1);
                    show = _treeSpots.TryGetValue(child, out var s) && s.Category == _selectedCategory;
                }
                kv.Value.gameObject.SetActive(show);
            }
            foreach (var kv in _connectorExtras)
            {
                bool show = _connectors.TryGetValue(kv.Key, out var main) && main.gameObject.activeSelf;
                foreach (var img in kv.Value) img.gameObject.SetActive(show);
            }
        }

        /// <summary>MV-1410: one orthogonal connector per parent-child link - down from the parent, across,
        /// down into the child - drawn as thin solid bars sharing one colour (RefreshConnectors tints the
        /// first; <see cref="SyncConnectorExtras"/> copies it to the rest).</summary>
        private void BuildConnectors(RectTransform parent)
        {
            _connectorExtras.Clear();
            foreach (var ab in Abilities)
            {
                if (string.IsNullOrEmpty(ab.Parent)) continue;
                if (!_treeSpots.TryGetValue(ab.Id, out var c) || !_treeSpots.TryGetValue(ab.Parent, out var p)) continue;
                string id = $"conn:ab:{ab.Parent}>{ab.Id}";
                const float w = 5f;
                float y0 = p.Y + p.R * 0.92f + (p.RootAlone ? 0f : LabelBlockHeight(ab.Parent)), y1 = c.Y - c.R * 0.92f, ym = (y0 + y1) * 0.5f;   // MV-1415: a non-root parent's label hangs under it, so the line leaves from below the label
                _connectors[id] = ConnectorBar(parent, id, p.X, y0, p.X, ym, w);
                var extras = new List<Image>();
                if (Mathf.Abs(c.X - p.X) > 0.5f) extras.Add(ConnectorBar(parent, id + "#h", Mathf.Min(p.X, c.X), ym, Mathf.Max(p.X, c.X), ym, w));
                extras.Add(ConnectorBar(parent, id + "#v", c.X, ym, c.X, y1, w));
                _connectorExtras[id] = extras;
            }
        }

        private static Image ConnectorBar(RectTransform parent, string name, float x0, float y0, float x1, float y1, float w)
        {
            var img = AddImage(parent, CrispUi.Solid(), Color.clear, name);
            Rect r = x0 == x1 ? new Rect(x0 - w * 0.5f, y0, w, Mathf.Max(1f, y1 - y0)) : new Rect(x0 - w * 0.5f, y0 - w * 0.5f, x1 - x0 + w, w);
            PlaceTopLeft(img.rectTransform, r);
            img.raycastTarget = false;
            return img;
        }

        private void SyncConnectorExtras()
        {
            foreach (var kv in _connectorExtras)
                if (_connectors.TryGetValue(kv.Key, out var main))
                    foreach (var img in kv.Value) img.color = main.color;
        }

        /// <summary>MV-1407: the draft has no full-board scrim any more - the modal itself (see
        /// <see cref="BuildDraftBand"/>) covers the tree panel and swallows taps over it, and the rail
        /// is inert while a draft is active (see <see cref="SelectCategory"/>), as is the detail button.</summary>
        private void BuildDraftScrim(RectTransform boardRoot) { _draftScrim = null; }

        private const int DraftMaxCandidates = 3;
        private readonly List<(RectTransform Root, Image Bg, TextMeshProUGUI Label, Button Button)> _draftChips =
            new List<(RectTransform, Image, TextMeshProUGUI, Button)>();

        /// <summary>MV-1407: the Morphing Module draft MODAL (was a full-width bottom band over the old
        /// 1920x1080 board): exactly the tree panel's rect, drawn above the tree, holding the title, the
        /// two explanatory lines and one tap chip per candidate. Candidates are chips here, not board
        /// nodes - a family candidate has no node in the one-tree layout (its rail tab is the family).</summary>
        private void BuildDraftBand(RectTransform boardRoot)
        {
            var p = _treeFrame.Panel;
            var band = NewRect("draft_band", boardRoot, new Vector2(0f, 1f), new Vector2(0f, 1f));
            band.pivot = new Vector2(0f, 1f);
            PlaceTopLeft(band, p);
            _draftBand = band;

            float cornerFraction = Mathf.Clamp(RigBoardLayout.RegionRectRadius * 2f / (Mathf.Min(p.width, p.height) * 0.5f), 0.05f, 0.5f);
            var bg = AddImage(band, CrispUi.RoundedBox(64, cornerFraction), new Color(0.02f, 0.05f, 0.07f, 0.97f), "Band BG");
            bg.type = Image.Type.Sliced;
            Stretch(bg.rectTransform);
            bg.raycastTarget = true;   // swallow taps over the tree underneath
            var edge = AddImage(band, CrispUi.RoundedBoxOutline(64, cornerFraction, 3f), DraftBadgeColor, "Band Hairline");
            edge.type = Image.Type.Sliced;
            Stretch(edge.rectTransform);
            edge.raycastTarget = false;

            float pad = 0.05f;
            _draftBandTitle = DraftLine(band, 40, 12, DraftBadgeColor, 0.76f, 0.97f, pad);
            _draftBandTitle.fontStyle = FontStyles.Bold;
            _draftBandSubtitle = DraftLine(band, 26, 10, Dim, 0.60f, 0.76f, pad);
            _draftBandReason = DraftLine(band, 22, 10, TextColor, 0.44f, 0.60f, pad);

            _draftChips.Clear();
            for (int i = 0; i < DraftMaxCandidates; i++)
            {
                var chipBg = AddImage(band, CrispUi.RoundedBox(64, 0.3f), Color.white, "Draft Candidate");
                chipBg.type = Image.Type.Sliced;
                chipBg.raycastTarget = true;
                var btn = chipBg.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                var label = AddText(chipBg.rectTransform, 30, Color.white, TextAnchor.MiddleCenter);
                Stretch(label.rectTransform, 6f);
                label.fontStyle = FontStyles.Bold;
                label.enableAutoSizing = true;
                label.fontSizeMin = 10f; label.fontSizeMax = 30f;
                label.textWrappingMode = TextWrappingModes.Normal;
                label.raycastTarget = false;
                chipBg.gameObject.SetActive(false);
                _draftChips.Add((chipBg.rectTransform, chipBg, label, btn));
            }

            band.gameObject.SetActive(false);
        }

        private static TextMeshProUGUI DraftLine(RectTransform band, int max, int min, Color colour, float yMin, float yMax, float pad)
        {
            var t = AddText(band, max, colour, TextAnchor.MiddleCenter);
            Anchor(t.rectTransform, new Vector2(pad, yMin), new Vector2(1f - pad, yMax), new Vector2(0.5f, 0.5f));
            t.rectTransform.offsetMin = Vector2.zero; t.rectTransform.offsetMax = Vector2.zero;
            t.enableAutoSizing = true;
            t.fontSizeMin = min; t.fontSizeMax = max;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>MV-605: the reveal ceremony's static shell, all built inactive and repositioned/
        /// retinted by <see cref="StartCeremony"/>/<see cref="ApplyCeremonyTiming"/> — a full-board dim
        /// scrim (stage b), a reusable soft-edged panel-sized glow over the lit category (stage c, same
        /// idiom MV-521's own single reveal glow used), a locked-placeholder label overlay that fades to
        /// reveal the real name beneath it (also stage c), a small pool of per-child highlight chips
        /// (stage d), and a full-board invisible tap target that skips straight to the settled state.
        /// Raycast off on everything except the skip catcher — the decoration itself must never block a
        /// tap.</summary>
        private void BuildCeremonyVisuals(RectTransform boardRoot)
        {
            var scrim = AddImage(boardRoot, CrispUi.Solid(), new Color(0f, 0f, 0f, 0f), "Ceremony Scrim");
            PlaceTopLeft(scrim.rectTransform, _treeFrame.Panel);   // MV-1407: the tree panel, not the old full board
            scrim.raycastTarget = false;
            scrim.gameObject.SetActive(false);
            _ceremonyScrim = scrim;

            var glow = AddImage(boardRoot, CrispUi.RoundedBox(64, 0.08f), Color.clear, "Ceremony Category Glow");
            glow.type = Image.Type.Sliced;   // the panel it's sized to match is almost never a 64px square
            glow.raycastTarget = false;
            glow.gameObject.SetActive(false);
            _ceremonyCategoryGlow = glow.rectTransform;
            _ceremonyCategoryGlowImage = glow;

            var labelOverlay = AddText(boardRoot, 30, TextColor, TextAnchor.MiddleCenter);
            labelOverlay.gameObject.name = "Ceremony Category Label";
            labelOverlay.text = "? ? ?";
            labelOverlay.raycastTarget = false;
            labelOverlay.gameObject.SetActive(false);
            _ceremonyCategoryLabelOverlay = labelOverlay;

            _ceremonyChildGlows.Clear();
            for (int i = 0; i < CeremonyMaxChildGlows; i++)
            {
                var childGlow = AddImage(boardRoot, CrispUi.RoundedBox(64, 0.5f), Color.clear, $"Ceremony Child Glow {i}");
                childGlow.type = Image.Type.Sliced;
                childGlow.raycastTarget = false;
                childGlow.gameObject.SetActive(false);
                _ceremonyChildGlows.Add(childGlow);
            }

            var skip = AddImage(boardRoot, CrispUi.Solid(), new Color(0f, 0f, 0f, 0f), "Ceremony Skip Catcher");
            PlaceTopLeft(skip.rectTransform, _treeFrame.Panel);
            skip.raycastTarget = true;
            var skipButton = skip.gameObject.AddComponent<Button>();
            skipButton.transition = Selectable.Transition.None;
            skipButton.onClick.AddListener(EndCeremony);
            skip.gameObject.SetActive(false);
            _ceremonySkipCatcher = skipButton;
        }

        /// <summary>FORGE row — divider, caption, and the four fusion diamonds. MV-423 (2/5) placed and
        /// labelled these (RigBoardLayoutTests covers position/size); MV-426 (5/5) gives them their
        /// real state machine via <see cref="RefreshFusionNode"/>, called once by <see cref="Refresh"/>
        /// right after <see cref="Build"/> — this method only builds the static shell each starts
        /// from.</summary>
        private void BuildForgeSection(RectTransform boardRoot)
        {
            // MV-850: no divider, no "FORGE" label, no caption, no diamonds — the whole row is gone in
            // World 2+, not just the un-buildable nodes (Fusions is already empty there, but the header
            // furniture above it isn't gated by that property).
            if (!RigFusionState.EnabledInWorld(RigBoard.ActiveWorldIndex)) return;

            // MV-1410: FORGE is a rail tab now - no divider, label or caption on the board.
            foreach (var fusion in Fusions) _fusionNodes[fusion.Id] = BuildFusionNode(boardRoot, fusion);
        }

        private RigNodeVisual BuildFusionNode(RectTransform boardRoot, RigFusionLayout fusion)
        {
            var spot = _treeSpots.TryGetValue(fusion.Id, out var fs) ? fs : new TreeSpot { X = fusion.X, Y = fusion.Y, R = RadiusFusion };
            float r = spot.R;
            var node = BuildNodeShell(boardRoot, fusion.Id, spot.X, spot.Y, r, FusionSides, out var shell);

            Color amber = SupercellColor;
            shell.HexFill.color = new Color(amber.r, amber.g, amber.b, 0.03f);
            shell.HexOutline.sprite = SolidPolygonOutlineSprite(FusionSides, r);
            shell.HexOutline.color = new Color(1f, 1f, 1f, 0.14f);

            int fuseIconSize = Mathf.RoundToInt(r * RigBoardLayout.IconScaleFusion);
            shell.Icon.sprite = CrispUi.VectorIcon(RigBoardLayout.Icon("fuse"), fuseIconSize);
            shell.Icon.rectTransform.sizeDelta = new Vector2(fuseIconSize, fuseIconSize);
            shell.Icon.color = Dim;

            shell.Label.text = "? ? ?";
            shell.Label.color = Dim;

            // MV-446 defect 3: fontSize off rig_board.json (was a hardcoded 13, dropping to 12 in the
            // locked state — both under the 16px readability floor); box grown to match. MV-729: height
            // grown again (24 -> 44) for the locked state's two-line requirement text — measured
            // preferredWidth showed the worst-case category pairing (PRIMARY + SECONDARY, plus a HAVE
            // flag and the cost) overflows the 280px box on one line (344px) and bleeds into the
            // neighbouring diamond's own sub-label, so categories and cost now sit on separate lines.
            var sub = AddText(node, Mathf.RoundToInt(FusionSubFontSize), Dim, TextAnchor.UpperCenter);
            Anchor(sub.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            // MV-731: AddText defaults to Overflow, so the box width alone never actually clamped the
            // rendered text — Wrap makes FusionSubLabelWidth's shrunk box a real clip boundary instead of
            // just a number a different test happened to check preferredWidth against.
            sub.textWrappingMode = TextWrappingModes.Normal;
            sub.rectTransform.sizeDelta = new Vector2(FusionSubLabelWidth(fusion, Fusions), 70f);   // MV-1410: wraps to up to three lines
            sub.rectTransform.anchoredPosition = new Vector2(0f, -(RigBoardLayout.LabelOffsetY(r) + 22f));
            sub.lineSpacing = 10f;   // TMP: additive percent (legacy multiplier 1.1)
            sub.text = $"{fusion.ParentA} + {fusion.ParentB}";
            shell.Sub = sub;

            shell.PillBg.gameObject.SetActive(false);   // fusions carry no level pill
            shell.PillBorder.gameObject.SetActive(false);
            shell.OuterRing.gameObject.SetActive(false);
            shell.CapMarker.gameObject.SetActive(false);
            shell.Button.interactable = false;   // RefreshFusionNode (MV-426) turns this on once eligible

            string id = fusion.Id;   // capture by value, not the loop variable
            shell.Button.onClick.AddListener(() => OnNodeTapped(id));
            AddSelectHit(shell, id);
            return shell;
        }

        private RigNodeVisual BuildCategoryNode(RectTransform boardRoot, RigCategoryLayout cat)
        {
            float r = RadiusCategory;
            BuildNodeShell(boardRoot, cat.Id, cat.X, cat.Y, r, HexSides, out var shell);

            shell.HexOutline.sprite = SolidHexOutlineSprite(r);
            int catIconSize = Mathf.RoundToInt(r * RigBoardLayout.IconScaleCategory);
            shell.Icon.sprite = CrispUi.VectorIcon(RigBoardLayout.Icon(cat.Icon), catIconSize);
            shell.Icon.rectTransform.sizeDelta = new Vector2(catIconSize, catIconSize);

            shell.Label.rectTransform.anchoredPosition = new Vector2(0f, -RigBoardLayout.CategoryLabelOffsetY(r));
            shell.Label.text = cat.Id;

            // MV-472 (current spec, defects 1+2): BuildNodeShell's label box/wrap/best-fit above is
            // sized and configured for an ABILITY label sharing a tight column with siblings — a
            // category label is one word alone above its hex, with the real column to itself
            // (cat.ColumnHalfWidth, MV-472's own content-proportional layout data, never consulted here
            // before). Two bugs shared one root cause:
            //   1. MV-489: MV-472 set fontSizeMin/fontSizeMax off CategoryLabelFontSize (36
            //      in phone mode) but left the base Text.fontSize field at whatever BuildNodeShell had
            //      already authored it to — the ABILITY size (32) — on the assumption that best-fit
            //      ignores the bare fontSize field entirely. Verified false by direct measurement
            //      (TextGenerator.Populate/fontSizeUsedForBestFit, the same call OnPopulateMesh makes):
            //      Unity's best-fit search is bounded ABOVE by fontSize as well as fontSizeMax, so
            //      a stale fontSize below the intended cap silently re-imposes it — the label rendered
            //      at 32 even with fontSizeMax correctly set to 36. fontSize must be kept in sync
            //      with fontSizeMax, not left at whatever the shared shell happened to author.
            //   2. The inherited box (phone: 190px/Wrap) is an ability-column width, not this category's
            //      own — narrower than "SECONDARY" needs, so it broke mid-word ("SECONDAR"/"Y").
            // Deriving the box from the real column width and using best-fit (Overflow, never Wrap) to
            // shrink rather than break fixes both: the box is now wide enough that best-fit lands at
            // CategoryLabelFontSize (its intended max) in the normal case, and a still-too-long word
            // shrinks gracefully instead of breaking or spilling into a neighbouring column.
            float categoryLabelBoxW = Mathf.Max(2f * cat.ColumnHalfWidth - 16f, 120f);
            shell.Label.rectTransform.sizeDelta = new Vector2(categoryLabelBoxW, _phoneMode ? 60f : 28f);
            shell.Label.textWrappingMode = TextWrappingModes.NoWrap;
            shell.Label.enableAutoSizing = true;
            shell.Label.fontSizeMin = Mathf.Max(1, Mathf.RoundToInt(CategoryLabelFontSize * 0.75f));
            shell.Label.fontSizeMax = Mathf.RoundToInt(CategoryLabelFontSize);
            shell.Label.fontSize = shell.Label.fontSizeMax;   // MV-489: keep in lockstep with the cap above — see note 1

            // MV-1391: PRIMARY/SECONDARY carry a small slot line in the gap between the hex and the
            // weapon-name line (the name line keeps its place, so nothing below it moves).
            if (cat.Id == "PRIMARY" || cat.Id == "SECONDARY")
            {
                // The name line may be long ("CONDENSATION DIRECTIONAL ACCELERATOR"): let best-fit shrink it
                // well below the plain category floor rather than overflow into the next column.
                shell.Label.fontSizeMin = Mathf.Max(1, Mathf.RoundToInt(CategoryLabelFontSize * 0.55f));
                // Best-fit only shrinks on the wrap axis: with Overflow it fits height alone and a long
                // name just runs wide. Wrap + a one-line-tall box makes it shrink until it fits the width.
                shell.Label.textWrappingMode = TextWrappingModes.Normal;
                shell.Label.overflowMode = TextOverflowModes.Truncate;   // Overflow here would also stop best-fit shrinking
                shell.Label.rectTransform.sizeDelta = new Vector2(categoryLabelBoxW, Mathf.Max(shell.Label.rectTransform.sizeDelta.y, 2.5f * shell.Label.fontSizeMin));
                int slotSize = Mathf.Max(1, Mathf.RoundToInt(CategoryLabelFontSize * 0.5f));
                var slot = AddText(shell.Label.transform.parent, slotSize, shell.Label.color, TextAnchor.LowerCenter);
                Anchor(slot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f));
                slot.rectTransform.sizeDelta = new Vector2(categoryLabelBoxW, slotSize + 4f);
                slot.rectTransform.anchoredPosition = shell.Label.rectTransform.anchoredPosition
                    + new Vector2(0f, shell.Label.rectTransform.sizeDelta.y * 0.5f - 2f);
                slot.fontStyle = FontStyles.Bold;
                slot.raycastTarget = false;
                slot.name = "SlotLabel";
                shell.SlotLabel = slot;
            }

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
            shell.OuterRing.sprite = CrispUi.PolygonOutline(HexSides, HexRotationDeg, Mathf.CeilToInt(ringW), Mathf.CeilToInt(ringH), 2f);
            shell.OuterRing.gameObject.SetActive(false);

            shell.CapMarker.gameObject.SetActive(false);
            shell.Button.interactable = false;   // MV-457: only tappable while it's a shed draft candidate — see RefreshCategoryNode

            string catId = cat.Id;   // capture by value, not the loop variable
            shell.Button.onClick.AddListener(() => OnNodeTapped(catId));
            return shell;
        }

        private RigNodeVisual BuildAbilityNode(RectTransform boardRoot, RigAbilityLayout ab)
        {
            var spot = _treeSpots.TryGetValue(ab.Id, out var ts) ? ts : new TreeSpot { X = ab.X, Y = ab.Y, R = RadiusAbility };
            float r = spot.R;
            BuildNodeShell(boardRoot, ab.Id, spot.X, spot.Y, r, HexSides, out var shell);

            // MV-1415: tree chrome. The name sits centred UNDER the hex with the level / price line below it
            // (a root alone on its row carries both to its right); there are no side badges. The sub line is
            // the old pill's text, so the pill itself is never drawn (u_slt keeps MV-1117's enlarged slots pill).
            float hexHalfW = r * Sqrt3 * 0.5f;
            float pillX = 0f;
            bool isSlt = ab.Id == "u_slt";
            {
                var lrect = _treeLabelRects.TryGetValue(ab.Id, out var lr0) ? lr0 : new Rect(spot.X - 90f, spot.Y + r + 2f, 180f, LabelBlockH - 2f);
                bool right = spot.RootAlone;
                float lw = lrect.width;
                float cx = lrect.center.x - spot.X;
                float nameY = right ? lrect.y + 15f : lrect.y + 2f + LabelNameH * 0.5f;
                float subY = right ? lrect.y + 45f : lrect.y + 2f + LabelNameH + LabelSubH * 0.5f;
                var align = ToTmpAlignment(right ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);

                var lr = shell.Label.rectTransform;
                shell.Label.enableAutoSizing = true;
                shell.Label.fontSizeMin = 14f;
                shell.Label.fontSizeMax = 26f;
                shell.Label.textWrappingMode = TextWrappingModes.NoWrap;   // a label shrinks to fit, never wraps
                shell.Label.overflowMode = TextOverflowModes.Ellipsis;
                shell.Label.alignment = align;
                lr.sizeDelta = new Vector2(lw, LabelNameH);
                lr.anchoredPosition = new Vector2(cx, -(nameY - spot.Y));

                var subPos = new Vector2(cx, -(subY - spot.Y));
                foreach (var rt in new[] { shell.PillBg.rectTransform, shell.PillBorder.rectTransform })
                {
                    rt.sizeDelta = new Vector2(lw, LabelSubH);
                    rt.anchoredPosition = subPos;
                }
                if (!isSlt)
                {
                    shell.PillBg.enabled = false;       // the pill chrome is gone; only its text remains, as the sub line
                    shell.PillBorder.enabled = false;
                    shell.PillText.enableAutoSizing = true;
                    shell.PillText.fontSizeMin = 12f;
                    shell.PillText.fontSizeMax = 24f;
                    shell.PillText.textWrappingMode = TextWrappingModes.NoWrap;
                    shell.PillText.overflowMode = TextOverflowModes.Ellipsis;
                    shell.PillText.alignment = align;
                }
            }

            // MV-1117 (Lee, 6 Oct 2026 device playtest): "Make the x/y sentinels text in the add circle
            // much bigger and indicate how many parts are needed to add a sentinel." u_slt is the one
            // node whose own level pill the player reads as "how many sentinels can I field" — doubled
            // here (never below SentinelSlotsPillMinFontSize at the reference resolution), with a
            // second, permanently-visible line for the actual per-deploy Parts price stacked into the
            // SAME pill rect, so neither resized line floats free of a container. RefreshSentinelPriceLine
            // drives its live value/colour; the generic cost tag (CostIcon/CostText) is forced off for
            // this node in RefreshAbilityNode — a DIFFERENT number (u_slt's own unlock/upgrade price,
            // which disappears once the node is maxed) from the deploy price, which never does.
            if (ab.Id == "u_slt")
            {
                float slotsFontSize = Mathf.Max(SentinelSlotsPillMinFontSize, LevelPillFontSize * 2f);
                float priceFontSize = Mathf.Max(SentinelPriceLineMinFontSize, CostFontSize);
                float priceIconSize = CostIconSize;

                // 1.3x each font's own size is the same line-height headroom BuildAbilityNode's own
                // generic cost-tag box already uses below (costFontSize * 1.2, rounded up a touch here
                // since this pill has no best-fit shrink to fall back on if a glyph's ascender/descender
                // runs long).
                const float RowHeightFactor = 1.3f;
                const float RowGap = 6f;
                float topRowH = slotsFontSize * RowHeightFactor;
                float bottomRowH = priceFontSize * RowHeightFactor;
                float pillH = topRowH + bottomRowH + RowGap;
                float pillW = Mathf.Max(LevelPillW, slotsFontSize * 2.3f, priceIconSize + priceFontSize * 2.6f + 8f);

                // Grows upward only (its own Y shifted up by half the extra height) rather than
                // symmetrically about LevelPillOffsetY's usual centre, so the pill's BOTTOM edge stays
                // exactly where every other node's sits — the real vertical budget between the pill and
                // the label below (MV1109RigBoardPanelHeightTests' own family-panel bound) is generic
                // board geometry this one node must not disturb; growing toward the hex's own icon
                // above costs nothing equivalent to guard.
                // MV-1415: the pill hangs under u_slt's name (centred), taking the sub line's place.
                shell.PillBg.rectTransform.sizeDelta = new Vector2(pillW, pillH);
                shell.PillBg.rectTransform.anchoredPosition = new Vector2(0f, -(r + 2f + 2f + LabelNameH + 2f + pillH * 0.5f));
                shell.PillBorder.rectTransform.sizeDelta = new Vector2(pillW, pillH);
                shell.PillBorder.rectTransform.anchoredPosition = shell.PillBg.rectTransform.anchoredPosition;

                shell.PillText.fontSize = Mathf.RoundToInt(slotsFontSize);
                Anchor(shell.PillText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                shell.PillText.rectTransform.sizeDelta = new Vector2(pillW, topRowH);
                shell.PillText.rectTransform.anchoredPosition = new Vector2(0f, (pillH - topRowH) * 0.5f);

                float priceRowY = -(pillH - bottomRowH) * 0.5f;

                var priceIcon = AddImage(shell.PillBg.rectTransform, WeaponHudIcons.PowerCell(Mathf.RoundToInt(priceIconSize) * CrispUi.Resolution), Color.clear, "Sentinel Price Icon");
                Anchor(priceIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f));
                priceIcon.rectTransform.sizeDelta = new Vector2(priceIconSize, priceIconSize);
                priceIcon.rectTransform.anchoredPosition = new Vector2(-2f, priceRowY);
                priceIcon.raycastTarget = false;
                priceIcon.gameObject.SetActive(false);

                var priceText = AddText(shell.PillBg.rectTransform, Mathf.RoundToInt(priceFontSize), TextColor, TextAnchor.MiddleLeft);
                Anchor(priceText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f));
                priceText.rectTransform.sizeDelta = new Vector2(pillW * 0.5f - 6f, bottomRowH);
                priceText.rectTransform.anchoredPosition = new Vector2(2f, priceRowY);
                priceText.fontStyle = FontStyles.Bold;
                priceText.raycastTarget = false;
                priceText.gameObject.SetActive(false);

                shell.SentinelPriceIcon = priceIcon;
                shell.SentinelPriceText = priceText;
            }

            shell.HexOutline.sprite = SolidHexOutlineSprite(r);
            int abIconSize = Mathf.RoundToInt(r * RigBoardLayout.IconScaleAbility);
            shell.Icon.sprite = CrispUi.VectorIcon(RigBoardLayout.Icon(ab.Icon), abIconSize);
            shell.NormalIcon = shell.Icon.sprite;
            shell.LockIcon = CrispUi.VectorIcon(PadlockSvg, abIconSize);
            shell.Icon.rectTransform.sizeDelta = new Vector2(abIconSize, abIconSize);

            // Outer dashed ring (capability draftable) — a hex at r + capOuterRingOffset, independent
            // of the node's own hex outline so it can toggle without disturbing it. MV-481: was a plain
            // circle (HudTextures.Ring) — see the category ring's own comment (BuildCategoryNode) for
            // why that doesn't match the hex-following glow underneath it.
            float ringR = r + RigBoardLayout.CapOuterRingOffset;
            float ringW = ringR * Sqrt3, ringH = ringR * 2f;
            shell.OuterRing.rectTransform.sizeDelta = new Vector2(ringW, ringH);
            shell.OuterRing.sprite = CrispUi.PolygonOutline(HexSides, HexRotationDeg, Mathf.CeilToInt(ringW), Mathf.CeilToInt(ringH), RigBoardLayout.StrokeActive, true);

            float markerR = RigBoardLayout.CapMarkerRadius;
            Vector2 markerOffset = RigBoardLayout.CapMarkerOffset(r);
            shell.CapMarker.rectTransform.sizeDelta = new Vector2(markerR * 2f, markerR * 2f);
            shell.CapMarker.rectTransform.anchoredPosition = markerOffset;
            shell.CapMarker.sprite = CrispUi.Disc(32);

            // MV-470: the accumulation ring — a plain (non-dashed) ring just inside the dashed
            // OuterRing/CapOuterRingOffset radius, revealed by Image.Type.Filled/Radial360 as
            // cellsBanked climbs toward whichever cost currently applies (RefreshAbilityNode sets
            // fillAmount off CellSpend.CellCostProgress01). Inactive until a cell cost applies.
            float progressRingR = r + 4f;

            // MV-538 AC4: the empty TRACK, built first so it renders BEHIND the fill — same size and
            // position, a plain (non-filled) ring at a dim, always-on alpha whenever the fill is
            // active, so a partial arc always reads as "part of a whole."
            shell.ProgressTrack = AddImage(shell.Root, CrispUi.Ring(96, RigBoardLayout.StrokeActive, false), Color.clear, "Progress Track");
            Anchor(shell.ProgressTrack.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            shell.ProgressTrack.rectTransform.sizeDelta = new Vector2(progressRingR * 2f, progressRingR * 2f);
            shell.ProgressTrack.raycastTarget = false;
            shell.ProgressTrack.gameObject.SetActive(false);

            shell.ProgressRing = AddImage(shell.Root, CrispUi.Ring(96, RigBoardLayout.StrokeActive, false), Color.clear, "Progress Ring");
            Anchor(shell.ProgressRing.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            shell.ProgressRing.rectTransform.sizeDelta = new Vector2(progressRingR * 2f, progressRingR * 2f);
            shell.ProgressRing.raycastTarget = false;
            shell.ProgressRing.type = Image.Type.Filled;
            shell.ProgressRing.fillMethod = Image.FillMethod.Radial360;
            shell.ProgressRing.fillOrigin = (int)Image.Origin360.Top;
            shell.ProgressRing.fillClockwise = true;
            shell.ProgressRing.fillAmount = 0f;
            shell.ProgressRing.gameObject.SetActive(false);

            // MV-470/MV-620: the cost tag — a small cell-glyph icon + number sitting in the real vertical
            // gap between the level pill (bottom edge at LevelPillOffsetY - h/2) and the label (top edge
            // at LabelOffsetY[Phone] - 12), so it never fights the pill's own "{level}/{max}" text or the
            // label's ability name. Same PowerCell glyph the CELLS header chip and the world HUD's own
            // counter use, so a node's cost reads as CELLS on sight, not a naked integer. Font/icon size
            // and box geometry are on the phone/standard ladder (RigBoardLayout.CostFontSize[Phone] /
            // CostIconSize[Phone]) rather than hardcoded — MV-620's fix, the tag used to render at 5.1pt
            // on phone and 9.6pt on tablet, both under the project's 11pt legibility floor.
            float pillBottom = -RigBoardLayout.LevelPillOffsetY(r) - LevelPillH * 0.5f;
            float labelTop = -(_phoneMode ? RigBoardLayout.LabelOffsetYPhone(r) : RigBoardLayout.LabelOffsetY(r)) + 12f;
            float costTagY = -(LevelPillH * 0.5f + 4f);   // MV-1410: under the pill, beside the hex
            Color moduleColour = RigBoardLayout.Colour("module");
            float costIconSize = CostIconSize;
            float costFontSize = CostFontSize;

            // MV-620: the icon and text boxes are centre-pivoted (both AddImage/AddText below use a
            // (0.5,0.5) pivot), so each box extends half its own width on EITHER side of its anchored x —
            // offsetting purely by the ICON's own half-width (the pre-fix idiom) left the much wider text
            // box's own left edge bleeding back past centre and into the icon once CostFontSize grew past
            // ~14. Half a gap is added to each side of centre instead, off each box's own half-width, so
            // the two can never overlap regardless of font size.
            const float TagGap = 4f;
            float textHalfWidth = costFontSize * 1.1f;

            shell.CostIcon = AddImage(shell.Root, WeaponHudIcons.PowerCell(Mathf.RoundToInt(costIconSize) * CrispUi.Resolution), Color.clear, "Cost Icon");
            Anchor(shell.CostIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            shell.CostIcon.rectTransform.sizeDelta = new Vector2(costIconSize, costIconSize);
            float costShiftX = pillX - (textHalfWidth * 2f - costIconSize) * 0.5f;   // centre the icon+number block under the pill
            shell.CostIcon.rectTransform.anchoredPosition = new Vector2(-(costIconSize * 0.5f + TagGap * 0.5f) + costShiftX, costTagY);
            shell.CostIcon.raycastTarget = false;
            shell.CostIcon.gameObject.SetActive(false);

            shell.CostText = AddText(shell.Root, Mathf.RoundToInt(costFontSize), moduleColour, TextAnchor.MiddleLeft);
            Anchor(shell.CostText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            shell.CostText.rectTransform.sizeDelta = new Vector2(textHalfWidth * 2f, costFontSize * 1.2f);
            shell.CostText.rectTransform.anchoredPosition = new Vector2(textHalfWidth + TagGap * 0.5f + costShiftX, costTagY);
            shell.CostText.fontStyle = FontStyles.Bold;
            shell.CostText.raycastTarget = false;
            shell.CostText.gameObject.SetActive(false);

            shell.Label.text = ab.Label;

            // MV-1440: the "can afford" badge - a green disc with an up-arrow on the hex's top-right corner.
            shell.AffordBadge = AddAffordBadge(shell.Root, new Vector2(r * Sqrt3 * 0.5f - 2f, r * 0.5f + 2f), AffordBadgeSize);
            shell.AffordBadge.gameObject.SetActive(false);

            string id = ab.Id;   // capture by value, not the loop variable
            shell.Button.onClick.AddListener(() => OnNodeTapped(id));
            AddSelectHit(shell, id);
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

            var outerRing = AddImage(root, CrispUi.Ring(96, RigBoardLayout.StrokeActive, true, 16), Color.clear, "Outer Ring");
            Anchor(outerRing.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            outerRing.raycastTarget = false;

            var capMarker = AddImage(root, CrispUi.Disc(32), Color.clear, "Cap Marker");
            Anchor(capMarker.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            // MV-1408: the real size, not the 100x100 default — category/fusion markers never get one
            // (they are always inactive), and at the fitted board scale that default outgrew its 64px texture.
            capMarker.rectTransform.sizeDelta = new Vector2(RigBoardLayout.CapMarkerRadius * 2f, RigBoardLayout.CapMarkerRadius * 2f);
            capMarker.raycastTarget = false;

            float pillW = LevelPillW, pillH = LevelPillH;
            var pillBg = AddImage(root, CrispUi.RoundedBox(32, 0.5f), PillBackdrop, "Pill");
            Anchor(pillBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            pillBg.rectTransform.sizeDelta = new Vector2(pillW, pillH);
            pillBg.rectTransform.anchoredPosition = new Vector2(0f, -RigBoardLayout.LevelPillOffsetY(r));
            pillBg.type = Image.Type.Sliced;
            pillBg.raycastTarget = false;

            // MV-443 defect 3: the level pill's own 2px border — a separate stacked image so its
            // colour (the state read: family/module/ink) never has to fight the backdrop's fixed
            // #0A0B0F fill.
            var pillBorder = AddImage(root, CrispUi.RoundedBoxOutline(32, 0.5f, 2f), Color.clear, "Pill Border");
            Anchor(pillBorder.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            pillBorder.rectTransform.sizeDelta = new Vector2(pillW, pillH);
            pillBorder.rectTransform.anchoredPosition = pillBg.rectTransform.anchoredPosition;
            pillBorder.type = Image.Type.Sliced;
            pillBorder.raycastTarget = false;

            var pillText = AddText(pillBg.rectTransform, Mathf.RoundToInt(LevelPillFontSize), TextColor, TextAnchor.MiddleCenter);
            Stretch(pillText.rectTransform);
            pillText.fontStyle = FontStyles.Bold;
            pillText.raycastTarget = false;

            var label = AddText(root, Mathf.RoundToInt(LabelFontSize), TextColor, TextAnchor.UpperCenter);
            Anchor(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            label.rectTransform.anchoredPosition = new Vector2(0f, -RigBoardLayout.LabelOffsetY(r));
            label.fontStyle = FontStyles.Bold;
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
                label.rectTransform.sizeDelta = new Vector2(RigBoardLayout.PhoneLabelBoxWidth, RigBoardLayout.LabelBoxHeightPhone);
                label.textWrappingMode = TextWrappingModes.Normal;
                label.enableAutoSizing = true;
                label.fontSizeMin = Mathf.RoundToInt(RigBoardLayout.PhoneLabelFontSizeMin);
                label.fontSizeMax = Mathf.RoundToInt(RigBoardLayout.LabelFontSizePhone);
            }
            else
            {
                label.rectTransform.sizeDelta = new Vector2(r * 3f, RigBoardLayout.LabelBoxHeight);
            }

            // MV-424: the Morphing Module draft's numbered badge (1-3), centred above the hex — a
            // different corner language from the SHED/spend markers so a candidate reads unmistakably
            // as "tap to take", not another passive state.
            var draftBadge = AddImage(root, CrispUi.Disc(40), Color.clear, "Draft Badge");
            Anchor(draftBadge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            draftBadge.rectTransform.sizeDelta = new Vector2(36f, 36f);
            draftBadge.rectTransform.anchoredPosition = new Vector2(0f, r + 34f);
            draftBadge.raycastTarget = false;
            draftBadge.gameObject.SetActive(false);

            var draftBadgeText = AddText(draftBadge.rectTransform, 18, PanelColor, TextAnchor.MiddleCenter);
            Stretch(draftBadgeText.rectTransform);
            draftBadgeText.fontStyle = FontStyles.Bold;
            draftBadgeText.raycastTarget = false;

            var hit = AddImage(root, CrispUi.Solid(), Color.clear, "Hit");
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

        private static Sprite PolygonFillSprite(int sides, int w, int h) => CrispUi.Polygon(sides, RotationFor(sides), w, h);

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
            return CrispUi.PolygonGlow(sides, RotationFor(sides), Mathf.CeilToInt(size.x), Mathf.CeilToInt(size.y), r, blurPx);
        }

        private Sprite SolidHexOutlineSprite(float r) => SolidPolygonOutlineSprite(HexSides, r);

        private Sprite SolidPolygonOutlineSprite(int sides, float r)
        {
            float w = sides == HexSides ? r * Sqrt3 : r * 2f, h = r * 2f;
            return CrispUi.PolygonOutline(sides, RotationFor(sides), Mathf.CeilToInt(w), Mathf.CeilToInt(h), RigBoardLayout.StrokeOwned);
        }

        // MV-445 defect 4: dash 13 / gap 9, walked as arc length around the closed hexagon (see
        // HudTextures.PolygonOutline's own doc comment for why the old angle-based dash broke).
        private const float DraftableDashLength = 13f;
        private const float DraftableGapLength = 9f;

        private Sprite DashedHexSprite(float r)
        {
            float w = r * Sqrt3, h = r * 2f;
            return CrispUi.PolygonOutline(HexSides, HexRotationDeg, Mathf.CeilToInt(w), Mathf.CeilToInt(h),
                RigBoardLayout.StrokeActive, true, DraftableDashLength, DraftableGapLength);
        }

        // ------------------------------------------------------------------ MV-1440: node symbology
        // Three independent signals: STATE (locked / available / owned / maxed - fill + outline + icon),
        // AFFORDABILITY (green up-arrow badge + green price; only on available / owned nodes) and
        // SELECTION (four white corner brackets outside the hex + white name). 1 pt = 2 frame units.

        /// <summary>MV-1440: the four mutually exclusive node states.</summary>
        public enum RigNodeSymbol { Locked, Available, Owned, Maxed }

        private static readonly Color AffordGreen = new Color32(0x4F, 0xE0, 0x8A, 0xFF);
        private static readonly Color PriceGrey = new Color32(0x7D, 0x85, 0x99, 0xFF);
        private static readonly Color LockedOutline = new Color32(0x3B, 0x33, 0x55, 0xFF);
        private static readonly Color LockedFill = new Color(0.035f, 0.03f, 0.06f, 1f);
        private const float AffordBadgeSize = 38f;          // ~19 pt
        private const float OutlineAvailable = 4f, OutlineMaxed = 6f, OutlineLocked = 3f;   // 2 / 3 pt
        private const float BracketStroke = 6f, BracketGap = 18f, BracketLen = 26f;   // 3 pt stroke, 9 pt beyond the hex edge

        private static string HexStr(Color c) => ColorUtility.ToHtmlStringRGB(c);

        private Sprite HexOutlineSprite(float r, float stroke)
        {
            float w = r * Sqrt3, h = r * 2f;
            return CrispUi.PolygonOutline(HexSides, HexRotationDeg, Mathf.CeilToInt(w), Mathf.CeilToInt(h), stroke);
        }

        private const string ArrowUpSvg = "<path d=\"M0,-10 L0,9 M-8,1 L0,9 L8,1\" fill=\"none\" stroke=\"#ICON#\" stroke-width=\"4\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>";

        /// <summary>A green disc with a dark up-arrow, centred at <paramref name="at"/> under <paramref name="parent"/>.</summary>
        private static Image AddAffordBadge(RectTransform parent, Vector2 at, float size)
        {
            var disc = AddImage(parent, CrispUi.Disc(64), AffordGreen, "Afford Badge");
            Anchor(disc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            disc.rectTransform.sizeDelta = new Vector2(size, size);
            disc.rectTransform.anchoredPosition = at;
            disc.raycastTarget = false;
            int px = Mathf.RoundToInt(size * 0.7f);
            var arrow = AddImage(disc.rectTransform, CrispUi.VectorIcon(ArrowUpSvg, px), new Color(0.05f, 0.09f, 0.07f, 1f), "Arrow");
            Anchor(arrow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            arrow.rectTransform.sizeDelta = new Vector2(size * 0.7f, size * 0.7f);
            arrow.raycastTarget = false;
            return disc;
        }

        /// <summary>MV-1440: test access - a tree node's resolved state symbol.</summary>
        public RigNodeSymbol NodeSymbolOf(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.Symbol : RigNodeSymbol.Locked;
        /// <summary>MV-1440: test access - whether a node shows the green can-afford badge right now.</summary>
        public bool NodeAffordBadgeShown(string id) => _abilityNodes.TryGetValue(id, out var v) && v.AffordBadge != null && v.AffordBadge.gameObject.activeSelf;
        /// <summary>MV-1440: test access - a node's name label (colour = white while selected).</summary>
        public TextMeshProUGUI NodeNameText(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.Label : null;
        /// <summary>MV-1440: test access - a node's second label line (rich text; the price carries its own colour tag).</summary>
        public string NodeSubLineText(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.PillText.text : null;
        /// <summary>MV-1440: test access - a node's hex outline image (colour = gold when maxed).</summary>
        public Image NodeOutlineImage(string id) => _abilityNodes.TryGetValue(id, out var v) ? v.HexOutline : null;
        /// <summary>MV-1440: the hex colour of the green used for the badge and affordable prices.</summary>
        public static Color AffordColour => AffordGreen;
        /// <summary>MV-1440: how many corner brackets are drawn right now (4 on the selected node, 0 with no selection).</summary>
        public int SelectionBracketCount()
        {
            if (_selectionRing == null || !_selectionRing.gameObject.activeSelf) return 0;
            int n = 0;
            foreach (Transform t in _selectionRing.transform) if (t.gameObject.activeSelf && t.name.StartsWith("Bracket", StringComparison.Ordinal)) n++;
            return n;
        }
        /// <summary>MV-1440: whether a rail tab shows the green can-afford badge (its tree has an affordable node).</summary>
        public bool RailTabAffordBadgeShown(string id) => _railTabs.TryGetValue(id, out var t) && t.Badge != null && t.Badge.gameObject.activeSelf;

        /// <summary>MV-1440: true when any node of <paramref name="categoryId"/>'s tree is available/owned and payable now.</summary>
        private bool CategoryHasAffordableNode(string categoryId)
        {
            if (categoryId == ForgeTabId)
            {
                foreach (var fu in Fusions) if (!RigFusionState.IsForged(fu.Id) && PickupWallet.PowerCells >= fu.CellCost) return true;
                return false;
            }
            foreach (var ab in Abilities)
                if (ab.Category == categoryId && NodeAffordable(ab.Id)) return true;
            return false;
        }

        /// <summary>MV-1440: an AVAILABLE or OWNED-below-max node whose next step the wallet covers now.</summary>
        private static bool NodeAffordable(string id)
        {
            bool owned = RigState.IsOwned(id);
            bool maxed = owned && !RigState.CanSpendPart(id);
            bool draftable = RigState.IsCellUnlockable(id) && !owned;
            if (maxed || !(draftable || owned)) return false;
            return CellSpend.IsCellActionAffordable(id, PickupWallet.PowerCells);
        }

        /// <summary>MV-443 defect 5: a locked ability's hex outline draws at <c>strokeLocked</c> (2px),
        /// not the owned state's <c>strokeOwned</c> (4px) <see cref="SolidHexOutlineSprite"/> uses.</summary>
        private Sprite LockedHexOutlineSprite(float r)
        {
            float w = r * Sqrt3, h = r * 2f;
            return CrispUi.PolygonOutline(HexSides, HexRotationDeg, Mathf.CeilToInt(w), Mathf.CeilToInt(h), RigBoardLayout.StrokeLocked);
        }

        /// <summary>The refs a built node hands back to <see cref="Refresh"/> — one shared shape for
        /// categories, abilities and fusions so all three can only ever drift apart in DATA (their
        /// json entry), never in code structure.</summary>
        private sealed class RigNodeVisual
        {
            public RectTransform Root;
            public Image Glow, HexFill, HexOutline, Icon, OuterRing, CapMarker, PillBg, PillBorder, DraftBadge;
            public TextMeshProUGUI PillText, Label, DraftBadgeText;
            /// <summary>MV-1391: PRIMARY/SECONDARY category only — the small slot line above the weapon-name <see cref="Label"/>.</summary>
            public TextMeshProUGUI SlotLabel;
            public Button Button;
            public float Radius;
            /// <summary>MV-1410: an ability's own icon, and the padlock it swaps to while locked.</summary>
            public Sprite NormalIcon, LockIcon;

            /// <summary>MV-1440: the resolved state symbol, and the green "can afford" badge (a disc with an up-arrow child) on the hex's top-right corner.</summary>
            public RigNodeSymbol Symbol;
            public Image AffordBadge;

            /// <summary>MV-516: the hex FILL/OUTLINE alpha <see cref="WeaponsScreen.RefreshAbilityNode"/>
            /// (or <see cref="WeaponsScreen.RefreshCandidateNode"/>) most recently set for this node's
            /// current state — <see cref="WeaponsScreen.Update"/> reads these as the pulse's OWN peak
            /// rather than re-deriving the state's alpha itself, so the animated tell can never drift
            /// out of sync with whatever RefreshAbilityNode's own branch decided this state looks like.</summary>
            public float FillBaseAlpha, OutlineBaseAlpha;

            /// <summary>Fusion nodes only (MV-426): the sub-label beneath the name — parent category
            /// names while unforgeable, "<c>N CELLS · SLOT B</c>" once eligible (MV-515: was parts),
            /// "<c>FORGED · SLOT B</c>" once forged. Null for category/ability nodes.</summary>
            public TextMeshProUGUI Sub;

            /// <summary>MV-470: ability nodes only (null for category/fusion) — the accumulation ring
            /// (<see cref="Image.Type.Filled"/>/<see cref="Image.FillMethod.Radial360"/>, fillAmount =
            /// cells-banked / cost) and the small cost-tag icon+number pair sitting between the level
            /// pill and the label, where THE RIG's row spacing leaves real clearance. MV-538 adds
            /// ProgressTrack, the dim full-circle drawn behind ProgressRing (AC4) — same lifetime as the
            /// ring itself. All null unless a cell cost currently applies to the node (draftable or
            /// owned-and-below-max).</summary>
            public Image ProgressRing, ProgressTrack, CostIcon;
            public TextMeshProUGUI CostText;

            /// <summary>MV-1117: <c>u_slt</c> only (null for every other node) — the deploy-price line
            /// stacked inside the bottom half of its own (enlarged) level pill, directly beneath the
            /// doubled-size slots readout. A different number from <see cref="CostText"/> (that one is
            /// CellSpend's unlock/upgrade price for the u_slt NODE itself, which hasCostToShow turns off
            /// once the node is maxed) — a Sentinel still costs Parts to deploy every time regardless of
            /// u_slt's own level, so this stays visible whenever the slots pill itself does.</summary>
            public Image SentinelPriceIcon;
            public TextMeshProUGUI SentinelPriceText;
        }

        // ------------------------------------------------------------------ top bar

        // MV-1408: the phone-first top bar — "THE RIG" / "MAX'S WORKBENCH" on the left; on the right, left
        // to right, the PARTS chip, a settings gear, QUIT and CLOSE (an X). Canvas units at the 852x393
        // reference: the bar is TopBarHeight (40) high and all four buttons are TopBarButtonHeight (36).
        // Built centre-anchored inside the safe root and sized by LayoutTopBar from the computed safe size.
        private const float TopBarButtonHeight = 36f;
        private const float TopBarButtonFont = 15f;
        private const float TopBarButtonGap = 6f;
        private const float TopBarPadX = 10f;
        private const float TopBarCorner = 9f;
        private const string CloseLabel = "\u00D7", QuitLabel = "QUIT TO MENU";   // U+00D7: the multiplication sign is in the SDF font's Latin-1 range

        private static readonly Color GearBorder = new Color(0.20f, 0.24f, 0.31f, 1f);
        private static readonly Color BarButtonFill = new Color(0.075f, 0.09f, 0.12f, 1f);

        private void BuildTopBar(RectTransform parent)
        {
            var bar = NewRect("Top Bar", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _topBar = bar;
            bar.pivot = new Vector2(0.5f, 0.5f);
            bar.sizeDelta = new Vector2(_safeW, TopBarHeight);
            bar.anchoredPosition = new Vector2(0f, _safeH * 0.5f - TopBarHeight * 0.5f);

            // Title block, left.
            var title = AddText(bar, 20, TextColor, TextAnchor.MiddleLeft);
            title.name = "Title";
            Anchor(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            float titleW = Mathf.Ceil(MeasureLabelWidth(bar, "THE RIG", 20f)) + 2f;
            title.rectTransform.anchoredPosition = new Vector2(TopBarPadX + 2f, 0f);
            title.rectTransform.sizeDelta = new Vector2(titleW, 30f);
            title.fontStyle = FontStyles.Bold;
            title.text = "THE RIG";

            var subtitle = AddText(bar, 9, Dim, TextAnchor.MiddleLeft);
            subtitle.name = "Subtitle";
            Anchor(subtitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            subtitle.rectTransform.anchoredPosition = new Vector2(TopBarPadX + 2f + titleW + 6f, -3f);
            subtitle.rectTransform.sizeDelta = new Vector2(120f, 16f);
            subtitle.fontStyle = FontStyles.Bold;
            subtitle.characterSpacing = 3f;
            subtitle.text = "MAX'S WORKBENCH";

            // Right to left: CLOSE, settings gear, PARTS (MV-1415: QUIT moved to the rail's foot).
            float cursor = -TopBarPadX;
            float closeW = TopBarButtonHeight;
            BuildCloseButton(bar, cursor, closeW);
            cursor -= closeW + TopBarButtonGap;

            float gearW = TopBarButtonHeight;
            BuildSettingsButton(bar, cursor, gearW);
            cursor -= gearW + TopBarButtonGap;

            const float iconSize = 12f, iconGap = 6f, padL = 10f, padR = 10f;
            float partsTextW = Mathf.Ceil(MeasureLabelWidth(bar, "999/999 PARTS", TopBarButtonFont));
            float partsW = padL + iconSize + iconGap + partsTextW + padR;
            var cellsChip = BuildChip(bar, new Vector2(cursor, 0f), partsW, CellsColor, out _cellsText,
                height: TopBarButtonHeight, textMaxSize: TopBarButtonFont, radius: TopBarCorner);
            cellsChip.name = "Parts Chip";
            _cellsText.enableAutoSizing = false;   // fixed size: the width above already fits 999/999
            _cellsText.fontSize = Mathf.RoundToInt(TopBarButtonFont);
            _cellsText.rectTransform.offsetMin = new Vector2(padL + iconSize + iconGap, 2f);
            _cellsText.rectTransform.offsetMax = new Vector2(-padR, -2f);

            var hex = AddImage(cellsChip, CrispUi.Polygon(HexSides, HexRotationDeg, 48, 48), SupercellColor, "Parts Icon");
            Anchor(hex.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            hex.rectTransform.anchoredPosition = new Vector2(padL, 0f);
            hex.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
            hex.raycastTarget = false;

            _cellsChipBg = cellsChip.Find("BG").GetComponent<Image>();
            _cellsChipButton = _cellsChipBg.gameObject.AddComponent<Button>();
            _cellsChipButton.transition = Selectable.Transition.None;
            _cellsChipButton.onClick.AddListener(OnCellsChipTapped);

            _cellsBorder = AddChipBorder(cellsChip, CellsColor, "Parts Border", TopBarCorner);
            if (_quitDialog != null) _quitDialog.SetAsLastSibling();   // MV-1415: the confirm dialog stays above the bar
        }

        /// <summary>MV-1408: sizes and places the top bar from the computed safe size — the bar is
        /// centre-anchored, so only its width and its offset from the safe area's centre ever change.</summary>
        private void LayoutTopBar()
        {
            if (_topBar == null) return;
            _topBar.sizeDelta = new Vector2(_safeW, TopBarHeight);
            _topBar.anchoredPosition = new Vector2(0f, _safeH * 0.5f - TopBarHeight * 0.5f);
        }

        /// <summary>Resolved (not authored) width of <paramref name="text"/> in bold at <paramref name="fontSize"/>.</summary>
        private static float MeasureLabelWidth(RectTransform under, string text, float fontSize)
        {
            var t = AddText(under, Mathf.RoundToInt(fontSize), Color.white, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyles.Bold;
            t.text = text;
            float w = t.preferredWidth;
            UnityEngine.Object.DestroyImmediate(t.gameObject);
            return w;
        }

        private static Image AddChipBorder(RectTransform chip, Color color, string name, float radius = 25f)
        {
            var sz = chip.sizeDelta;
            float frac = Mathf.Clamp(radius / (Mathf.Min(sz.x, sz.y) * 0.5f), 0.05f, 0.5f);
            var border = AddImage(chip, CrispUi.RoundedBoxOutline(32, frac, 2.5f), color, name);
            Stretch(border.rectTransform);
            border.type = Image.Type.Sliced;
            border.raycastTarget = false;
            return border;
        }

        private void OnSettingsTapped()
        {
            var settings = FindFirstObjectByType<SettingsPanel>();
            if (settings != null) settings.OpenFromRig();
        }

        /// <summary>A right-anchored, bar-centred button rect <paramref name="rightEdge"/> from the bar's right edge.</summary>
        private static RectTransform NewBarButton(RectTransform bar, string name, float rightEdge, float w)
        {
            var rt = NewRect(name, bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(rightEdge, 0f);
            rt.sizeDelta = new Vector2(w, TopBarButtonHeight);
            return rt;
        }

        /// <summary>The orange CLOSE (X) button pinned at <paramref name="rightEdge"/> from the bar's right edge.</summary>
        private void BuildCloseButton(RectTransform bar, float rightEdge, float w)
        {
            var bg = AddImage(bar, CrispUi.RoundedBox(32, Mathf.Clamp(TopBarCorner / (TopBarButtonHeight * 0.5f), 0.05f, 0.5f)),
                SupercellColor, "Close Button");
            Anchor(bg.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            bg.rectTransform.anchoredPosition = new Vector2(rightEdge, 0f);
            bg.rectTransform.sizeDelta = new Vector2(w, TopBarButtonHeight);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;
            // MV-962: padded raycast beyond the drawn button keeps CLOSE an easy target (kept inside the 6-unit gap).
            bg.raycastPadding = new Vector4(-3f, -3f, -3f, -3f);

            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Close);

            var label = AddText(bg.rectTransform, 26, PanelColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.text = CloseLabel;
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;
        }

        /// <summary>MV-1408: the settings gear — a square dark button with a drawn gear (ring + eight teeth);
        /// opens <see cref="SettingsPanel"/> over the paused RIG exactly as MV-1390's SETTINGS button did.</summary>
        private void BuildSettingsButton(RectTransform bar, float rightEdge, float w)
        {
            var chip = NewBarButton(bar, "Settings Button", rightEdge, w);

            float frac = Mathf.Clamp(TopBarCorner / (TopBarButtonHeight * 0.5f), 0.05f, 0.5f);
            var bg = AddImage(chip, CrispUi.RoundedBox(32, frac), BarButtonFill, "BG");
            Stretch(bg.rectTransform);
            bg.type = Image.Type.Sliced;
            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnSettingsTapped);

            AddChipBorder(chip, GearBorder, "Settings Border", TopBarCorner);

            var teeth = new System.Text.StringBuilder();
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                string x0 = (Mathf.Sin(a) * 12f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                string y0 = (-Mathf.Cos(a) * 12f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                string x1 = (Mathf.Sin(a) * 18f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                string y1 = (-Mathf.Cos(a) * 18f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                teeth.Append($"<path d=\"M{x0},{y0} L{x1},{y1}\" stroke=\"#ICON#\" stroke-width=\"5\"/>");
            }
            var gearTeeth = AddImage(chip, CrispUi.VectorIcon(teeth.ToString(), 64), new Color(0.80f, 0.84f, 0.90f), "Gear Teeth");
            Anchor(gearTeeth.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            gearTeeth.rectTransform.sizeDelta = new Vector2(20f, 20f);
            gearTeeth.raycastTarget = false;
            var gearRing = AddImage(chip, CrispUi.Ring(64, 7f), new Color(0.80f, 0.84f, 0.90f), "Gear Ring");
            Anchor(gearRing.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            gearRing.rectTransform.sizeDelta = new Vector2(13f, 13f);
            gearRing.raycastTarget = false;
        }

        /// <summary>A rounded rect: a live count/label, right-anchored at <paramref name="offset"/> from the
        /// top bar's right edge (the PARTS chip), over a dark fill.</summary>
        private RectTransform BuildChip(RectTransform bar, Vector2 offset, float width, Color accent,
            out TextMeshProUGUI label, float height = 52f, float textMaxSize = 24f, float radius = 25f)
        {
            float cornerFraction = Mathf.Clamp(radius / (Mathf.Min(width, height) * 0.5f), 0.05f, 0.5f);

            var chip = NewRect("Chip", bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            chip.pivot = new Vector2(1f, 0.5f);
            chip.sizeDelta = new Vector2(width, height);
            chip.anchoredPosition = offset;

            var bg = AddImage(chip, CrispUi.RoundedBox(32, cornerFraction), PanelColor, "BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;

            label = AddText(chip, (int)textMaxSize, accent, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(4f, 2f);
            label.rectTransform.offsetMax = new Vector2(-4f, -2f);
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = false;
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

        private static TextMeshProUGUI AddText(Transform parent, int size, Color color, TextAnchor anchor)
        {
            // MV-1406: TextMeshProUGUI on the SDF font (CrispUi.Text), replacing the legacy UI.Text.
            var t = CrispUi.Text(parent, "Text", string.Empty, size, color, ToTmpAlignment(anchor));
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        private static TextAlignmentOptions ToTmpAlignment(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
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
