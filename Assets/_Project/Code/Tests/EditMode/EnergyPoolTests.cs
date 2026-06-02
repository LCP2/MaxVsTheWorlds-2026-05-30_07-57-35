using NUnit.Framework;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>Unit tests for the gadget energy/ammo model (YT-35).</summary>
    public sealed class EnergyPoolTests
    {
        [Test]
        public void StartsFull()
        {
            var e = new EnergyPool(100f, 25f, 0.5f);
            Assert.AreEqual(100f, e.Current);
            Assert.AreEqual(1f, e.Normalized);
        }

        [Test]
        public void TrySpend_DrainsWhenAvailable()
        {
            var e = new EnergyPool(100f, 25f, 0.5f);
            Assert.IsTrue(e.TrySpend(30f));
            Assert.AreEqual(70f, e.Current, 1e-4);
        }

        [Test]
        public void TrySpend_FailsAndNoChangeWhenInsufficient()
        {
            var e = new EnergyPool(10f, 25f, 0.5f);
            Assert.IsFalse(e.TrySpend(30f));
            Assert.AreEqual(10f, e.Current, 1e-4);
        }

        [Test]
        public void Regen_WaitsForDelayThenRefills()
        {
            var e = new EnergyPool(100f, 50f, 0.5f);
            e.TrySpend(50f);                 // Current = 50, drain timer reset
            e.Tick(0.4f);                    // still within delay
            Assert.AreEqual(50f, e.Current, 1e-4);
            e.Tick(0.2f);                    // now past 0.5s delay -> regen this frame
            Assert.Greater(e.Current, 50f);
        }

        [Test]
        public void Regen_ClampsToMax()
        {
            var e = new EnergyPool(100f, 1000f, 0f);
            e.TrySpend(40f);
            e.Tick(10f);
            Assert.AreEqual(100f, e.Current, 1e-4);
        }

        [Test]
        public void Changed_FiresOnSpend()
        {
            var e = new EnergyPool(100f, 25f, 0.5f);
            int calls = 0;
            e.Changed += _ => calls++;
            e.TrySpend(5f);
            Assert.AreEqual(1, calls);
        }
    }
}
