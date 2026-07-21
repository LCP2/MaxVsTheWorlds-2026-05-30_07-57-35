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

        private IHealthReadout _source;
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
        private bool _alwaysShow;

        /// <summary>Metres above the unit's origin the bar floats. Read back by the layout tests.</summary>
        public float HeightAboveCentre => _heightAboveCentre;

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

        private void Build()
        {
            if (_pivot != null) return;

            _camera = Camera.main;

            var pivotGo = new GameObject("HealthBar");
            _pivot = pivotGo.transform;
            _pivot.SetParent(transform, false);

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
            _nameText.text = _source.ReadoutName;

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
            Vector3 body = transform.lossyScale;
            _pivot.localScale = WorldBar.Unscale(body);
            _pivot.localPosition = new Vector3(0f, WorldBar.LocalOffsetY(_heightAboveCentre, body.y), 0f);
            _canvas.localScale = Vector3.one * WorldBar.CanvasScaleFor(_worldWidth, BarPixelWidth);
        }

        private void LateUpdate()
        {
            if (_pivot == null || _source == null) return;
            SyncToBody();
            Refresh();
        }

        private void Refresh()
        {
            float n = Mathf.Clamp01(_source.HealthNormalized);
            bool show = _source.IsAlive && (_alwaysShow || n < FullEnough);

            if (_pivot.gameObject.activeSelf != show) _pivot.gameObject.SetActive(show);
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
