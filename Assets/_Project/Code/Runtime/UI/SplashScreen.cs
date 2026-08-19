using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Dev;

namespace MaxWorlds.UI
{
    /// <summary>
    /// MV-252: the first thing on screen at cold boot — Lee's <c>Splash.png</c> key art, full-bleed,
    /// cropped (never letterboxed/distorted) to whatever the device's aspect ratio is, then it fades
    /// out to reveal <see cref="HomeScreen"/> underneath (which has already opened itself the same
    /// <c>AfterSceneLoad</c> tick, per its own doc comment).
    ///
    /// Crop keeps the image's top-left corner pinned to the screen's top-left corner and only ever
    /// trims off the right/bottom — that is what keeps the art's open teal-sky region (top-left, per
    /// the ticket) in the safe area for a future title/logo overlay, on every aspect ratio, by
    /// construction rather than by hoping a centred crop happens to miss it.
    ///
    /// Skips itself under <see cref="PressKitDirector.Armed()"/> or
    /// <see cref="MaxWorlds.Dev.UiScreensDirector.Armed()"/> — a filming or fixed-state UI capture run
    /// can't click through it and doesn't want a delay (or a sortingOrder=300 canvas sitting over
    /// everything) before its staged shots, same rationale as HomeScreen (MV-441).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SplashScreen : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (PressKitDirector.Armed() || MaxWorlds.Dev.UiScreensDirector.Armed()) return;
            if (FindFirstObjectByType<SplashScreen>() != null) return;
            new GameObject("SplashScreen").AddComponent<SplashScreen>();
        }

        private const string SpriteResourcePath = "Art/Splash";
        private const float HoldSeconds = 0.9f;
        private const float FadeSeconds = 0.25f;

        private RectTransform _frame;
        private Image _art;
        private CanvasGroup _group;
        private int _lastFrameW, _lastFrameH;

        private void Start()
        {
            var sprite = Resources.Load<Sprite>(SpriteResourcePath);
            if (sprite == null)
            {
                // Nothing to show and nothing to block on — don't leave a dead frozen frame up.
                Destroy(gameObject);
                return;
            }

            Build(sprite);
            StartCoroutine(HoldThenFade());
        }

        private IEnumerator HoldThenFade()
        {
            yield return new WaitForSecondsRealtime(HoldSeconds);

            float t = 0f;
            while (t < FadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = 1f - Mathf.Clamp01(t / FadeSeconds);
                yield return null;
            }

            Destroy(gameObject);
        }

        private void Update()
        {
            // Cheap resize guard (WebGL browser resize / orientation change) — the splash is
            // short-lived but must never show a gap at the frame edge if the viewport moves under it.
            if (_frame == null) return;
            int w = Screen.width, h = Screen.height;
            if (w == _lastFrameW && h == _lastFrameH) return;
            _lastFrameW = w; _lastFrameH = h;
            FitCover(_frame, _art.rectTransform, _art.sprite);
        }

        // ------------------------------------------------------------------ build

        private void Build(Sprite sprite)
        {
            var canvasGo = new GameObject("Splash Canvas", typeof(Canvas), typeof(CanvasGroup), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;   // above HomeScreen (220) and every other screen

            _group = canvasGo.GetComponent<CanvasGroup>();

            _frame = NewStretchedRect("Splash Frame", canvasGo.transform);
            _frame.gameObject.AddComponent<RectMask2D>();

            var artGo = new GameObject("Splash Art", typeof(RectTransform), typeof(Image));
            artGo.transform.SetParent(_frame, false);
            _art = artGo.GetComponent<Image>();
            _art.sprite = sprite;
            _art.preserveAspect = false;   // sized by FitCover below, not Unity's fit-within
            _art.raycastTarget = true;     // eats taps so nothing behind is triggered while it's up

            _lastFrameW = Screen.width; _lastFrameH = Screen.height;
            FitCover(_frame, _art.rectTransform, sprite);
        }

        /// <summary>
        /// "background-size: cover" for a UI Image inside a masked, full-screen frame: scale the art
        /// up (never down below native, never distorted — uniform scale only) until it fully covers
        /// <paramref name="frame"/> in both axes, then pin its top-left corner to the frame's top-left
        /// corner so any overflow is trimmed from the right/bottom only.
        /// </summary>
        public static void FitCover(RectTransform frame, RectTransform art, Sprite sprite)
        {
            Vector2 size = CoverSize(frame.rect.width, frame.rect.height, sprite.rect.width, sprite.rect.height);

            art.anchorMin = new Vector2(0f, 1f);
            art.anchorMax = new Vector2(0f, 1f);
            art.pivot = new Vector2(0f, 1f);
            art.anchoredPosition = Vector2.zero;
            art.sizeDelta = size;
        }

        /// <summary>Pure sizing math (MV-252 EditMode coverage) — the smallest uniform scale-up of a
        /// <paramref name="texW"/> x <paramref name="texH"/> image that fully covers a
        /// <paramref name="containerW"/> x <paramref name="containerH"/> frame with no distortion.</summary>
        public static Vector2 CoverSize(float containerW, float containerH, float texW, float texH)
        {
            if (containerW <= 0f || containerH <= 0f || texW <= 0f || texH <= 0f) return new Vector2(containerW, containerH);

            float containerAspect = containerW / containerH;
            float texAspect = texW / texH;

            return containerAspect > texAspect
                ? new Vector2(containerW, containerW / texAspect)   // match width, overflow height
                : new Vector2(containerH * texAspect, containerH);  // match height, overflow width
        }

        private static RectTransform NewStretchedRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }
    }
}
