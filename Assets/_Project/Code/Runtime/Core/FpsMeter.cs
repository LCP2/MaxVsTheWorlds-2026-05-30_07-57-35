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
        // MV-537: worst-frame tracking, in 1-second buckets covering a trailing ~5-second window — the
        // number that tells a steady-but-slow frame rate apart from an occasional long stall. Fed from
        // the same Tick() calls Bootstrap already makes every frame for Fps, so this never becomes a
        // second, divergent measurement path.
        private const int HistoryBuckets = 5;
        private const float BucketSeconds = 1f;

        private readonly float _window;
        private readonly float[] _bucketWorstMs = new float[HistoryBuckets];
        private int _bucketIndex = -1;
        private float _bucketStart;
        private float _lastTick;
        private int _frames;
        private float _windowStart;
        private bool _started;

        /// <summary>Frames per second over the last completed window. 0 until the first window closes.</summary>
        public float Fps { get; private set; }

        /// <summary>True once a real measurement exists.</summary>
        public bool HasReading => Fps > 0f;

        /// <summary>Current frame time in ms, derived from <see cref="Fps"/> — one formula, so the
        /// overlay and the existing [FPS] log line can never disagree about what a frame costs.</summary>
        public float FrameMs => Fps > 0f ? 1000f / Fps : 0f;

        /// <summary>Worst single-frame time, in ms, seen within the trailing ~5-second window. A run
        /// averaging a healthy fps with one long spike reads very differently here than a steady slow
        /// one, even though <see cref="Fps"/> alone can't tell them apart.</summary>
        public float WorstFrameMs
        {
            get
            {
                float worst = 0f;
                for (int i = 0; i < _bucketWorstMs.Length; i++)
                    if (_bucketWorstMs[i] > worst) worst = _bucketWorstMs[i];
                return worst;
            }
        }

        public FpsMeter(float windowSeconds = 0.5f)
        {
            _window = Mathf.Max(0.05f, windowSeconds);
        }

        /// <summary>Worst-frame-per-bucket, oldest first — a short rolling history so spikes read as
        /// periodic, clustered, or continuous at a glance. Allocates a small snapshot array; call only
        /// while the overlay displaying it is actually open.</summary>
        public float[] SnapshotHistoryOldestFirstMs()
        {
            var result = new float[HistoryBuckets];
            if (_bucketIndex < 0) return result;
            for (int i = 0; i < HistoryBuckets; i++)
                result[i] = _bucketWorstMs[(_bucketIndex + 1 + i) % HistoryBuckets];
            return result;
        }

        /// <summary>Call once per rendered frame with a monotonic clock (Time.realtimeSinceStartup).
        /// Returns true on the frames where a new Fps reading was produced.</summary>
        public bool Tick(float now)
        {
            if (!_started)
            {
                _started = true;
                _windowStart = now;
                _frames = 0;
                _bucketIndex = 0;
                _bucketStart = now;
                _bucketWorstMs[0] = 0f;
                _lastTick = now;
                return false;
            }

            _frames++;

            float dtMs = (now - _lastTick) * 1000f;
            _lastTick = now;
            AdvanceBucket(now);
            if (dtMs > _bucketWorstMs[_bucketIndex]) _bucketWorstMs[_bucketIndex] = dtMs;

            float elapsed = now - _windowStart;
            if (elapsed < _window) return false;

            Fps = elapsed > 0f ? _frames / elapsed : 0f;
            _frames = 0;
            _windowStart = now;
            return true;
        }

        /// <summary>Rotates the ring buffer forward to the bucket "now" falls in, zeroing every bucket
        /// it passes through on the way — that's the decay: a bucket's worst figure can only survive
        /// while it's within the trailing window, never longer.</summary>
        private void AdvanceBucket(float now)
        {
            int steps = Mathf.FloorToInt((now - _bucketStart) / BucketSeconds);
            if (steps <= 0) return;

            steps = Mathf.Min(steps, HistoryBuckets);
            for (int i = 0; i < steps; i++)
            {
                _bucketIndex = (_bucketIndex + 1) % HistoryBuckets;
                _bucketWorstMs[_bucketIndex] = 0f;
            }
            _bucketStart += steps * BucketSeconds;
        }
    }
}
