using NUnit.Framework;
using MaxWorlds.Factories;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>Unit tests for the Mower Hutch factory (YT-37): the destructible health model
    /// and the HUD arena tracker switching from the kill stand-in to real factory signals.</summary>
    public sealed class FactoryTests
    {
        // ---- DestructibleHealth ----

        [Test]
        public void Health_StartsFullAndAlive()
        {
            var h = new DestructibleHealth(240f);
            Assert.IsTrue(h.IsAlive);
            Assert.AreEqual(1f, h.Normalized, 1e-4);
        }

        [Test]
        public void Health_DamageReducesAndFiresChanged()
        {
            var h = new DestructibleHealth(100f);
            float last = -1f;
            h.Changed += c => last = c;
            bool died = h.TakeDamage(30f);
            Assert.IsFalse(died);
            Assert.AreEqual(70f, last, 1e-4);
            Assert.AreEqual(0.7f, h.Normalized, 1e-4);
        }

        [Test]
        public void Health_DestroyedFiresOnceOnLethalHit()
        {
            var h = new DestructibleHealth(50f);
            int destroyed = 0;
            h.Destroyed += () => destroyed++;
            Assert.IsFalse(h.TakeDamage(20f));
            Assert.IsTrue(h.TakeDamage(40f));  // crosses zero -> lethal
            Assert.IsFalse(h.IsAlive);
            Assert.AreEqual(1, destroyed);
            Assert.IsFalse(h.TakeDamage(10f)); // no further damage/events after death
            Assert.AreEqual(1, destroyed);
        }

        [Test]
        public void Health_IgnoresNonPositiveDamage()
        {
            var h = new DestructibleHealth(50f);
            Assert.IsFalse(h.TakeDamage(0f));
            Assert.IsFalse(h.TakeDamage(-5f));
            Assert.AreEqual(50f, h.Current, 1e-4);
        }

        // ---- HudModel external-factory wiring ----

        [Test]
        public void Model_ExternalFactoriesDisablesKillStandIn()
        {
            var m = new HudModel(subZonesTotal: 1, factoriesTotal: 1, killsPerFactory: 2);
            m.UseExternalFactories();
            for (int i = 0; i < 6; i++) m.RegisterKill();
            // Kills no longer destroy factories or engage the boss — a real factory drives that.
            Assert.AreEqual(0, m.Arena.FactoriesDestroyed);
            Assert.IsFalse(m.Boss.Active);
        }

        [Test]
        public void Model_RealFactoryDestroyClearsArenaAndEngagesBoss()
        {
            var m = new HudModel(subZonesTotal: 1, factoriesTotal: 1);
            m.RegisterFactoryDestroyed();
            Assert.AreEqual(1, m.Arena.FactoriesDestroyed);
            Assert.AreEqual(1, m.Arena.SubZonesCleared);
            Assert.IsTrue(m.Arena.Complete);
            Assert.IsTrue(m.Boss.Active);
        }

        [Test]
        public void Model_RealFactoryDestroyIsClampedBeyondTotal()
        {
            var m = new HudModel(subZonesTotal: 1, factoriesTotal: 1);
            m.RegisterFactoryDestroyed();
            m.RegisterFactoryDestroyed(); // extra signal must not over-count
            Assert.AreEqual(1, m.Arena.FactoriesDestroyed);
        }
    }
}
