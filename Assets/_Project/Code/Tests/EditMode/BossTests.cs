using NUnit.Framework;
using MaxWorlds.Bosses;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>Unit tests for the Big Bermuda boss (YT-27): the pure attack-cycle sequencer
    /// and the HUD boss bar being driven by a real boss instead of the kill stand-in.</summary>
    public sealed class BossTests
    {
        // ---- BigBermudaBrain ----

        [Test]
        public void Brain_StartsInRepositionAndEntered()
        {
            var b = new BigBermudaBrain();
            Assert.AreEqual(BossAction.Reposition, b.Current);
            Assert.IsTrue(b.JustEntered);
            Assert.IsFalse(b.Enraged);
        }

        [Test]
        public void Brain_CyclesInOrder()
        {
            var b = new BigBermudaBrain();
            // Drive well past the first phase; step small so only one transition per tick.
            var seen = new System.Collections.Generic.List<BossAction>();
            seen.Add(b.Current);
            for (int i = 0; i < 2000 && seen.Count < 5; i++)
            {
                b.Tick(0.05f, 1f);
                if (b.JustEntered) seen.Add(b.Current);
            }
            Assert.AreEqual(BossAction.Reposition, seen[0]);
            Assert.AreEqual(BossAction.ChargeWindup, seen[1]);
            Assert.AreEqual(BossAction.Charge, seen[2]);
            Assert.AreEqual(BossAction.Recover, seen[3]);
            Assert.AreEqual(BossAction.Reposition, seen[4]); // wraps around
        }

        [Test]
        public void Brain_EnragesBelowThreshold()
        {
            var b = new BigBermudaBrain(enrageThreshold: 0.5f);
            b.Tick(0.01f, 0.9f);
            Assert.IsFalse(b.Enraged);
            b.Tick(0.01f, 0.4f);
            Assert.IsTrue(b.Enraged);
        }

        [Test]
        public void Brain_EnrageShortensPhases()
        {
            // Measure across several transitions: the opening phase length is fixed at
            // construction (enrage unknown yet), but every phase after enrage kicks in is
            // scaled down, so the cumulative time to the Nth transition is clearly shorter.
            float TimeToNthTransition(float hp, int n)
            {
                var b = new BigBermudaBrain(enrageThreshold: 0.5f, enrageTimeScale: 0.5f);
                float t = 0f;
                int transitions = 0;
                for (int i = 0; i < 100000; i++)
                {
                    b.Tick(0.01f, hp);
                    t += 0.01f;
                    if (b.JustEntered && ++transitions >= n) return t;
                }
                return t;
            }
            float calm = TimeToNthTransition(1f, 4);
            float enraged = TimeToNthTransition(0.2f, 4);
            Assert.Less(enraged, calm); // enraged reaches later phases sooner
        }

        // ---- HUD boss bar driven by a real boss ----

        [Test]
        public void Model_ExternalBossStopsKillAndArenaStandIn()
        {
            var m = new HudModel(subZonesTotal: 1, factoriesTotal: 1);
            m.UseExternalBoss();
            m.RegisterFactoryDestroyed();      // arena completes...
            Assert.IsFalse(m.Boss.Active);     // ...but the stand-in boss must NOT engage
        }

        [Test]
        public void Model_RealBossEngageHealthAndDefeat()
        {
            var m = new HudModel();
            m.EngageBossExternal("BIG BERMUDA", 2);
            Assert.IsTrue(m.Boss.Active);
            Assert.AreEqual("BIG BERMUDA", m.Boss.Name);
            Assert.AreEqual(2, m.Boss.Phases);

            m.SetBossHealth(0.5f);
            Assert.AreEqual(0.5f, m.Boss.HpNormalized, 1e-4);
            Assert.AreEqual(2, m.Boss.CurrentPhase); // 50% -> second phase segment

            m.SetBossHealth(0f);
            Assert.IsFalse(m.Boss.Active);          // reaching 0 defeats it
        }

        [Test]
        public void Model_RealBossKillsDoNotDrainBossBar()
        {
            var m = new HudModel();
            m.EngageBossExternal("X", 2);
            m.SetBossHealth(0.8f);
            m.RegisterKill();                        // kills must not move the real boss bar
            Assert.AreEqual(0.8f, m.Boss.HpNormalized, 1e-4);
        }
    }
}
