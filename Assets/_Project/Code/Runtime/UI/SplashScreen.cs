using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using MaxWorlds.Dev;
using MaxWorlds.Intro;

namespace MaxWorlds.UI
{
    /// <summary>
    /// MV-252: the first thing on screen at cold boot — cropped (never letterboxed/distorted) to
    /// whatever the device's aspect ratio is, then it fades out to reveal <see cref="HomeScreen"/>
    /// underneath (which has already opened itself the same <c>AfterSceneLoad</c> tick, per its own
    /// doc comment).
    ///
    /// MV-843: the primary boot sequence is now the animated title reveal — <see cref="IntroVideo"/>'s
    /// RenderTexture+RawImage renderer (MV-835), reused rather than a second video path being written,
    /// pointed at <see cref="TitleClipResourcePath"/>/<see cref="TitleStreamingRelativePath"/> instead
    /// of the opening cinematic's film. <c>Splash.png</c> key art (MV-252's original still, full-bleed
    /// crop keeping the art's open teal-sky top-left corner in the safe area) is now only the fallback
    /// (<see cref="ShowStillFallback"/>) for whenever the video errors or stalls — see
    /// <see cref="RunVideo"/>.
    ///
    /// Skips itself under <see cref="PressKitDirector.Armed()"/> or
    /// <see cref="MaxWorlds.Dev.UiScreensDirector.Armed()"/> — a filming or fixed-state UI capture run
    /// can't click through it and doesn't want a delay (or a sortingOrder=300 canvas sitting over
    /// everything) before its staged shots, same rationale as HomeScreen (MV-441).
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("hud")]
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

        /// <summary>MV-843: off WebGL, a <see cref="UnityEngine.Video.VideoClip"/> loaded from here via
        /// <see cref="Resources"/> — same idiom as <see cref="IntroVideo.ClipResourcePath"/>, but for the
        /// title reveal rather than the opening cinematic.</summary>
        public const string TitleClipResourcePath = "Video/title";

        /// <summary>MV-843: on WebGL, the title reveal's path under <c>StreamingAssets</c>.</summary>
        public const string TitleStreamingRelativePath = "Video/title.mp4";

        // The asset is 1280x714 (per the ticket) — same as IntroVideo's own default, but named
        // separately here since the two are unrelated coincidences, not a shared contract.
        private const int TitleTextureWidth = 1280;
        private const int TitleTextureHeight = 714;

        /// <summary>MV-843: how long the final frame holds before the existing fade starts.</summary>
        private const float VideoHoldSeconds = 0.5f;

        /// <summary>MV-843: a stream that has produced no frame within this long is never going to —
        /// fall back to the still rather than hold a black screen indefinitely.</summary>
        private const float VideoStallSeconds = 3f;

        private RectTransform _frame;
        private Image _art;
        private CanvasGroup _group;
        private int _lastFrameW, _lastFrameH;

        private IntroVideo _video;

        /// <summary>MV-843: exposed for EditMode coverage — the resolved video source/render mode, not
        /// the reasoning that picked it.</summary>
        public IntroVideo Video => _video;
        public bool UsingVideo => _video != null && _video.HasSource;

        private void Start() => Initialize();

        /// <summary>The Start work, exposed so an EditMode test can build it directly — Start never fires
        /// outside Play Mode, same rationale as <see cref="IntroCinematic.Initialize"/>.</summary>
        public void Initialize()
        {
            _video = new IntroVideo(null, transform, TitleClipResourcePath, TitleStreamingRelativePath,
                TitleTextureWidth, TitleTextureHeight);

            if (UsingVideo) StartCoroutine(RunVideo());
            else ShowStillFallback();
        }

        private void ShowStillFallback()
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

        /// <summary>
        /// The title reveal: play until it errors, stalls, completes, or is skipped, then fade to
        /// <see cref="HomeScreen"/> — falling back to <see cref="ShowStillFallback"/> instead of the fade
        /// on an error or a stall (AC: "never leave a black screen").
        /// </summary>
        private IEnumerator RunVideo()
        {
            // IntroVideo has no fade of its own (IntroCinematic hard-cuts to its handoff overlay
            // instead) — add a CanvasGroup to its overlay so this can fade it the same way the still
            // fallback fades, rather than writing a second reveal transition.
            var group = _video.Overlay.gameObject.AddComponent<CanvasGroup>();

            float stallClock = 0f;
            bool skipped = false;
            bool stalled = false;

            while (true)
            {
                if (_video.Errored) break;
                // Skip takes priority over the stall clock — a tap before the first frame renders must
                // still be honoured as a skip, never mistaken for a stalled stream.
                if (SkipRequested()) { skipped = true; break; }
                if (_video.IsComplete) break;

                if (!_video.HasShownFrame)
                {
                    stallClock += Time.unscaledDeltaTime;
                    if (stallClock > VideoStallSeconds) { stalled = true; break; }
                }

                yield return null;
            }

            if (_video.Errored || stalled)
            {
                _video.Dispose();
                ShowStillFallback();
                yield break;
            }

            if (!skipped)
            {
                float held = 0f;
                while (held < VideoHoldSeconds)
                {
                    if (SkipRequested()) break;
                    held += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            float t = 0f;
            while (t < FadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(t / FadeSeconds);
                yield return null;
            }

            _video.Dispose();
            Destroy(gameObject);
        }

        /// <summary>A tap, click, pen, or any key skips straight to the fade — same idiom as
        /// <see cref="IntroCinematic"/>'s skip, polled on the New Input System so it works with touch on
        /// device and mouse/keyboard in the editor and on WebGL.</summary>
        private static bool SkipRequested()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
            var ptr = Pointer.current;
            if (ptr != null && ptr.press.wasPressedThisFrame) return true;
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.wasPressedThisFrame) return true;
            return false;
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
