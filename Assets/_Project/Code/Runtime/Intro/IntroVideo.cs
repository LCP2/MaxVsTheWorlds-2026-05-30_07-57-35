using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
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

        /// <summary>MV-835: the render texture's size until <see cref="VideoPlayer.width"/>/<see
        /// cref="VideoPlayer.height"/> report the real clip resolution (1280x714) via <c>prepareCompleted</c>.</summary>
        private const int DefaultTextureWidth = 1280;
        private const int DefaultTextureHeight = 714;

        /// <summary>Test-only: force which source kind the constructor resolves to, bypassing the real
        /// Resources/StreamingAssets lookup — there is no committed video asset for a live test to
        /// exercise. Cleared by <see cref="IntroCinematic.ResetForTests"/>.</summary>
        public static IntroVideoSourceKind? OverrideKindForTests;

        /// <summary>Test-only: skip the actual <see cref="VideoPlayer.Play"/> call in <see cref="Build"/>.
        /// A real, committed film now exists under StreamingAssets (MV-826), and <c>Play()</c> is what
        /// makes the player start opening/decoding it; the CI Linux runner can't decode that container and
        /// logs an error that fails the EditMode run (MV-827). Everything else in <c>Build</c> — the
        /// GameObject, the <see cref="VideoPlayer"/> component, <see cref="Player"/>, <see cref="SourceKind"/>,
        /// <see cref="HasSource"/>, the render texture and overlay — still runs, so <see cref="IntroCinematic"/>'s
        /// routing and skip/restore behaviour on the video path stay fully exercised. Cleared by
        /// <see cref="IntroCinematic.ResetForTests"/>.</summary>
        public static bool SuppressPlaybackForTests;

        public IntroVideoSourceKind SourceKind { get; }

        /// <summary>True once a source resolved — the harness plays this instead of the box timeline.</summary>
        public bool HasSource => SourceKind != IntroVideoSourceKind.None;

        /// <summary>MV-843: true once <see cref="VideoPlayer.errorReceived"/> has fired — distinct from
        /// <see cref="IsComplete"/> (which an error also sets, so an error still ends any playback loop
        /// keyed on completion) so a caller that needs a different outcome for "played to the end" versus
        /// "never played at all" — e.g. the boot splash falling back to its still image — can tell them
        /// apart.</summary>
        public bool Errored { get; private set; }

        /// <summary>MV-843: true once the first decoded frame has been revealed on <see cref="Overlay"/>
        /// — the resolved signal a caller polls to detect a stalled stream (a source that resolved but
        /// never actually produces a frame) rather than guessing from <see cref="Position"/>.</summary>
        public bool HasShownFrame => _frameShown;

        public VideoPlayer Player { get; private set; }

        /// <summary>MV-835: the full-screen overlay the film renders into — a <see cref="RenderTexture"/>
        /// shown on a <see cref="RawImage"/>, since the film's previous camera-near-plane surface produced
        /// no visible output under URP on WebGL (the reported black screen + static green band). Null
        /// whenever no source resolved (<see cref="HasSource"/> false).</summary>
        public Canvas Overlay { get; private set; }

        private RenderTexture _texture;
        private RawImage _rawImage;
        private AspectRatioFitter _fitter;
        private bool _frameShown;

        private readonly string _clipResourcePath;
        private readonly string _streamingRelativePath;
        private readonly int _textureWidth;
        private readonly int _textureHeight;

        /// <summary>Playback position in seconds; 0 while nothing has resolved or started.</summary>
        public double Position => Player != null ? Player.time : 0d;

        /// <summary>True once the clip has played through to its end (<see cref="VideoPlayer.loopPointReached"/>).</summary>
        public bool IsComplete { get; private set; }

        /// <summary>MV-843: <paramref name="clipResourcePath"/>/<paramref name="streamingRelativePath"/>/
        /// <paramref name="textureWidth"/>/<paramref name="textureHeight"/> default to the opening
        /// cinematic's film so <see cref="IntroCinematic"/>'s call site is unchanged; the boot splash
        /// (<see cref="MaxWorlds.UI.SplashScreen"/>) passes its own title-reveal path/size instead of a
        /// second RenderTexture+RawImage renderer being written for it.</summary>
        public IntroVideo(Camera introCam, Transform parent,
            string clipResourcePath = ClipResourcePath, string streamingRelativePath = StreamingRelativePath,
            int textureWidth = DefaultTextureWidth, int textureHeight = DefaultTextureHeight)
        {
            _clipResourcePath = clipResourcePath;
            _streamingRelativePath = streamingRelativePath;
            _textureWidth = textureWidth;
            _textureHeight = textureHeight;

            SourceKind = OverrideKindForTests ?? ResolveSourceKind(
                Application.platform == RuntimePlatform.WebGLPlayer,
                () => ClipExists(_clipResourcePath), () => StreamingFileExists(_streamingRelativePath));

            if (SourceKind == IntroVideoSourceKind.None) return;
            Build(introCam, parent);
        }

        /// <summary>
        /// Source resolution as a pure static function so it is testable without a live player: on
        /// WebGL a streamed URL is the only source a <see cref="VideoPlayer"/> can use there, and
        /// existence can't be confirmed synchronously over HTTP (MV-826), so it is always chosen
        /// unconditionally. Off WebGL, a streaming file (if committed) wins over the Resources
        /// <see cref="VideoClip"/> — both resolve on local disk there, and the streamed path is the one
        /// that actually ships (MV-826) — and reports <see cref="IntroVideoSourceKind.None"/> when
        /// neither asset is present.
        /// </summary>
        public static IntroVideoSourceKind ResolveSourceKind(bool isWebGl, Func<bool> hasClip, Func<bool> hasStreamingFile)
        {
            if (isWebGl) return IntroVideoSourceKind.Url;
            if (hasStreamingFile()) return IntroVideoSourceKind.Url;
            return hasClip() ? IntroVideoSourceKind.Clip : IntroVideoSourceKind.None;
        }

        private static bool ClipExists(string path) => Resources.Load<VideoClip>(path) != null;

        // WebGL serves StreamingAssets over HTTP, not a local disk, so this check only ever resolves
        // correctly in the Editor and Windows standalone (what cc-verify builds) — ResolveSourceKind
        // never calls it on WebGL (MV-826), it always resolves Url unconditionally there instead.
        private static bool StreamingFileExists(string relativePath) => File.Exists(Path.Combine(Application.streamingAssetsPath, relativePath));

        private void Build(Camera introCam, Transform parent)
        {
            var go = new GameObject("IntroVideo");
            go.transform.SetParent(parent, worldPositionStays: false);
            Player = go.AddComponent<VideoPlayer>();
            Player.playOnAwake = false;
            Player.isLooping = false;
            Player.audioOutputMode = VideoAudioOutputMode.None;   // the film ships with no audio track
            Player.loopPointReached += _ => IsComplete = true;
            // A missing or unplayable stream (e.g. a 404 on the WebGL host) must hand over to gameplay,
            // never leave a black screen behind (MV-826).
            Player.errorReceived += (_, __) => { IsComplete = true; Errored = true; };

            // MV-835: the old camera-near-plane surface produced no visible output under URP on WebGL — render into a
            // texture instead and show it on a full-screen overlay (BuildOverlay below). introCam is no
            // longer the film's surface; IntroCinematic blanks it (cullingMask = 0) on this path so it
            // only ever contributes its solid clear behind the overlay.
            _texture = new RenderTexture(_textureWidth, _textureHeight, 0) { name = "IntroVideoRT" };
            Player.renderMode = VideoRenderMode.RenderTexture;
            Player.targetTexture = _texture;
            Player.sendFrameReadyEvents = true;
            Player.frameReady += OnFrameReady;
            Player.prepareCompleted += OnPrepareCompleted;

            BuildOverlay(go.transform);

            if (SourceKind == IntroVideoSourceKind.Clip)
            {
                var clip = Resources.Load<VideoClip>(_clipResourcePath);
                if (clip == null) return;   // a test-forced kind with no real asset behind it — no-op surface
                Player.source = VideoSource.VideoClip;
                Player.clip = clip;
                if (!SuppressPlaybackForTests) Player.Play();
            }
            else
            {
                // Forward slash, no Path.Combine (MV-826): this is a URL, not a local disk path — WebGL
                // serves StreamingAssets over HTTP, where a backslash would break the request.
                string path = Application.streamingAssetsPath + "/" + _streamingRelativePath;
                // The File.Exists guard only resolves correctly off WebGL (see StreamingFileExists) — on
                // WebGL, ResolveSourceKind already chose Url unconditionally, so this must not veto it.
                if (Application.platform != RuntimePlatform.WebGLPlayer && !File.Exists(path)) return;
                Player.source = VideoSource.Url;
                Player.url = path;
                if (!SuppressPlaybackForTests) Player.Play();
            }
        }

        /// <summary>The full-screen surface: a solid black backing (so the screen stays black before the
        /// first frame, and behind the crop on whichever axis the film's aspect doesn't fill), and a
        /// <see cref="RawImage"/> on top sized by an <see cref="AspectRatioFitter"/> in
        /// <see cref="AspectRatioFitter.AspectMode.EnvelopeParent"/> mode — the film fills the screen and
        /// crops rather than ever being letterboxed or squashed.</summary>
        private void BuildOverlay(Transform parent)
        {
            var canvasGo = new GameObject("IntroVideoOverlay");
            canvasGo.transform.SetParent(parent, worldPositionStays: false);
            Overlay = canvasGo.AddComponent<Canvas>();
            Overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            Overlay.sortingOrder = 32000;
            canvasGo.AddComponent<CanvasScaler>();

            var backingGo = new GameObject("Backing");
            backingGo.transform.SetParent(canvasGo.transform, worldPositionStays: false);
            var backingRect = backingGo.AddComponent<RectTransform>();
            backingRect.anchorMin = Vector2.zero;
            backingRect.anchorMax = Vector2.one;
            backingRect.offsetMin = Vector2.zero;
            backingRect.offsetMax = Vector2.zero;
            var backing = backingGo.AddComponent<Image>();
            backing.color = Color.black;
            backing.raycastTarget = false;

            var rawGo = new GameObject("Film");
            rawGo.transform.SetParent(canvasGo.transform, worldPositionStays: false);
            var rawRect = rawGo.AddComponent<RectTransform>();
            rawRect.anchorMin = Vector2.zero;
            rawRect.anchorMax = Vector2.one;
            rawRect.pivot = new Vector2(0.5f, 0.5f);
            rawRect.anchoredPosition = Vector2.zero;
            _rawImage = rawGo.AddComponent<RawImage>();
            _rawImage.texture = _texture;
            _rawImage.raycastTarget = false;
            _rawImage.enabled = false;   // hidden until OnFrameReady — the black backing shows until then
            _fitter = rawGo.AddComponent<AspectRatioFitter>();
            _fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            _fitter.aspectRatio = (float)_textureWidth / _textureHeight;
        }

        /// <summary>Resize the render texture (and the fitter's aspect) once the player reports the
        /// clip's real dimensions, if they differ from the placeholder 1280x714.</summary>
        private void OnPrepareCompleted(VideoPlayer player)
        {
            int w = (int)player.width, h = (int)player.height;
            if (w <= 0 || h <= 0 || (_texture != null && w == _texture.width && h == _texture.height)) return;

            var resized = new RenderTexture(w, h, 0) { name = "IntroVideoRT" };
            player.targetTexture = resized;
            if (_rawImage != null) _rawImage.texture = resized;
            if (_fitter != null) _fitter.aspectRatio = (float)w / h;

            var old = _texture;
            _texture = resized;
            if (old != null)
            {
                old.Release();
                UnityEngine.Object.Destroy(old);
            }
        }

        /// <summary>Reveal the overlay on the first real decoded frame — before this, the render texture
        /// is empty and showing it would draw garbage instead of solid black.</summary>
        private void OnFrameReady(VideoPlayer player, long frame)
        {
            if (_frameShown || frame <= 0) return;
            _frameShown = true;
            if (_rawImage != null) _rawImage.enabled = true;
        }

        /// <summary>MV-835: tear the overlay and release the render texture — called once the intro hands
        /// over or is skipped (<see cref="IntroCinematic.Handoff"/>), never left running past the
        /// cinematic's own lifetime. Idempotent.</summary>
        public void Dispose()
        {
            if (Player != null)
            {
                Player.frameReady -= OnFrameReady;
                Player.prepareCompleted -= OnPrepareCompleted;
            }

            if (Overlay != null)
            {
                var go = Overlay.gameObject;
                if (Application.isPlaying) UnityEngine.Object.Destroy(go);
                else UnityEngine.Object.DestroyImmediate(go);
                Overlay = null;
                _rawImage = null;
                _fitter = null;
            }

            if (_texture != null)
            {
                _texture.Release();
                if (Application.isPlaying) UnityEngine.Object.Destroy(_texture);
                else UnityEngine.Object.DestroyImmediate(_texture);
                _texture = null;
            }
        }
    }
}
