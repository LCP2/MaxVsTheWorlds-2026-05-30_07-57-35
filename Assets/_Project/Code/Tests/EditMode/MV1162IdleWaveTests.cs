using NUnit.Framework;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1162 (Rule 1): the one new test, against the pure <see cref="IdleWaveTracker"/> only — no
    /// scene, no MaxRig, no AnimSequence. Fails on base (the class does not exist yet); quoted in the
    /// fix comment per the testing policy.
    /// </summary>
    public sealed class MV1162IdleWaveTests
    {
        [Test]
        public void IdleClock_TriggersAt20s_ResetsOnInput_AndCancelsAnActiveWave()
        {
            var tracker = new IdleWaveTracker();

            // 19.9 s of no input does not trigger.
            Assert.IsFalse(tracker.Tick(19.9f, hasInput: false), "must not trigger before 20 s");
            Assert.IsFalse(tracker.IsWaving, "must not be waving before 20 s");

            // The remaining 0.1 s (20.0 s total) triggers exactly once.
            Assert.IsTrue(tracker.Tick(0.1f, hasInput: false), "must trigger at exactly 20 s");
            Assert.IsTrue(tracker.IsWaving, "must be waving the instant it triggers");

            // Let the wave finish on its own, then prove a fresh 20 s is needed again afterwards.
            tracker.EndWave();
            Assert.IsFalse(tracker.IsWaving, "EndWave must clear the waving flag");

            // An aim input at 10 s resets the clock, so 20 s of total elapsed time (10 before the
            // input + 10 after) does not trigger — only a fresh 20 s of UNBROKEN idle would.
            Assert.IsFalse(tracker.Tick(10f, hasInput: false), "10 s idle alone must not trigger");
            Assert.IsFalse(tracker.Tick(0f, hasInput: true), "the aim input itself must not trigger");
            Assert.IsFalse(tracker.Tick(10f, hasInput: false),
                "only 10 s has elapsed since the reset — 20 s of total wall time must not trigger");

            // Finish that fresh countdown and confirm cancellation mid-wave.
            Assert.IsTrue(tracker.Tick(10f, hasInput: false), "20 s since the reset must trigger");
            Assert.IsTrue(tracker.IsWaving);

            // During the wave, an input cancels it on that very tick.
            bool triggeredWhileCancelling = tracker.Tick(0.1f, hasInput: true);
            Assert.IsFalse(triggeredWhileCancelling, "a cancelling tick must never also trigger");
            Assert.IsFalse(tracker.IsWaving, "input during the wave must cancel it immediately");
            Assert.AreEqual(0f, tracker.IdleSeconds, "a cancelling input must restart the idle clock at 0");
        }
    }
}
