using NUnit.Framework;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-889 — pins <see cref="AllocationAssert"/>'s own contract: it must report exactly zero for a
    /// loop that genuinely allocates nothing, and a non-zero figure for a loop that deliberately
    /// allocates. Both are resolved values read back from the measurement itself (Tier 2 per MV-465),
    /// never an authored constant. Fails to compile on the base commit named in the PR —
    /// AllocationAssert does not exist there.
    ///
    /// The 1000-iteration/4096-byte sizing below isn't arbitrary: it's the exact shape used to verify
    /// <see cref="AllocationAssert"/> during this ticket's investigation (10 trials of the
    /// allocation-free loop measured exactly 0 every time; 5 trials of this allocating loop measured
    /// an identical non-zero delta every time), so this test pins the same measurement this ticket's
    /// fix comment cites as evidence.
    /// </summary>
    public sealed class Mv889AllocationAssertTests
    {
        private static int SumNoAlloc(int n)
        {
            int total = 0;
            for (int i = 0; i < n; i++) total += i;
            return total;
        }

        private static byte[] s_sink;

        private static void AllocateN(int n)
        {
            for (int i = 0; i < n; i++)
            {
                s_sink = new byte[4096];
            }
        }

        [Test]
        public void MeasureAllocatedBytes_IsZeroForAnAllocationFreeLoop_AndNonZeroForALoopThatAllocates()
        {
            // Warm up JIT/static caches outside the measured window, same as every allocation guard
            // in this suite.
            SumNoAlloc(10);
            AllocateN(1);

            long allocationFree = AllocationAssert.MeasureAllocatedBytes(() => SumNoAlloc(1000));
            Assert.AreEqual(0L, allocationFree,
                "a loop doing only value-type arithmetic must measure exactly zero allocated bytes");

            long allocating = AllocationAssert.MeasureAllocatedBytes(() => AllocateN(1000));
            Assert.That(allocating, Is.GreaterThan(0),
                "a loop that deliberately allocates a new array each iteration must measure a non-zero figure");
        }
    }
}
