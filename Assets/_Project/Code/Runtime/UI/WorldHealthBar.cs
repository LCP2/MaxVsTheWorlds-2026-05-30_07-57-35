using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Core;

namespace MaxWorlds.UI
{
    /// <summary>
    /// A health bar that floats above a unit and reports its HP as a number (YT-111) — the
    /// Brawl Stars read: name, bar, figure, over every actor on the field at once.
    ///
    /// One component serves Max and every robot. It knows nothing about either: it asks an
    /// <see cref="IHealthReadout"/> what to draw, so the difference between the player's bar and a
    /// rusher's is the numbers they return, not two pieces of code that have to be kept in step.
    ///
    /// Built in code and parented to the unit, following the Mower Hutch's bar (YT-71). Parenting
    /// matters more than it looks for robots: they are POOLED, so a dead one is deactivated and
    /// handed back rather than destroyed. A bar that is a child deactivates and returns with its
    /// body and needs no reattachment logic — the whole class of "the second wave spawned with no
    /// bars" bugs simply cannot happen.
    ///
    /// Nothing here is a MeshRenderer, so neither of the per-frame material directors can see it
    /// (they both enumerate MeshRenderer only, and UI draws through CanvasRenderer).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldHealthBar : MonoBehaviour
    {
        // Sizes in metres, so the bar reads as a label on the unit rather than a banner over the
        // arena. Prominence comes from WIDTH, not height (YT-136): a flat, wide Brawl-Stars strip.
        private const float BarPixelWidth = 180f;
        // Flattened for YT-136: 34, down from YT-128's 64. The tall bar (plus the water gauge and name
        // stacked over it) reared up over Max and buried the character it floats above. A flat wide
        // strip reads just as clearly at the 23 m phone zoom and leaves all of Max visible. The
        // width:height ratio here (~5.3:1) is what makes it a bar rather than a block.
        private const float BarPixelHeight = 34f;
        private const float LabelPixelWidth = 260f;
        private const float LabelPixelHeight = 30f;
        private const int LabelFontSize = 22;
        private const int NumberFontSize = 26;

        // The solid dark border, in canvas pixels. Thick enough to read as a deliberate outline that
        // separates the bar from the grass, not a hairline; trimmed to 5 with the flatter bar (YT-136)
        // so the coloured fill inside the thinner strip still reads.
        private const float OutlinePx = 5f;

        /// <summary>Hide the bar once a unit is this close to full. A field of untouched robots each
        /// carrying a full green bar is the clutter the ticket warned about; a bar that appears when
        /// something has been hit is information.</summary>
        private const float FullEnough = 0.999f;

        /// <summary>MV-788: seconds a bar stays fully visible after its last damage/target trigger,
        /// before the fade below starts. Lee's own numbers from the "Stormdrain Surface Kit" design
        /// review — not to be substituted.</summary>
        private const float TriggerHoldSeconds = 3.0f;

        /// <summary>MV-788: how long the fade itself takes once <see cref="TriggerHoldSeconds"/> has
        /// elapsed with no further damage and no longer being the target.</summary>
        private const float TriggerFadeSeconds = 0.4f;

        /// <summary>MV-788: a frame-to-frame health DROP of at least this much counts as "took damage"
        /// — floors out float noise between two reads of the same unchanged value.</summary>
        private const float DamageDetectEpsilon = 0.0001f;

        // Near-black, mostly opaque: the outline that makes the capsule pop. The track (unfilled
        // part) is a translucent dark, so a drained bar reads as an empty capsule, not a black slab.
        private static readonly Color OutlineColor = new Color(0.02f, 0.03f, 0.02f, 0.92f);
        private static readonly Color BackColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color NameColor = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color ReplicatorMarkerColor = new Color(0.3f, 1f, 1f, 0.95f); // MV-706: matches the Replicator's own cyan LED

        /// <summary>MV-747: Max's bar always wins Unity's canvas draw order over any enemy nameplate,
        /// regardless of whether their screen rects happen to intersect on a given frame — simpler and
        /// stronger than detecting the intersection every frame, and it can never regress into
        /// "usually behind, sometimes in front" as bars drift past each other on screen.</summary>
        private const int PlayerSortingOrder = 10;
        private const int EnemySortingOrder = 0;

        // Height of the optional secondary gauge (Max's water), as a fraction of the health bar's.
        private const float SecondaryHeightFraction = 0.62f;

        /// <summary>
        /// Extra clearance the bar is pushed UP THE SCREEN, in metres, on top of its world-up anchor
        /// (YT-149).
        ///
        /// The world-up anchor (<see cref="_heightAboveCentre"/>) leaves ~0.8 m of real space over
        /// Max's head — but at the fixed ~72° camera a world-up offset barely separates ON SCREEN: it
        /// projects at cos(pitch) ≈ 0.31, so that 0.8 m collapsed to a sliver above his hair. The 3 m
        /// camera look-ahead (CameraTargetRig) then closed even that sliver the moment he ran up-screen
        /// — it re-aims the camera to a steeper ~79° over the top of him, lifting his silhouette into
        /// the bar. That is the whole bug: his head slid under the bar when he ran away from camera.
        ///
        /// The camera's up axis is (almost) screen-up, so the same metres buy ~3× the on-screen
        /// clearance AND the lift is measured from the camera, not the world — it holds at every
        /// movement angle and rides the look-ahead automatically, instead of being a bigger world
        /// number that would just reappear as the bug at the next steeper frame. Picked to clear his
        /// hair-tips (1.83 m) with visible daylight at the phone zoom without floating the bar off him.
        ///
        /// MV-473: retuned 0.45 -> 0.6 for the 60° pitch (was 64.88°/72° when 0.45 was picked). A
        /// world-up offset's screen-space payoff is ~cos(pitch), so the shallower the pitch the more
        /// of a character's OWN height lands on screen too — the two effects partly cancel, but not
        /// exactly, because this term (camera-space, pitch-invariant by construction) does not grow
        /// with pitch the way the character's silhouette does, so it needs a direct bump to keep the
        /// same relative daylight above a taller-reading body.
        /// </summary>
        private const float ScreenClearance = 0.6f;

        private IHealthReadout _source;
        private Transform _scaleAnchor;
        private Transform _pivot;
        private RectTransform _canvas;
        private RectTransform _barVisuals;
        private Image _fill;
        private Text _nameText;
        private Text _numberText;
        private Text _replicatorMarker;
        private Camera _camera;
        private CanvasGroup _canvasGroup;

        /// <summary>MV-788: optional "this is Max's current target" hook — null for every existing call
        /// site (no such tracking exists in this codebase yet; see the ticket's own PR notes), so it is
        /// simply never true until something wires it up. Kept as a hook rather than omitted so the
        /// visibility rule below reads as the ticket's actual two-trigger design, not just "on damage".</summary>
        private System.Func<bool> _isTarget;

        /// <summary>MV-788: Max's own bar and an AreaGate's additionally desaturate their healthy-band
        /// colour — see <see cref="HealthBarColor.At"/>'s own doc comment for why those two specifically.</summary>
        private bool _desaturateWhenHealthy;

        /// <summary>MV-788: seconds since this bar's last damage/target trigger — seeded already past
        /// <see cref="TriggerHoldSeconds"/> + <see cref="TriggerFadeSeconds"/> so a freshly built,
        /// untouched bar starts at alpha 0 without needing an infinity sentinel.</summary>
        private float _secondsSinceTrigger = TriggerHoldSeconds + TriggerFadeSeconds;

        private float _lastNormalizedHealth = -1f;
        private bool _hasNormalizedHealthBaseline;

        // Optional secondary gauge stacked ABOVE the life bar (YT-121 — Max's water level). Null for
        // robots, who carry only a life bar.
        private System.Func<float> _secondary;
        private Color _secondaryColor;
        private Image _secondaryFill;

        private float _worldWidth;
        private float _heightAboveCentre;
        private int _shownHp = int.MinValue;
        private string _shownName;
        private bool _alwaysShow;
        private bool _isPlayerBar;
        private bool _groupable;
        private Canvas _canvasComponent;

        /// <summary>MV-740: an area gate's pill has no HP figure a player can interpret — Lee's first
        /// World 2 playthrough read every gate as "GATE 74" and "74 means nothing" to him. True for
        /// every other unit (Max, robots), which is why this defaults on rather than becoming a new
        /// required Attach() argument every existing call site would have to learn about.</summary>
        private bool _showNumber = true;

        /// <summary>MV-569: forces the bar off regardless of <see cref="_alwaysShow"/> or the unit's own
        /// health — for a condition-locked gate (<see cref="MaxWorlds.Arena.AreaGate.Locked"/>), whose
        /// health can never move under fire, a full bar that never depletes is the clearest possible
        /// signal that the game is broken, not that a lock is waiting on something else.</summary>
        private bool _forceHidden;

        /// <summary>MV-571: hide the bar strip (outline, track, fill, HP number, water gauge) while
        /// leaving the name label showing — a condition-locked gate has no HP worth drawing, but it
        /// still has something to say, and the label is what says it. Unlike
        /// <see cref="_forceHidden"/>, which takes the whole bar (label included) off screen, this
        /// leaves the pivot itself alone so <see cref="Refresh"/> keeps the label live.</summary>
        private bool _barHiddenKeepLabel;

        /// <summary>Extra world-up metres from the MV-473 de-clutter pass (<see cref="WorldHealthBarDeclutter"/>)
        /// — zero unless this bar is currently clustered with another showing bar. Added on top of
        /// <see cref="_heightAboveCentre"/> every frame in <see cref="SyncToBody"/>, never baked into it,
        /// so it tracks the cluster living or dying without needing its own reset hook.</summary>
        private float _clutterLift;

        /// <summary>Metres above the unit's origin the bar floats. Read back by the layout tests.</summary>
        public float HeightAboveCentre => _heightAboveCentre;

        /// <summary>MV-473: re-anchor the world-up offset after the fact. A pooled robot's
        /// <see cref="WorldHealthBar.Attach"/> runs in <c>Awake</c>, before the spawner's
        /// <c>RobotEnemy.Apply</c> stamps the real archetype (same ordering gap the ReadoutName
        /// re-read in <see cref="Refresh"/> already works around) — so a per-kind height needs a way
        /// to land after the kind is actually known.</summary>
        public void SetHeightAboveCentre(float heightAboveCentre) => _heightAboveCentre = heightAboveCentre;

        /// <summary>Is the bar currently on screen? Exposed so a test can assert the fade rule
        /// without reading pixels.</summary>
        public bool Showing => _pivot != null && _pivot.gameObject.activeSelf;

        /// <summary>MV-788: the whole plate's current opacity (0..1) — 1 while held/always-shown, then
        /// ramping to 0 over <see cref="TriggerFadeSeconds"/> once <see cref="TriggerHoldSeconds"/> has
        /// elapsed with no damage and no longer being the target. Exposed so a test can assert the fade
        /// itself, not just the binary <see cref="Showing"/> it eventually drives.</summary>
        public float VisibilityAlpha => _canvasGroup != null ? _canvasGroup.alpha : 1f;

        /// <summary>Unity draw-order for this bar's world-space canvas (MV-747) — Max's is always
        /// higher than any enemy's. Exposed so a test can assert the ORDERING directly rather than
        /// re-deriving it from on-screen geometry, per the acceptance criterion's own wording.</summary>
        public int SortingOrder => _canvasComponent != null ? _canvasComponent.sortingOrder : 0;

        /// <summary>
        /// Hang a bar over <paramref name="owner"/>.
        ///
        /// <paramref name="alwaysShow"/> is true for Max: you should always be able to find your own
        /// health without waiting to be hit. Robots earn their bar by taking damage.
        /// </summary>
        public static WorldHealthBar Attach(GameObject owner, IHealthReadout source,
                                            float heightAboveCentre, float worldWidth,
                                            bool alwaysShow = false,
                                            System.Func<float> secondary = null,
                                            Color secondaryColor = default,
                                            bool showNumber = true,
                                            bool isPlayerBar = false,
                                            bool groupable = false,
                                            System.Func<bool> isTarget = null,
                                            bool desaturateWhenHealthy = false)
        {
            if (owner == null || source == null) return null;

            var bar = owner.GetComponent<WorldHealthBar>();
            if (bar == null) bar = owner.AddComponent<WorldHealthBar>();

            bar._source = source;
            bar._heightAboveCentre = heightAboveCentre;
            bar._worldWidth = worldWidth;
            bar._alwaysShow = alwaysShow;
            bar._secondary = secondary;
            bar._secondaryColor = secondaryColor;
            bar._showNumber = showNumber;
            bar._isPlayerBar = isPlayerBar;
            bar._groupable = groupable;
            bar._isTarget = isTarget;
            bar._desaturateWhenHealthy = desaturateWhenHealthy;
            bar.Build();
            return bar;
        }

        /// <summary>Is the secondary (water) gauge present? Exposed for the tests.</summary>
        public bool HasSecondary => _secondaryFill != null;

        /// <summary>MV-569: hide (or release) this bar independent of health/<see cref="_alwaysShow"/>.
        /// Applied immediately when hiding, so a caller never has to wait for the next
        /// <see cref="LateUpdate"/> to see the bar disappear; releasing it lets the next
        /// <see cref="Refresh"/> decide visibility exactly as it always has.</summary>
        public void SetForceHidden(bool hidden)
        {
            _forceHidden = hidden;
            if (hidden && _pivot != null) _pivot.gameObject.SetActive(false);
        }

        /// <summary>Hide the bar strip but keep the name label (MV-571). A condition-locked gate has
        /// no HP worth drawing, but it still has something to say — the label carries it. Only forces
        /// the strip off immediately, same asymmetry as <see cref="SetForceHidden"/>: un-hiding is
        /// picked up by the next <see cref="Refresh"/>, which already re-evaluates visibility every
        /// frame.</summary>
        public void SetBarHiddenKeepLabel(bool hidden)
        {
            _barHiddenKeepLabel = hidden;
            if (hidden && _barVisuals != null) _barVisuals.gameObject.SetActive(false);
        }

        /// <summary>Show/hide the Replicator lure tell (MV-706) above this unit's nameplate. Called on
        /// the discrete state transition (<see cref="MaxWorlds.Enemies.RobotEnemy.SeekReplicator"/>/
        /// <see cref="MaxWorlds.Enemies.RobotEnemy.CancelReplicatorSeeking"/>/consumption), not every
        /// frame.</summary>
        public void SetReplicatorMarker(bool show)
        {
            if (_replicatorMarker != null) _replicatorMarker.gameObject.SetActive(show);
        }

        private void Build()
        {
            if (_pivot != null) return;

            _camera = Camera.main;

            // Cancels the owner's scale WITHOUT taking on any of its rotation (MV-302): a child that
            // both inherits a non-uniform scale AND is then rotated away from its parent's own axes (as
            // the camera-facing pivot below must be) renders SHEARED, not merely stretched --
            // Transform.lossyScale doesn't even report it, since Unity's scale/rotation composition
            // assumes no shear exists. A gate is the one body in this game that is both anisotropically
            // scaled (long, thin) and yaw-rotated (an E/W-wall doorway spins the box 90 degrees), so it
            // is the one case that actually showed the bug; every other unit here is uniform enough in
            // X/Z that the shear was never visible. Leaving this anchor's localRotation at its default
            // identity is what makes the fix work: with zero rotation between it and its parent,
            // cancelling scale here is a same-axis multiply (safe), so everything it parents afterward
            // inherits a PURE rotation with UNIFORM (1,1,1) scale — a combination that can never shear
            // no matter what independent rotation the pivot below applies to face the camera.
            var anchorGo = new GameObject("HealthBarScaleAnchor");
            _scaleAnchor = anchorGo.transform;
            _scaleAnchor.SetParent(transform, false);

            var pivotGo = new GameObject("HealthBar");
            _pivot = pivotGo.transform;
            _pivot.SetParent(_scaleAnchor, false);

            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = _isPlayerBar ? PlayerSortingOrder : EnemySortingOrder;
            _canvasComponent = canvas;
            _canvas = (RectTransform)canvasGo.transform;
            _canvas.SetParent(_pivot, false);
            _canvas.sizeDelta = new Vector2(BarPixelWidth, BarPixelHeight);

            // MV-788: one CanvasGroup over the whole plate (bar + label + number + water gauge) so the
            // damage/target fade dims everything together instead of needing a per-part alpha.
            _canvasGroup = canvasGo.AddComponent<CanvasGroup>();

            // MV-571: everything that reads as "the bar" (outline, track, fill, water gauge, HP
            // number) lives under one container, so SetBarHiddenKeepLabel can hide all of it with a
            // single SetActive while _nameText, parented straight to _canvas, is left alone.
            var barVisualsGo = new GameObject("BarVisuals", typeof(RectTransform));
            _barVisuals = (RectTransform)barVisualsGo.transform;
            _barVisuals.SetParent(_canvas, false);
            Stretch(_barVisuals, 0f);

            // The life bar: a bold outlined capsule filling the whole canvas.
            _fill = BuildCapsule(_barVisuals, HealthBarColor.Ramp(1f), "");

            // The water gauge (YT-121) stacks directly ABOVE the life bar, and gets the same beefed
            // treatment (YT-125). Slightly shorter so the stack reads as "gauge on top, health below"
            // and the life bar stays the dominant one. Built only for Max — robots pass no secondary.
            float nameLift = 4f;
            if (_secondary != null)
            {
                float h = BarPixelHeight * SecondaryHeightFraction;
                var host = new GameObject("Water", typeof(RectTransform)).GetComponent<RectTransform>();
                host.SetParent(_barVisuals, false);
                host.anchorMin = new Vector2(0f, 1f); host.anchorMax = new Vector2(1f, 1f);
                host.pivot = new Vector2(0.5f, 0f);
                host.offsetMin = Vector2.zero; host.offsetMax = Vector2.zero;
                host.sizeDelta = new Vector2(0f, h);
                host.anchoredPosition = new Vector2(0f, 3f);   // a hair above the life bar

                _secondaryFill = BuildCapsule(host, _secondaryColor, "Water ");

                nameLift = h + 8f;   // push the name clear of the water gauge
            }

            _nameText = NewText(_canvas, LabelFontSize, NameColor, TextAnchor.LowerCenter);
            var nr = _nameText.rectTransform;
            nr.anchorMin = nr.anchorMax = new Vector2(0.5f, 1f);
            nr.pivot = new Vector2(0.5f, 0f);
            nr.sizeDelta = new Vector2(LabelPixelWidth, LabelPixelHeight);
            nr.anchoredPosition = new Vector2(0f, nameLift);
            // Text itself is set by Refresh() below (MV-312) — it re-reads ReadoutName every call, so
            // there is no separate "initial" assignment to keep in step with that one.

            // MV-706: the Replicator lure tell — "⇈" above the nameplate while a robot is seeking a
            // hatch instead of Max, the readability tell the craft bible demands for "who is running for
            // the box". Off by default; SetReplicatorMarker toggles it on state change only, not every
            // frame, since it's driven by a discrete state transition, not a continuous value.
            _replicatorMarker = NewText(_canvas, LabelFontSize + 8, ReplicatorMarkerColor, TextAnchor.LowerCenter);
            var mr = _replicatorMarker.rectTransform;
            mr.anchorMin = mr.anchorMax = new Vector2(0.5f, 1f);
            mr.pivot = new Vector2(0.5f, 0f);
            mr.sizeDelta = new Vector2(LabelPixelWidth, LabelPixelHeight);
            mr.anchoredPosition = new Vector2(0f, nameLift + LabelPixelHeight);
            // ASCII only (MV-600): LegacyRuntime.ttf has no coverage for the double-up-arrow glyph the
            // ticket names, so this stands in for it rather than adding a new non-ASCII allow-list entry.
            _replicatorMarker.text = "^^";
            _replicatorMarker.gameObject.SetActive(false);

            // The number sits ON the bar, Brawl-Stars style, so the figure and the length it
            // describes are one object rather than two things to look between.
            _numberText = NewText(_barVisuals, NumberFontSize, Color.white, TextAnchor.MiddleCenter);
            Stretch(_numberText.rectTransform, 0f);
            // MV-740: an area gate's pill has no HP figure worth printing — deactivated outright
            // (not just left untouched) so Refresh() never has stale digits to leave behind.
            if (!_showNumber) _numberText.gameObject.SetActive(false);

            SyncToBody();
            Refresh();
        }

        /// <summary>
        /// Re-derive the metre-space transform from whatever the body currently measures.
        ///
        /// Every frame, not once at build: a robot's scale is stamped on by its archetype AFTER the
        /// component exists (a rusher is 0.8x0.7x0.8, a bruiser 1.15 all round), and a bar sized
        /// before that is a bar sized for the wrong machine. Doing it continuously means there is no
        /// ordering to get right and no re-init to remember on pooled reuse.
        /// </summary>
        private void SyncToBody()
        {
            _scaleAnchor.localScale = WorldBar.Unscale(transform.lossyScale);
            // The anchor above has already cancelled the owner's scale in a shear-free way, so the
            // pivot's own offset is plain world metres now — no further division by the parent's
            // Y-scale needed (and none of the anchor's local ROTATION is ever touched, which is the
            // part that keeps this shear-free; see the comment in Build()).
            _pivot.localPosition = new Vector3(0f, _heightAboveCentre + _clutterLift, 0f);
            _canvas.localScale = Vector3.one * WorldBar.CanvasScaleFor(_worldWidth, BarPixelWidth);
        }

        private void OnEnable() => _active.Add(this);

        private void OnDisable()
        {
            _active.Remove(this);
            _clutterLift = 0f;   // a pooled robot must not come back already lifted from its last cluster
        }

        private void LateUpdate()
        {
            if (_pivot == null || _source == null) return;
            SyncToBody();
            Refresh();
        }

        // ------------------------------------------------------------------ MV-473 de-clutter

        /// <summary>Every bar currently attached, showing or not — <see cref="WorldHealthBarDeclutter"/>
        /// filters to <see cref="Showing"/> itself so this list can stay a flat registry.</summary>
        private static readonly List<WorldHealthBar> _active = new List<WorldHealthBar>();

        /// <summary>
        /// MV-473: nudge SHOWING bars apart when several robots cluster (a hedge choke-point, a
        /// death-surge pile) instead of letting their fixed-height bars stack on top of each other.
        /// O(n²) over only the currently-showing bars — not every pooled robot in the scene, most of
        /// which are inactive or off-screen. <paramref name="clusterRadius"/>/<paramref name="stackStep"/>
        /// are passed in rather than hard-coded here so the one call site (<see cref="WorldHealthBarDeclutter"/>)
        /// is the single place that owns the tuning.
        ///
        /// MV-611: this comment used to bound the pass at "the live population cap (~25)" — that stopped
        /// being true once <c>AreaSpawnQueue</c>'s live-count cap became PER-AREA rather than field-wide
        /// (robots behind the player are never despawned, only ever killed) and every bar here is
        /// <c>alwaysShow</c>, so N is really the field-wide ALIVE count, which can reach the full
        /// accumulated population (~180 by area 15). Left O(n²) rather than reached for a spatial
        /// partition: this pass is a cosmetic bar-declutter nudge, not gameplay, and even that worst
        /// case is tens of thousands of cheap squared-distance compares — comfortably inside a 60fps
        /// budget on its own (see <see cref="LastShowingCount"/> /
        /// <see cref="WorldHealthBarDeclutter.LastResolveMicroseconds"/> for the actual measured cost,
        /// not this comment's word for it). If the despawn-behind-the-player question (explicitly out
        /// of MV-611's scope) is ever resolved the other way, this pass shrinks back down on its own —
        /// nothing here would need to change.
        ///
        /// Rank, not a physical shove: each bar counts how many OTHER showing bars within
        /// <paramref name="clusterRadius"/> have a lower <see cref="Object.GetInstanceID"/>, and lifts
        /// by that rank × <paramref name="stackStep"/>. Instance ID is stable for the life of a pooled
        /// GameObject, so two robots standing together stack in a fixed order instead of fighting over
        /// who goes on top frame to frame — the flicker a mutual "push apart by whoever's closer"
        /// scheme would produce.
        /// </summary>
        internal static void ResolveClutter(float clusterRadius, float stackStep)
        {
            // Showing-only, gathered once so the O(n²) pass below never touches a hidden/pooled bar.
            _showingScratch.Clear();
            for (int i = 0; i < _active.Count; i++)
                if (_active[i].Showing) _showingScratch.Add(_active[i]);
            LastShowingCount = _showingScratch.Count;

            float clusterRadiusSqr = clusterRadius * clusterRadius;
            for (int i = 0; i < _showingScratch.Count; i++)
            {
                var bar = _showingScratch[i];
                Vector3 pos = bar.transform.position;
                int rank = 0;
                for (int j = 0; j < _showingScratch.Count; j++)
                {
                    if (i == j) continue;
                    var other = _showingScratch[j];
                    Vector3 d = other.transform.position - pos;
                    d.y = 0f;   // cluster test is planar — two robots stacked in height alone aren't visually crowded
                    if (d.sqrMagnitude <= clusterRadiusSqr && other.GetInstanceID() < bar.GetInstanceID())
                        rank++;
                }
                bar._clutterLift = rank * stackStep;
            }
        }

        private static readonly List<WorldHealthBar> _showingScratch = new List<WorldHealthBar>();

        /// <summary>How many bars the last <see cref="ResolveClutter"/> pass actually compared — the
        /// real N behind <see cref="WorldHealthBarDeclutter.LastResolveMicroseconds"/>, since
        /// <c>RobotEnemy.ActiveCount</c> undercounts a pose-held capture rig (a disabled RobotEnemy
        /// still carries a live, showing bar).</summary>
        public static int LastShowingCount { get; private set; }

        // ------------------------------------------------------------------ MV-747 nameplate grouping

        /// <summary>XZ metres within which two SHOWING, same-kind <c>groupable</c> bars merge into one
        /// combined plate. Wider than <see cref="WorldHealthBarDeclutter"/>'s own cluster-lift radius:
        /// that pass staggers bars that are merely crowded, this one COLLAPSES bars that are the same
        /// kind of robot — the actual "SALVAGE CRAB behind SALVAGE CRAB behind SALVAGE CRAB" stack
        /// MV-747 reported.</summary>
        public const float DefaultGroupRadius = 6f;

        /// <summary>Most nameplates drawn at once, post-grouping (MV-747 change item 3) — a HUD with
        /// more than a handful of readable plates stops being readable at all. Beyond this, only the
        /// candidates nearest the reference position (the camera, in production) stay drawn; the rest
        /// hide exactly like a non-leader group member already does.</summary>
        public const int DefaultPlateCap = 10;

        private sealed class GroupInfo
        {
            public WorldHealthBar Leader;
            public int Count;
            public float HealthCurrentSum;
            public float NormalizedSum;
            public bool CapVisible;
        }

        private static readonly List<WorldHealthBar> _groupScratch = new List<WorldHealthBar>();
        private static readonly List<int> _unionParent = new List<int>();
        private static readonly List<GroupInfo> _groupInfos = new List<GroupInfo>();
        private static readonly List<GroupInfo> _capScratch = new List<GroupInfo>();

        private static int Find(List<int> parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static void Union(List<int> parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra != rb) parent[ra] = rb;
        }

        /// <summary>
        /// MV-747: collapse SHOWING, <c>groupable</c> bars of the same kind
        /// (<see cref="IHealthReadout.ReadoutName"/>) within <paramref name="groupRadius"/> into one
        /// combined plate — the fix for six Salvage Crabs drawing three overlapping "SALVAGE CRAB"
        /// strings on top of each other and on top of Max's own bar. Grouping is TRANSITIVE (A-B and
        /// B-C within radius merges all three even though A and C may not be directly in range) and
        /// recomputed fresh from current positions every call — never tracked as sticky state — so a
        /// group splits back into individual plates the instant its members move apart, with no
        /// separate "ungroup" step to keep in sync.
        ///
        /// Only bars passed <c>groupable: true</c> at <see cref="Attach"/> (robots) take part — Max's
        /// own bar and an area-gate's pill are never folded into a crowd.
        ///
        /// Beyond <paramref name="plateCap"/> resolved plates, only the ones nearest
        /// <paramref name="referencePosition"/> stay drawn; the same "hide the pivot" mechanism a
        /// non-leader group member already uses.
        /// </summary>
        internal static void ResolveGroups(float groupRadius, int plateCap, Vector3 referencePosition)
        {
            _groupScratch.Clear();
            for (int i = 0; i < _active.Count; i++)
                if (_active[i]._groupable && _active[i].Showing) _groupScratch.Add(_active[i]);

            int n = _groupScratch.Count;
            _unionParent.Clear();
            for (int i = 0; i < n; i++) _unionParent.Add(i);

            float radiusSqr = groupRadius * groupRadius;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (_groupScratch[i]._source.ReadoutName != _groupScratch[j]._source.ReadoutName) continue;
                    Vector3 d = _groupScratch[i].transform.position - _groupScratch[j].transform.position;
                    d.y = 0f;   // grouping, like the cluster test above, is planar
                    if (d.sqrMagnitude <= radiusSqr) Union(_unionParent, i, j);
                }
            }

