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
        public void ClearOverrides()
        {
            DevTuning.Reset();
            BossCensus.Reset();
        }

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

        // ---- MV-542: 2+ boss fights — combined HUD health, victory gated on the LAST death ----

        /// <summary>AC2. The old single-boss code called <c>HudSignals.EmitBossHealth</c> and
        /// <c>EmitBossDefeated</c> off each boss's OWN death — fine for one boss, but with two it
        /// means a last-write-wins HUD bar and a fight that ends the moment either boss falls, not
        /// both. <see cref="BossCensus"/> (new this ticket — it doesn't exist before MV-542, so this
        /// can't be run red against the old code, only reasoned about it: reading BigBermudaBoss's own
        /// pre-542 Wake/TakeDamage/OnDeath shows exactly that per-instance-only signal pattern) fixes
        /// that: the bar shows the COMBINED (sum current / sum max) fraction, and BossDefeated — the
        /// signal BossVictoryPayoff, the exit door and results all wait on — fires only once every
        /// boss reports in. Exercises BossCensus directly rather than driving BigBermudaBoss's own
        /// Intro/Fight phase machine, which only advances on Update; EditMode tests cannot tick the
        /// player loop, but the census logic itself does not need a boss to be mid-fight.</summary>
        [Test]
        public void BossCensus_CombinesHealthAndGatesDefeatOnTheLastBoss()
        {
            GameObject go1 = NewBossHandle();
            GameObject go2 = NewBossHandle();
            var boss1 = go1.GetComponent<BigBermudaBoss>();
            var boss2 = go2.GetComponent<BigBermudaBoss>();

            float lastHealth = -1f;
            int defeatedCount = 0;
            System.Action<float> onHealth = h => lastHealth = h;
            System.Action onDefeated = () => defeatedCount++;

            HudSignals.BossHealthChanged += onHealth;
            HudSignals.BossDefeated += onDefeated;
            try
            {
                BossCensus.Register(boss1, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 1);
                BossCensus.Register(boss2, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 1);
                Assert.AreEqual(1f, lastHealth, 1e-4f, "two full-health bosses must combine to a full bar");

                BossCensus.ReportHealth(boss1, current: 0f, max: 100f); // boss1 fully drained, still standing
                Assert.AreEqual(0.5f, lastHealth, 1e-4f,
                    "one drained boss + one full boss must average to a half bar, not last-write-wins");
                Assert.AreEqual(0, defeatedCount, "must not defeat while a boss is still standing");

                BossCensus.ReportDefeated(boss1);
                Assert.AreEqual(0, defeatedCount, "the FIRST boss dying must not fire BossDefeated -- boss2 is still up");
                Assert.AreEqual(1f, lastHealth, 1e-4f, "combined bar must now read boss2's health alone");

                BossCensus.ReportDefeated(boss2);
                Assert.AreEqual(1, defeatedCount, "BossDefeated must fire once the LAST boss dies");
            }
            finally
            {
                HudSignals.BossHealthChanged -= onHealth;
                HudSignals.BossDefeated -= onDefeated;
                Object.DestroyImmediate(go1);
                Object.DestroyImmediate(go2);
            }
        }

        // ---- MV-591: "no boss left" must be per-area, not scene-wide ----

        /// <summary>The pre-fix code kept <see cref="BossCensus"/>'s Living list scene-global, so a12's
        /// one authored boss being the only one registered read as the LAST boss in the entire game —
        /// defeating it fired <c>BossDefeated</c> (and, downstream, the whole victory chain) 18 areas
        /// early. World 1 v4 authors bosses at a12 (x1), a20 (x2) and a30 (x3); each area's payoff must
        /// wait for its OWN last boss, never the whole scene's, and areas must not cross-contaminate.</summary>
        [Test]
        public void ReportDefeated_GatesOnBossesRemainingInTheSameArea_NotSceneWide()
        {
            GameObject go12 = NewBossHandle();
            GameObject go20A = NewBossHandle();
            GameObject go20B = NewBossHandle();
            var boss12 = go12.GetComponent<BigBermudaBoss>();
            var boss20A = go20A.GetComponent<BigBermudaBoss>();
            var boss20B = go20B.GetComponent<BigBermudaBoss>();

            int defeatedCount = 0;
            System.Action onDefeated = () => defeatedCount++;
            HudSignals.BossDefeated += onDefeated;
            try
            {
                BossCensus.Register(boss12, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 12);
                BossCensus.Register(boss20A, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 20);
                BossCensus.Register(boss20B, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 20);

                BossCensus.ReportDefeated(boss12);
                Assert.AreEqual(1, defeatedCount,
                    "a12's only boss dying must clear ITS OWN area and fire BossDefeated for it");
                Assert.IsTrue(BossCensus.AnyLivingIn(20),
                    "a20's bosses must be entirely unaffected by a12's boss dying");

                BossCensus.ReportDefeated(boss20A);
                Assert.AreEqual(1, defeatedCount,
                    "one of a20's TWO bosses dying must NOT fire BossDefeated -- a20's other boss is still up");
                Assert.IsTrue(BossCensus.AnyLivingIn(20), "a20's second boss is still standing");

                BossCensus.ReportDefeated(boss20B);
                Assert.AreEqual(2, defeatedCount, "a20's LAST boss dying must fire BossDefeated for a20");
                Assert.IsFalse(BossCensus.AnyLivingIn(20), "a20 must now read as clear of bosses");
            }
            finally
            {
                HudSignals.BossDefeated -= onDefeated;
                Object.DestroyImmediate(go12);
                Object.DestroyImmediate(go20A);
                Object.DestroyImmediate(go20B);
            }
        }

        private static GameObject NewBossHandle()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            go.AddComponent<BigBermudaBoss>();
            return go;
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
