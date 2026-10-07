using UnityEngine;
using UnityEngine.UI;

namespace MaxWorlds.UI
{
    /// <summary>
    /// MV-1079: the finale's centre banner — shared by Beat A ("NEW WEAPON" + the new primary's own
    /// banner copy) and Beat B ("EXIT OPEN"). MV-1131: now parents onto the live HUD's own scaled canvas
    /// (<see cref="HudController.ActiveCanvas"/>, the one with a <see cref="CanvasScaler"/>) instead of
    /// building a bare overlay canvas of its own — Lee's device observation ("my weapon changed with no
    /// indication") traced in part to the old banner's 32/56 raw-pixel font sizes reading as tiny on a
    /// 2556x1179 phone. Falls back to a standalone scaled canvas when no HUD is built (a
    /// geometry-independent test/fixture), so it stays usable on its own.
    /// </summary>
    public sealed class FinaleBanner : MonoBehaviour
    {
        private const float RefW = 1920f, RefH = 1080f;

        // Fractions of the real, resolved canvas height (MV-1131 ticket's own numbers).
        private const float BandTopFraction = 0.11f;
        private const float BandHeightFraction = 0.23f;
        private const float Line1SizeFraction = 0.046f;
        private const float Line2SizeFraction = 0.105f;
        private const float Line3SizeFraction = 0.034f;

        // MV-1125: Beat B's own "EXIT OPEN" treatment — a shallower full-width band, large white text
        // (not the small cyan Line1 slot Beat A's copy uses) and a green underline. Ticket's own numbers.
        private const float ExitBandHeightFraction = 0.14f;
        private const float ExitTextSizeFraction = 0.093f;
        private const float ExitUnderlineHeightFraction = 0.006f;
        private static readonly Color ExitUnderlineColor = new Color(0.208f, 0.878f, 0.420f); // #35E06B

        private RectTransform _root;
        private Image _band;
        private Text _line1, _line2, _line3;
        private Image _exitUnderline;
        private CanvasGroup _group;

        public Text Line1 => _line1;
        public Text Line2 => _line2;
        public Text Line3 => _line3;

        public static FinaleBanner Create()
        {
            var go = new GameObject("MV-1079 Finale Banner", typeof(RectTransform), typeof(CanvasGroup));
            var root = (RectTransform)go.transform;

            Canvas hudCanvas = HudController.ActiveCanvas;
            Transform parent;
            if (hudCanvas != null)
            {
                parent = hudCanvas.transform;
            }
            else
            {
                // Geometry-independent fallback: no live HUD built, so stand up a scaled canvas of our
                // own rather than go pixel-raw the way this class used to unconditionally.
                var canvasGo = new GameObject("MV-1079 Finale Banner Canvas", typeof(Canvas), typeof(CanvasScaler));
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 150;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(RefW, RefH);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                parent = canvas.transform;
            }

            root.SetParent(parent, worldPositionStays: false);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero; root.offsetMax = Vector2.zero;

            var banner = go.AddComponent<FinaleBanner>();
            banner._root = root;
            banner._group = go.GetComponent<CanvasGroup>();
            banner._group.alpha = 0f;
            banner._group.blocksRaycasts = false;
            banner._group.interactable = false;

            banner._band = NewImage(root, "Band", new Color(0.03f, 0.04f, 0.05f, 0.82f));
            banner._line1 = BuildLine(root, "Line1", new Color(0.4f, 0.95f, 1f));
            banner._line2 = BuildLine(root, "Line2", Color.white);
            banner._line3 = BuildLine(root, "Line3", new Color(0.812f, 0.847f, 0.878f));
            banner._exitUnderline = NewImage(root, "ExitUnderline", ExitUnderlineColor);
            banner._exitUnderline.gameObject.SetActive(false);

            banner.Layout();
            return banner;
        }

