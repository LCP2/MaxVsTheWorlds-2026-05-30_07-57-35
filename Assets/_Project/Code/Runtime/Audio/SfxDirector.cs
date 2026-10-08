using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.UI;
using Cue = MaxWorlds.Audio.SfxCueLibrary.Cue;

namespace MaxWorlds.Audio
{
    /// <summary>
    /// MV-1007: plays every synthesised cue (<see cref="SfxCueLibrary"/>/<see cref="ProcSfx"/>) off the
    /// existing <see cref="HudSignals"/> bus — same "installs itself, listens, gameplay never knows it
    /// exists" idiom as <c>CombatVfx</c>. A pool of 16 2D <see cref="AudioSource"/>s (2D because the
    /// camera is fixed top-down — there is no listener position to pan against), a per-cue rate limit
    /// and ±8% random pitch so a burst of the same cue reads as texture rather than a machine gun.
    ///
    /// Some cues aren't HudSignals events at all: the hose loop and (MV-1136) the beam loop each track a
    /// weapon's own <see cref="WaterBlaster.IsEmitting"/>/<see cref="Undertow.IsEmitting"/> directly
    /// (fading in/out over 0.1s, per the ticket, via the shared <see cref="LoopVoice"/> machinery), and
    /// the UI click cue self-wires onto every uGUI <see cref="Button"/> in the scene rather than
    /// requiring every screen file to call a shared helper — screens build their buttons in their own
    /// <c>Start()</c>, at an order this director can't predict, so it re-scans for newly-built buttons
    /// on a short interval instead.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("audio")]
    public sealed class SfxDirector : MonoBehaviour
    {
        /// <summary>The Settings panel's default before any override (MV-1007 AC4).</summary>
        public const float DefaultSfxVolume = 0.8f;

