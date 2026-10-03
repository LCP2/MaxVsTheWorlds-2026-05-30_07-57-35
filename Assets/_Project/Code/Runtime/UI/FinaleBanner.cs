using UnityEngine;
using UnityEngine.UI;

namespace MaxWorlds.UI
{
    /// <summary>
    /// MV-1079: the finale's centre banner — shared by Beat A ("NEW WEAPON" + the catalog's long name)
    /// and Beat B ("EXIT OPEN"). A tiny overlay canvas built entirely in code (same idiom as
    /// <see cref="ResultScreen"/>), shown and hidden by whichever beat is driving it — the HUD itself
    /// stays up underneath throughout both beats (ticket: "the HUD stays on").
    /// </summary>
    public sealed class FinaleBanner : MonoBehaviour
    {
        private Text _line1;
        private Text _line2;
        private CanvasGroup _group;

        public static FinaleBanner Create()
        {
            var go = new GameObject("MV-1079 Finale Banner", typeof(Canvas), typeof(CanvasGroup));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // above the ordinary HUD, which stays visible underneath

            var banner = go.AddComponent<FinaleBanner>();
            banner._group = go.GetComponent<CanvasGroup>();
            banner._group.alpha = 0f;
            banner._group.blocksRaycasts = false;
            banner._group.interactable = false;

            banner._line1 = BuildLine(go.transform, "Line1", 32, new Vector2(0f, 46f));
            banner._line2 = BuildLine(go.transform, "Line2", 56, new Vector2(0f, -10f));
            return banner;
        }

        private static Text BuildLine(Transform parent, string name, int fontSize, Vector2 anchoredPos)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(900f, 64f);
            rt.anchoredPosition = anchoredPos;

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.4f, 0.95f, 1f);
            text.text = string.Empty;
            return text;
        }

        /// <summary>Sets both lines (either may be empty) and the banner's overall alpha for this
        /// instant — the caller (the finale beat) owns the fade-in/hold/fade-out timing and just calls
        /// this every tick with whatever alpha that timing resolves to.</summary>
        public void Show(string line1, string line2, float alpha)
        {
            _line1.text = line1;
            _line2.text = line2;
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
