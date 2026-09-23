using System;
using NUnit.Framework;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-916 — pins the fix for <see cref="AllocationAssert.MeasureAllocatedBytes(Action)"/>
    /// returning a negative delta when a GC lands inside the measured window (see
    /// <see cref="AllocationAssert"/>'s doc comment for the mechanism). Fails to compile on the base
    /// commit named in the PR: the internal (Action, Func&lt;bool,long&gt;, int) overload driving the
    /// second half of this test does not exist there. On any commit that has the overload but not the
    /// retry/throw behaviour, the first half fails deterministically with a negative measured delta
    /// (reproduced pre-fix: "trial 0: ... measured -67571712").
    /// </summary>
    public sealed class Mv916AllocationAssertRetryTests
    {
        [Test]
        public void MeasureAllocatedBytes_NeverReturnsOrSilentlySwallowsANegativeDelta()
        {
            // --- AC1: forcing a GC inside the measured window, across 20 consecutive trials, must
            // never surface a negative number — only a valid non-negative figure or an explicit
            // failure.
            for (int trial = 0; trial < 20; trial++)
            {
                long? measured = null;
                try
                {
                    measured = AllocationAssert.MeasureAllocatedBytes(() =>
                    {
                        var sink = new byte[200 * 4096];
                        GC.KeepAlive(sink);
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    });
                }
                catch (InvalidOperationException)
                {
                    // An explicit "could not obtain a clean window" failure is an acceptable outcome —
                    // a negative number silently reaching a caller is not.
                }

                if (measured.HasValue)
                {
                    Assert.That(measured.Value, Is.GreaterThanOrEqualTo(0L),
                        $"trial {trial}: a garbage collection inside the measured window must never " +
                        $"produce a negative delta, measured {measured.Value}");
                }
            }

            // --- AC3: when every attempt comes back negative, the retry path must fail loudly rather
            // than returning a value — driven by an injected reader, not a real GC race, so this half
            // is deterministic.
            long fakeHeap = 1_000_000L;
            long ReadShrinkingHeap(bool _)
            {
                fakeHeap -= 1024L; // every read is smaller than the last: the delta is always negative
                return fakeHeap;
            }

            var ex = Assert.Throws<InvalidOperationException>(() =>
                AllocationAssert.MeasureAllocatedBytes(() => { }, ReadShrinkingHeap, maxAttempts: 3));
            Assert.That(ex.Message, Does.Contain("could not obtain a clean measurement window"),
                "the exhaustion failure must say why, not just throw silently");
        }
    }
}
