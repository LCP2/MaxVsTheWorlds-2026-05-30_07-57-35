using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Cue = MaxWorlds.Audio.SfxCueLibrary.Cue;

namespace MaxWorlds.Audio
{
    /// <summary>MV-1437: the four mixer buses under Master.</summary>
    public enum AudioBusId { Music, Sfx, Ui, Ambience }

    /// <summary>
    /// MV-1437: the one <see cref="AudioMixer"/> (<c>Resources/Audio/MaxMixer</c>: Master -> Music, Sfx,
    /// Ui, Ambience) and the group every <see cref="AudioSource"/> the game creates is routed to.
    /// Exposed parameters <c>MusicDuck</c> and <c>AmbienceDuck</c> (dB) carry the ducking offset; on a
    /// platform or run where the mixer is absent everything silently no-ops (sources then just play on
    /// the default output, as before this ticket).
    /// </summary>
    public static class AudioBus
    {
        public const string MixerResource = "Audio/MaxMixer";
        public const string MusicDuckParam = "MusicDuck";
        public const string AmbienceDuckParam = "AmbienceDuck";

        private static AudioMixer _mixer;
        private static bool _loaded;

        public static AudioMixer Mixer
        {
            get
            {
                if (!_loaded || (_mixer == null && !ReferenceEquals(_mixer, null))) { _loaded = true; _mixer = Resources.Load<AudioMixer>(MixerResource); }
                return _mixer;
            }
        }

        public static AudioMixerGroup Group(AudioBusId bus)
        {
            var mixer = Mixer;
            if (mixer == null) return null;
            var found = mixer.FindMatchingGroups("Master/" + bus);
            return found != null && found.Length > 0 ? found[0] : null;
        }

        /// <summary>Routes <paramref name="source"/> to <paramref name="bus"/> (no-op without a mixer).</summary>
        public static void Route(AudioSource source, AudioBusId bus) { if (source != null) source.outputAudioMixerGroup = Group(bus); }

        /// <summary>Pushes a ducking offset (dB, 0 or negative) onto the Music and Ambience buses.</summary>
        public static void ApplyDuck(float db)
        {
            var mixer = Mixer;
            if (mixer == null) return;
            mixer.SetFloat(MusicDuckParam, db);
            mixer.SetFloat(AmbienceDuckParam, db);
        }

        /// <summary>The Music bus's current attenuation in dB as the mixer itself reports it.</summary>
        public static bool TryGetMusicDuck(out float db)
        {
            db = 0f;
            var mixer = Mixer;
            return mixer != null && mixer.GetFloat(MusicDuckParam, out db);
        }
    }

    /// <summary>
    /// MV-1437 ducking: Music and Ambience dip by <see cref="DuckDb"/> over <see cref="AttackSeconds"/>,
    /// hold for the triggering cue's length, then recover over <see cref="RecoverSeconds"/>. A repeated
    /// trigger extends the hold (max, never a sum) and never deepens the dip. Pure and time-injected so
    /// EditMode can drive it without a running game loop.
    /// </summary>
    public sealed class AudioDucking
    {
        public const float DuckDb = -6f;
        public const float AttackSeconds = 0.05f;
        public const float RecoverSeconds = 0.6f;

        private float _holdRemaining;

        /// <summary>Current attenuation in dB: 0 (idle) down to <see cref="DuckDb"/>.</summary>
        public float CurrentDb { get; private set; }

        public float HoldRemaining => _holdRemaining;

        public void Trigger(float holdSeconds) => _holdRemaining = Mathf.Max(_holdRemaining, Mathf.Max(0f, holdSeconds));

        public void Advance(float dt)
        {
            if (dt <= 0f) return;
            if (_holdRemaining > 0f)
            {
                _holdRemaining = Mathf.Max(0f, _holdRemaining - dt);
                CurrentDb = Mathf.MoveTowards(CurrentDb, DuckDb, -DuckDb / AttackSeconds * dt);
            }
            else
            {
                CurrentDb = Mathf.MoveTowards(CurrentDb, 0f, -DuckDb / RecoverSeconds * dt);
            }
        }

