using NUnit.Framework;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-881 — <see cref="FrameCost.FormatLine"/> used to take an external "windowTotalMs" and report
    /// <c>other = windowTotalMs - sum(buckets)</c>. That parameter was always the wall-clock length of
    /// the display's own 0.25s refresh window, not any frame's real busy time, so <c>other</c> came out
    /// ~237-282 ms on every one of Lee's eight readouts regardless of load — a tautology, not a
    /// measurement. This drives <see cref="FrameCost"/> through its injectable <see cref="FrameCost.IClock"/>
    /// seam with hand-picked, exact per-frame deltas and asserts the residual it reports is that ONE
    /// closed frame's own elapsed time minus the sum of the named buckets — never the length of any
    /// accumulation window, proven by letting a large, unrelated stretch of wall time pass afterward
    /// without moving the residual at all.
    /// </summary>
    public sealed class Mv881FrameCostResidualTests
    {
        private sealed class FakeClock : FrameCost.IClock
        {
            private long _ticks;

            public long GetTimestamp() => _ticks;

            public void AdvanceMs(double ms) =>
                _ticks += (long)System.Math.Round(ms * System.Diagnostics.Stopwatch.Frequency / 1000.0);
        }

        [TearDown]
        public void TearDown()
        {
            FrameCost.Reset();
            FrameCost.UseClockForTest(null);
        }

        [Test]
        public void Residual_IsThatFramesOwnTimeMinusNamedBuckets_NotTheAccumulationWindowLength()
        {
            var clock = new FakeClock();
            FrameCost.UseClockForTest(clock);
            FrameCost.Reset();

            // Opens frame 0's own timing window.
            FrameCost.MarkFrameRendered();

            // 10ms of named cost charged inside that frame.
            FrameCost.Begin(FrameCost.Bucket.Robot);
            clock.AdvanceMs(10.0);
            FrameCost.End(FrameCost.Bucket.Robot);

            // The rest of frame 0's own 100ms elapses with no further bucket charged.
            clock.AdvanceMs(90.0);

            // Closes frame 0 — elapsed since the previous mark is exactly 100ms — and opens frame 1.
            FrameCost.MarkFrameRendered();

            // A long, unrelated stretch of wall time passes before the display actually reads the line,
            // with frame 1 never closed. If the residual were still measuring window length (the MV-876
            // bug), this alone would push "other" up by roughly 5000; it must not move it at all.
            clock.AdvanceMs(5000.0);

            string line = FrameCost.FormatLine();

            Assert.That(line, Does.Contain("robot 10.0"), line);
            Assert.That(line, Does.Contain("other 90.0"), line);
        }
    }
}
