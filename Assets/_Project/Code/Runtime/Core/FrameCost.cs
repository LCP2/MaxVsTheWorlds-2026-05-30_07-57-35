using System.Diagnostics;

namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-876: World 2 is pinned at 11 fps and three confident causal stories — GPU fill rate, the
    /// shadow-caster pass, per-robot script cost scaling with population — all died against direct
    /// measurement (render scale, shadow distance, population count) that moved the frame rate not at
    /// all. A cost fixed across every load reduction is the signature of something that doesn't scale
    /// with any of those; this is the instrument that replaces the next guess with attribution.
    ///
    /// Six call sites each charge their own wall-clock cost into a <see cref="Bucket"/>; whatever is
    /// left over — <c>other</c> in <see cref="FormatLine"/> — is the single most informative figure
    /// this ticket can produce, because it says the cost is NOT in any system instrumented here.
    ///
    /// Static and shared rather than an injected instance: every wrapped call site (RobotEnemy,
    /// Replicator, SludgeFlowDirector, GroundAnchorVfx, HudController, CombatVfx) is a different,
    /// independently self-installed MonoBehaviour with no shared reference to hand a per-instance
    /// accumulator to — the same reason <see cref="Bootstrap.PopulationLineProvider"/> is static.
    ///
    /// Buckets accumulate across every rendered frame since the last <see cref="Reset"/>, and are only
    /// read and reset at the readout's own refresh cadence — never once per single frame — so the
    /// accumulator itself allocates nothing, and the "ms" figures and <c>other</c> stay directly
    /// comparable against that same window's own total wall time.
    /// </summary>
    public static class FrameCost
    {
        public enum Bucket { Robot, Repl, Sludge, Anchor, Hud, Vfx }

        private const int BucketCount = 6;

        /// <summary>The raw tick source, isolated behind an interface so a test can inject exact,
        /// hand-picked deltas instead of wall time — same seam <see cref="IFrameTimingSource"/> already
        /// uses for <see cref="FrameTimingProbe"/>.</summary>
        public interface IClock
        {
            long GetTimestamp();
        }

        private sealed class StopwatchClock : IClock
        {
            public long GetTimestamp() => Stopwatch.GetTimestamp();
        }

        private static IClock s_clock = new StopwatchClock();

        /// <summary>Test seam — production code never calls this.</summary>
        public static void UseClockForTest(IClock clock) => s_clock = clock ?? new StopwatchClock();

        private static readonly long[] s_beginTicks = new long[BucketCount];
        private static readonly double[] s_bucketMs = new double[BucketCount];
        private static int s_fixedUpdateCount;
        private static int s_frameCount;

        /// <summary>First statement of a wrapped method.</summary>
        public static void Begin(Bucket bucket) => s_beginTicks[(int)bucket] = s_clock.GetTimestamp();

        /// <summary>Last statement of a wrapped method.</summary>
        public static void End(Bucket bucket)
        {
            long elapsedTicks = s_clock.GetTimestamp() - s_beginTicks[(int)bucket];
            s_bucketMs[(int)bucket] += elapsedTicks * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Call once per rendered frame — Bootstrap's own per-frame marker, not one of the six
        /// attributed systems. The denominator behind <see cref="FormatLine"/>'s "fixed N.N/frame".</summary>
        public static void MarkFrameRendered() => s_frameCount++;

        /// <summary>Call from a FixedUpdate. Fixed-timestep catch-up (several physics steps behind one
        /// rendered frame) is the other hypothesis this ticket exists to rule in or out.</summary>
        public static void NotifyFixedUpdate() => s_fixedUpdateCount++;

        /// <summary>Formats the accumulated buckets against <paramref name="windowTotalMs"/> — the same
        /// window's own total wall time, so <c>other</c> is directly comparable. Allocates; call only at
        /// the readout's own refresh cadence, never per frame (see class comment).</summary>
        public static string FormatLine(double windowTotalMs)
        {
            double sum = 0.0;
            for (int i = 0; i < BucketCount; i++) sum += s_bucketMs[i];
            double other = windowTotalMs - sum;
            double fixedPerFrame = s_fixedUpdateCount / (double)System.Math.Max(1, s_frameCount);

            return $"ms robot {s_bucketMs[(int)Bucket.Robot]:0.0} repl {s_bucketMs[(int)Bucket.Repl]:0.0} " +
                   $"sludge {s_bucketMs[(int)Bucket.Sludge]:0.0} anch {s_bucketMs[(int)Bucket.Anchor]:0.0} " +
                   $"hud {s_bucketMs[(int)Bucket.Hud]:0.0} vfx {s_bucketMs[(int)Bucket.Vfx]:0.0} " +
                   $"other {other:0.0}  fixed {fixedPerFrame:0.0}/frame";
        }

        /// <summary>Clears every bucket and the fixed-update/frame counters — the top of the next
        /// accumulation window.</summary>
        public static void Reset()
        {
            for (int i = 0; i < BucketCount; i++) s_bucketMs[i] = 0.0;
            s_fixedUpdateCount = 0;
            s_frameCount = 0;
        }
    }
}
