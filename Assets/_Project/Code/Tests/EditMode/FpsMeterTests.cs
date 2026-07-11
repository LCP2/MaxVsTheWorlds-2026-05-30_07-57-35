using NUnit.Framework;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-62 — the frame-rate readout.
    ///
    /// The old counter could report a confident, permanent "0 fps" while the game was plainly
    /// drawing frames, which is worse than no instrument at all: it sent QA looking for a stall that
    /// wasn't there. These tests pin the property that matters — if frames are happening, the number
    /// is not zero.
    /// </summary>
    public sealed class FpsMeterTests
    {
        [Test]
        public void SixtyFramesInOneSecond_ReadsSixty()
        {
            var meter = new FpsMeter(0.5f);

            float t = 0f;
            meter.Tick(t);                       // first call just starts the window
            for (int i = 0; i < 60; i++)
            {
                t += 1f / 60f;
                meter.Tick(t);
            }

            Assert.That(meter.Fps, Is.EqualTo(60f).Within(2f));
        }

        [Test]
        public void ThirtyFramesInOneSecond_ReadsThirty()
        {
            var meter = new FpsMeter(0.5f);

            float t = 0f;
            meter.Tick(t);
            for (int i = 0; i < 30; i++)
            {
                t += 1f / 30f;
                meter.Tick(t);
            }

            Assert.That(meter.Fps, Is.EqualTo(30f).Within(2f));
        }

        [Test]
        public void ItNeverReportsZeroWhileFramesAreBeingDrawn()
        {
            var meter = new FpsMeter(0.5f);

            float t = 0f;
            meter.Tick(t);
            for (int i = 0; i < 120; i++)
            {
                t += 1f / 60f;
                meter.Tick(t);
                // Once a window has closed, a running game must never read as stopped.
                if (meter.HasReading) Assert.That(meter.Fps, Is.GreaterThan(0f));
            }

            Assert.IsTrue(meter.HasReading);
        }

        [Test]
        public void BeforeTheFirstWindowCloses_ItSaysItHasNoReadingYet()
        {
            var meter = new FpsMeter(0.5f);

            meter.Tick(0f);
            meter.Tick(0.01f);

            Assert.IsFalse(meter.HasReading,
                "with no reading yet it must say so — printing '0 fps' is a lie the UI then repeats");
        }
    }
}
