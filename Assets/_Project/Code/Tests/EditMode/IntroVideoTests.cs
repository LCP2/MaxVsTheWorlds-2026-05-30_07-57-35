using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-710: the opening cinematic's VIDEO path. <see cref="IntroVideo.ResolveSourceKind"/> is proven
    /// directly (no live player, no committed asset needed), and <see cref="IntroCinematic"/> is proven
    /// to fall back to the box-built beat timeline whenever no source resolves — which is every build
    /// until a video ships — and to share the same skip/restore harness on both paths.
    ///
    /// Built via <see cref="IntroCinematic.Initialize"/> rather than <c>AddComponent</c> + a frame wait:
    /// a MonoBehaviour without <c>[ExecuteAlways]</c> only receives <c>Awake</c> once Unity is actually in
    /// Play Mode, which this EditMode suite never enters (this project bans PlayMode tests entirely —
    /// MV-299/311/330 — they hang headless in batch mode).
    /// </summary>
    public sealed class IntroVideoTests
    {
        // ------------------------------------------------------------------ AC3: source resolution

        [Test]
        public void ResolvesUrlKindOnWebGLWhenTheStreamingFileExists()
        {
            var kind = IntroVideo.ResolveSourceKind(isWebGl: true, hasClip: () => true, hasStreamingFile: () => true);
            Assert.AreEqual(IntroVideoSourceKind.Url, kind,
                "a WebGL build target must resolve to a streamed URL, never a VideoClip.");
        }

        [Test]
        public void ResolvesClipKindOffWebGLWhenTheClipExists()
        {
            var kind = IntroVideo.ResolveSourceKind(isWebGl: false, hasClip: () => true, hasStreamingFile: () => true);
            Assert.AreEqual(IntroVideoSourceKind.Clip, kind,
                "off WebGL must resolve to the Resources VideoClip, never a streamed URL.");
        }

        [Test]
        public void ResolvesNoneOnWebGLWhenNeitherAssetIsPresent()
        {
            var kind = IntroVideo.ResolveSourceKind(isWebGl: true, hasClip: () => false, hasStreamingFile: () => false);
            Assert.AreEqual(IntroVideoSourceKind.None, kind,
                "with no streaming file, a WebGL target must report no source, not a broken URL.");
        }

        [Test]
        public void ResolvesNoneOffWebGLWhenNeitherAssetIsPresent()
        {
            var kind = IntroVideo.ResolveSourceKind(isWebGl: false, hasClip: () => false, hasStreamingFile: () => false);
            Assert.AreEqual(IntroVideoSourceKind.None, kind,
                "with no clip resource, a non-WebGL target must report no source, not a broken clip.");
        }

        // ------------------------------------------------------------------ AC4/AC5: the harness

        private GameObject _camGo;
        private IntroCinematic _intro;

        [SetUp]
        public void SetUp()
        {
            foreach (var stray in Object.FindObjectsByType<IntroCinematic>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            _camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            _camGo.AddComponent<Camera>();

            RenderSettings.fog = true;   // a known state the intro must restore
            IntroCinematic.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            if (_intro != null) Object.DestroyImmediate(_intro.gameObject);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            IntroCinematic.ResetForTests();
        }

        private IntroCinematic Build()
        {
            var go = new GameObject("IntroCinematic");
            _intro = go.AddComponent<IntroCinematic>();
            _intro.Initialize();   // Awake never fires outside Play Mode — see class doc comment
            return _intro;
        }

        [Test]
        public void FallbackBeatTimelineRunsWhenNoVideoSourceResolves()
        {
            // No committed video asset exists yet, so this is the real, unforced resolution.
            var intro = Build();

            Assert.IsFalse(intro.UsingVideo,
                "the box-built beat timeline must be the fallback whenever no video source resolves.");

            intro.Tick(0.1f);
            Assert.AreEqual("space-open", intro.BeatName,
                "the beat timeline never advanced — the no-video fallback is not actually intact.");
        }

        [Test]
        public void ForcedVideoPathIsSelectedOverTheBeatTimeline()
        {
            IntroVideo.OverrideKindForTests = IntroVideoSourceKind.Clip;
            var intro = Build();

            Assert.IsTrue(intro.UsingVideo,
                "IntroCinematic did not route to the video path once a source resolved.");

            intro.Tick(0.1f);
            Assert.AreEqual(-1, intro.BeatIndex,
                "the box beat timeline advanced even though a video source resolved — it must not scrub.");
        }

        // ------------------------------------------------------------------ AC5: skip restores state on BOTH paths

        [Test]
        public void SkipRestoresHudFogAndPlayerControl_OnTheBeatPath()
        {
            var playerGo = new GameObject("Player");
            var player = playerGo.AddComponent<PlayerController>();
            var hudGo = new GameObject("Hud");
            hudGo.AddComponent<HudController>();

            var intro = Build();
            Assert.IsFalse(intro.UsingVideo, "sanity: this case exercises the beat path.");
            Assert.IsFalse(player.enabled, "player control was not suspended for the cinematic.");
            Assert.IsFalse(hudGo.activeSelf, "the HUD was not hidden for the cinematic.");

            intro.Skip();

            Assert.IsTrue(RenderSettings.fog, "Skip did not hand the yard's fog back on the beat path.");
            Assert.IsTrue(player.enabled, "Skip did not return player control on the beat path.");
            Assert.IsFalse(intro.PlayerControlSuspended, "the intro still reports holding control after Skip.");
            Assert.IsTrue(hudGo.activeSelf, "Skip did not bring the HUD back on the beat path.");

            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(hudGo);
        }

        [Test]
        public void SkipRestoresHudFogAndPlayerControl_OnTheVideoPath()
        {
            IntroVideo.OverrideKindForTests = IntroVideoSourceKind.Url;
            var playerGo = new GameObject("Player");
            var player = playerGo.AddComponent<PlayerController>();
            var hudGo = new GameObject("Hud");
            hudGo.AddComponent<HudController>();

            var intro = Build();
            Assert.IsTrue(intro.UsingVideo, "sanity: this case exercises the video path.");
            Assert.IsFalse(player.enabled, "player control was not suspended for the cinematic.");
            Assert.IsFalse(hudGo.activeSelf, "the HUD was not hidden for the cinematic.");

            intro.Skip();

            Assert.IsTrue(RenderSettings.fog, "Skip did not hand the yard's fog back on the video path.");
            Assert.IsTrue(player.enabled, "Skip did not return player control on the video path.");
            Assert.IsFalse(intro.PlayerControlSuspended, "the intro still reports holding control after Skip.");
            Assert.IsTrue(hudGo.activeSelf, "Skip did not bring the HUD back on the video path.");

            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(hudGo);
        }
    }
}
