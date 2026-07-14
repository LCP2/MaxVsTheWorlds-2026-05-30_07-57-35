using NUnit.Framework;
using UnityEngine;
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

        // ---- the HUD counts the factories the LEVEL has (YT-92) ----

        /// <summary>
        /// The HUD is built before the level is, so it cannot be told how many factories the run has —
        /// it has to learn. Two factories registering must make the tracker read "of 2", and the arena
        /// must NOT complete on the first kill: a HUD that says the yard is clear while a factory is
        /// still pumping robots is worse than no HUD at all.
        /// </summary>
        [Test]
        public void Model_LearnsHowManyFactoriesTheLevelHas_AndWaitsForAllOfThem()
        {
            var m = new HudModel();   // the default, one-factory guess — the level knows better

            m.RegisterFactory();
            m.RegisterFactory();
            Assert.AreEqual(2, m.Arena.FactoriesTotal, "the HUD is still counting toward one factory");

            m.RegisterFactoryDestroyed();
            Assert.AreEqual(1, m.Arena.FactoriesDestroyed);
            Assert.IsFalse(m.Arena.Complete, "the arena read as cleared with a factory still standing");
            Assert.IsFalse(m.Boss.Active, "the boss bar engaged with a factory still standing");

            m.RegisterFactoryDestroyed();
            Assert.IsTrue(m.Arena.Complete, "the arena never cleared, with every factory down");
            Assert.IsTrue(m.Boss.Active);
        }

        // ---- the gate takes as many keys as the level gives it (YT-92) ----

        [Test]
        public void Gate_WithNoKeys_OpensOnTheFirstUnlock()
        {
            var gate = New<SubZoneGate>();
            Assert.AreEqual(0, gate.Keys);

            gate.Unlock();

            Assert.AreEqual(0, gate.KeysRemaining);
            Assert.IsTrue(Opening(gate), "a gate nobody keyed refused to open — a hand-built level " +
                                         "would have a door that never opens");
        }

        [Test]
        public void Gate_WithTwoKeys_OpensOnTheSecond_NotTheFirst()
        {
            var gate = New<SubZoneGate>();
            gate.AddKey();
            gate.AddKey();

            Assert.AreEqual(2, gate.Keys);

            gate.Unlock();
            Assert.AreEqual(1, gate.KeysRemaining);
            Assert.IsFalse(Opening(gate),
                "the gate opened on the first factory — the second is still standing, and the player " +
                "can walk past it straight to the boss");

            gate.Unlock();
            Assert.AreEqual(0, gate.KeysRemaining);
            Assert.IsTrue(Opening(gate), "the gate never opened, with every factory down");
        }

        /// <summary>Can Max walk through it? The gate stops blocking the moment it starts to open; the
        /// sink into the ground that follows is theatre. (Asked of the gate rather than of its
        /// collider, because in EditMode nothing has woken up to disable one.)</summary>
        private static bool Opening(SubZoneGate gate) => gate.Unlocked;

        private static T New<T>() where T : Component => new GameObject("Gate").AddComponent<T>();
    }
}
