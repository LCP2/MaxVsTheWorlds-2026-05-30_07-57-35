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
        // MV-1437: every variant per cue, round-robin; voice limits; ducking of the Music/Ambience buses.
        private readonly Dictionary<Cue, AudioClip[]> _variants = new Dictionary<Cue, AudioClip[]>();
        private readonly Dictionary<Cue, int> _lastVariant = new Dictionary<Cue, int>();
        private readonly SfxVoiceTracker _voices = new SfxVoiceTracker();
        private readonly AudioDucking _ducking = new AudioDucking();

        /// <summary>MV-1437: the ducking state machine (test/inspection access).</summary>
        public AudioDucking Ducking => _ducking;
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
        private AudioSource _rigHum;   // MV-1444

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
            foreach (var cue in SfxCueLibrary.AllCues)
            {
                var variants = SfxCueLibrary.ResolveVariants(cue);
                _variants[cue] = variants;
                _clips[cue] = variants[0];
            }

            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject($"SfxVoice{i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.spatialBlend = 0f;   // 2D — fixed top-down camera, no listener panning
                src.playOnAwake = false;
                AudioBus.Route(src, AudioBusId.Sfx);   // MV-1437
                _pool[i] = src;
            }

            _loops = new[]
            {
                MakeLoop(Cue.HoseLoop, "SfxHoseLoop", FindIsEmitting<WaterBlaster>(wb => wb.IsEmitting)),
                MakeLoop(Cue.UndertowLoop, "SfxUndertowLoop", FindIsEmitting<Undertow>(u => u.IsEmitting)),
            };

            // MV-1444: the RIG's hum - on the Ui bus, driven by RigAudioMix rather than a weapon.
            var humGo = new GameObject("SfxRigHum");
            humGo.transform.SetParent(transform, false);
            _rigHum = humGo.AddComponent<AudioSource>();
            _rigHum.spatialBlend = 0f;
            _rigHum.playOnAwake = false;
            _rigHum.loop = true;
            _rigHum.volume = 0f;
            _rigHum.clip = _clips[Cue.RigHum];
            AudioBus.Route(_rigHum, AudioBusId.Ui);

            _onButtonClicked = OnAnyButtonClicked;
        }

        /// <summary>MV-1444: the RIG hum's source (test/inspection access).</summary>
        public AudioSource RigHumSource => _rigHum;

        /// <summary>MV-1444: one frame of the RIG soundscape - advances the shared fade and applies the hum.</summary>
        public void TickRigAudio(bool rigOpen, float dt)
        {
            RigAudioMix.Advance(rigOpen, dt);
            if (_rigHum == null) return;
            float v = IsCueMuted(Cue.RigHum) || !AudioMaster.IsAllSoundOn ? 0f : RigAudioMix.HumGain * RigAudioMix.HumTargetVolume;
            _rigHum.volume = v;
            if (v <= 0f)
            {
                if (_rigHum.isPlaying) _rigHum.Stop();
            }
            else if (!_rigHum.isPlaying) _rigHum.Play();
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
            HudSignals.SentinelArrived += OnSentinelArrived;
            HudSignals.BlinkerTeleported += OnBlinkerTeleported;
            HudSignals.BossEngaged += OnBossEngaged;
            HudSignals.BossDefeated += OnBossDefeated;
            HudSignals.WeaponCoreCollected += OnWeaponCoreCollected;
            HudSignals.FinaleGateCrossed += OnFinaleGateCrossed;
            HudSignals.LppePulseFired += OnLppePulseFired;
            HudSignals.ForceFieldRaised += OnForceFieldRaised;
            HudSignals.ForceFieldPopped += OnForceFieldPopped;
            HudSignals.ShieldDeflected += OnShieldDeflected;   // MV-1380
            HudSignals.DomeBlocked += OnDomeBlocked;           // MV-1380
            HudSignals.CanisterDetonated += OnCanisterDetonated; // MV-1421
            HudSignals.ArcStrikeLanded += OnArcStrikeLanded;     // MV-1426
            HudSignals.PartsCacheUnsealed += OnPartsCacheUnsealed; // MV-1478
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
            HudSignals.SentinelArrived -= OnSentinelArrived;
            HudSignals.BlinkerTeleported -= OnBlinkerTeleported;
            HudSignals.BossEngaged -= OnBossEngaged;
            HudSignals.BossDefeated -= OnBossDefeated;
            HudSignals.WeaponCoreCollected -= OnWeaponCoreCollected;
            HudSignals.FinaleGateCrossed -= OnFinaleGateCrossed;
            HudSignals.LppePulseFired -= OnLppePulseFired;
            HudSignals.ForceFieldRaised -= OnForceFieldRaised;
            HudSignals.ForceFieldPopped -= OnForceFieldPopped;
            HudSignals.ShieldDeflected -= OnShieldDeflected;
            HudSignals.DomeBlocked -= OnDomeBlocked;
            HudSignals.CanisterDetonated -= OnCanisterDetonated;
            HudSignals.ArcStrikeLanded -= OnArcStrikeLanded;
            HudSignals.PartsCacheUnsealed -= OnPartsCacheUnsealed;
        }

        private void Update()
        {
            if (_loops != null) foreach (var loop in _loops) if (loop.Source != null) UpdateLoop(loop);
            ScanForNewButtons();
            AudioMaster.Apply(Application.isBatchMode);   // MV-1467: keeps the master mute across scene reloads
            TickRigAudio(RigAudioMix.RigOpen, Time.unscaledDeltaTime);   // MV-1444

            // MV-1437: advance ducking on real (unscaled) time and push it to the mixer.
            float before = _ducking.CurrentDb;
            _ducking.Advance(Time.unscaledDeltaTime);
            if (_ducking.CurrentDb != before) AudioBus.ApplyDuck(_ducking.CurrentDb);
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
        private void OnSentinelArrived(Vector3 pos) => PlayCue(Cue.SentinelArrived, pos);
        private void OnBlinkerTeleported(Vector3 from, Vector3 to) => PlayCue(Cue.BlinkerTeleported, to);
        private void OnBossEngaged(string name, int phases) => PlayCueHere(Cue.BossEngaged);
        private void OnBossDefeated() => PlayCueHere(Cue.BossDefeated);
        private void OnWeaponCoreCollected() => PlayCueHere(Cue.WeaponCoreCollected);
        private void OnFinaleGateCrossed() => PlayCueHere(Cue.FinaleGateCrossed);
        private void OnLppePulseFired(Vector3 pos, Vector3 forward) => PlayCue(Cue.LppePulseFired, pos);
        private void OnForceFieldRaised(Vector3 pos) => PlayCue(Cue.ForceFieldUp, pos);
        private void OnForceFieldPopped(Vector3 pos) => PlayCue(Cue.ForceFieldPop, pos);
        private void OnShieldDeflected(Vector3 pos, Vector3 dir) => PlayCue(Cue.ShieldBlocked, pos);   // MV-1380
        private void OnDomeBlocked(Vector3 pos) => PlayCue(Cue.ShieldBlocked, pos);                  // MV-1380
        private void OnCanisterDetonated(Vector3 pos) => PlayCue(Cue.CanisterExplosion, pos);   // MV-1421
        private void OnArcStrikeLanded(Vector3 pos) => PlayCue(Cue.ArcStrikeCrack, pos);   // MV-1426
        private void OnPartsCacheUnsealed(Vector3 pos) => PlayCue(Cue.CacheUnsealed, pos);   // MV-1478
        private void OnAnyButtonClicked() => PlayCueHere(Cue.UiClick);

        // --- playback ---

        // MV-1437 leak, closed by MV-1441: an EditMode test that DestroyImmediate()s the director never gets
        // OnDisable (not ExecuteAlways), so the static HudSignals handlers outlive it. Every entry point
        // therefore checks the director is still alive, and a dead one unsubscribes itself.
        private bool IsDeadAndUnsubscribe()
        {
            if (this != null) return false;
            OnDisable();   // only touches statics, safe on a destroyed object
            return true;
        }

        private void PlayCueHere(Cue cue)
        {
            if (IsDeadAndUnsubscribe()) return;
            PlayCue(cue, transform.position);
        }

        private void PlayCue(Cue cue, Vector3 worldPos)
        {
            if (IsDeadAndUnsubscribe()) return;
            if (_pool == null) return;
            if (IsCueMuted(cue)) return;   // MV-1009: OFF cues never start a voice, checked first — zero cost
            if (!TryConsumeRateLimit(cue)) return;
            if (!_clips.TryGetValue(cue, out var clip0) || clip0 == null) return;

            if (!_variants.TryGetValue(cue, out var variants) || variants.Length == 0) return;
            int last = _lastVariant.TryGetValue(cue, out var lv) ? lv : -1;
            int pick = SfxVariants.Next(variants.Length, last);
            var clip = variants[pick] != null ? variants[pick] : clip0;

            int slot = _poolCursor;
            if (!_voices.TryStart(cue, Time.realtimeSinceStartup, clip.length, slot, out int stolen)) return;
            _lastVariant[cue] = pick;
            if (stolen >= 0 && stolen < _pool.Length && _pool[stolen] != null) _pool[stolen].Stop();   // oldest voice stolen at the limit

            var source = NextSource();
            if (source == null) return;   // pooled source destroyed under us
            if (AudioDucking.Ducks(cue)) _ducking.Trigger(clip.length);
            source.outputAudioMixerGroup = AudioBus.Group(cue == Cue.UiClick ? AudioBusId.Ui : AudioBusId.Sfx);
            source.transform.position = worldPos;
            source.clip = clip;
            source.pitch = 1f + UnityEngine.Random.Range(-PitchVariance, PitchVariance);
            source.volume = MasterVolume() * SfxVariants.GainFromUnit(UnityEngine.Random.value);
            source.Play();
        }

        private AudioSource NextSource()
        {
            var source = _pool[_poolCursor];
            _poolCursor = (_poolCursor + 1) % _pool.Length;
            return source;
        }

        private static float MasterVolume() => Mathf.Clamp01(DevTuning.Or(DevTuning.SfxVolume, DefaultSfxVolume));

        // MV-1237: ordinal 24 and up route to DevTuning.MutedSfxCuesMaskHigh instead of ...Mask — see
        // that field's own doc for why a second mask, not a wider one, is what had to change.
        private const int HighMaskStartOrdinal = 24;

        /// <summary>Whether the Settings panel's SOUND tab has this cue toggled OFF (MV-1009).</summary>
        public static bool IsCueMuted(Cue cue)
        {
            int ordinal = (int)cue;
            if (ordinal >= HighMaskStartOrdinal)
            {
                int maskHigh = (int)DevTuning.Or(DevTuning.MutedSfxCuesMaskHigh, 0f);
                return (maskHigh & (1 << (ordinal - HighMaskStartOrdinal))) != 0;
            }

            int mask = (int)DevTuning.Or(DevTuning.MutedSfxCuesMask, 0f);
            return (mask & (1 << ordinal)) != 0;
        }

        /// <summary>Flips one cue's mute bit (MV-1009) — the SOUND tab's per-cue toggle setter.</summary>
        public static void SetCueMuted(Cue cue, bool muted)
        {
            int ordinal = (int)cue;
            if (ordinal >= HighMaskStartOrdinal)
            {
                int bit = 1 << (ordinal - HighMaskStartOrdinal);
                int maskHigh = (int)DevTuning.Or(DevTuning.MutedSfxCuesMaskHigh, 0f);
                maskHigh = muted ? (maskHigh | bit) : (maskHigh & ~bit);
                DevTuning.MutedSfxCuesMaskHigh = maskHigh;
                return;
            }

            int mask = (int)DevTuning.Or(DevTuning.MutedSfxCuesMask, 0f);
            mask = muted ? (mask | (1 << ordinal)) : (mask & ~(1 << ordinal));
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
            AudioBus.Route(source, AudioBusId.Sfx);   // MV-1437
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

            // MV-1443: weapons keep ticking on Update while the RIG/any modal holds timeScale at 0, and a
            // fire input whose release the modal swallowed leaves IsEmitting latched true, so the beam
            // loop droned through the RIG and resumed on close. Paused = silent, snapped, not faded.
            if (Time.timeScale <= 0f)
            {
                loop.Volume = 0f;
                loop.Source.volume = 0f;
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