        /// <summary>The cues that duck the bed (ticket: BossEngaged, BossDefeated, FactoryDestroyed,
        /// CanisterExplosion, WeaponCoreCollected, FinaleGateCrossed).</summary>
        public static bool Ducks(Cue cue) =>
            cue == Cue.BossEngaged || cue == Cue.BossDefeated || cue == Cue.FactoryDestroyed ||
            cue == Cue.CanisterExplosion || cue == Cue.WeaponCoreCollected || cue == Cue.FinaleGateCrossed;
    }

    /// <summary>MV-1437 variant choice: round-robin that never repeats a variant back-to-back.</summary>
    public static class SfxVariants
    {
        /// <summary>Next variant index in [0,count) given the previous one (-1 = none yet).</summary>
        public static int Next(int count, int last) => count <= 1 ? 0 : (last + 1) % count;

        /// <summary>Plus/minus 1.5 dB as a linear gain multiplier, from a unit sample in [0,1].</summary>
        public static float GainFromUnit(float unit) => Mathf.Pow(10f, Mathf.Lerp(-1.5f, 1.5f, Mathf.Clamp01(unit)) / 20f);
    }

    /// <summary>
    /// MV-1437 voice limits: per-cue max concurrent voices (default 4; 6 for the four chatty hit cues),
    /// oldest voice stolen at the limit, and a retrigger inside <see cref="MinIntervalSeconds"/> skipped.
    /// Pure and time-injected; the director maps the returned slot to a real <see cref="AudioSource"/>.
    /// </summary>
    public sealed class SfxVoiceTracker
    {
        public const float MinIntervalSeconds = 0.03f;
        public const int DefaultMaxVoices = 4;

        private struct Voice { public int Slot; public float End; public float Start; }

        private readonly Dictionary<Cue, List<Voice>> _voices = new Dictionary<Cue, List<Voice>>();
        private readonly Dictionary<Cue, float> _lastPlay = new Dictionary<Cue, float>();

        public static int MaxVoices(Cue cue) =>
            cue == Cue.DamageDealt || cue == Cue.EnemyKilled || cue == Cue.LightMissileHit || cue == Cue.OrbitalsHit
                ? 6 : DefaultMaxVoices;

        /// <summary>Tries to start a voice for <paramref name="cue"/>. Returns false if retriggered inside
        /// 30 ms. <paramref name="stolenSlot"/> is the slot of the voice to cut (or -1).</summary>
        public bool TryStart(Cue cue, float now, float clipSeconds, int slot, out int stolenSlot)
        {
            stolenSlot = -1;
            if (_lastPlay.TryGetValue(cue, out var last) && now - last < MinIntervalSeconds) return false;
            _lastPlay[cue] = now;
            ReleaseSlot(slot);   // the pool slot is being reused: whatever sounded on it is cut off

            if (!_voices.TryGetValue(cue, out var list)) _voices[cue] = list = new List<Voice>();
            list.RemoveAll(v => v.End <= now);
            int max = MaxVoices(cue);
            while (list.Count >= max)
            {
                int oldest = 0;
                for (int i = 1; i < list.Count; i++) if (list[i].Start < list[oldest].Start) oldest = i;
                stolenSlot = list[oldest].Slot;
                list.RemoveAt(oldest);
            }
            list.Add(new Voice { Slot = slot, Start = now, End = now + clipSeconds });
            return true;
        }

        /// <summary>Voices of <paramref name="cue"/> still sounding at <paramref name="now"/>.</summary>
        public int Alive(Cue cue, float now)
        {
            if (!_voices.TryGetValue(cue, out var list)) return 0;
            int n = 0;
            foreach (var v in list) if (v.End > now) n++;
            return n;
        }

        /// <summary>Drops every voice record on a reused pool slot.</summary>
        public void ReleaseSlot(int slot)
        {
            foreach (var list in _voices.Values) list.RemoveAll(v => v.Slot == slot);
        }
    }
}
