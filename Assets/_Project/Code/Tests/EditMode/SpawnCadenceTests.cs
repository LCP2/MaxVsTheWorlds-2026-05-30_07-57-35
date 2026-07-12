using NUnit.Framework;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>Unit tests for the factory spawn-cadence ramp (YT-63).</summary>
    public sealed class SpawnCadenceTests
    {
        [Test]
        public void StartsAtTheOpeningInterval()
        {
            Assert.AreEqual(1.8f, SpawnCadence.IntervalAt(0f, 1.8f, 1.2f, 45f), 1e-4);
        }

        [Test]
        public void HoldsAtTheMinimumAfterTheRamp()
        {
            Assert.AreEqual(1.2f, SpawnCadence.IntervalAt(45f, 1.8f, 1.2f, 45f), 1e-4);
            Assert.AreEqual(1.2f, SpawnCadence.IntervalAt(90f, 1.8f, 1.2f, 45f), 1e-4);
        }

        [Test]
        public void EasesDownAcrossTheRamp()
        {
            float mid = SpawnCadence.IntervalAt(22.5f, 1.8f, 1.2f, 45f);
            Assert.AreEqual(1.5f, mid, 1e-3);                 // halfway
            Assert.Less(mid, 1.8f);
            Assert.Greater(mid, 1.2f);
        }

        [Test]
        public void ZeroRampIsImmediatelyAtMin()
        {
            Assert.AreEqual(1.2f, SpawnCadence.IntervalAt(0f, 1.8f, 1.2f, 0f), 1e-4);
        }

        [Test]
        public void NegativeElapsedClampsToStart()
        {
            Assert.AreEqual(1.8f, SpawnCadence.IntervalAt(-5f, 1.8f, 1.2f, 45f), 1e-4);
        }
    }
}
