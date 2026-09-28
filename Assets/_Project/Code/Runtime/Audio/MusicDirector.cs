using System.Collections;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Audio
{
    /// <summary>
    /// MV-1008: plays <see cref="ProcMusic"/>'s per-world loop, same "installs itself, listens, gameplay
    /// never knows it exists" idiom as <see cref="SfxDirector"/>. One looping <see cref="AudioSource"/>
    /// for the base loop, a second in-sync one for the boss-intensity layer that crossfades in/out on
    /// <see cref="HudSignals.BossEngaged"/>/<see cref="HudSignals.BossDefeated"/>, and a poll of
    /// <c>BackyardPath.ResolvedWorldIndex</c> (same pattern <c>Sentinel.ResolveWorldIndex</c> uses) to
    /// notice a scene reload rather than needing a dedicated "world changed" event.
    ///
    /// Rendering happens via <see cref="ProcMusic.RenderIncremental"/>, spread across frames — never a
    /// real background thread. WebGL (this project's other shipping target alongside iOS) has no
    /// <c>System.Threading</c> support, so a coroutine is the one approach that works everywhere this
    /// project builds.
    /// </summary>
    [DisallowMultipleComponent]
    [PerfSection("audio")]
    public sealed class MusicDirector : MonoBehaviour
    {
        /// <summary>The Settings panel's default before any override (MV-1008 AC3).</summary>
        public const float DefaultMusicVolume = 0.5f;

        private const float BossCrossfadeSeconds = 1f;
        private const float SceneFadeOutSeconds = 0.8f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<MusicDirector>() != null) return;
            new GameObject("MusicDirector").AddComponent<MusicDirector>();
        }

        private AudioSource _mainSource;
        private AudioSource _intensitySource;

        private readonly AudioClip[] _loopCache = new AudioClip[3];
        private readonly AudioClip[] _intensityCache = new AudioClip[3];

        private MusicWorld? _currentWorld;
        private MusicWorld? _renderingWorld;
        private Coroutine _renderRoutine;

        private bool _bossEngaged;
        private float _intensityBlend;     // 0..1, toward _bossEngaged
        private float _sceneFade = 1f;     // 1 = full volume, 0 = faded out for a reload

        private BackyardPath _backyardPath;

        private void Awake()
        {
            _mainSource = BuildSource("MusicMain");
            _intensitySource = BuildSource("MusicIntensity");
        }

        private AudioSource BuildSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;   // 2D — fixed top-down camera, no listener panning
            src.playOnAwake = false;
            src.loop = true;
            src.volume = 0f;
            return src;
        }

        private void OnEnable()
        {
            HudSignals.BossEngaged += OnBossEngaged;
            HudSignals.BossDefeated += OnBossDefeated;
        }

        private void OnDisable()
        {
            HudSignals.BossEngaged -= OnBossEngaged;
            HudSignals.BossDefeated -= OnBossDefeated;
        }

        private void OnBossEngaged(string name, int phases) => _bossEngaged = true;
        private void OnBossDefeated() => _bossEngaged = false;

        private void Update()
        {
            TickWorldTracking();
            TickVolumes();
        }

        private void TickWorldTracking()
        {
            if (_backyardPath == null) _backyardPath = FindFirstObjectByType<BackyardPath>();
            if (_backyardPath == null) return;

            var resolved = (MusicWorld)Mathf.Clamp(_backyardPath.ResolvedWorldIndex, 0, 2);
            if (resolved == _currentWorld || resolved == _renderingWorld) return;

            _renderingWorld = resolved;
            if (_renderRoutine != null) StopCoroutine(_renderRoutine);
            _renderRoutine = StartCoroutine(SwitchToWorld(resolved));
        }

        /// <summary>0.8 s fade to silence, then (once rendered — cached worlds are instant) the new
        /// world's loop, per the ticket. The fade and the render happen concurrently: nothing about
        /// rendering the next world depends on the old one having finished fading out.</summary>
        private IEnumerator SwitchToWorld(MusicWorld world)
        {
            float startFade = _sceneFade;
            float t = 0f;
            while (t < SceneFadeOutSeconds)
            {
                t += Time.unscaledDeltaTime;
                _sceneFade = Mathf.Lerp(startFade, 0f, Mathf.Clamp01(t / SceneFadeOutSeconds));
                yield return null;
            }
            _sceneFade = 0f;

            AudioClip loopClip = _loopCache[(int)world];
            AudioClip intensityClip = _intensityCache[(int)world];
            if (loopClip == null || intensityClip == null)
            {
                yield return ProcMusic.RenderIncremental(world, intensity: false,
                    samples => loopClip = ProcMusic.BuildStereoClip($"Music_{world}", samples));
                yield return ProcMusic.RenderIncremental(world, intensity: true,
                    samples => intensityClip = ProcMusic.BuildStereoClip($"MusicIntensity_{world}", samples));
                _loopCache[(int)world] = loopClip;
                _intensityCache[(int)world] = intensityClip;
            }

            _mainSource.clip = loopClip;
            _intensitySource.clip = intensityClip;
            _mainSource.Play();
            _intensitySource.Play();
            // Keep both loops phase-locked: same clip length (ProcMusic.LoopSampleCount) and started
            // on the same frame, so AudioClip's own loop wrap keeps them in sync without re-syncing.
            _intensitySource.timeSamples = _mainSource.timeSamples;

            _currentWorld = world;
            _renderingWorld = null;
            _sceneFade = 1f;
        }

        private void TickVolumes()
        {
            float target = _bossEngaged ? 1f : 0f;
            _intensityBlend = Mathf.MoveTowards(_intensityBlend, target, Time.unscaledDeltaTime / BossCrossfadeSeconds);

            float master = Mathf.Clamp01(DevTuning.Or(DevTuning.MusicVolume, DefaultMusicVolume));
            if (!IsMusicOn) master = 0f;   // MV-1009: OFF stops the music source immediately, same frame
            _mainSource.volume = master * _sceneFade;
            _intensitySource.volume = master * _sceneFade * _intensityBlend;
        }

        /// <summary>The Settings panel's SOUND-tab Music toggle state (MV-1009). Default ON.</summary>
        public static bool IsMusicOn => DevTuning.Or(DevTuning.MusicOn, 1f) >= 0.5f;

        /// <summary>The Music toggle's setter.</summary>
        public static void SetMusicOn(bool on) => DevTuning.MusicOn = on ? 1f : 0f;
    }
}
