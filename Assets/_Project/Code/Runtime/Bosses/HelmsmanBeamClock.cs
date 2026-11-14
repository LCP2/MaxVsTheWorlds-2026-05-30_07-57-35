using UnityEngine;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Phase 1's twin-lens-beam cadence (MV-1370): every <see cref="HelmsmanTuning.BeamInterval"/> seconds
    /// (tell start to tell start) a gold floor line shows for <see cref="HelmsmanTuning.BeamTellSeconds"/>,
    /// then both beams sweep <see cref="HelmsmanTuning.BeamSweepDegrees"/> over
    /// <see cref="HelmsmanTuning.BeamSweepSeconds"/>. Pure state, no MonoBehaviour - same reasoning as
    /// <see cref="LensSweepTelegraph"/>.
    /// </summary>
    public sealed class HelmsmanBeamClock
    {
        public enum Stage { Idle, Tell, Sweep }

        private float _clock;   // seconds since the current cycle's tell started (Idle: since the last one's)

        public Stage Current { get; private set; } = Stage.Idle;

        /// <summary>How many cycles have started (the first one is 1).</summary>
        public int Cycles { get; private set; }

        /// <summary>True for exactly the tick a tell begins.</summary>
        public bool TellStarted { get; private set; }

        /// <summary>0..1 through the live sweep; 0 outside it.</summary>
        public float SweepProgress01 =>
            Current == Stage.Sweep
                ? Mathf.Clamp01((_clock - HelmsmanTuning.BeamTellSeconds) / HelmsmanTuning.BeamSweepSeconds)
                : 0f;

        public void Tick(float dt)
        {
            TellStarted = false;
            _clock += Mathf.Max(0f, dt);

            if (Current == Stage.Idle)
            {
                if (_clock < HelmsmanTuning.BeamInterval) return;
                _clock -= HelmsmanTuning.BeamInterval;   // keep the cadence exact across dt jitter
                Current = Stage.Tell;
                Cycles++;
                TellStarted = true;
            }

            if (Current == Stage.Tell && _clock >= HelmsmanTuning.BeamTellSeconds)
                Current = Stage.Sweep;

            if (Current == Stage.Sweep &&
                _clock >= HelmsmanTuning.BeamTellSeconds + HelmsmanTuning.BeamSweepSeconds)
                Current = Stage.Idle;
        }

        /// <summary>Cancels a cycle in flight (the P1 -> P2 beat) and restarts the interval.</summary>
        public void Reset()
        {
            Current = Stage.Idle;
            _clock = 0f;
        }
    }
}
