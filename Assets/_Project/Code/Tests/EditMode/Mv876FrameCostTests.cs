using NUnit.Framework;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-876 — World 2 is pinned at 11 fps and render scale, shadow distance and population count
    /// all moved nothing, which rules out GPU fill, the shadow pass and per-robot script scaling. This
    /// proves the replacement instrument itself: an injected clock drives exact, non-flaking charges
    /// into <see cref="FrameCost"/>'s buckets, and the RESOLVED formatted line is asserted — never the
    /// authored millisecond numbers — so a bug in the formatter (the exact MV-465 trap this policy
    /// exists for) cannot pass silently.
    /// </summary>
    public sealed class Mv876FrameCostTests
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
        public void ChargedBuckets_ResolveExactly_AndAFrameWithNoChargesResetsToZero()
        {
            var clock = new FakeClock();
            FrameCost.UseClockForTest(clock);
            FrameCost.Reset();

            FrameCost.MarkFrameRendered();
            FrameCost.NotifyFixedUpdate();
            FrameCost.NotifyFixedUpdate();

            FrameCost.Begin(FrameCost.Bucket.Robot);
            clock.AdvanceMs(5.0);
            FrameCost.End(FrameCost.Bucket.Robot);

            FrameCost.Begin(FrameCost.Bucket.Sludge);
            clock.AdvanceMs(2.0);
            FrameCost.End(FrameCost.Bucket.Sludge);

            FrameCost.Begin(FrameCost.Bucket.Hud);
            clock.AdvanceMs(1.0);
            FrameCost.End(FrameCost.Bucket.Hud);

            string chargedLine = FrameCost.FormatLine(20.0);

            Assert.That(chargedLine, Does.Contain("robot 5.0"), chargedLine);
            Assert.That(chargedLine, Does.Contain("repl 0.0"), chargedLine);
            Assert.That(chargedLine, Does.Contain("sludge 2.0"), chargedLine);
            Assert.That(chargedLine, Does.Contain("anch 0.0"), chargedLine);
            Assert.That(chargedLine, Does.Contain("hud 1.0"), chargedLine);
            Assert.That(chargedLine, Does.Contain("vfx 0.0"), chargedLine);
            Assert.That(chargedLine, Does.Contain("other 12.0"), chargedLine);
            Assert.That(chargedLine, Does.Contain("fixed 2.0/frame"), chargedLine);

            // A second frame with no charges resets every bucket (and the fixed-step count) to 0.0.
            FrameCost.Reset();
            string emptyLine = FrameCost.FormatLine(0.0);

            Assert.That(emptyLine, Does.Contain("robot 0.0"), emptyLine);
            Assert.That(emptyLine, Does.Contain("repl 0.0"), emptyLine);
            Assert.That(emptyLine, Does.Contain("sludge 0.0"), emptyLine);
            Assert.That(emptyLine, Does.Contain("anch 0.0"), emptyLine);
            Assert.That(emptyLine, Does.Contain("hud 0.0"), emptyLine);
            Assert.That(emptyLine, Does.Contain("vfx 0.0"), emptyLine);
            Assert.That(emptyLine, Does.Contain("other 0.0"), emptyLine);
            Assert.That(emptyLine, Does.Contain("fixed 0.0/frame"), emptyLine);
        }
    }
}
