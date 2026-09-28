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
    /// Two cues aren't HudSignals events at all: the hose loop tracks <see cref="WaterBlaster.IsEmitting"/>
    /// directly (fading in/out over 0.1s, per the ticket), and the UI click cue self-wires onto every
    /// uGUI <see cref="Button"/> in the scene rather than requiring every screen file to call a shared
    /// helper — screens build their buttons in their own <c>Start()</c>, at an order this director can't
    /// predict, so it re-scans for newly-built buttons on a short interval instead.
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

        private AudioSource _hoseLoopSource;
        private WaterBlaster _waterBlaster;
        private float _hoseLoopVolume;

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
            foreach (var cue in SfxCueLibrary.AllCues) _clips[cue] = SfxCueLibrary.RenderClip(cue);

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

            var hoseGo = new GameObject("SfxHoseLoop");
            hoseGo.transform.SetParent(transform, false);
            _hoseLoopSource = hoseGo.AddComponent<AudioSource>();
            _hoseLoopSource.spatialBlend = 0f;
            _hoseLoopSource.playOnAwake = false;
            _hoseLoopSource.loop = true;
            _hoseLoopSource.clip = _clips[Cue.HoseLoop];
            _hoseLoopSource.volume = 0f;

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
            UpdateHoseLoop();
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

        // --- the hose loop (WaterBlaster.IsEmitting, not a HudSignals event) ---

        private void UpdateHoseLoop()
        {
            if (_waterBlaster == null)
            {
                _waterBlaster = FindFirstObjectByType<WaterBlaster>();
                if (_waterBlaster == null) return;
            }

            float target = _waterBlaster.IsEmitting ? 1f : 0f;
            _hoseLoopVolume = Mathf.MoveTowards(_hoseLoopVolume, target, Time.unscaledDeltaTime / HoseFadeSeconds);

            if (_hoseLoopVolume <= 0f)
            {
                if (_hoseLoopSource.isPlaying) _hoseLoopSource.Stop();
            }
            else
            {
                if (!_hoseLoopSource.isPlaying) _hoseLoopSource.Play();
                _hoseLoopSource.volume = _hoseLoopVolume * MasterVolume();
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
