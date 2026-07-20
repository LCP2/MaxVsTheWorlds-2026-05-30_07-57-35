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
        // arena. Deliberately narrower than a body: it is a readout, not architecture.
        private const float BarPixelWidth = 180f;
        private const float BarPixelHeight = 20f;
        private const float LabelPixelWidth = 260f;
        private const float LabelPixelHeight = 26f;
        private const int LabelFontSize = 20;
        private const int NumberFontSize = 22;

        /// <summary>Hide the bar once a unit is this close to full. A field of untouched robots each
        /// carrying a full green bar is the clutter the ticket warned about; a bar that appears when
        /// something has been hit is information.</summary>
        private const float FullEnough = 0.999f;

        private static readonly Color BackColor = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color HealthyColor = new Color(0.36f, 0.85f, 0.32f);
        private static readonly Color HurtColor = new Color(0.95f, 0.72f, 0.16f);
        private static readonly Color CriticalColor = new Color(0.92f, 0.24f, 0.20f);
        private static readonly Color NameColor = new Color(1f, 1f, 1f, 0.85f);

        private IHealthReadout _source;
        private Transform _pivot;
        private RectTransform _canvas;
        private Image _fill;
        private Text _nameText;
        private Text _numberText;
        private Camera _camera;

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
                                            bool alwaysShow = false)
        {
            if (owner == null || source == null) return null;

            var bar = owner.GetComponent<WorldHealthBar>();
            if (bar == null) bar = owner.AddComponent<WorldHealthBar>();

            bar._source = source;
            bar._heightAboveCentre = heightAboveCentre;
            bar._worldWidth = worldWidth;
            bar._alwaysShow = alwaysShow;
            bar.Build();
            return bar;
        }

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

            var bg = NewImage(_canvas, HudTextures.RoundedBox(24, 0.5f), BackColor, "Back");
            Stretch(bg.rectTransform, 0f);

            _fill = NewImage(_canvas, HudTextures.RoundedBox(24, 0.5f), HealthyColor, "Fill");
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Horizontal;
            _fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _fill.fillAmount = 1f;
            Stretch(_fill.rectTransform, -3f);

            _nameText = NewText(_canvas, LabelFontSize, NameColor, TextAnchor.LowerCenter);
            var nr = _nameText.rectTransform;
            nr.anchorMin = nr.anchorMax = new Vector2(0.5f, 1f);
            nr.pivot = new Vector2(0.5f, 0f);
            nr.sizeDelta = new Vector2(LabelPixelWidth, LabelPixelHeight);
            nr.anchoredPosition = new Vector2(0f, 3f);
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
            _fill.color = n > 0.6f ? HealthyColor : n > 0.3f ? HurtColor : CriticalColor;

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
