using NUnit.Framework;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-940 Phase 1 AC: "the iPhone overlay shows the per-system buckets and they sum to within 10% of
    /// cpu main". <see cref="FrameCost.Bucket"/> gained two new members (<c>Gate</c>, <c>Sentinel</c>) —
    /// this proves they are wired into the SAME sum-vs-frame-time residual <see cref="Mv881FrameCostResidualTests"/>
    /// already guards for the original seven, not merely displayed and forgotten. Fails to compile on the
    /// pre-fix commit: <c>Bucket.Gate</c>/<c>Bucket.Sentinel</c>/<c>RobotSubPhase</c>/<c>BeginRobotSub</c>/
    /// <c>EndRobotSub</c> did not exist there at all.
    ///
    /// Also proves the new robot sub-phase breakdown (<see cref="FrameCost.RobotSubPhase"/>) is charged
    /// and displayed WITHOUT moving the top-level residual — see that enum's own doc comment for why it
    /// is deliberately excluded from <see cref="FrameCost.Bucket"/>'s summed array (it nests inside the
    /// existing Robot charge and would double-count the same milliseconds if it were not excluded).
    /// </summary>
    public sealed class Mv940FrameCostGateSentinelTests
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
        public void GateAndSentinelBuckets_SumWithinTenPercentOfFrameTime_SubPhasesDoNotMoveTheResidual()
        {
            var clock = new FakeClock();
            FrameCost.UseClockForTest(clock);
            FrameCost.Reset();

            FrameCost.MarkFrameRendered(); // opens the synthetic frame's own 100ms window

            FrameCost.Begin(FrameCost.Bucket.Robot);
            FrameCost.BeginRobotSub(FrameCost.RobotSubPhase.Dormant);
            clock.AdvanceMs(3.0);
            FrameCost.EndRobotSub(FrameCost.RobotSubPhase.Dormant);
            clock.AdvanceMs(17.0); // robot bucket totals 20.0
            FrameCost.End(FrameCost.Bucket.Robot);

            FrameCost.Begin(FrameCost.Bucket.Repl);    clock.AdvanceMs(10.0); FrameCost.End(FrameCost.Bucket.Repl);
            FrameCost.Begin(FrameCost.Bucket.Sludge);  clock.AdvanceMs(10.0); FrameCost.End(FrameCost.Bucket.Sludge);
            FrameCost.Begin(FrameCost.Bucket.Anchor);  clock.AdvanceMs(10.0); FrameCost.End(FrameCost.Bucket.Anchor);
            FrameCost.Begin(FrameCost.Bucket.Hud);     clock.AdvanceMs(10.0); FrameCost.End(FrameCost.Bucket.Hud);
            FrameCost.Begin(FrameCost.Bucket.Vfx);     clock.AdvanceMs(10.0); FrameCost.End(FrameCost.Bucket.Vfx);
            FrameCost.Begin(FrameCost.Bucket.Debug);   clock.AdvanceMs(10.0); FrameCost.End(FrameCost.Bucket.Debug);
            FrameCost.Begin(FrameCost.Bucket.Gate);    clock.AdvanceMs(10.0); FrameCost.End(FrameCost.Bucket.Gate);
            FrameCost.Begin(FrameCost.Bucket.Sentinel); clock.AdvanceMs(5.0); FrameCost.End(FrameCost.Bucket.Sentinel);

            // Named buckets sum to 95.0ms; 5.0ms of this synthetic frame's 100ms is deliberately left
            // uncharged, so the residual ("other") must resolve to exactly that gap.
            clock.AdvanceMs(5.0);

            FrameCost.MarkFrameRendered(); // closes the frame at exactly 100ms elapsed

            string line = FrameCost.FormatLine();

            // The 10% check is derived from FrameCost's OWN resolved "other" figure, never from the
            // literals charged above — a bug that stopped Gate/Sentinel being folded into FormatLine's
            // internal sum (see FrameCost.cs's own sum loop) would move this parsed value, not just the
            // numbers this test already knows are correct by construction.
            var otherMatch = System.Text.RegularExpressions.Regex.Match(line, @"other (-?\d+\.\d)");
            Assert.That(otherMatch.Success, $"expected an 'other' figure in the formatted line: {line}");
            double residualMs = double.Parse(otherMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            const double totalFrameMs = 100.0;
            double namedBucketSumMs = totalFrameMs - residualMs;
            Assert.That(namedBucketSumMs, Is.GreaterThanOrEqualTo(totalFrameMs * 0.90),
                $"named buckets (frame time minus FrameCost's own residual) must sum to within 10% of the frame's own total time: {line}");

            Assert.That(line, Does.Contain("robot 20.0"), line);
            Assert.That(line, Does.Contain("gate 10.0"), line);
            Assert.That(line, Does.Contain("sent 5.0"), line);
            // "other" is computed internally as frameTimeSum - sum(all named buckets) — this being
            // exactly 5.0 (not 15.0) is the proof Gate/Sentinel are folded into that same sum, not
            // merely displayed alongside it.
            Assert.That(line, Does.Contain("other 5.0"), line);

            // The robot sub-phase breakdown is charged and shown, and moved none of the above — proving
            // it is genuinely excluded from the top-level sum rather than accidentally double-counted.
            Assert.That(line, Does.Contain("robot/ dormant 3.0"), line);
        }
    }
}