        private static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, worldPositionStays: false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Text BuildLine(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, worldPositionStays: false);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.text = string.Empty;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Re-reads the real, resolved canvas height every call (not just at construction) —
        /// the same <c>CanvasScaler.ScaleWithScreenSize</c> hand-sizing idiom <c>MV960HomeLayoutTests</c>
        /// established resizes the canvas AFTER a screen is built, so a static one-time layout would miss
        /// it. "4.6%/10.5%/3.4% of screen height" (ticket's own numbers) is exactly "a fraction of the
        /// canvas's own rect height" — <see cref="CanvasScaler.ScaleWithScreenSize"/> always keeps that
        /// rect's height equal to Screen.height / its own scale factor, so this fraction is a true
        /// screen-height fraction on any aspect, not just the 16:9 the reference resolution assumes.</summary>
        private void Layout()
        {
            Canvas canvas = _root.GetComponentInParent<Canvas>();
            float canvasHeight = canvas != null ? Mathf.Max(1f, ((RectTransform)canvas.transform).rect.height) : RefH;

            float bandHeight = canvasHeight * BandHeightFraction;
            float bandTop = canvasHeight * BandTopFraction;

            PositionTopAnchored(_band.rectTransform, bandTop, bandHeight, stretchWidth: true);
            _band.rectTransform.sizeDelta = new Vector2(0f, bandHeight);

            PlaceLine(_line1, canvasHeight, bandTop + bandHeight * 0.20f, canvasHeight * Line1SizeFraction);
            PlaceLine(_line2, canvasHeight, bandTop + bandHeight * 0.52f, canvasHeight * Line2SizeFraction);
            PlaceLine(_line3, canvasHeight, bandTop + bandHeight * 0.84f, canvasHeight * Line3SizeFraction);
        }

        private static void PositionTopAnchored(RectTransform rt, float topInset, float height, bool stretchWidth)
        {
            rt.anchorMin = stretchWidth ? new Vector2(0f, 1f) : new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(stretchWidth ? 1f : 0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -topInset);
            if (!stretchWidth) rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        }

        private static void PlaceLine(Text text, float canvasHeight, float centreTopInset, float fontSize)
        {
            text.fontSize = Mathf.Max(1, Mathf.RoundToInt(fontSize));
            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(canvasHeight * 1.5f, fontSize * 1.4f);
            rt.anchoredPosition = new Vector2(0f, -centreTopInset);
        }

        /// <summary>Sets all three lines (any may be empty) and the banner's overall alpha for this
        /// instant — the caller (the finale beat) owns the fade-in/hold/fade-out timing and just calls
        /// this every tick with whatever alpha that timing resolves to. Re-lays out every call (MV-1131)
        /// so a canvas resize between calls (a test, or a device rotation) is always reflected.</summary>
        public void Show(string line1, string line2, string line3, float alpha)
        {
            Layout();
            _exitUnderline.gameObject.SetActive(false);
            _line1.text = line1 ?? string.Empty;
            _line2.text = line2 ?? string.Empty;
            _line3.text = line3 ?? string.Empty;
            _group.alpha = Mathf.Clamp01(alpha);
        }

        /// <summary>Two-line overload — Beat B's "EXIT OPEN" never had a third line.</summary>
        public void Show(string line1, string line2, float alpha) => Show(line1, line2, string.Empty, alpha);

        /// <summary>MV-1125, Beat B's own "EXIT OPEN" treatment: a shallower full-width dark band (14% of
        /// the real, resolved canvas height), the centre line large and white (9.3%, not Line1's small
        /// cyan 4.6% the old two-line <see cref="Show(string,string,float)"/> call put it in) with a
        /// green underline beneath it — the fix for AC2(d)'s floor (90 px at 2556x1179), which the old
        /// placement (54 px at that resolution) fell under.</summary>
        public void ShowExitOpen(float alpha)
        {
            LayoutExit();
            _line1.text = string.Empty;
            _line2.text = "EXIT OPEN";
            _line3.text = string.Empty;
            _exitUnderline.gameObject.SetActive(true);
            _group.alpha = Mathf.Clamp01(alpha);
        }

        private void LayoutExit()
        {
            Canvas canvas = _root.GetComponentInParent<Canvas>();
            float canvasHeight = canvas != null ? Mathf.Max(1f, ((RectTransform)canvas.transform).rect.height) : RefH;

            float bandHeight = canvasHeight * ExitBandHeightFraction;
            float bandTop = canvasHeight * BandTopFraction;

            PositionTopAnchored(_band.rectTransform, bandTop, bandHeight, stretchWidth: true);
            _band.rectTransform.sizeDelta = new Vector2(0f, bandHeight);

            float textSize = canvasHeight * ExitTextSizeFraction;
            float textCentreInset = bandTop + bandHeight * 0.5f;
            PlaceLine(_line2, canvasHeight, textCentreInset, textSize);

            float underlineHeight = Mathf.Max(2f, canvasHeight * ExitUnderlineHeightFraction);
            float underlineWidth = textSize * 4.2f;
            RectTransform urt = _exitUnderline.rectTransform;
            urt.anchorMin = new Vector2(0.5f, 1f); urt.anchorMax = new Vector2(0.5f, 1f); urt.pivot = new Vector2(0.5f, 1f);
            urt.sizeDelta = new Vector2(underlineWidth, underlineHeight);
            urt.anchoredPosition = new Vector2(0f, -(textCentreInset + textSize * 0.62f));
        }

        public void Hide() => _group.alpha = 0f;

        public void DestroySelf()
        {
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }
    }
}
