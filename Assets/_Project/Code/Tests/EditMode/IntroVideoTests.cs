using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.CameraRig;
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

        // ------------------------------------------------------------------ MV-719: pre-warm + cross-fade handover

        /// <summary>
        /// MV-719 — the natural end of the beats (last beat crossing TotalDuration) no longer hard-cuts
        /// to gameplay: it pre-warms <c>Camera.main</c> onto <see cref="FixedAngleCameraRig"/>'s resting
        /// pose over the map's <see cref="EntityKind.PlayerSpawn"/> (AC2), reveals it under a named,
        /// sub-half-second cross-fade constant (AC3) that advances on the caller's unscaled <c>dt</c>
        /// regardless of <see cref="Time.timeScale"/> (AC5), and still lets a tap mid-fade jump straight
        /// to the fully-restored state (AC4's "during" case — the other two points, before and after,
        /// are already covered by <see cref="SkipRestoresHudFogAndPlayerControl_OnTheBeatPath"/> and the
        /// existing <c>if (_done) return;</c> guard).
        /// </summary>
        [Test]
        public void HandoverPreWarmsCameraCrossFadesUnderBudgetAndStaysSkippableMidFade()
        {
            foreach (var stray in Object.FindObjectsByType<FixedAngleCameraRig>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            // MV-417: an EditMode run still has whatever scene the Editor had open at launch loaded, and
            // it may carry its own active MainCamera-tagged object — Camera.main can resolve to THAT one
            // instead of SetUp's _camGo. Suppress every ambient MainCamera, then re-activate _camGo (it
            // got caught by the same sweep, being tagged MainCamera itself) so it is the only one left.
            Camera[] suppressed = CameraTestUtil.SuppressAmbientMainCameras();
            _camGo.SetActive(true);

            var rigGo = new GameObject("Rig");
            var playerGo = new GameObject("Player");
            var hudGo = new GameObject("Hud");
            try
            {
                var rig = rigGo.AddComponent<FixedAngleCameraRig>();
                var player = playerGo.AddComponent<PlayerController>();
                hudGo.AddComponent<HudController>();

                var intro = Build();
                Assert.IsFalse(intro.UsingVideo, "sanity: this exercises the beat path's natural end.");

                // Reach the natural end of the beats in one jump — this is what begins the handover.
                intro.Tick(intro.TotalDuration + 0.001f);
                Assert.IsTrue(intro.IsCrossFading,
                    "reaching the end of the beats did not begin the pre-warm + cross-fade handover.");
                Assert.IsTrue(intro.IsPlaying, "the handover must not report done before the reveal finishes.");

                // AC2 — the pre-warmed camera already sits exactly where FixedAngleCameraRig would rest
                // over PlayerSpawn, computed fresh from the live rig and the shipped map, never hard-coded.
                MapData map = MapLibrary.Load(MapLibrary.BackyardSlice);
                MapEntity spawn = map.First(EntityKind.PlayerSpawn);
                rig.RestingPose(spawn.GroundedCenter, out Vector3 expectedPos, out Quaternion expectedRot);
                Assert.Less(Vector3.Distance(expectedPos, _camGo.transform.position), 0.01f,
                    "the pre-warmed camera position does not match FixedAngleCameraRig's resting pose over PlayerSpawn.");
                Assert.Less(Quaternion.Angle(expectedRot, _camGo.transform.rotation), 0.1f,
                    "the pre-warmed camera rotation does not match FixedAngleCameraRig's resting pitch.");

                // AC3 — the cross-fade duration is the named constant, with headroom under the 0.5s budget.
                Assert.Less(IntroCinematic.CrossFadeSeconds, 0.5f,
                    "the named cross-fade constant leaves no headroom under the half-second handover budget.");

                // AC5 — the fade advances on the caller-supplied unscaled dt, not Time.timeScale/deltaTime.
                float prevTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                try
                {
                    intro.Tick(IntroCinematic.CrossFadeSeconds * 0.4f);
                    Assert.IsTrue(intro.IsCrossFading, "sanity: the fade must still be running partway through.");

                    // AC4 (during) — a tap mid cross-fade jumps straight to the fully restored state, never
                    // leaving the HUD hidden or the player suspended.
                    intro.Skip();
                }
                finally { Time.timeScale = prevTimeScale; }

                Assert.IsFalse(intro.IsPlaying, "Skip mid cross-fade did not end the cinematic.");
                Assert.IsTrue(RenderSettings.fog, "Skip mid cross-fade did not restore fog.");
                Assert.IsTrue(player.enabled, "Skip mid cross-fade left the player suspended.");
                Assert.IsTrue(hudGo.activeSelf, "Skip mid cross-fade left the HUD hidden.");

                // AC4 (after) — calling Skip again on an already-handed-off cinematic must stay a no-op.
                intro.Skip();
                Assert.IsTrue(player.enabled, "a second Skip after handoff must not disturb the restored state.");
            }
            finally
            {
                Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(hudGo);
                Object.DestroyImmediate(rigGo);
                CameraTestUtil.RestoreAmbientMainCameras(suppressed);
            }
        }
    }
}
