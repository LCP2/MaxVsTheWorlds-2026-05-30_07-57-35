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

        // Near-black, mostly opaque: the outline that makes the capsule pop. The track (unfilled
        // part) is a translucent dark, so a drained bar reads as an empty capsule, not a black slab.
        private static readonly Color OutlineColor = new Color(0.02f, 0.03f, 0.02f, 0.92f);
        private static readonly Color BackColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color NameColor = new Color(1f, 1f, 1f, 0.9f);

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
        private Image _fill;
        private Text _nameText;
        private Text _numberText;
        private Camera _camera;

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

        /// <summary>MV-569: forces the bar off regardless of <see cref="_alwaysShow"/> or the unit's own
        /// health — for a condition-locked gate (<see cref="MaxWorlds.Arena.AreaGate.Locked"/>), whose
        /// health can never move under fire, a full bar that never depletes is the clearest possible
        /// signal that the game is broken, not that a lock is waiting on something else.</summary>
        private bool _forceHidden;

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
                                            Color secondaryColor = default)
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
            _canvas = (RectTransform)canvasGo.transform;
            _canvas.SetParent(_pivot, false);
            _canvas.sizeDelta = new Vector2(BarPixelWidth, BarPixelHeight);

            // The life bar: a bold outlined capsule filling the whole canvas.
            _fill = BuildCapsule(_canvas, HealthBarColor.Ramp(1f), "");

            // The water gauge (YT-121) stacks directly ABOVE the life bar, and gets the same beefed
            // treatment (YT-125). Slightly shorter so the stack reads as "gauge on top, health below"
            // and the life bar stays the dominant one. Built only for Max — robots pass no secondary.
            float nameLift = 4f;
            if (_secondary != null)
            {
                float h = BarPixelHeight * SecondaryHeightFraction;
                var host = new GameObject("Water", typeof(RectTransform)).GetComponent<RectTransform>();
                host.SetParent(_canvas, false);
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

            // The number sits ON the bar, Brawl-Stars style, so the figure and the length it
            // describes are one object rather than two things to look between.
            _numberText = NewText(_canvas, NumberFontSize, Color.white, TextAnchor.MiddleCenter);
            Stretch(_numberText.rectTransform, 0f);

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
        /// which are inactive or off-screen — so the live population cap (~25) bounds it to at most a
        /// few hundred XZ distance checks a frame. <paramref name="clusterRadius"/>/<paramref name="stackStep"/>
        /// are passed in rather than hard-coded here so the one call site (<see cref="WorldHealthBarDeclutter"/>)
        /// is the single place that owns the tuning.
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

        private void Refresh()
        {
            float n = Mathf.Clamp01(_source.HealthNormalized);
            bool show = !_forceHidden && _source.IsAlive && (_alwaysShow || n < FullEnough);

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

            _fill.fillAmount = n;
            // Shared ramp: green → yellow → orange → red, flashing when critical (YT-121). unscaled
            // time so it keeps pulsing even if the game is paused on a low-health beat.
            _fill.color = HealthBarColor.At(n, Time.unscaledTime);

            if (_secondaryFill != null && _secondary != null)
                _secondaryFill.fillAmount = Mathf.Clamp01(_secondary());

            // Only rebuild the string when the printed number actually changes. At ~25 robots a
            // per-frame ToString is 1500 allocations a second for text nobody can read changing.
            int hp = Mathf.Max(0, Mathf.CeilToInt(_source.HealthCurrent));
            if (hp != _shownHp)
            {
                _shownHp = hp;
                _numberText.text = hp.ToString();
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
