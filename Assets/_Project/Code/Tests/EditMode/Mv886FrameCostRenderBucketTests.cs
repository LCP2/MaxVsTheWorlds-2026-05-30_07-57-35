using NUnit.Framework;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-886 — cam/phys/scr/batch report n/a(dev) in a release player (MV-885), but a2-vs-a10 shows
    /// the frame is submission-bound (setp/tri scale 1:1 with the frame; the six instrumented buckets
    /// account for under 9% of it). URP's RenderPipelineManager begin/endFrameRendering events give a
    /// render-cost figure with no Profiler and no Development Build. This drives the new render bucket
    /// through the same injectable <see cref="FrameCost.IClock"/> seam the six buckets already use —
    /// FrameCost.BeginRenderFrame/EndRenderFrame are what the production URP event handlers call, so a
    /// test can exercise them directly with no dependency on URP ever actually rendering a frame in
    /// EditMode — and asserts the RESOLVED formatted line, never an authored constant (Rule 2).
    /// </summary>
    public sealed class Mv886FrameCostRenderBucketTests
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
        public void RenderBucket_ReportsElapsedTimeBetweenBeginEndFramePair_IndependentOfRefreshCadence()
        {
            var clock = new FakeClock();
            FrameCost.UseClockForTest(clock);
            FrameCost.Reset();

            // One begin/end frame pair — exactly what one production URP frame fires.
            FrameCost.BeginRenderFrame();
            clock.AdvanceMs(12.5);
            FrameCost.EndRenderFrame();

            // A long, unrelated stretch of wall time passes before the display actually reads the line.
            // If this figure were measuring the readout's own refresh window (the MV-876 tautology this
            // project keeps guarding against — see Mv881FrameCostResidualTests) rather than the one
            // closed begin/end pair, this alone would move it; it must not.
            clock.AdvanceMs(5000.0);

            string line = FrameCost.FormatLine();

            Assert.That(line, Does.Contain("render 12.5"), line);
        }
    }
}
