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
    /// The ticket's own suggested mechanism — `GC.GetAllocatedBytesForCurrentThread()` deltas — was
    /// tried first and rejected on hard evidence, not preference: in this project's Unity Editor Mono
    /// runtime that API is a dead stub. It reported a flat 0 for a loop that deliberately allocated
    /// 40 MB (10,000 iterations of `new byte[4096]`), proven with <c>Mv889AllocationAssertTests</c>
    /// during this ticket's own investigation. A guard built on it could never fail, which is worse
    /// than the flaky guard it would replace — a Tier-1-shaped defect (see MV-465), just arrived at by
    /// a different route.
    ///
    /// <see cref="GC.GetTotalMemory"/> was verified instead: 10 trials of an allocation-free loop
    /// measured exactly 0 every time, and 5 trials of a loop deliberately allocating 1000×4096-byte
    /// arrays measured the identical non-zero delta every time (see <c>Mv889AllocationAssertTests</c>,
    /// which pins both). It reads the WHOLE managed heap rather than a single thread's allocations, so
    /// it is not immune in principle to a concurrent background thread allocating during the measured
    /// window — but EditMode tests run synchronously on Unity's single main thread with no yield
    /// inside the measured delegate, so there is no user code running concurrently with it, and the
    /// repeated trials above found no such noise in practice.
    /// </summary>
    internal static class AllocationAssert
    {
        /// <summary>The raw measurement: the managed-heap delta across <paramref name="action"/>.</summary>
        public static long MeasureAllocatedBytes(Action action)
        {
            long before = GC.GetTotalMemory(false);
            action();
            long after = GC.GetTotalMemory(false);
            return after - before;
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
