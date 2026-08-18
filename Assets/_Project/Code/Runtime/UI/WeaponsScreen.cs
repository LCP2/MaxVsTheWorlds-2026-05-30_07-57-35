using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.UI
{
    /// <summary>
    /// THE RIG (MV-423) — Max's ability board, replacing the old ABILITIES screen's primary-track
    /// grid, Water Balloon add-on row and 6-slot abilities grid with a single node-graph rendering of
    /// <c>rig_board.json</c> (MV-422's canonical model, MV-423's own <see cref="RigBoardLayout"/> for
    /// the geometry/colours/icons that model layer deliberately ignores). Every category, ability and
    /// fusion node is placed at the data file's own pixel coordinates on a fixed 1920x1080 board frame
    /// — <see cref="RigBoardLayoutTests"/> asserts this exactly, so this class never re-derives a
    /// position; if a layout decision isn't in the JSON, it doesn't belong here.
    ///
    /// Top bar keeps its existing geometry (28/104 inset/height), CLOSE, QUIT TO MENU and the CELLS
    /// chip; PARTS becomes a six-socket tray and PAUSED is gone (there's no room and no need — the
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

        /// <summary>MV-433: below this, <see cref="ComputeBoardScale"/> refuses to shrink the board
        /// further — an ability node (r 50) at the floor is 90 ref-px / ~39.6pt at the project's
        /// established 6-inch-screen scale (<c>SettingsPanel.Scale6Inch</c>, 0.44 — see that file's own
        /// derivation), already under Apple's 44pt HIG minimum on its own. The floor doesn't claim to
        /// clear 44pt (it can't, at this node size — flagged in the MV-433 fix comment); it exists so a
        /// narrower aspect than 1.6:1 doesn't shrink tap targets even further chasing zero crop. Below
        /// the floor, a little edge crop is accepted instead (see <see cref="VisibleRefXWindow"/>).</summary>
        private const float BoardScaleFloor = 0.9f;

        private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.97f);
        private static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.06f, 0.99f);
        private static readonly Color HeaderAccent = new Color(0.07f, 0.17f, 0.15f, 1f);
        private static readonly Color RowColor = new Color(0.10f, 0.12f, 0.15f, 1f);
        private static readonly Color TextColor = Color.white;
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);

        private static readonly Color CellsColor = new Color(0.35f, 0.85f, 0.95f);
        private static readonly Color PartsColor = new Color(1f, 0.72f, 0.28f);
        private static readonly Color SpendReady = PartsColor;
        private static readonly Color SpendDisabled = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color QuitColor = new Color(0.85f, 0.20f, 0.20f);   // MV-257: destructive-red

        private const float TopBarHeight = 104f;
        private const float ContentMargin = 28f;

        // ------------------------------------------------------------------ THE RIG board (MV-423)

        private const int HexSides = 6;
        private const int FusionSides = 4;
        private const float HexRotationDeg = -90f;   // pointy-top: vertex angles 60*i-90
        private const float FusionRotationDeg = 45f; // MV-433: diamond, not the hex's pointy-top rotation
        private const float Sqrt3 = 1.7320508f;
        private const float PartsSocketSize = 34f;

        // MV-433: owned/lit-category halo radius as a multiple of the node's own radius, and the two
        // peak alphas the halo's shared Glow texture is tinted to (module cyan for a draftable
        // capability, family colour for owned/lit) — the halo itself fades to 0 by its own outer edge.
        private const float GlowRadiusMultiplier = 1.30f;
        private const float GlowAlphaOwned = 0.28f;
        private const float GlowAlphaDraftable = 0.22f;

        private Canvas _canvas;
        private Image _background;
        private RectTransform _safeRoot;
        private GameObject _root;
        private RectTransform _boardScaleRoot;
        private RectTransform _boardRoot;

        private Text _cellsText;
        private Image _cellsChipBg;
        private Button _cellsChipButton;

        private Image _partsTrayBg;
        private Text _partsTrayLabel, _partsTraySub;
        private readonly List<Image> _partsSockets = new List<Image>();
        private Text _partsOverflowText;

        private readonly Dictionary<string, RigNodeVisual> _abilityNodes = new Dictionary<string, RigNodeVisual>();
        private readonly Dictionary<string, RigNodeVisual> _categoryNodes = new Dictionary<string, RigNodeVisual>();
        private readonly Dictionary<string, Image> _categoryPanels = new Dictionary<string, Image>();

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
            if (_boardRoot == null) return null;
            var t = _boardRoot.Find(id);
            return t != null ? (RectTransform)t : null;
        }

        /// <summary>MV-433: the full-canvas opaque backdrop, first child of the Rig's own canvas
        /// GameObject (drawn behind the Safe Area, the top bar and the board) — test-only access, same
        /// idiom as <see cref="BoardNode"/>.</summary>
        public Image Background => _background;

        /// <summary>MV-433: a category's tinted backdrop column — test-only access, same idiom as
        /// <see cref="BoardNode"/>.</summary>
        public Image CategoryPanel(string id) => _categoryPanels.TryGetValue(id, out var p) ? p : null;

        /// <summary>MV-433: the board's own scale-to-fit wrapper (never the same object as
        /// <see cref="BoardNode"/>'s parent frame, which stays fixed at 1920x1080 in its own local
        /// space regardless of this wrapper's scale) — test-only access to confirm the clamp applied.</summary>
        public float BoardScale => _boardScaleRoot != null ? _boardScaleRoot.localScale.x : 1f;

        private void Start() => Build();

        private void OnEnable()
        {
            RigState.Changed += Refresh;
            PickupWallet.PartsChanged += OnPartsChanged;
            PickupWallet.PowerCellsChanged += OnCellsChanged;
            PickupWallet.CapacityChanged += OnCellsChanged;
        }

        private void OnDisable()
        {
            RigState.Changed -= Refresh;
            PickupWallet.PartsChanged -= OnPartsChanged;
            PickupWallet.PowerCellsChanged -= OnCellsChanged;
            PickupWallet.CapacityChanged -= OnCellsChanged;
        }

        private void OnDestroy()
        {
            // Never leave the world frozen if we're torn down mid-open (a scene swap, a test).
            if (_open) Time.timeScale = _prevTimeScale;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        /// <summary>The PARTS tray's glow-ring alpha, 0..1 (MV-327, carried over) — a pure function so
        /// the beat is pinned by an EditMode test without building a canvas. Zero whenever nothing is
        /// banked; otherwise the same trough/peak sine beat every "something is waiting" tell on this
        /// HUD uses.</summary>
        public static float PartsGlowAlpha(float unscaledTime, int partsBanked)
        {
            if (partsBanked <= 0) return 0f;
            float t = 0.5f + 0.5f * Mathf.Sin(unscaledTime * 6f);
            return 0.5f * t;
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

        private void Update()
        {
            if (!_open) return;
            float dt = Time.unscaledDeltaTime;

            if (_partsTrayBg != null)
            {
                int banked = PickupWallet.PartsBanked;
                var glow = PartsColor;
                glow.a = 0.25f + PartsGlowAlpha(Time.unscaledTime, banked);
                // Only the tray's own outline breathes; a fully dark tray (no parts) stays flat.
                if (banked > 0) _partsTrayBg.color = glow;
            }

            // The draftable-capability dashed ring pulses (ticket: "Left to you — the pulse rate on a
            // draftable capability"). Chosen: same family the PARTS glow uses, twice as slow, so the
            // two "something's waiting" tells read as related but distinct.
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
            foreach (var kv in _abilityNodes)
            {
                var v = kv.Value;
                if (v.OuterRing != null && v.OuterRing.gameObject.activeSelf)
                {
                    var c = v.OuterRing.color;
                    c.a = pulse;
                    v.OuterRing.color = c;

                    // MV-433: the draftable node's module-cyan halo pulses with the same ring/cadence —
                    // OuterRing is only ever active in the draftable state, so this never touches the
                    // owned/lit halo (which stays a flat GlowAlphaOwned, no pulse).
                    if (v.Glow != null && v.Glow.gameObject.activeSelf)
                    {
                        var g = v.Glow.color;
                        g.a = pulse * GlowAlphaDraftable;
                        v.Glow.color = g;
                    }
                }
            }
            _ = dt;
        }

        private void OnPartsChanged(int banked) => Refresh();
        private void OnCellsChanged(int cells) => Refresh();

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
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;   // freeze the fight while the player reads/spends

            Refresh();
            _root.SetActive(true);
        }

        /// <summary>Close THE RIG and resume at whatever speed it paused from.</summary>
        public void Close()
        {
            if (!_open) return;
            _open = false;
            _draftActive = false;
            _draftCandidateIds.Clear();
            Time.timeScale = _prevTimeScale;
            _root.SetActive(false);
        }

        /// <summary>A Morphing Module was collected (MV-424, replacing the old shed → badge → BUILD
        /// ABILITY modal chain): 0 candidates consumes the module with nothing granted, 1 grants it
        /// directly with no screen, 2-3 opens THE RIG with just those candidates lit on the board —
        /// numbered, TAKE-labelled — and everything else dimmed. One tap takes it and closes the
        /// screen; the two left behind simply stay in <see cref="RigState.EligibleCapIds"/> for a
        /// later module.</summary>
        public void OpenMorphingModuleDraft(string[] candidateIds)
        {
            if (candidateIds == null || candidateIds.Length == 0)
            {
                Debug.Log("[WeaponsScreen] Morphing Module consumed — nothing left to offer.");
                return;
            }
            if (candidateIds.Length == 1)
            {
                WeaponSystemState.AcquireById(candidateIds[0]);
                return;
            }

            if (_canvas == null) Build();

            _draftCandidateIds.Clear();
            _draftCandidateIds.AddRange(candidateIds);
            _draftActive = true;

            if (!_open)
            {
                _open = true;
                _prevTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }

            Refresh();
            _root.SetActive(true);
        }

        // ------------------------------------------------------------------ live state

        /// <summary>Redraws the CELLS/PARTS banks and every node's visual state off
        /// <see cref="RigState"/> and <see cref="PickupWallet"/> — so a spend, a shed pickup, or a
        /// draft acquire (once MV-424 lands) while this screen happens to be open reflects immediately.</summary>
        private void Refresh()
        {
            if (_root == null) return;

            int banked = PickupWallet.PartsBanked;
            _cellsText.text = $"{PickupWallet.PowerCells}/{PickupWallet.Capacity} CELLS";

            bool capacitySpendable = banked > 0 && PickupWallet.PowerCellCapacityLevel < PickupWallet.PowerCellCapacityMaxLevel;
            _cellsChipButton.interactable = capacitySpendable;
            _cellsChipBg.color = capacitySpendable ? SpendReady : RowColor;

            RefreshPartsTray(banked);
            ApplyBoardScale();

            foreach (var cat in RigBoardLayout.Categories) RefreshCategoryNode(cat, banked);
            foreach (var ab in RigBoardLayout.Abilities) RefreshAbilityNode(ab, banked);

            RefreshMorphingModuleDraft();
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
                if (!_abilityNodes.TryGetValue(_draftCandidateIds[i], out var v)) continue;
                v.Root.SetAsLastSibling();   // render above the scrim
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

        private void RefreshPartsTray(int banked)
        {
            const int socketCount = 6;
            bool any = banked > 0;
            _partsTrayLabel.color = any ? PartsColor : Dim;
            _partsTraySub.text = any ? "tap a node to fit one" : "none banked";
            _partsTraySub.color = any ? new Color(TextColor.r, TextColor.g, TextColor.b, 0.7f) : Dim;
            if (!any) _partsTrayBg.color = RowColor;

            for (int i = 0; i < socketCount; i++)
            {
                bool filled = i < banked;
                _partsSockets[i].sprite = filled
                    ? PolygonFillSprite(HexSides, Mathf.CeilToInt(PartsSocketSize * Sqrt3 * 0.5f), Mathf.CeilToInt(PartsSocketSize))
                    : SolidHexOutlineSprite(PartsSocketSize * 0.5f);
                _partsSockets[i].color = filled ? PartsColor : new Color(1f, 1f, 1f, 0.10f);
            }
            int overflow = Mathf.Max(0, banked - socketCount);
            _partsOverflowText.gameObject.SetActive(overflow > 0);
            _partsOverflowText.text = $"+{overflow}";
        }

        private void RefreshCategoryNode(RigCategoryLayout cat, int banked)
        {
            if (!_categoryNodes.TryGetValue(cat.Id, out var v)) return;
            int owned = 0, total = 0;
            foreach (var ab in RigBoardLayout.Abilities)
            {
                if (ab.Category != cat.Id) continue;
                total++;
                if (RigState.IsOwned(ab.Id)) owned++;
            }
            bool lit = owned > 0;
            Color family = RigBoardLayout.Colour(cat.Family);

            if (_categoryPanels.TryGetValue(cat.Id, out var panel))
                panel.color = new Color(family.r, family.g, family.b, lit ? RigBoardLayout.RegionOpacityLit : RigBoardLayout.RegionOpacityDark);

            v.HexFill.color = new Color(family.r, family.g, family.b, lit ? 0.20f : 0.05f);
            v.HexOutline.color = lit ? family : new Color(family.r, family.g, family.b, 0.35f);
            v.Glow.gameObject.SetActive(lit);
            if (lit) v.Glow.color = new Color(family.r, family.g, family.b, GlowAlphaOwned);

            v.PillText.text = $"{owned}/{total}";
            v.PillBg.color = lit ? new Color(family.r, family.g, family.b, 0.30f) : new Color(1f, 1f, 1f, 0.06f);
            v.PillText.color = lit ? TextColor : Dim;
            v.Icon.color = lit ? TextColor : Dim;
            v.Label.color = lit ? TextColor : Dim;

            _ = banked;
        }

        private void RefreshAbilityNode(RigAbilityLayout ab, int banked)
        {
            if (!_abilityNodes.TryGetValue(ab.Id, out var v)) return;

            int candidateIndex = _draftActive ? _draftCandidateIds.IndexOf(ab.Id) : -1;
            if (candidateIndex >= 0)
            {
                RefreshCandidateNode(v, ab, candidateIndex);
                return;
            }
            v.DraftBadge.gameObject.SetActive(false);

            bool owned = RigState.IsOwned(ab.Id);
            bool reached = RigState.IsReached(ab.Id);
            // Schema 3 (MV-436): every ability is unlocked the same way, so "reached and unowned"
            // is the one capability state — it used to also require ab.Kind == "cap" back when a
            // stat could be reached-and-spendable without ever being a draft candidate.
            bool draftable = reached && !owned;
            bool spendable = RigState.CanSpendPart(ab.Id) && banked > 0;

            Color family = RigBoardLayout.Colour(RigBoardLayout.CategoryFamily(ab.Category));
            Color cyan = RigBoardLayout.Colour("sec");
            Color module = RigBoardLayout.Colour("module");

            v.OuterRing.gameObject.SetActive(draftable);
            v.CapMarker.gameObject.SetActive(draftable);
            v.HexOutline.sprite = draftable ? DashedHexSprite(v.Radius) : SolidHexOutlineSprite(v.Radius);

            if (owned)
            {
                v.HexFill.color = new Color(family.r, family.g, family.b, 0.20f);
                v.HexOutline.color = family;
                v.Glow.gameObject.SetActive(true);
                v.Glow.rectTransform.sizeDelta = new Vector2(v.Radius * GlowRadiusMultiplier * 2f, v.Radius * GlowRadiusMultiplier * 2f);
                v.Glow.color = new Color(family.r, family.g, family.b, GlowAlphaOwned);
                v.PillText.text = $"{RigState.Level(ab.Id)}/{ab.MaxLevel}";
                v.PillBg.color = new Color(family.r, family.g, family.b, 0.30f);
                v.PillText.color = TextColor;
                v.Label.text = ab.Label;
                v.Label.color = TextColor;
                v.Icon.color = TextColor;
            }
            else if (draftable)
            {
                v.HexFill.color = new Color(family.r, family.g, family.b, 0.10f);
                v.HexOutline.color = new Color(family.r, family.g, family.b, 0.8f);
                // MV-433 item 3: the dashed-outer-ring halo — module cyan, on the ring's own radius
                // (r + capOuterRingOffset), pulsing in Update() alongside the ring itself.
                float ringR = v.Radius + RigBoardLayout.CapOuterRingOffset;
                v.Glow.gameObject.SetActive(true);
                v.Glow.rectTransform.sizeDelta = new Vector2(ringR * 2f, ringR * 2f);
                v.Glow.color = new Color(module.r, module.g, module.b, GlowAlphaDraftable);
                v.CapMarker.color = cyan;
                v.PillText.text = "SHED";
                v.PillBg.color = new Color(cyan.r, cyan.g, cyan.b, 0.25f);
                v.PillText.color = cyan;
                v.Label.text = ab.Label;
                v.Label.color = TextColor;
                v.Icon.color = new Color(family.r, family.g, family.b, 0.9f);
            }
            else   // not reached
            {
                v.HexFill.color = new Color(1f, 1f, 1f, 0.02f);
                v.HexOutline.color = new Color(1f, 1f, 1f, 0.12f);
                v.Glow.gameObject.SetActive(false);
                v.PillText.text = "LOCK";
                v.PillBg.color = new Color(1f, 1f, 1f, 0.05f);
                v.PillText.color = Dim;
                v.Label.text = "? ? ?";
                v.Label.color = Dim;
                v.Icon.color = new Color(1f, 1f, 1f, 0.15f);
            }

            v.PartBadge.gameObject.SetActive(spendable);
            v.Button.interactable = spendable;
        }

        /// <summary>A Morphing Module draft candidate (MV-424): lit in its family colour with a strong
        /// glow, numbered 1-3 in a badge above the hex, and <c>TAKE</c> in the level pill in place of
        /// the usual level/SHED/LOCK reading. Always tappable — draft candidates ignore the PARTS bank
        /// entirely, a different currency from the amber "+" spend.</summary>
        private void RefreshCandidateNode(RigNodeVisual v, RigAbilityLayout ab, int candidateIndex)
        {
            Color family = RigBoardLayout.Colour(RigBoardLayout.CategoryFamily(ab.Category));

            v.OuterRing.gameObject.SetActive(false);
            v.CapMarker.gameObject.SetActive(false);
            v.PartBadge.gameObject.SetActive(false);
            v.HexOutline.sprite = SolidHexOutlineSprite(v.Radius);

            v.HexFill.color = new Color(family.r, family.g, family.b, 0.22f);
            v.HexOutline.color = family;
            v.Glow.gameObject.SetActive(true);
            v.Glow.rectTransform.sizeDelta = new Vector2(v.Radius * GlowRadiusMultiplier * 2f, v.Radius * GlowRadiusMultiplier * 2f);
            v.Glow.color = new Color(family.r, family.g, family.b, 0.55f);   // MV-424's own stronger draft-candidate glow, unchanged by MV-433
            v.Icon.color = TextColor;
            v.Label.text = ab.Label;
            v.Label.color = TextColor;

            v.PillText.text = "TAKE";
            v.PillBg.color = new Color(DraftBadgeColor.r, DraftBadgeColor.g, DraftBadgeColor.b, 0.30f);
            v.PillText.color = DraftBadgeColor;

            v.DraftBadge.gameObject.SetActive(true);
            v.DraftBadge.color = DraftBadgeColor;
            v.DraftBadgeText.text = (candidateIndex + 1).ToString();

            v.Button.interactable = true;
        }

        private void OnRigNodeTapped(string id)
        {
            if (_draftActive)
            {
                if (_draftCandidateIds.Contains(id))
                {
                    WeaponSystemState.AcquireById(id);
                    Close();
                }
                return;   // the scrim already blocks non-candidate taps; belt-and-suspenders here
            }
            PartSpend.TrySpendOnRigNode(id);
        }

        private void OnCellsChipTapped() => PartSpend.TrySpendOnCellCapacity();

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

            // MV-433: opaque, first child of the CANVAS itself (not Safe Area) so it sits behind the
            // top bar too and ignores the safe-area inset — the board draws over live gameplay
            // otherwise, which is what washed every family colour out against the lawn.
            _background = AddImage(_canvas.transform, HudTextures.Solid(), OpaqueBase(), "Background");
            Stretch(_background.rectTransform);
            _background.raycastTarget = false;

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

            // MV-433: a scale-to-fit wrapper, pivoted at the board's own centre (960,540) so
            // ComputeBoardScale's shrink is centred rather than pinned to a corner — occupies exactly
            // the same screen rect Board Root used to occupy directly, so at scale 1 (16:9 and wider)
            // nothing about the board's own position changes.
            _boardScaleRoot = NewRect("Board Scale Root", rootRt, new Vector2(0f, 1f), new Vector2(0f, 1f));
            _boardScaleRoot.pivot = new Vector2(0.5f, 0.5f);
            _boardScaleRoot.sizeDelta = new Vector2(RefW, RefH);
            _boardScaleRoot.anchoredPosition = new Vector2(RefW * 0.5f, -RefH * 0.5f);

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

            BuildCategoryPanels(_boardRoot);
            BuildForgeSection(_boardRoot);
            foreach (var cat in RigBoardLayout.Categories) _categoryNodes[cat.Id] = BuildCategoryNode(_boardRoot, cat);
            foreach (var ab in RigBoardLayout.Abilities) _abilityNodes[ab.Id] = BuildAbilityNode(_boardRoot, ab);

            BuildDraftScrim(_boardRoot);   // MV-424: last board child so it dims everything built above,
            BuildDraftBand(_boardRoot);    // then the draft nodes come back on top of it (RefreshMorphingModuleDraft)

            BuildTopBar(rootRt);   // drawn after the board so it sits above it in the hierarchy

            ApplyBoardScale();
            _root.SetActive(false);
        }

        /// <summary>MV-433: recomputes and applies the board's scale-to-fit factor from the current
        /// screen aspect. Called from <see cref="Build"/> once and from <see cref="Refresh"/> on every
        /// state change so a resize (or a different device) since the last <see cref="Open"/> is picked
        /// up without needing its own event — cheap enough to just fold into the existing refresh.</summary>
        private void ApplyBoardScale()
        {
            if (_boardScaleRoot == null) return;
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : RefW / RefH;
            float scale = ComputeBoardScale(aspect);
            _boardScaleRoot.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>The five tinted backdrop columns behind each category's tree (MV-423.png) — one
        /// per category, spanning from the midpoint with its left neighbour to the midpoint with its
        /// right one (the fusion row's diamonds sit exactly on these boundaries: <c>f_del</c> at
        /// x=430 is the PRIMARY/SECONDARY midpoint, etc. — confirmed against the design file rather
        /// than guessed). Drawn before the nodes so they sit behind everything.</summary>
        private void BuildCategoryPanels(RectTransform boardRoot)
        {
            var categories = RigBoardLayout.Categories;
            int n = categories.Count;
            if (n == 0) return;
            float spacing = n > 1 ? categories[1].X - categories[0].X : 0f;
            float y = RigBoardLayout.RegionRectY, h = RigBoardLayout.RegionRectH, radius = RigBoardLayout.RegionRectRadius;

            for (int i = 0; i < n; i++)
            {
                float left = i == 0 ? categories[i].X - spacing * 0.5f : (categories[i - 1].X + categories[i].X) * 0.5f;
                float right = i == n - 1 ? categories[i].X + spacing * 0.5f : (categories[i].X + categories[i + 1].X) * 0.5f;
                float w = right - left;

                var panel = AddImage(boardRoot, HudTextures.RoundedBox(64, Mathf.Clamp(radius / (Mathf.Min(w, h) * 0.5f), 0.05f, 0.5f)),
                    new Color(1f, 1f, 1f, RigBoardLayout.RegionOpacityDark), $"{categories[i].Id} Panel");
                Anchor(panel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
                panel.rectTransform.sizeDelta = new Vector2(w, h);
                panel.rectTransform.anchoredPosition = new Vector2(left, -y);
                panel.type = Image.Type.Sliced;
                panel.raycastTarget = false;
                _categoryPanels[categories[i].Id] = panel;
            }
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
        /// its position is pinned in the fixed 1920x1080 board frame regardless of Safe Area —
        /// <see cref="RigBoardLayoutTests"/>'s own trick for exact-pixel assertions. Deliberately at the
        /// BOTTOM, not the top: a top banner would cover the category row, and the whole value of
        /// drafting on the board is seeing the current build while choosing (ticket, non-negotiable).</summary>
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

        /// <summary>FORGE row — divider, caption, and the four fusion diamonds. MV-423 (2/5) only has
        /// to PLACE and LABEL these (RigBoardLayoutTests covers position/size); a fusion's own
        /// draft/spend state machine is 5/5's job, so every diamond here renders permanently locked —
        /// "???" over its parent-category pairing, never the mock's one-off lit OVERCHARGE state,
        /// which depends on logic this ticket doesn't build.</summary>
        private void BuildForgeSection(RectTransform boardRoot)
        {
            float dividerY = RigBoardLayout.ForgeDividerY;
            var divider = AddImage(boardRoot, HudTextures.Solid(), new Color(1f, 1f, 1f, 0.12f), "Forge Divider");
            Anchor(divider.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            divider.rectTransform.offsetMin = new Vector2(ContentMargin, 0f);
            divider.rectTransform.offsetMax = new Vector2(-ContentMargin, 0f);
            divider.rectTransform.anchoredPosition = new Vector2(0f, -dividerY);
            divider.rectTransform.sizeDelta = new Vector2(0f, 1.5f);

            var forgeLabel = AddText(boardRoot, 22, PartsColor, TextAnchor.UpperLeft);
            Anchor(forgeLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            forgeLabel.rectTransform.anchoredPosition = new Vector2(ContentMargin, -(dividerY + 24f));
            forgeLabel.rectTransform.sizeDelta = new Vector2(200f, 28f);
            forgeLabel.fontStyle = FontStyle.Bold;
            forgeLabel.text = "FORGE";

            var caption = AddText(boardRoot, 18, Dim, TextAnchor.UpperLeft);
            Anchor(caption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            caption.rectTransform.anchoredPosition = new Vector2(ContentMargin + 170f, -(dividerY + 24f));
            caption.rectTransform.sizeDelta = new Vector2(700f, 28f);
            caption.text = "two lit categories · costs parts, never a shed · lands in the B / U slot";

            foreach (var fusion in RigBoardLayout.Fusions) BuildFusionNode(boardRoot, fusion);
        }

        private RigNodeVisual BuildFusionNode(RectTransform boardRoot, RigFusionLayout fusion)
        {
            float r = RigBoardLayout.RadiusFusion;
            var node = BuildNodeShell(boardRoot, fusion.Id, fusion.X, fusion.Y, r, FusionSides, out var shell);

            Color amber = PartsColor;
            shell.HexFill.color = new Color(amber.r, amber.g, amber.b, 0.03f);
            shell.HexOutline.sprite = SolidPolygonOutlineSprite(FusionSides, r);
            shell.HexOutline.color = new Color(1f, 1f, 1f, 0.14f);

            int fuseIconSize = Mathf.RoundToInt(r * RigBoardLayout.IconScaleFusion);
            shell.Icon.sprite = HudTextures.VectorIcon(RigBoardLayout.Icon("fuse"), fuseIconSize);
            shell.Icon.rectTransform.sizeDelta = new Vector2(fuseIconSize, fuseIconSize);
            shell.Icon.color = Dim;

            shell.Label.text = "? ? ?";
            shell.Label.color = Dim;

            var sub = AddText(node, 13, Dim, TextAnchor.UpperCenter);
            Anchor(sub.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            sub.rectTransform.sizeDelta = new Vector2(260f, 20f);
            sub.rectTransform.anchoredPosition = new Vector2(0f, -(RigBoardLayout.LabelOffsetY(r) + 22f));
            sub.text = $"{fusion.ParentA} + {fusion.ParentB}";

            shell.PillBg.gameObject.SetActive(false);   // fusions carry no level pill
            shell.PartBadge.gameObject.SetActive(false);
            shell.OuterRing.gameObject.SetActive(false);
            shell.CapMarker.gameObject.SetActive(false);
            shell.Button.interactable = false;
            return shell;
        }

        private RigNodeVisual BuildCategoryNode(RectTransform boardRoot, RigCategoryLayout cat)
        {
            float r = RigBoardLayout.RadiusCategory;
            BuildNodeShell(boardRoot, cat.Id, cat.X, cat.Y, r, HexSides, out var shell);

            shell.HexOutline.sprite = SolidHexOutlineSprite(r);
            int catIconSize = Mathf.RoundToInt(r * RigBoardLayout.IconScaleCategory);
            shell.Icon.sprite = HudTextures.VectorIcon(RigBoardLayout.Icon(cat.Icon), catIconSize);
            shell.Icon.rectTransform.sizeDelta = new Vector2(catIconSize, catIconSize);

            shell.Label.fontSize = Mathf.RoundToInt(RigBoardLayout.CategoryLabelFontSize);
            shell.Label.rectTransform.anchoredPosition = new Vector2(0f, -RigBoardLayout.CategoryLabelOffsetY(r));
            shell.Label.text = cat.Id;

            shell.PartBadge.gameObject.SetActive(false);   // categories are never spendable
            shell.OuterRing.gameObject.SetActive(false);
            shell.CapMarker.gameObject.SetActive(false);
            shell.Button.interactable = false;
            return shell;
        }

        private RigNodeVisual BuildAbilityNode(RectTransform boardRoot, RigAbilityLayout ab)
        {
            float r = RigBoardLayout.RadiusAbility;
            BuildNodeShell(boardRoot, ab.Id, ab.X, ab.Y, r, HexSides, out var shell);

            shell.HexOutline.sprite = SolidHexOutlineSprite(r);
            int abIconSize = Mathf.RoundToInt(r * RigBoardLayout.IconScaleAbility);
            shell.Icon.sprite = HudTextures.VectorIcon(RigBoardLayout.Icon(ab.Icon), abIconSize);
            shell.Icon.rectTransform.sizeDelta = new Vector2(abIconSize, abIconSize);

            // Outer dashed ring (capability draftable) — a circle at r + capOuterRingOffset, independent
            // of the node's own hex outline so it can toggle without disturbing it.
            float ringR = r + RigBoardLayout.CapOuterRingOffset;
            shell.OuterRing.rectTransform.sizeDelta = new Vector2(ringR * 2f, ringR * 2f);
            shell.OuterRing.sprite = HudTextures.Ring(96, RigBoardLayout.StrokeActive, true, 16);

            float markerR = RigBoardLayout.CapMarkerRadius;
            Vector2 markerOffset = RigBoardLayout.CapMarkerOffset(r);
            shell.CapMarker.rectTransform.sizeDelta = new Vector2(markerR * 2f, markerR * 2f);
            shell.CapMarker.rectTransform.anchoredPosition = markerOffset;
            shell.CapMarker.sprite = HudTextures.Disc(32);

            float badgeR = RigBoardLayout.PartBadgeRadius;
            Vector2 badgeOffset = RigBoardLayout.PartBadgeOffset(r);
            shell.PartBadge.rectTransform.sizeDelta = new Vector2(badgeR * 2f, badgeR * 2f);
            shell.PartBadge.rectTransform.anchoredPosition = badgeOffset;
            shell.PartBadge.sprite = HudTextures.Disc(32);
            shell.PartBadge.color = PartsColor;

            var plus = AddText(shell.PartBadge.rectTransform, 18, PanelColor, TextAnchor.MiddleCenter);
            Stretch(plus.rectTransform);
            plus.text = "+";
            plus.fontStyle = FontStyle.Bold;
            plus.raycastTarget = false;

            shell.Label.text = ab.Label;

            string id = ab.Id;   // capture by value, not the loop variable
            shell.Button.onClick.AddListener(() => OnRigNodeTapped(id));
            return shell;
        }

        /// <summary>The shared shell every node (category/ability/fusion) is built from: a
        /// <paramref name="sides"/>-gon of circumradius <paramref name="r"/> centred at
        /// (<paramref name="x"/>, <paramref name="y"/>) in the board's own frame, plus the pieces every
        /// state needs — fill, outline, glow, outer ring, cap marker, part badge, level pill, label,
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

            // MV-433: a round radial-falloff halo behind the node plate (drawn first among this shell's
            // children, so it renders behind Fill/Outline/Icon), not the old flat-alpha hex-shaped fill —
            // one shared HudTextures.Glow texture, resized/tinted per state in Refresh*Node below (owned
            // at r*GlowRadiusMultiplier, draftable at the outer dashed ring's own radius).
            var glow = AddImage(root, HudTextures.Glow(128), Color.clear, "Glow");
            Anchor(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            glow.rectTransform.sizeDelta = new Vector2(r * GlowRadiusMultiplier * 2f, r * GlowRadiusMultiplier * 2f);
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

            var partBadge = AddImage(root, HudTextures.Disc(32), Color.clear, "Part Badge");
            Anchor(partBadge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            partBadge.raycastTarget = false;

            float pillW = RigBoardLayout.LevelPillW, pillH = RigBoardLayout.LevelPillH;
            var pillBg = AddImage(root, HudTextures.RoundedBox(32, 0.5f), RowColor, "Pill");
            Anchor(pillBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            pillBg.rectTransform.sizeDelta = new Vector2(pillW, pillH);
            pillBg.rectTransform.anchoredPosition = new Vector2(0f, -RigBoardLayout.LevelPillOffsetY(r));
            pillBg.type = Image.Type.Sliced;
            pillBg.raycastTarget = false;

            var pillText = AddText(pillBg.rectTransform, Mathf.RoundToInt(RigBoardLayout.LevelPillFontSize), TextColor, TextAnchor.MiddleCenter);
            Stretch(pillText.rectTransform);
            pillText.fontStyle = FontStyle.Bold;
            pillText.raycastTarget = false;

            var label = AddText(root, Mathf.RoundToInt(RigBoardLayout.LabelFontSize), TextColor, TextAnchor.UpperCenter);
            Anchor(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            label.rectTransform.sizeDelta = new Vector2(r * 3f, 24f);
            label.rectTransform.anchoredPosition = new Vector2(0f, -RigBoardLayout.LabelOffsetY(r));
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;

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
                OuterRing = outerRing, CapMarker = capMarker, PartBadge = partBadge,
                PillBg = pillBg, PillText = pillText, Label = label, Button = button, Radius = r,
                DraftBadge = draftBadge, DraftBadgeText = draftBadgeText
            };
            return root;
        }

        // MV-433: FORGE's fusion nodes (sides == FusionSides) render as diamonds — Polygon(4, 45) per
        // geometry.radius.fusion — not squares; every other caller (hex nodes, the parts tray's hex
        // sockets) keeps the pointy-top hex rotation. A single shared rotation constant for both shapes
        // was the bug (fusion squares in MV-423's build).
        private static float RotationFor(int sides) => sides == FusionSides ? FusionRotationDeg : HexRotationDeg;

        private static Sprite PolygonFillSprite(int sides, int w, int h) => HudTextures.Polygon(sides, RotationFor(sides), w, h);

        private Sprite SolidHexOutlineSprite(float r) => SolidPolygonOutlineSprite(HexSides, r);

        private Sprite SolidPolygonOutlineSprite(int sides, float r)
        {
            float w = sides == HexSides ? r * Sqrt3 : r * 2f, h = r * 2f;
            return HudTextures.PolygonOutline(sides, RotationFor(sides), Mathf.CeilToInt(w), Mathf.CeilToInt(h), RigBoardLayout.StrokeOwned);
        }

        private Sprite DashedHexSprite(float r)
        {
            float w = r * Sqrt3, h = r * 2f;
            return HudTextures.PolygonOutline(HexSides, HexRotationDeg, Mathf.CeilToInt(w), Mathf.CeilToInt(h),
                RigBoardLayout.StrokeActive, true, 14);
        }

        /// <summary>The refs a built node hands back to <see cref="Refresh"/> — one shared shape for
        /// categories, abilities and fusions so all three can only ever drift apart in DATA (their
        /// json entry), never in code structure.</summary>
        private sealed class RigNodeVisual
        {
            public RectTransform Root;
            public Image Glow, HexFill, HexOutline, Icon, OuterRing, CapMarker, PartBadge, PillBg, DraftBadge;
            public Text PillText, Label, DraftBadgeText;
            public Button Button;
            public float Radius;
        }

        // ------------------------------------------------------------------ top bar

        private void BuildTopBar(RectTransform parent)
        {
            var bar = NewRect("Top Bar", parent, new Vector2(0f, 1f), new Vector2(1f, 1f));
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
            subtitle.rectTransform.offsetMin = new Vector2(190f, -30f);
            subtitle.rectTransform.offsetMax = new Vector2(-12f, 30f);
            subtitle.fontStyle = FontStyle.Bold;
            subtitle.text = "MAX'S WORKBENCH";

            float cursor = -16f;
            cursor = BuildCloseButton(bar, cursor) - 16f;
            cursor = BuildQuitButton(bar, cursor) - 16f;

            const float partsTrayWidth = 340f;
            cursor = BuildPartsTray(bar, cursor, partsTrayWidth) - 16f;

            // MV-433: widened from 170 — "28 / 30 CELLS" at the chip's own min best-fit size (14pt)
            // was clipping its leading digit against the icon at the old width.
            const float cellsChipWidth = 190f;
            var cellsChip = BuildChip(bar, new Vector2(cursor, 0f), cellsChipWidth, CellsColor,
                HudTextures.Disc(32), 20f, out _cellsText, out _);
            cellsChip.name = "Cells Chip";

            _cellsChipBg = cellsChip.Find("BG").GetComponent<Image>();
            _cellsChipButton = _cellsChipBg.gameObject.AddComponent<Button>();
            _cellsChipButton.transition = Selectable.Transition.None;
            _cellsChipButton.onClick.AddListener(OnCellsChipTapped);
        }

        /// <summary>MV-423's replacement for the old spinning-gear PARTS chip: six hex sockets (filled
        /// amber up to the banked count, a <c>+N</c> overflow past six), captioned "tap a node to fit
        /// one" while anything's banked and "none banked" (whole tray dark) when empty — the design's
        /// own before/after pair (<c>MV-423.png</c> vs <c>MV-423-noparts.png</c>).</summary>
        private float BuildPartsTray(RectTransform bar, float rightEdge, float width)
        {
            var tray = NewRect("Parts Tray", bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            tray.pivot = new Vector2(1f, 0.5f);
            tray.sizeDelta = new Vector2(width, 68f);
            tray.anchoredPosition = new Vector2(rightEdge, 0f);

            var bg = AddImage(tray, HudTextures.RoundedBox(32, 0.3f), RowColor, "Tray BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;
            _partsTrayBg = bg;

            var inner = AddImage(tray, HudTextures.RoundedBox(32, 0.3f), PanelColor, "Tray Inner");
            Stretch(inner.rectTransform, -2.5f); inner.type = Image.Type.Sliced;

            _partsTrayLabel = AddText(tray, 18, PartsColor, TextAnchor.UpperLeft);
            Anchor(_partsTrayLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1f));
            _partsTrayLabel.rectTransform.offsetMin = new Vector2(14f, -30f);
            _partsTrayLabel.rectTransform.offsetMax = Vector2.zero;
            _partsTrayLabel.fontStyle = FontStyle.Bold;
            _partsTrayLabel.text = "PARTS";

            _partsTraySub = AddText(tray, 13, Dim, TextAnchor.LowerLeft);
            Anchor(_partsTraySub.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f));
            _partsTraySub.rectTransform.offsetMin = new Vector2(14f, 8f);
            _partsTraySub.rectTransform.offsetMax = new Vector2(0f, 30f);

            const int socketCount = 6;
            const float socketGap = 4f;
            float socketsWidth = socketCount * PartsSocketSize + (socketCount - 1) * socketGap;
            var socketRow = NewRect("Sockets", tray, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f));
            socketRow.offsetMin = new Vector2(0f, -PartsSocketSize * 0.5f);
            socketRow.offsetMax = new Vector2(-16f, PartsSocketSize * 0.5f);

            _partsSockets.Clear();
            for (int i = 0; i < socketCount; i++)
            {
                var socket = AddImage(socketRow, SolidHexOutlineSprite(PartsSocketSize * 0.5f), new Color(1f, 1f, 1f, 0.1f), $"Socket {i}");
                Anchor(socket.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                socket.rectTransform.sizeDelta = new Vector2(PartsSocketSize * Sqrt3 * 0.5f, PartsSocketSize);
                socket.rectTransform.anchoredPosition = new Vector2(-(socketsWidth - (i + 0.5f) * (PartsSocketSize + socketGap)), 0f);
                _partsSockets.Add(socket);
            }

            _partsOverflowText = AddText(socketRow, 14, PartsColor, TextAnchor.MiddleRight);
            Anchor(_partsOverflowText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            _partsOverflowText.rectTransform.sizeDelta = new Vector2(50f, 30f);
            _partsOverflowText.rectTransform.anchoredPosition = new Vector2(6f, 0f);
            _partsOverflowText.fontStyle = FontStyle.Bold;
            _partsOverflowText.gameObject.SetActive(false);

            return rightEdge - width;
        }

        /// <summary>A dismiss pill pinned at <paramref name="rightEdge"/> from the bar's right edge.</summary>
        private float BuildCloseButton(RectTransform bar, float rightEdge)
        {
            const float w = 104f, h = 56f;
            var bg = AddImage(bar, HudTextures.RoundedBox(32, 0.5f), PartsColor, "Close Button");
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
            label.text = "✕ CLOSE";
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

        /// <summary>A rounded pill: a tinted icon + a live count/label, right-anchored at
        /// <paramref name="offset"/> from the top bar's right edge (CELLS).</summary>
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
