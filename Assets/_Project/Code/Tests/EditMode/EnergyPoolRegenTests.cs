using NUnit.Framework;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The tank's refill rate became settable so the dev tuning panel can retune a LIVE pool
    /// (YT-105). Rebuilding the pool instead would refill it mid-fight and drop the HUD's
    /// subscription, so these prove the setter does the job without either side effect.
    /// </summary>
    public sealed class EnergyPoolRegenTests
    {
        [Test]
        public void SettingRegen_ChangesTheRefillRate()
        {
            var pool = new EnergyPool(100f, 10f, 0f);
            pool.TrySpend(50f);

            pool.RegenPerSec = 40f;
            pool.Tick(1f);

            Assert.That(pool.Current, Is.EqualTo(90f).Within(0.001f),
                "Expected the new 40/s rate, not the 10/s the pool was built with.");
        }

        [Test]
        public void SettingRegen_DoesNotRefillTheTankOrLoseSubscribers()
        {
            var pool = new EnergyPool(100f, 10f, 0f);
            pool.TrySpend(60f);

            int fired = 0;
            pool.Changed += _ => fired++;

            pool.RegenPerSec = 55f;

            Assert.That(pool.Current, Is.EqualTo(40f).Within(0.001f),
                "Retuning the rate must not top the tank up — that would be a free reload.");

            pool.Tick(0.1f);
            Assert.That(fired, Is.GreaterThan(0), "The HUD subscription must survive a retune.");
        }

        [Test]
        public void ANegativeRegenIsClampedToZero()
        {
            var pool = new EnergyPool(100f, 10f, 0f) { RegenPerSec = -5f };
            pool.TrySpend(50f);
            pool.Tick(1f);

            Assert.That(pool.Current, Is.EqualTo(50f).Within(0.001f),
                "A negative rate must stall the refill, never drain the tank.");
        }
    }
}
