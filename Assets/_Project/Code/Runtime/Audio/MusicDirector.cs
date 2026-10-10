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
                int i = (int)_currentWorld.Value;
                _bossGenerated.RestartWithClip(_generatedBossCache[i], BossLoopEndSeconds[i]);
            }
        }

        private static float Bars(int count, float bpm) => count * 4f * 60f / bpm;

        // MV-1160: loop end points for each generated track, authored into audio_manifest.json's
        // loopEndSeconds alongside the BPM used to derive them — keep these two in sync by hand, the
        // manifest isn't read at runtime. Index = (int)MusicWorld. Values are the last whole bar before
        // each track's tail (or before clip end, for the two tracks with no detected tail).
        private static readonly float[] ExploreLoopEndSeconds =
        {
            Bars(35, 96f),   // Backyard_explore: no tail detected, last whole bar before the 90.04s clip end
            Bars(31, 84f),   // Stormdrain_explore: no tail detected, last whole bar before the 90.04s clip end
            Bars(24, 70f),   // Reef_explore: tail starts ~82.5s
        };
        private static readonly float[] BossLoopEndSeconds =
        {
            Bars(32, 132f),  // Backyard_boss: tail starts ~58.5s
            Bars(28, 120f),  // Stormdrain_boss: tail starts ~56.75s
            Bars(30, 126f),  // Reef_boss: tail starts ~58.0s
        };

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
                int i = (int)world;
                _exploreGenerated.RestartWithClip(exploreClip, ExploreLoopEndSeconds[i]);
                _bossGenerated.RestartWithClip(bossClip, BossLoopEndSeconds[i]);
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

        /// <summary>MV-1160: the seam-crossfade state machine's pure core, one tick. <paramref
        /// name="activeTimeBeforeTick"/> is where the active pass's own playhead sits before this
        /// tick's <paramref name="deltaTime"/> is applied. Once crossfading, the returned
        /// <c>activeTime</c> is reconstructed as <c>wrapAtSeconds + crossfadeT</c> (clamped to
        /// <paramref name="crossfadeWindowSeconds"/>) rather than accumulated, so it can never exceed
        /// <c>wrapAtSeconds + crossfadeWindowSeconds</c> — for the MV-1160 loop-end path that bound is
        /// exactly <c>loopEndSeconds + 0.075</c>. <see cref="SeamLoopPlayer.Tick(float, float)"/> is a
        /// thin wrapper that applies the result to its two <see cref="AudioSource"/>s; pulling the
        /// decision out as a static function lets an EditMode test drive it with a known clip length
        /// and loop end, without any AudioSource or Time dependency.</summary>
        public static (bool crossfading, float crossfadeT, float activeTime, float activeGain, float inactiveGain, bool swapped)
            AdvanceSeam(bool crossfadingBefore, float crossfadeTBefore, float activeTimeBeforeTick, float deltaTime,
                float wrapAtSeconds, float crossfadeWindowSeconds)
        {
            bool crossfading = crossfadingBefore;
            float crossfadeT = crossfadeTBefore;
            float activeTime = activeTimeBeforeTick;

            if (!crossfading)
            {
                activeTime += deltaTime;
                if (activeTime >= wrapAtSeconds)
                {
                    crossfading = true;
                    crossfadeT = 0f;
                }
            }

            bool swapped = false;
            float activeGain = 1f;
            float inactiveGain = 0f;

            if (crossfading)
            {
                crossfadeT = Mathf.Min(crossfadeT + deltaTime, crossfadeWindowSeconds);
                activeTime = wrapAtSeconds + crossfadeT;
                float p = crossfadeT / crossfadeWindowSeconds;
                activeGain = Mathf.Cos(p * Mathf.PI * 0.5f);
                inactiveGain = Mathf.Sin(p * Mathf.PI * 0.5f);

                if (crossfadeT >= crossfadeWindowSeconds)
                {
                    swapped = true;
                    crossfading = false;
                    crossfadeT = 0f;
                }
            }

            return (crossfading, crossfadeT, activeTime, activeGain, inactiveGain, swapped);
        }

        /// <summary>MV-1137: a generated track doesn't loop cleanly on its own, so this plays it on one
        /// of two <see cref="AudioSource"/>s (<c>loop = false</c> on both) and, some window before the
        /// active one reaches its hand-off point, starts the other from sample 0 and equal-power
        /// crossfades between them over that window, then swaps which one is "active". Two sources
        /// rather than one because starting the next play from 0 needs a clip already loaded and
        /// playing before the first one's hand-off sample — there's no gap to do that in on a single
        /// source.
        ///
        /// MV-1160: when <see cref="RestartWithClip"/> is given a <c>loopEndSeconds</c>, the hand-off
        /// point is that beat-aligned loop end (not the clip's raw end) and the crossfade window
        /// narrows to <see cref="CrossfadeHalfSeconds"/>*2 so the two unrelated-sounding halves of the
        /// tune no longer overlap audibly. Tracks given no <c>loopEndSeconds</c> keep the original
        /// <see cref="LegacyWrapLeadSeconds"/>-before-clip-end, 3s-crossfade behaviour.</summary>
        private sealed class SeamLoopPlayer
        {
            private const float CrossfadeHalfSeconds = 0.075f;
            private const float LegacyWrapLeadSeconds = 3f;

            private readonly AudioSource _a;
            private readonly AudioSource _b;
            private bool _activeIsA = true;
            private bool _crossfading;
            private float _crossfadeT;
            private float? _loopEndSeconds;

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

            public void RestartWithClip(AudioClip clip, float? loopEndSeconds = null)
            {
                _a.Stop();
                _b.Stop();
                _activeIsA = true;
                _crossfading = false;
                _loopEndSeconds = loopEndSeconds;
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

            public void Tick(float targetVolume) => Tick(targetVolume, Time.unscaledDeltaTime);

            // MV-1160: deltaTime is a parameter (rather than read from Time in here) purely so
            // AdvanceSeam, the state machine it drives, can be exercised by an EditMode test with a
            // known clip length and loop end; the only real caller is Tick(float) above.
            internal void Tick(float targetVolume, float deltaTime)
            {
                AudioClip clip = Active.clip;
                if (clip == null) return;

                float wrapAt = _loopEndSeconds.HasValue
                    ? _loopEndSeconds.Value - CrossfadeHalfSeconds
                    : clip.length - LegacyWrapLeadSeconds;
                float window = _loopEndSeconds.HasValue
                    ? CrossfadeHalfSeconds * 2f
                    : LegacyWrapLeadSeconds;

                bool wasCrossfading = _crossfading;
                var step = AdvanceSeam(_crossfading, _crossfadeT, Active.time, deltaTime, wrapAt, window);

                if (!wasCrossfading && step.crossfading)
                {
                    Inactive.clip = clip;
                    Inactive.time = 0f;
                    Inactive.Play();
                }

                Active.volume = targetVolume * step.activeGain;
                Inactive.volume = targetVolume * step.inactiveGain;
                _crossfading = step.crossfading;
                _crossfadeT = step.crossfadeT;

                if (step.swapped)
                {
                    Active.Stop();   // the pass that just finished crossfading out
                    Active.volume = 0f;
                    _activeIsA = !_activeIsA;
                }
            }
        }

        /// <summary>The Settings panel's SOUND-tab Music toggle state (MV-1009). Default ON.</summary>
        public static bool IsMusicOn => DevTuning.Or(DevTuning.MusicOn, 1f) >= 0.5f;

        /// <summary>The Music toggle's setter.</summary>
        public static void SetMusicOn(bool on) => DevTuning.MusicOn = on ? 1f : 0f;
    }
}
