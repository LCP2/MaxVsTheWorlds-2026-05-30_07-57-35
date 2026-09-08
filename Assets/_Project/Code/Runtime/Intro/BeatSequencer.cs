using System;
using UnityEngine;

namespace MaxWorlds.Intro
{
    /// <summary>One named span of a beat timeline: a duration, a per-frame scrub (t in 0..1 across the
    /// beat) and an optional one-shot callback fired the instant the timeline enters it.</summary>
    public readonly struct Beat
    {
        public readonly string Name;
        public readonly float Duration;
        public readonly Action<float> Apply;
        public readonly Action OnEnter;

        public Beat(string name, float duration, Action<float> apply = null, Action onEnter = null)
        {
            Name = name; Duration = duration; Apply = apply; OnEnter = onEnter;
        }
    }

    /// <summary>
    /// A code-driven beat timeline (YT-155): sum the durations, resolve which beat a clock value falls
    /// in, and fire that beat's <see cref="Beat.OnEnter"/>/<see cref="Beat.Apply"/>. Pulled out of
    /// <see cref="IntroCinematic"/> (MV-704) so a second cinematic never has to hand-roll a second copy
    /// of the "which beat are we in" loop — <see cref="WorldTransitionCinematic"/> is the first other
    /// user.
    /// </summary>
    public sealed class BeatSequencer
    {
        private readonly Beat[] _beats;
        private float _clock;
        private int _beat = -1;
        private bool _done;

        public BeatSequencer(Beat[] beats)
        {
            _beats = beats ?? Array.Empty<Beat>();
            float total = 0f;
            foreach (Beat b in _beats) total += b.Duration;
            TotalDuration = total;
        }

        /// <summary>Sum of every beat's duration.</summary>
        public float TotalDuration { get; }

        /// <summary>How many beats the timeline; a test reads this to prove the authored count.</summary>
        public int Count => _beats.Length;

        /// <summary>Seconds since the timeline started (or since the last <see cref="JumpTo"/>).</summary>
        public float Elapsed => _clock;

        /// <summary>The live beat's index, or -1 before the first <see cref="Tick"/>/<see cref="JumpTo"/>.</summary>
        public int BeatIndex => _beat;

        /// <summary>The live beat's name, or "(none)" before the first tick.</summary>
        public string BeatName => _beat >= 0 && _beat < _beats.Length ? _beats[_beat].Name : "(none)";

        /// <summary>Seconds elapsed within the live beat (0 at the beat's own start).</summary>
        public float BeatElapsed { get; private set; }

        /// <summary>True once <see cref="Elapsed"/> has reached <see cref="TotalDuration"/>.</summary>
        public bool IsDone => _done;

        /// <summary>Advance by <paramref name="dt"/> unscaled seconds: resolve the live beat, fire its
        /// <see cref="Beat.OnEnter"/> on a change, scrub it via <see cref="Beat.Apply"/>, and clamp to
        /// the last beat once the clock reaches the end. Returns true the instant this call crosses
        /// <see cref="TotalDuration"/> — a one-shot "just finished" signal, not a sticky flag (once
        /// <see cref="IsDone"/> is true, further calls are no-ops and return false).</summary>
        public bool Tick(float dt)
        {
            if (_done || _beats.Length == 0) return false;
            _clock += dt;

            float acc = 0f;
            int idx = _beats.Length - 1;
            float local = 1f;
            for (int i = 0; i < _beats.Length; i++)
            {
                if (_clock < acc + _beats[i].Duration || i == _beats.Length - 1)
                {
                    idx = i;
                    local = _beats[i].Duration > 0f ? Mathf.Clamp01((_clock - acc) / _beats[i].Duration) : 1f;
                    break;
                }
                acc += _beats[i].Duration;
            }

            EnterIfChanged(idx);
            BeatElapsed = local * _beats[_beat].Duration;
            _beats[_beat].Apply?.Invoke(local);

            if (_clock >= TotalDuration) { _done = true; return true; }
            return false;
        }

        /// <summary>Jump straight to <paramref name="beatIndex"/> with a given elapsed-within-beat,
        /// firing that beat's <see cref="Beat.OnEnter"/>/<see cref="Beat.Apply"/> immediately — how a
        /// skip lands on a specific beat (e.g. the last one) instead of running the natural end-of-
        /// timeline handoff early. Does not mark the timeline done, even when it lands on the final
        /// beat: the caller still has to <see cref="Tick"/> that beat out.</summary>
        public void JumpTo(int beatIndex, float elapsedInBeat = 0f)
        {
            if (_beats.Length == 0 || beatIndex < 0 || beatIndex >= _beats.Length) return;

            float acc = 0f;
            for (int i = 0; i < beatIndex; i++) acc += _beats[i].Duration;

            EnterIfChanged(beatIndex);
            _clock = acc + elapsedInBeat;
            float dur = _beats[beatIndex].Duration;
            BeatElapsed = elapsedInBeat;
            float local = dur > 0f ? Mathf.Clamp01(elapsedInBeat / dur) : 0f;
            _beats[beatIndex].Apply?.Invoke(local);
        }

        private void EnterIfChanged(int idx)
        {
            if (idx == _beat) return;
            _beat = idx;
            _beats[_beat].OnEnter?.Invoke();
        }
    }
}
