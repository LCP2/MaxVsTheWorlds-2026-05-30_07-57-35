using NUnit.Framework;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-888 — Bootstrap.OnGUI drew seven lines of IMGUI with no FrameCost bucket wrapping it at all,
    /// so whatever it cost landed silently in `other`. This drives the new Debug bucket through the
    /// same injected IClock seam every other bucket already uses (Mv876FrameCostTests,
    /// Mv886FrameCostRenderBucketTests): OnGUI is called at least twice per rendered frame (Layout then
    /// Repaint — see the ticket), so charging Bucket.Debug through two separate Begin/End pairs is what
    /// one real rendered frame actually does, and the RESOLVED formatted line — never an authored
    /// constant — is asserted to have accumulated both.
    ///
    /// The "reports zero when the overlay is toggled off" clause is deliberately proven at the
    /// FrameCost level too, not by instantiating Bootstrap and invoking its real OnGUI: this project
    /// never authors PlayMode tests, and driving GUI.Label/GUIStyle.CalcHeight from an EditMode test
    /// with no live IMGUI event context is exactly the kind of environment-shaped flake this policy
    /// exists to avoid — no other test in this codebase invokes a MonoBehaviour's OnGUI directly for
    /// that reason. Bootstrap's toggled-off path (see its OnGUI) is a plain `if (!_overlayVisible)
    /// return;` BEFORE FrameCost.Begin(Bucket.Debug) is ever reached, so "toggled off" and "this bucket
    /// never gets charged this window" are the same fact by construction; asserting the latter — the
    /// same "no charge resolves to zero" contract Mv876FrameCostTests already proves for the other six
    /// buckets — is what's actually reachable and resolved from EditMode.
    /// </summary>
    public sealed class Mv888FrameCostDebugBucketTests
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
        public void DebugBucket_AccumulatesAcrossMultipleOnGuiInvocations_AndReadsZeroOnceOverlayStopsCharging()
        {
            var clock = new FakeClock();
            FrameCost.UseClockForTest(clock);
            FrameCost.Reset();

            // Layout event.
            FrameCost.Begin(FrameCost.Bucket.Debug);
            clock.AdvanceMs(1.2);
            FrameCost.End(FrameCost.Bucket.Debug);

            // Repaint event, same rendered frame.
            FrameCost.Begin(FrameCost.Bucket.Debug);
            clock.AdvanceMs(0.8);
            FrameCost.End(FrameCost.Bucket.Debug);

            string chargedLine = FrameCost.FormatLine();
            Assert.That(chargedLine, Does.Contain("dbg 2.0"), chargedLine);

            // MV-888 AC1: toggled off means Bootstrap.OnGUI returns before ever charging this bucket
            // again — the same "no charge this window resolves to zero" contract every other bucket
            // already has.
            FrameCost.Reset();
            string offLine = FrameCost.FormatLine();
            Assert.That(offLine, Does.Contain("dbg 0.0"), offLine);
        }
    }
}
