using System;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace MaxWorlds.Intro
{
    /// <summary>Which asset (if any) the video path resolved to (MV-710).</summary>
    public enum IntroVideoSourceKind { None, Clip, Url }

    /// <summary>
    /// MV-710: the opening cinematic's VIDEO path — plays a pre-rendered clip instead of scrubbing the
    /// box-built acts (<see cref="IntroSpace"/>/<see cref="IntroDescent"/>/<see cref="IntroShed"/>), which
    /// stay committed as the fallback until a real video ships. Self-installing and code-only, like every
    /// other system in this repo (<c>docs/CODE_DRIVEN_SCENES.md</c>) — no scene wiring, no inspector
    /// bindings, and it must build and run with no video asset present at all.
    /// </summary>
    public sealed class IntroVideo
    {
        /// <summary>Off WebGL: a <see cref="VideoClip"/> loaded from here via <see cref="Resources"/>.</summary>
        public const string ClipResourcePath = "Video/intro";

        /// <summary>On WebGL: a <see cref="VideoPlayer"/> can't use a <see cref="VideoClip"/> and must
        /// stream from a URL — this is the file's path under <c>StreamingAssets</c>.</summary>
        public const string StreamingRelativePath = "Video/intro.mp4";

        /// <summary>Test-only: force which source kind <see cref="IntroVideo(Camera, Transform)"/> resolves
        /// to, bypassing the real Resources/StreamingAssets lookup — there is no committed video asset for
        /// a live test to exercise. Cleared by <see cref="IntroCinematic.ResetForTests"/>.</summary>
        public static IntroVideoSourceKind? OverrideKindForTests;

        public IntroVideoSourceKind SourceKind { get; }

        /// <summary>True once a source resolved — the harness plays this instead of the box timeline.</summary>
        public bool HasSource => SourceKind != IntroVideoSourceKind.None;

        public VideoPlayer Player { get; private set; }

        /// <summary>Playback position in seconds; 0 while nothing has resolved or started.</summary>
        public double Position => Player != null ? Player.time : 0d;

        /// <summary>True once the clip has played through to its end (<see cref="VideoPlayer.loopPointReached"/>).</summary>
        public bool IsComplete { get; private set; }

        public IntroVideo(Camera introCam, Transform parent)
        {
            SourceKind = OverrideKindForTests ?? ResolveSourceKind(
                Application.platform == RuntimePlatform.WebGLPlayer, ClipExists, StreamingFileExists);

            if (SourceKind == IntroVideoSourceKind.None) return;
            Build(introCam, parent);
        }

        /// <summary>
        /// Source resolution as a pure static function so it is testable without a live player: off
        /// WebGL prefers a <see cref="VideoClip"/> resource, on WebGL a streamed URL (the only source a
        /// WebGL <see cref="VideoPlayer"/> can use), and reports <see cref="IntroVideoSourceKind.None"/>
        /// when the platform's asset isn't present.
        /// </summary>
        public static IntroVideoSourceKind ResolveSourceKind(bool isWebGl, Func<bool> hasClip, Func<bool> hasStreamingFile)
        {
            if (isWebGl) return hasStreamingFile() ? IntroVideoSourceKind.Url : IntroVideoSourceKind.None;
            return hasClip() ? IntroVideoSourceKind.Clip : IntroVideoSourceKind.None;
        }

        private static bool ClipExists() => Resources.Load<VideoClip>(ClipResourcePath) != null;

        // WebGL serves StreamingAssets over HTTP, not a local disk — this check only resolves correctly
        // in the Editor and Windows standalone (what cc-verify builds). A real WebGL deploy can't confirm
        // the file synchronously, so until a follow-up ticket adds a real streamed-existence check it
        // resolves to None there and the box timeline keeps covering that build target.
        private static bool StreamingFileExists() => File.Exists(Path.Combine(Application.streamingAssetsPath, StreamingRelativePath));

        private void Build(Camera introCam, Transform parent)
        {
            var go = new GameObject("IntroVideo");
            go.transform.SetParent(parent, worldPositionStays: false);
            Player = go.AddComponent<VideoPlayer>();
            Player.playOnAwake = false;
            Player.isLooping = false;
            Player.renderMode = VideoRenderMode.CameraNearPlane;   // a full-screen surface owned by the intro camera
            Player.targetCamera = introCam;
            Player.aspectRatio = VideoAspectRatio.FitOutside;
            Player.loopPointReached += _ => IsComplete = true;

            if (SourceKind == IntroVideoSourceKind.Clip)
            {
                var clip = Resources.Load<VideoClip>(ClipResourcePath);
                if (clip == null) return;   // a test-forced kind with no real asset behind it — no-op surface
                Player.source = VideoSource.VideoClip;
                Player.clip = clip;
                Player.Play();
            }
            else
            {
                string path = Path.Combine(Application.streamingAssetsPath, StreamingRelativePath);
                if (!File.Exists(path)) return;
                Player.source = VideoSource.Url;
                Player.url = path;
                Player.Play();
            }
        }
    }
}