            _groupInfos.Clear();
            for (int i = 0; i < n; i++) _groupInfos.Add(null);

            for (int i = 0; i < n; i++)
            {
                int root = Find(_unionParent, i);
                var info = _groupInfos[root];
                if (info == null)
                {
                    info = new GroupInfo();
                    _groupInfos[root] = info;
                }
                var bar = _groupScratch[i];
                info.Count++;
                info.HealthCurrentSum += bar._source.HealthCurrent;
                info.NormalizedSum += bar._source.HealthNormalized;
                // Same deterministic tie-break as ResolveClutter's rank: lowest instance ID wins, so
                // the same group leads with the same bar frame to frame instead of flickering.
                if (info.Leader == null || bar.GetInstanceID() < info.Leader.GetInstanceID())
                    info.Leader = bar;
            }

            _capScratch.Clear();
            for (int i = 0; i < n; i++)
                if (_groupInfos[i] != null) _capScratch.Add(_groupInfos[i]);

            _capScratch.Sort((a, b) =>
                (a.Leader.transform.position - referencePosition).sqrMagnitude
                    .CompareTo((b.Leader.transform.position - referencePosition).sqrMagnitude));

            for (int i = 0; i < _capScratch.Count; i++)
                _capScratch[i].CapVisible = i < plateCap;

