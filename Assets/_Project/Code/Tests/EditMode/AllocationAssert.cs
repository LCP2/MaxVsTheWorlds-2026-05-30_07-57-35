using System;
using NUnit.Framework;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-889 — NUnit's `Is.Not.AllocatingGCMemory()` (and the equivalent
    /// `ConstraintExtensions.AllocatingGCMemory(Is.Not)` form) failed non-deterministically on commits
    /// that never touched the code it measured (see AnimSequenceTests's doc comment for the specific
    /// QA-run evidence). This helper replaces it with a direct delta read instead of routing through
    /// that constraint.
    ///
    /// The obvious alternative — `GC.GetAllocatedBytesForCurrentThread()` deltas — was tried and
    /// rejected on hard evidence, not preference: in this project's Unity Editor Mono runtime that API
    /// is a dead stub. It reported a flat 0 for a loop that deliberately allocated 40 MB (10,000
    /// iterations of `new byte[4096]`), proven with <c>Mv889AllocationAssertTests</c> during MV-889's
    /// own investigation. A guard built on it could never fail, which is worse than the flaky guard it
    /// would replace — a Tier-1-shaped defect (see MV-465), just arrived at by a different route.
    ///
    /// <see cref="GC.GetTotalMemory"/> is what's actually used: 10 trials of an allocation-free loop
    /// measured exactly 0 every time, and 5 trials of a loop deliberately allocating 1000×4096-byte
    /// arrays measured the identical non-zero delta every time (see <c>Mv889AllocationAssertTests</c>,
    /// which pins both). It reads the WHOLE managed heap rather than a single thread's allocations, so
    /// a collection landing inside the measured window can make the delta go negative — MV-916:
    /// <see cref="MeasureAllocatedBytes"/> forces a collection immediately before the "before"
    /// snapshot to shrink that window, and retries (discarding the reading) if the delta still comes
    /// back negative, rather than ever returning or clamping a negative figure.
    ///
    /// MV-923: that forced collection was paid on every retry attempt, not just the first — up to
    /// <see cref="MaxMeasurementAttempts"/> full blocking `GC.Collect()` + `WaitForPendingFinalizers()`
    /// + `GC.Collect()` rounds per call. Measured locally (1976-test EditMode suite,
    /// <c>Logs/editmode-before-full.xml</c>): <c>Mv916AllocationAssertRetryTests</c>'s 20-trial retry
    /// test alone cost 19.4s of a 427s suite. The collection now runs once before the first attempt;
    /// a clean collection makes a negative delta rare, so retries — which still run with no forced
    /// collection of their own — should almost never trigger in practice. Retry correctness (MV-916
    /// AC1/AC3) is unaffected: a negative delta is still discarded and retried, and exhaustion still
    /// throws rather than returning or clamping.
    /// </summary>
    internal static class AllocationAssert
    {
        private const int MaxMeasurementAttempts = 5;

        /// <summary>The raw measurement: the managed-heap delta across <paramref name="action"/>.</summary>
        public static long MeasureAllocatedBytes(Action action)
        {
            return MeasureAllocatedBytes(action, GC.GetTotalMemory, MaxMeasurementAttempts);
        }

        /// <summary>
        /// Same measurement, with the heap reader and attempt budget injected so a test can drive the
        /// retry-exhaustion path deterministically without relying on a real GC race (MV-916 AC3).
        /// </summary>
        internal static long MeasureAllocatedBytes(Action action, Func<bool, long> readTotalMemory, int maxAttempts)
        {
            // Force a collection once, before the first attempt, to shrink the chance a collection
            // lands inside the measured window below (MV-916). MV-923: this used to run again before
            // every retry, which is what made the guard expensive — after one clean collection a
            // negative delta should be rare, so retries below don't force another.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                long before = readTotalMemory(false);
                action();
                long after = readTotalMemory(false);
                long delta = after - before;

                // A negative delta means a collection landed inside the window anyway — the reading is
                // invalid, not zero and not negative. Discard it and retry rather than return or clamp it.
                if (delta >= 0) return delta;
            }

            throw new InvalidOperationException(
                $"AllocationAssert could not obtain a clean measurement window after {maxAttempts} attempts " +
                "(a garbage collection kept landing inside the measured delta)");
        }

        /// <summary>Asserts <paramref name="action"/> allocates exactly zero bytes on the managed heap.</summary>
        public static void NoGcMemory(Action action, string message = null)
        {
            long allocated = MeasureAllocatedBytes(action);
            Assert.AreEqual(0L, allocated,
                message ?? $"expected zero bytes allocated on the managed heap, measured {allocated}");
        }
    }
}
