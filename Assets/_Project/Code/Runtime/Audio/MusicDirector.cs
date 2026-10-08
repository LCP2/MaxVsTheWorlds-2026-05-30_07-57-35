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

        // MV-1137: generated tracks per world, checked once and cached (null = not generated for this
        // world, fall back to the synthesised path — see EnsureGeneratedLoaded).
        private readonly AudioClip[] _generatedExploreCache = new AudioClip[3];
        private readonly AudioClip[] _generatedBossCache = new AudioClip[3];
        private readonly bool[] _generatedChecked = new bool[3];
        private bool _generatedModeActive;
        private SeamLoopPlayer _exploreGenerated;
        private SeamLoopPlayer _bossGenerated;

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

            _exploreGenerated = new SeamLoopPlayer(BuildSource("MusicExploreGenA"), BuildSource("MusicExploreGenB"));
            _bossGenerated = new SeamLoopPlayer(BuildSource("MusicBossGenA"), BuildSource("MusicBossGenB"));
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

        /// <summary>Resolves and caches whether world has a generated explore+boss pair, once per
        /// world — <see cref="Resources.Load{T}"/> is cheap but there's no reason to repeat it on
        /// every visit to the same world.</summary>
        private void EnsureGeneratedLoaded(MusicWorld world)
        {
            int i = (int)world;
            if (_generatedChecked[i]) return;
            _generatedChecked[i] = true;
            _generatedExploreCache[i] = Resources.Load<AudioClip>("Audio/Music/" + world + "_explore");
            _generatedBossCache[i] = Resources.Load<AudioClip>("Audio/Music/" + world + "_boss");
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

        private void OnBossEngaged(string name, int phases)
        {
            _bossEngaged = true;
            // MV-1137: the generated boss track isn't layered under the explore track like the
            // synthesised intensity layer is — it starts fresh on every engagement, per the ticket.
            if (_generatedModeActive && _currentWorld.HasValue)
            {
                _bossGenerated.RestartWithClip(_generatedBossCache[(int)_currentWorld.Value]);
            }
        }

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

            MusicWorld resolved = ResolveWorld(_backyardPath.ResolvedWorldIndex);
            if (resolved == _currentWorld || resolved == _renderingWorld) return;

            _renderingWorld = resolved;
            if (_renderRoutine != null) StopCoroutine(_renderRoutine);
            _renderRoutine = StartCoroutine(SwitchToWorld(resolved));
        }

        /// <summary>The music a given resolved world index plays (MV-1141) — read off
        /// <see cref="WorldCatalog"/>'s own row rather than a <c>Mathf.Clamp(..., 0, 2)</c> that would
        /// silently hand a fourth world the Reef's track. Extracted as its own method (rather than
        /// inlined in <see cref="TickWorldTracking"/>) so an EditMode test can assert this resolution
        /// without going through <see cref="TickWorldTracking"/>'s own <c>StartCoroutine</c>, which
        /// EditMode cannot run.</summary>
        public static MusicWorld ResolveWorld(int worldIndex) => WorldCatalog.Get(worldIndex).Music;

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

            EnsureGeneratedLoaded(world);
            AudioClip exploreClip = _generatedExploreCache[(int)world];
            AudioClip bossClip = _generatedBossCache[(int)world];

            if (exploreClip != null && bossClip != null)
            {
                _generatedModeActive = true;
                _mainSource.Stop();
                _intensitySource.Stop();
                _exploreGenerated.RestartWithClip(exploreClip);
                _bossGenerated.RestartWithClip(bossClip);
            }
            else
            {
                _generatedModeActive = false;
                _exploreGenerated.Stop();
                _bossGenerated.Stop();

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
            }

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

            if (_generatedModeActive)
            {
                var (mainVolume, bossVolume) = GeneratedVolumes(master, _sceneFade, _intensityBlend);
                _exploreGenerated.Tick(mainVolume);
                _bossGenerated.Tick(bossVolume);
            }
            else
            {
                _mainSource.volume = master * _sceneFade;
                _intensitySource.volume = master * _sceneFade * _intensityBlend;
            }
        }

        /// <summary>MV-1137: a generated explore/boss pair crossfades instead of layering (two
        /// independently generated tracks can't be sample-aligned, so layering them would phase-beat
        /// against each other) — a pure function so the EditMode test can assert it without a running
        /// <see cref="MusicDirector"/> instance.</summary>
        public static (float main, float boss) GeneratedVolumes(float master, float sceneFade, float intensityBlend) =>
            (master * sceneFade * (1f - intensityBlend), master * sceneFade * intensityBlend);

        /// <summary>MV-1137: a generated track doesn't loop cleanly on its own, so this plays it on one
        /// of two <see cref="AudioSource"/>s (<c>loop = false</c> on both) and, <see cref="WrapLeadSeconds"/>
        /// before the active one reaches its end, starts the other from sample 0 and equal-power
        /// crossfades between them over that same window, then swaps which one is "active". Two
        /// sources rather than one because starting the next play from 0 needs a clip already loaded
        /// and playing before the first one's final sample — there's no gap to do that in on a single
        /// source.</summary>
        private sealed class SeamLoopPlayer
        {
            private const float WrapLeadSeconds = 3f;

            private readonly AudioSource _a;
            private readonly AudioSource _b;
            private bool _activeIsA = true;
            private bool _crossfading;
            private float _crossfadeT;

            public SeamLoopPlayer(AudioSource a, AudioSource b)
            {
                _a = a;
                _b = b;
                _a.loop = false;
                _b.loop = false;
                _a.volume = 0f;
                _b.volume = 0f;
            }

            private AudioSource Active => _activeIsA ? _a : _b;
            private AudioSource Inactive => _activeIsA ? _b : _a;

            public void RestartWithClip(AudioClip clip)
            {
                _a.Stop();
                _b.Stop();
                _activeIsA = true;
                _crossfading = false;
                _a.clip = clip;
                _a.time = 0f;
                _a.Play();
            }

            public void Stop()
            {
                _a.Stop();
                _b.Stop();
                _a.volume = 0f;
                _b.volume = 0f;
                _crossfading = false;
            }

            public void Tick(float targetVolume)
            {
                AudioClip clip = Active.clip;
                if (clip == null) return;

                if (!_crossfading && Active.isPlaying && clip.length - Active.time <= WrapLeadSeconds)
                {
                    _crossfading = true;
                    _crossfadeT = 0f;
                    Inactive.clip = clip;
                    Inactive.time = 0f;
                    Inactive.Play();
                }

                if (_crossfading)
                {
                    _crossfadeT += Time.unscaledDeltaTime;
                    float p = Mathf.Clamp01(_crossfadeT / WrapLeadSeconds);
                    float outGain = Mathf.Cos(p * Mathf.PI * 0.5f);
                    float inGain = Mathf.Sin(p * Mathf.PI * 0.5f);
                    Active.volume = targetVolume * outGain;
                    Inactive.volume = targetVolume * inGain;

                    if (p >= 1f)
                    {
                        _crossfading = false;
                        _activeIsA = !_activeIsA;
                        Inactive.Stop();
                        Inactive.volume = 0f;
                    }
                }
                else
                {
                    Active.volume = targetVolume;
                }
            }
        }

        /// <summary>The Settings panel's SOUND-tab Music toggle state (MV-1009). Default ON.</summary>
        public static bool IsMusicOn => DevTuning.Or(DevTuning.MusicOn, 1f) >= 0.5f;

        /// <summary>The Music toggle's setter.</summary>
        public static void SetMusicOn(bool on) => DevTuning.MusicOn = on ? 1f : 0f;
    }
}
