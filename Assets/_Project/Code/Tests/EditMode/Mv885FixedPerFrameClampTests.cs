using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-885 — Lee's capture on the MV-883 commit read "fixed 6.5/frame" against a clamp that caps
    /// catch-up at <see cref="Time.maximumDeltaTime"/> / <see cref="Time.fixedDeltaTime"/> steps per
    /// rendered frame. The average of per-frame counts that are each individually capped can never
    /// exceed that cap — so a reading above it means the accumulator's math is wrong, not the clamp.
    /// <see cref="FrameCost.NotifyFixedUpdate"/> accumulates unconditionally from
    /// <see cref="FrameCost.Reset"/> onward, but <see cref="FrameCost.MarkFrameRendered"/> only
    /// increments the frame count from its SECOND call on (the first call has no previous timestamp to
    /// diff, so it can't measure that frame's elapsed time) — so across N calls, N frames' worth of
    /// fixed-update catch-up land in the numerator while only N-1 frames land in the denominator. This
    /// drives three frames, each legitimately AT the resolved cap, through the
    /// <see cref="FrameCost.IClock"/> seam and asserts the "fixed .../frame" figure parsed back out of
    /// <see cref="FrameCost.FormatLine"/> cannot exceed the resolved
    /// <see cref="Time.maximumDeltaTime"/> / <see cref="Time.fixedDeltaTime"/> ratio — never an authored
    /// constant (Rule 2 / MV-465).
    /// </summary>
    public sealed class Mv885FixedPerFrameClampTests
    {
        private sealed class FakeClock : FrameCost.IClock
        {
            private long _ticks;

            public long GetTimestamp() => _ticks;

            public void AdvanceMs(double ms) =>
                _ticks += (long)System.Math.Round(ms * System.Diagnostics.Stopwatch.Frequency / 1000.0);
        }

        private static readonly Regex FixedPerFrameRegex = new Regex(@"fixed (\d+(?:\.\d+)?)/frame");

        private float _originalMaxDeltaTime;

        [SetUp]
        public void SetUp()
        {
            _originalMaxDeltaTime = Time.maximumDeltaTime;
            Time.maximumDeltaTime = 0.1f;
        }

        [TearDown]
        public void TearDown()
        {
            Time.maximumDeltaTime = _originalMaxDeltaTime;
            FrameCost.Reset();
            FrameCost.UseClockForTest(null);
        }

        [Test]
        public void FixedPerFrame_NeverExceedsMaximumDeltaTimeOverFixedDeltaTime_AcrossSeveralClampedFrames()
        {
            var clock = new FakeClock();
            FrameCost.UseClockForTest(clock);
            FrameCost.Reset();

            // Resolved bound, never an authored constant — see class comment.
            float bound = Time.maximumDeltaTime / Time.fixedDeltaTime;
            int stepsAtCap = Mathf.FloorToInt(bound);

            // Three separate rendered frames, each doing exactly the resolved cap's worth of
            // fixed-update catch-up — the legitimate worst case under a correctly-applied clamp.
            for (int frame = 0; frame < 3; frame++)
            {
                for (int i = 0; i < stepsAtCap; i++)
                {
                    FrameCost.NotifyFixedUpdate();
                }
                clock.AdvanceMs(20.0);
                FrameCost.MarkFrameRendered();
            }

            string line = FrameCost.FormatLine();
            Match match = FixedPerFrameRegex.Match(line);
            Assert.IsTrue(match.Success, "FormatLine must report a 'fixed .../frame' figure: " + line);

            float fixedPerFrame = float.Parse(match.Groups[1].Value);
            Assert.LessOrEqual(fixedPerFrame, bound,
                $"fixed/frame ({fixedPerFrame}) must never exceed maximumDeltaTime/fixedDeltaTime " +
                $"({bound}) when every individual frame stayed at or under the cap: {line}");
        }
    }
}
