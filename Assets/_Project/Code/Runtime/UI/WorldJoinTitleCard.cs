using UnityEngine;
using UnityEngine.UI;

namespace MaxWorlds.UI
{
    /// <summary>
    /// MV-1123 §7: the world-join title card — a full-width dark band at the top of the screen carrying
    /// "WORLD n" (small, cyan) over the world's own name (large, white), shown as the exit corridor's
    /// fade-to-black completes and held through the destination world's own fade-in. Parents onto the
    /// live HUD's own scaled canvas (<see cref="HudController.ActiveCanvas"/>) exactly the way
    /// <see cref="FinaleBanner"/> does, falling back to a standalone scaled canvas when no HUD is built
    /// (a geometry-independent test/fixture).
    /// </summary>
    public sealed class WorldJoinTitleCard : MonoBehaviour
    {
        private const float RefW = 1920f, RefH = 1080f;

        // Fractions of the real, resolved canvas height (ticket's own numbers).
        private const float BandHeightFraction = 0.17f;
        private const float WorldLineSizeFraction = 0.037f;
        private const float NameLineSizeFraction = 0.099f;

        private RectTransform _root;
        private Image _band;
        private Text _worldLine;
        private Text _nameLine;
        private CanvasGroup _group;

        public static WorldJoinTitleCard Create()
        {
            var go = new GameObject("MV-1123 World Join Title Card", typeof(RectTransform), typeof(CanvasGroup));
            var root = (RectTransform)go.transform;

            Canvas hudCanvas = HudController.ActiveCanvas;
            Transform parent;
            if (hudCanvas != null)
            {
                parent = hudCanvas.transform;
            }
            else
            {
                var canvasGo = new GameObject("MV-1123 Title Card Canvas", typeof(Canvas), typeof(CanvasScaler));
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 160;
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

            var card = go.AddComponent<WorldJoinTitleCard>();
            card._root = root;
            card._group = go.GetComponent<CanvasGroup>();
            card._group.alpha = 0f;
            card._group.blocksRaycasts = false;
            card._group.interactable = false;

            card._band = NewImage(root, "Band", new Color(0.03f, 0.04f, 0.05f, 0.88f));
            card._worldLine = BuildLine(root, "WorldLine", new Color(0.4f, 0.95f, 1f));
            card._nameLine = BuildLine(root, "NameLine", Color.white);

            card.Layout();
            return card;
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

        /// <summary>Re-reads the real, resolved canvas height every call — same
        /// <c>CanvasScaler.ScaleWithScreenSize</c> idiom <see cref="FinaleBanner.Layout"/> uses, so a
        /// canvas resize between calls is always reflected.</summary>
        private void Layout()
        {
            Canvas canvas = _root.GetComponentInParent<Canvas>();
            float canvasHeight = canvas != null ? Mathf.Max(1f, ((RectTransform)canvas.transform).rect.height) : RefH;

            float bandHeight = canvasHeight * BandHeightFraction;
            _band.rectTransform.anchorMin = new Vector2(0f, 1f);
            _band.rectTransform.anchorMax = new Vector2(1f, 1f);
            _band.rectTransform.pivot = new Vector2(0.5f, 1f);
            _band.rectTransform.anchoredPosition = Vector2.zero;
            _band.rectTransform.sizeDelta = new Vector2(0f, bandHeight);

            PlaceLine(_worldLine, canvasHeight, bandHeight * 0.32f, canvasHeight * WorldLineSizeFraction);
            PlaceLine(_nameLine, canvasHeight, bandHeight * 0.68f, canvasHeight * NameLineSizeFraction);
        }

        private static void PlaceLine(Text text, float canvasHeight, float centreTopInset, float fontSize)
        {
            text.fontSize = Mathf.Max(1, Mathf.RoundToInt(fontSize));
            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(canvasHeight * 1.5f, fontSize * 1.4f);
            rt.anchoredPosition = new Vector2(0f, -centreTopInset);
        }

        /// <summary>Shows the card with <paramref name="worldLine"/> (e.g. "WORLD 2") and
        /// <paramref name="nameLine"/> (e.g. "STORMDRAIN") at the given alpha.</summary>
        public void Show(string worldLine, string nameLine, float alpha)
        {
            Layout();
            _worldLine.text = worldLine ?? string.Empty;
            _nameLine.text = nameLine ?? string.Empty;
            _group.alpha = Mathf.Clamp01(alpha);
        }

        public void Hide() => _group.alpha = 0f;

        public void DestroySelf()
        {
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }
    }
}