        private const int PoolSize = 16;
        private const float PitchVariance = 0.08f;   // ±8%, per the ticket
        private const float HoseFadeSeconds = 0.1f;
        private const float ButtonScanIntervalSeconds = 0.5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<SfxDirector>() != null) return;
            new GameObject("SfxDirector").AddComponent<SfxDirector>();
        }

        private readonly Dictionary<Cue, AudioClip> _clips = new Dictionary<Cue, AudioClip>();
        private AudioSource[] _pool;
        private int _poolCursor;

        /// <summary>MV-1136: one implementation serving every cue that tracks a weapon's own
        /// <c>IsEmitting</c> directly instead of a HudSignals event — today the hose loop
        /// (<see cref="WaterBlaster"/>) and the beam loop (<see cref="Undertow"/>).</summary>
        private sealed class LoopVoice
        {
            public Cue Cue;
            public AudioSource Source;
            public Func<bool> IsEmitting;
            public float Volume;
        }

        private LoopVoice[] _loops;

        private readonly HashSet<Button> _wiredButtons = new HashSet<Button>();
        private UnityEngine.Events.UnityAction _onButtonClicked;
        private float _buttonScanCooldown;

        // Per-cue rate limiting: how many voices this cue has started in its current 1-second window.
        // Time.realtimeSinceStartup rather than Time.time — it advances in the Editor outside Play
        // mode too, so an EditMode test can drive this without a running game loop.
        private readonly Dictionary<Cue, float> _windowStart = new Dictionary<Cue, float>();
        private readonly Dictionary<Cue, int> _windowCount = new Dictionary<Cue, int>();

        private void Awake()
        {
            foreach (var cue in SfxCueLibrary.AllCues) _clips[cue] = SfxCueLibrary.ResolveClip(cue);

            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject($"SfxVoice{i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.spatialBlend = 0f;   // 2D — fixed top-down camera, no listener panning
                src.playOnAwake = false;
                _pool[i] = src;
            }

            _loops = new[]
            {
                MakeLoop(Cue.HoseLoop, "SfxHoseLoop", FindIsEmitting<WaterBlaster>(wb => wb.IsEmitting)),
                MakeLoop(Cue.UndertowLoop, "SfxUndertowLoop", FindIsEmitting<Undertow>(u => u.IsEmitting)),
            };

            _onButtonClicked = OnAnyButtonClicked;
        }

        private void OnEnable()
        {
            HudSignals.DamageDealt += OnDamageDealt;
            HudSignals.PlayerHit += OnPlayerHit;
            HudSignals.Pickup += OnPickup;
            HudSignals.SupercellCollected += OnSupercellCollected;
            HudSignals.EnemyKilled += OnEnemyKilled;
            HudSignals.FactoryDestroyed += OnFactoryDestroyed;
            HudSignals.FittingDestroyed += OnFittingDestroyed;
            HudSignals.RocketMuzzle += OnRocketMuzzle;
            HudSignals.RocketImpact += OnRocketImpact;
            HudSignals.MissileImpact += OnMissileImpact;
            HudSignals.ShockPulseLanded += OnShockPulseLanded;
            HudSignals.MaxTeleported += OnMaxTeleported;
            HudSignals.BlinkerTeleported += OnBlinkerTeleported;
            HudSignals.BossEngaged += OnBossEngaged;
            HudSignals.BossDefeated += OnBossDefeated;
            HudSignals.WeaponCoreCollected += OnWeaponCoreCollected;
            HudSignals.FinaleGateCrossed += OnFinaleGateCrossed;
            HudSignals.LppePulseFired += OnLppePulseFired;
            HudSignals.ForceFieldRaised += OnForceFieldRaised;
            HudSignals.ForceFieldPopped += OnForceFieldPopped;
        }

        private void OnDisable()
        {
            // HudSignals is static — a missed -= would keep this object (and its rendered clips) alive
            // across scene reloads, same reasoning as CombatVfx.OnDisable.
            HudSignals.DamageDealt -= OnDamageDealt;
            HudSignals.PlayerHit -= OnPlayerHit;
            HudSignals.Pickup -= OnPickup;
            HudSignals.SupercellCollected -= OnSupercellCollected;
            HudSignals.EnemyKilled -= OnEnemyKilled;
            HudSignals.FactoryDestroyed -= OnFactoryDestroyed;
            HudSignals.FittingDestroyed -= OnFittingDestroyed;
            HudSignals.RocketMuzzle -= OnRocketMuzzle;
            HudSignals.RocketImpact -= OnRocketImpact;
            HudSignals.MissileImpact -= OnMissileImpact;
            HudSignals.ShockPulseLanded -= OnShockPulseLanded;
            HudSignals.MaxTeleported -= OnMaxTeleported;
            HudSignals.BlinkerTeleported -= OnBlinkerTeleported;
            HudSignals.BossEngaged -= OnBossEngaged;
            HudSignals.BossDefeated -= OnBossDefeated;
            HudSignals.WeaponCoreCollected -= OnWeaponCoreCollected;
            HudSignals.FinaleGateCrossed -= OnFinaleGateCrossed;
            HudSignals.LppePulseFired -= OnLppePulseFired;
            HudSignals.ForceFieldRaised -= OnForceFieldRaised;
            HudSignals.ForceFieldPopped -= OnForceFieldPopped;
        }

        private void Update()
        {
            foreach (var loop in _loops) UpdateLoop(loop);
            ScanForNewButtons();
        }

        // --- event handlers ---

        private void OnDamageDealt(Vector3 pos, float amount, bool crit) => PlayCue(Cue.DamageDealt, pos);
        private void OnPlayerHit(Vector3 pos, Vector3 dir, bool isContact) => PlayCue(Cue.PlayerHit, pos);
        private void OnPickup(Vector3 pos, string label, Color color) => PlayCue(Cue.Pickup, pos);
        private void OnSupercellCollected(Vector3 pos, int before, int after) => PlayCue(Cue.SupercellCollected, pos);
        private void OnEnemyKilled(Vector3 pos) => PlayCue(Cue.EnemyKilled, pos);
        private void OnFactoryDestroyed(Vector3 pos) => PlayCue(Cue.FactoryDestroyed, pos);
        private void OnFittingDestroyed(Vector3 pos) => PlayCue(Cue.FittingDestroyed, pos);
        private void OnRocketMuzzle(Vector3 pos, Vector3 forward) => PlayCue(Cue.RocketMuzzle, pos);
        private void OnRocketImpact(Vector3 pos, float damage) => PlayCue(Cue.RocketImpact, pos);
        private void OnMissileImpact(Vector3 pos, float damage) => PlayCue(Cue.MissileImpact, pos);
        private void OnShockPulseLanded(Vector3 pos) => PlayCue(Cue.ShockPulseLanded, pos);
        private void OnMaxTeleported(Vector3 from, Vector3 to) => PlayCue(Cue.MaxTeleported, to);
        private void OnBlinkerTeleported(Vector3 from, Vector3 to) => PlayCue(Cue.BlinkerTeleported, to);
        private void OnBossEngaged(string name, int phases) => PlayCue(Cue.BossEngaged, transform.position);
        private void OnBossDefeated() => PlayCue(Cue.BossDefeated, transform.position);
        private void OnWeaponCoreCollected() => PlayCue(Cue.WeaponCoreCollected, transform.position);
        private void OnFinaleGateCrossed() => PlayCue(Cue.FinaleGateCrossed, transform.position);
        private void OnLppePulseFired(Vector3 pos, Vector3 forward) => PlayCue(Cue.LppePulseFired, pos);
        private void OnForceFieldRaised(Vector3 pos) => PlayCue(Cue.ForceFieldUp, pos);
        private void OnForceFieldPopped(Vector3 pos) => PlayCue(Cue.ForceFieldPop, pos);
        private void OnAnyButtonClicked() => PlayCue(Cue.UiClick, transform.position);

        // --- playback ---

        private void PlayCue(Cue cue, Vector3 worldPos)
        {
            if (IsCueMuted(cue)) return;   // MV-1009: OFF cues never start a voice, checked first — zero cost
            if (!TryConsumeRateLimit(cue)) return;
            if (!_clips.TryGetValue(cue, out var clip) || clip == null) return;

            var source = NextSource();
            source.transform.position = worldPos;
            source.clip = clip;
            source.pitch = 1f + UnityEngine.Random.Range(-PitchVariance, PitchVariance);
            source.volume = MasterVolume();
            source.Play();
        }

        private AudioSource NextSource()
        {
            var source = _pool[_poolCursor];
            _poolCursor = (_poolCursor + 1) % _pool.Length;
            return source;
        }

        private static float MasterVolume() => Mathf.Clamp01(DevTuning.Or(DevTuning.SfxVolume, DefaultSfxVolume));

        /// <summary>Whether the Settings panel's SOUND tab has this cue toggled OFF (MV-1009).</summary>
        public static bool IsCueMuted(Cue cue)
        {
            int mask = (int)DevTuning.Or(DevTuning.MutedSfxCuesMask, 0f);
            return (mask & (1 << (int)cue)) != 0;
        }

        /// <summary>Flips one cue's mute bit (MV-1009) — the SOUND tab's per-cue toggle setter.</summary>
        public static void SetCueMuted(Cue cue, bool muted)
        {
            int mask = (int)DevTuning.Or(DevTuning.MutedSfxCuesMask, 0f);
            mask = muted ? (mask | (1 << (int)cue)) : (mask & ~(1 << (int)cue));
            DevTuning.MutedSfxCuesMask = mask;
        }

        private bool TryConsumeRateLimit(Cue cue)
        {
            float now = Time.realtimeSinceStartup;
            float start = _windowStart.TryGetValue(cue, out var s) ? s : -1f;
            int count = _windowCount.TryGetValue(cue, out var c) ? c : 0;

            if (start < 0f || now - start >= 1f)
            {
                start = now;
                count = 0;
            }

            int limit = SfxCueLibrary.RateLimitPerSecond.TryGetValue(cue, out var l) ? l : PoolSize;
            if (count >= limit)
            {
                _windowStart[cue] = start;
                _windowCount[cue] = count;
                return false;
            }

            count++;
            _windowStart[cue] = start;
            _windowCount[cue] = count;
            return true;
        }

        /// <summary>Test-only instrumentation (MV-1007): voices this cue has started in its current
        /// one-second rate-limit window — proves the per-cue cap holds under a burst without needing
        /// real audio playback.</summary>
        public int VoiceStartsThisWindow(Cue cue) => _windowCount.TryGetValue(cue, out var c) ? c : 0;

        // --- weapon-emitting loops (WaterBlaster/Undertow IsEmitting, not HudSignals events) ---

        private LoopVoice MakeLoop(Cue cue, string goName, Func<bool> isEmitting)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.playOnAwake = false;
            source.loop = true;
            source.clip = _clips[cue];
            source.volume = 0f;
            return new LoopVoice { Cue = cue, Source = source, IsEmitting = isEmitting, Volume = 0f };
        }

        /// <summary>Lazily finds (and caches) the one scene instance of <typeparamref name="T"/>,
        /// retrying every call while it's still null — the same "retried while null" shape the old
        /// hose-only field used, generalised so a second loop can share it.</summary>
        private static Func<bool> FindIsEmitting<T>(Func<T, bool> isEmitting) where T : Component
        {
            T found = null;
            return () =>
            {
                if (found == null) found = FindFirstObjectByType<T>();
                return found != null && isEmitting(found);
            };
        }

        /// <summary>The fade step, pulled out pure and static so it can be unit-tested without a
        /// running loop: moves <paramref name="current"/> toward 1 (emitting) or 0 (not), at
        /// <see cref="HoseFadeSeconds"/>'s rate.</summary>
        public static float NextLoopVolume(float current, bool emitting, float deltaSeconds) =>
            Mathf.MoveTowards(current, emitting ? 1f : 0f, deltaSeconds / HoseFadeSeconds);

        private void UpdateLoop(LoopVoice loop)
        {
            if (IsCueMuted(loop.Cue))
            {
                // MV-1009: muted must stop it immediately, not just gate future starts — an
                // already-playing loop must not ring out its own fade after the toggle flips OFF.
                loop.Volume = 0f;
                if (loop.Source.isPlaying) loop.Source.Stop();
                return;
            }

            loop.Volume = NextLoopVolume(loop.Volume, loop.IsEmitting(), Time.unscaledDeltaTime);

            if (loop.Volume <= 0f)
            {
                if (loop.Source.isPlaying) loop.Source.Stop();
            }
            else
            {
                if (!loop.Source.isPlaying) loop.Source.Play();
                loop.Source.volume = loop.Volume * MasterVolume();
            }
        }

        // --- "any UI button tap" (not a HudSignals event — screens build their own buttons) ---

        private void ScanForNewButtons()
        {
            _buttonScanCooldown -= Time.unscaledDeltaTime;
            if (_buttonScanCooldown > 0f) return;
            _buttonScanCooldown = ButtonScanIntervalSeconds;

            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                if (button == null || _wiredButtons.Contains(button)) continue;
                _wiredButtons.Add(button);
                button.onClick.AddListener(_onButtonClicked);
            }
        }
    }
}
