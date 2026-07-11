using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// Measures frame rate by COUNTING FRAMES over a real-time window (YT-62).
    ///
    /// The counter this replaces used an exponential average of deltaTime seeded at zero, guarded by
    /// <c>smoothed > 0 ? 1/smoothed : 0</c>. If that value never advanced, the readout sat at a
    /// confident, permanent "0 fps" while the game was plainly animating — which is exactly what the
    /// WebGL build showed, and it's a bad property for the one instrument we use to decide whether
    /// the frame budget is being met.
    ///
    /// Counting frames cannot do that. If frames are being drawn, the number is non-zero, and it is
    /// the true number rather than a filtered guess.
    ///
    /// Pure C# with an injected clock, so it is unit-testable with no game running.
    /// </summary>
    public sealed class FpsMeter
    {
        private readonly float _window;
        private int _frames;
        private float _windowStart;
        private bool _started;

        /// <summary>Frames per second over the last completed window. 0 until the first window closes.</summary>
        public float Fps { get; private set; }

        /// <summary>True once a real measurement exists.</summary>
        public bool HasReading => Fps > 0f;

        public FpsMeter(float windowSeconds = 0.5f)
        {
            _window = Mathf.Max(0.05f, windowSeconds);
        }

        /// <summary>Call once per rendered frame with a monotonic clock (Time.realtimeSinceStartup).
        /// Returns true on the frames where a new reading was produced.</summary>
        public bool Tick(float now)
        {
            if (!_started)
            {
                _started = true;
                _windowStart = now;
                _frames = 0;
                return false;
            }

            _frames++;

            float elapsed = now - _windowStart;
            if (elapsed < _window) return false;

            Fps = elapsed > 0f ? _frames / elapsed : 0f;
            _frames = 0;
            _windowStart = now;
            return true;
        }
    }
}
