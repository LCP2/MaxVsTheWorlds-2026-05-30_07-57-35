using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>Unit tests for the Big Bermuda boss (YT-27): the pure attack-cycle sequencer
    /// and the HUD boss bar being driven by a real boss instead of the kill stand-in.</summary>
    public sealed class BossTests
    {
        [SetUp]
        [TearDown]
        public void ClearOverrides() => DevTuning.Reset();

        // ---- MV-410: wall clipping / scale / speed / spawn-rate fix ----

        /// <summary>The likely cause of "boss goes through walls": <c>GameObject.CreatePrimitive</c>
        /// leaves a BoxCollider that <see cref="BigBermudaBoss"/>'s required CharacterController then
        /// sits alongside. Unity does not support a Collider and a CharacterController co-located on
        /// one GameObject — the CharacterController must be the sole physical shape.</summary>
        [Test]
        public void BossPrimitive_HasNoColliderBesidesTheCharacterController()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var stray = go.GetComponent<BoxCollider>();
                if (stray != null) Object.DestroyImmediate(stray);
                go.AddComponent<BigBermudaBoss>();

                var colliders = go.GetComponents<Collider>();
                Assert.AreEqual(1, colliders.Length,
                    "the boss must carry exactly one Collider — its CharacterController");
                Assert.IsInstanceOf<CharacterController>(colliders[0]);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>MV-410: "let's make it 1/4 the speed" — reposition speed only, not the charge
        /// (an attack parameter, not locomotion).</summary>
        [Test]
        public void MoveSpeed_IsAQuarterOfTheOldValue()
        {
            const float oldMoveSpeed = 3.6f;
            Assert.AreEqual(oldMoveSpeed * 0.25f, BossTuning.MoveSpeed, 1e-4f);
            Assert.Less(BossTuning.MoveSpeed, BossTuning.ChargeSpeed,
                "the charge must stay dramatically faster than the approach speed");
        }

        /// <summary>MV-410: "make it spawn robots much fast[er]" — halved from 7s to 3.5s.</summary>
        [Test]
        public void VolleyInterval_IsHalvedFromTheOldValue()
        {
            const float oldInterval = 7f;
            Assert.AreEqual(oldInterval * 0.5f, BossTuning.VolleyInterval, 1e-4f);
        }

        /// <summary>MV-413: the Settings panel's "Boss move speed" knob (ENEMIES tab) writes
        /// <see cref="DevTuning.BossMoveSpeed"/>, and <see cref="BigBermudaBoss"/>.Reposition reads
        /// it back through this exact <see cref="DevTuning.Or"/> expression every frame — this is
        /// the regression guard that the wiring between the two stays live.</summary>
        [Test]
        public void BossMoveSpeed_IsLiveTunable()
        {
            Assert.AreEqual(BossTuning.MoveSpeed, DevTuning.Or(DevTuning.BossMoveSpeed, BossTuning.MoveSpeed), 1e-4f,
                "precondition: an untouched knob must play at the authored speed");

            DevTuning.BossMoveSpeed = BossTuning.MoveSpeed * 4f;

            Assert.AreEqual(BossTuning.MoveSpeed * 4f, DevTuning.Or(DevTuning.BossMoveSpeed, BossTuning.MoveSpeed), 1e-4f,
                "a moved Boss move speed slider must reach the same expression the boss's Reposition reads live");
        }

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