            for (int i = 0; i < n; i++)
            {
                var bar = _groupScratch[i];
                var info = _groupInfos[Find(_unionParent, i)];
                bool isLeader = ReferenceEquals(bar, info.Leader);

                if (!isLeader || !info.CapVisible)
                {
                    bar._pivot.gameObject.SetActive(false);
                    continue;
                }

                bar.ApplyGroupDisplay(info.Count, info.HealthCurrentSum, info.NormalizedSum / info.Count);
            }
        }

        /// <summary>Paints this bar as the leader of a group of <paramref name="count"/> (1 for an
        /// ungrouped bar) — the merged "<c>NAME xN</c>" label, the group's summed HP figure, and a
        /// fill reading the group's average normalized health. Writes straight to the Text/Image
        /// components rather than through <see cref="Refresh"/>'s per-instance diff cache: a former
        /// leader that drops back to a group of one must show its own plain name on the very next
        /// call, not stay stuck on a stale "×N" until its own HP happens to change.</summary>
        private void ApplyGroupDisplay(int count, float healthCurrentSum, float normalizedAvg)
        {
            // ASCII only (MV-600): LegacyRuntime.ttf has no glyph for U+00D7 outside the two files
            // MV-600 already allow-listed (MapScreen.cs/WeaponsScreen.cs) — a lowercase "x" reads the
            // same way ("SALVAGE CRAB x3") without risking a blank gap where the multiply sign would be.
            _nameText.text = count > 1 ? $"{_source.ReadoutName} x{count}" : _source.ReadoutName;

            if (_showNumber)
                _numberText.text = Mathf.Max(0, Mathf.CeilToInt(healthCurrentSum)).ToString();

            if (_fill != null)
            {
                float n = Mathf.Clamp01(normalizedAvg);
                _fill.fillAmount = n;
                _fill.color = HealthBarColor.At(n, Time.unscaledTime, _desaturateWhenHealthy);
            }
        }

        /// <summary>MV-788: alpha for a bar this many seconds past its last damage/target trigger —
        /// full through <see cref="TriggerHoldSeconds"/>, then a linear fade to 0 over
        /// <see cref="TriggerFadeSeconds"/>. Pure, so a test can assert it without a live clock — same
        /// "time as a parameter, not read off Time.unscaledTime itself" idiom as
        /// <see cref="HealthBarColor.At"/>.</summary>
        internal static float AlphaSinceTrigger(float secondsSinceTrigger) =>
            secondsSinceTrigger <= TriggerHoldSeconds
                ? 1f
                : Mathf.Clamp01(1f - (secondsSinceTrigger - TriggerHoldSeconds) / TriggerFadeSeconds);

        private void Refresh()
        {
            float n = Mathf.Clamp01(_source.HealthNormalized);

            // MV-788: a bar earns its visibility by something happening — taking damage, or being
            // Max's current target — not by health alone. A field of untouched robots each carrying a
            // full bar was exactly the clutter the ticket fixed. _alwaysShow (Max's own bar, an
            // AreaGate's) is untouched and still wins outright, same as before.
            bool triggeredNow = (_hasNormalizedHealthBaseline && n < _lastNormalizedHealth - DamageDetectEpsilon)
                                 || (_isTarget != null && _isTarget());
            if (triggeredNow) _secondsSinceTrigger = 0f;
            else _secondsSinceTrigger += Time.unscaledDeltaTime;
            _lastNormalizedHealth = n;
            _hasNormalizedHealthBaseline = true;

            float visibilityAlpha = _alwaysShow ? 1f : AlphaSinceTrigger(_secondsSinceTrigger);
            if (_canvasGroup != null) _canvasGroup.alpha = visibilityAlpha;

            bool wouldShowBar = _alwaysShow || visibilityAlpha > 0f;
            // MV-571: a bar-hidden-keep-label gate still needs the pivot (and so the label) on screen
            // even though it has nothing bar-shaped to draw — that's the whole point of the flag.
            bool show = !_forceHidden && _source.IsAlive && (_barHiddenKeepLabel || wouldShowBar);

            if (_pivot.gameObject.activeSelf != show) _pivot.gameObject.SetActive(show);

            // Re-read every frame, diffed like the HP figure below (MV-312). A pooled robot's Kind is
            // stamped by RobotEnemy.Apply() AFTER this bar was first Build() — Awake (which attaches
            // the bar) runs before the spawner's Apply call — so the name baked in Build() belongs to
            // whatever kind Awake saw, which for a freshly created robot is always the Rusher default.
            // That is why a Gunner's nameplate shipped reading "RUSHER": it was never wrong per-kind,
            // it was just never refreshed after the real kind arrived.
            string name = _source.ReadoutName;
            if (name != _shownName)
            {
                _shownName = name;
                _nameText.text = name;
            }

            if (!show) return;

            bool showBarVisuals = wouldShowBar && !_barHiddenKeepLabel;
            if (_barVisuals.gameObject.activeSelf != showBarVisuals)
                _barVisuals.gameObject.SetActive(showBarVisuals);

            if (showBarVisuals)
            {
                _fill.fillAmount = n;
                // Shared ramp: cool neutral → yellow → orange → red, flashing when critical (YT-121,
                // MV-788). unscaled time so it keeps pulsing even if the game is paused on a low-health
                // beat.
                _fill.color = HealthBarColor.At(n, Time.unscaledTime, _desaturateWhenHealthy);

                if (_secondaryFill != null && _secondary != null)
                    _secondaryFill.fillAmount = Mathf.Clamp01(_secondary());

                // Only rebuild the string when the printed number actually changes. At ~25 robots a
                // per-frame ToString is 1500 allocations a second for text nobody can read changing.
                if (_showNumber)
                {
                    int hp = Mathf.Max(0, Mathf.CeilToInt(_source.HealthCurrent));
                    if (hp != _shownHp)
                    {
                        _shownHp = hp;
                        _numberText.text = hp.ToString();
                    }
                }
            }

            if (_camera == null) _camera = Camera.main;
            if (_camera != null)
            {
                // Lift the bar up the SCREEN, not just up the world (YT-149). SyncToBody has already
                // re-set the pivot to its world-up anchor this frame, so this rides on top of it and
                // cannot accumulate. See ScreenClearance for why the camera's up axis rather than
                // world up — it is what keeps his head out from under the bar when he runs up-screen.
                _pivot.position += _camera.transform.up * ScreenClearance;
                _pivot.rotation = Quaternion.LookRotation(
                    _pivot.position - _camera.transform.position, Vector3.up);
            }
        }

        /// <summary>
        /// A Brawl-Stars-style capsule bar filling <paramref name="host"/> (YT-125): a solid dark
        /// outline capsule, a translucent dark track inside it, and a coloured fill inset by the
        /// outline width so the dark border shows all the way round. Returns the fill Image, whose
        /// <c>fillAmount</c> is what tracks the value. Used for both the life bar and the water gauge
        /// so they cannot drift apart in style.
        /// </summary>
        private Image BuildCapsule(RectTransform host, Color fillColor, string prefix)
        {
            // Higher-res rounded sprite so the capsule ends stay crisp at the bigger size.
            Sprite capsule = HudTextures.RoundedBox(48, 0.5f);

            var outline = NewImage(host, capsule, OutlineColor, prefix + "Outline");
            Stretch(outline.rectTransform, 0f);

            var track = NewImage(host, capsule, BackColor, prefix + "Back");
            Stretch(track.rectTransform, -OutlinePx);

            var fill = NewImage(host, capsule, fillColor, prefix + "Fill");
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            Stretch(fill.rectTransform, -OutlinePx);
            return fill;
        }

        // ------------------------------------------------------------------ small builders

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-inset, -inset);
            rt.offsetMax = new Vector2(inset, inset);
        }

        private static Image NewImage(Transform parent, Sprite sprite, Color color, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Text NewText(Transform parent, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = HudFont.Get();
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }
    }
}
