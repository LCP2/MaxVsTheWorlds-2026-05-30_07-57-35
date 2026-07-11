using NUnit.Framework;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>Unit tests for the YT-30 HUD's pure-logic models: ability cooldowns,
    /// XP rollover, arena progress, boss phases, floating-text motion, and the slice
    /// kill-driver in <see cref="HudModel"/>.</summary>
    public sealed class HudModelTests
    {
        // ---- AbilityCooldown ----

        [Test]
        public void Cooldown_StartsReady()
        {
            var cd = new AbilityCooldown(2f);
            Assert.IsTrue(cd.Ready);
            Assert.AreEqual(0f, cd.RadialFill, 1e-4);
        }

        [Test]
        public void Cooldown_TriggerFillsRadialThenDrainsToReady()
        {
            var cd = new AbilityCooldown(2f);
            Assert.IsTrue(cd.Trigger());
            Assert.IsFalse(cd.Ready);
            Assert.AreEqual(1f, cd.RadialFill, 1e-4); // full wipe the instant it fires
            cd.Tick(1f);
            Assert.AreEqual(0.5f, cd.RadialFill, 1e-4);
            cd.Tick(1f);
            Assert.IsTrue(cd.Ready);
            Assert.AreEqual(0f, cd.RadialFill, 1e-4);
        }

        [Test]
        public void Cooldown_TriggerWhileCoolingIsRejected()
        {
            var cd = new AbilityCooldown(2f);
            cd.Trigger();
            cd.Tick(0.5f);
            Assert.IsFalse(cd.Trigger());        // still cooling
            Assert.AreEqual(1.5f, cd.Remaining, 1e-4); // not re-armed
        }

        [Test]
        public void Cooldown_ZeroLengthIsAlwaysReady()
        {
            var cd = new AbilityCooldown(0f);
            Assert.IsTrue(cd.Trigger());
            Assert.IsTrue(cd.Ready);
        }

        // ---- XpTrack ----

        [Test]
        public void Xp_FillsWithoutLevellingBelowThreshold()
        {
            var xp = new XpTrack(baseRequirement: 20, perLevelGrowth: 10);
            Assert.AreEqual(0, xp.Add(10));
            Assert.AreEqual(1, xp.Level);
            Assert.AreEqual(0.5f, xp.Normalized, 1e-4);
        }

        [Test]
        public void Xp_RollsOverAndCarriesRemainder()
        {
            var xp = new XpTrack(baseRequirement: 20, perLevelGrowth: 10);
            Assert.AreEqual(1, xp.Add(25)); // 20 clears L1 (needs 20), 5 into L2 (needs 30)
            Assert.AreEqual(2, xp.Level);
            Assert.AreEqual(5, xp.XpIntoLevel);
        }

        [Test]
        public void Xp_MultiLevelJump()
        {
            var xp = new XpTrack(baseRequirement: 10, perLevelGrowth: 0);
            Assert.AreEqual(3, xp.Add(35)); // 10+10+10 clears three levels, 5 remainder
            Assert.AreEqual(4, xp.Level);
            Assert.AreEqual(5, xp.XpIntoLevel);
        }

        // ---- ArenaProgress ----

        [Test]
        public void Arena_FractionAndComplete()
        {
            var a = new ArenaProgress(subZonesTotal: 1, factoriesTotal: 3);
            Assert.AreEqual(0f, a.Fraction, 1e-4);
            a.DestroyFactory(); a.DestroyFactory(); a.DestroyFactory();
            a.ClearSubZone();
            Assert.IsTrue(a.Complete);
            Assert.AreEqual(1f, a.Fraction, 1e-4);
        }

        [Test]
        public void Arena_CountsClampAndSignalProminence()
        {
            var a = new ArenaProgress(subZonesTotal: 1, factoriesTotal: 1);
            bool? lastProminent = null;
            a.Changed += p => lastProminent = p;

            a.DestroyFactory();
            Assert.IsFalse(lastProminent.Value);     // factory tick = quiet
            a.DestroyFactory();                       // over cap: clamped, no event
            Assert.AreEqual(1, a.FactoriesDestroyed);

            a.ClearSubZone();
            Assert.IsTrue(lastProminent.Value);       // sub-zone = prominent pop
        }

        // ---- BossState ----

        [Test]
        public void Boss_EngageShowsFullBarWithNameAndPhases()
        {
            var b = new BossState();
            b.Engage("BIG BERMUDA", 3);
            Assert.IsTrue(b.Active);
            Assert.AreEqual("BIG BERMUDA", b.Name);
            Assert.AreEqual(1f, b.HpNormalized, 1e-4);
            Assert.AreEqual(1, b.CurrentPhase);
        }

        [Test]
        public void Boss_PhaseSegmentsAdvanceWithDamage()
        {
            var b = new BossState();
            b.Engage("X", 3);
            b.Damage(0.4f); // hp 0.6 -> damageDone 0.4 -> floor(1.2)=1 -> phase 2
            Assert.AreEqual(2, b.CurrentPhase);
        }

        [Test]
        public void Boss_DefeatDeactivatesAndFires()
        {
            var b = new BossState();
            bool? active = null;
            b.ActiveChanged += a => active = a;
            b.Engage("X", 2);
            b.Damage(1f);
            Assert.IsFalse(b.Active);
            Assert.IsFalse(active.Value);
            Assert.AreEqual(0f, b.HpNormalized, 1e-4);
        }

        // ---- FloatingTextMotion ----

        [Test]
        public void FloatingText_AlphaHoldsThenFades()
        {
            Assert.AreEqual(1f, FloatingTextMotion.AlphaAt(0f), 1e-4);
            Assert.AreEqual(1f, FloatingTextMotion.AlphaAt(0.4f), 1e-4);
            Assert.AreEqual(0f, FloatingTextMotion.AlphaAt(1f), 1e-4);
            Assert.Less(FloatingTextMotion.AlphaAt(0.9f), FloatingTextMotion.AlphaAt(0.6f));
        }

        [Test]
        public void FloatingText_RiseMonotonicUp()
        {
            Assert.AreEqual(0f, FloatingTextMotion.RiseAt(0f), 1e-4);
            Assert.AreEqual(FloatingTextMotion.RisePixels, FloatingTextMotion.RiseAt(1f), 1e-3);
            Assert.Greater(FloatingTextMotion.RiseAt(0.5f), FloatingTextMotion.RiseAt(0.25f));
        }

        [Test]
        public void FloatingText_CritPopsThenSettles()
        {
            Assert.AreEqual(1f, FloatingTextMotion.ScaleAt(0.5f, crit: false), 1e-4);
            Assert.Greater(FloatingTextMotion.ScaleAt(0f, crit: true), 1f);
            Assert.AreEqual(1f, FloatingTextMotion.ScaleAt(1f, crit: true), 1e-4);
        }

        // ---- HudModel slice kill-driver ----

        [Test]
        public void Model_KillGrantsXpAndChargesUltimate()
        {
            var m = new HudModel(xpPerKill: 6, ultimateChargePerKill: 0.25f);
            m.RegisterKill();
            Assert.AreEqual(1, m.Kills);
            Assert.AreEqual(0.25f, m.UltimateCharge, 1e-4);
            Assert.AreEqual(0.75f, m.UltimateRadialFill, 1e-4);
            Assert.IsFalse(m.UltimateReady);
        }

        [Test]
        public void Model_UltimateReadyAtFullChargeThenSpends()
        {
            var m = new HudModel(ultimateChargePerKill: 0.5f);
            m.RegisterKill(); m.RegisterKill();
            Assert.IsTrue(m.UltimateReady);
            Assert.IsTrue(m.TriggerUltimate());
            Assert.AreEqual(0f, m.UltimateCharge, 1e-4);
            Assert.IsFalse(m.TriggerUltimate()); // no longer charged
        }

        [Test]
        public void Model_KillsDestroyFactoriesClearSubZoneThenEngageBoss()
        {
            var m = new HudModel(subZonesTotal: 1, factoriesTotal: 3, killsPerFactory: 2,
                                 bossName: "BIG BERMUDA", bossPhases: 3);
            // 6 kills -> 3 factories destroyed -> sub-zone cleared -> boss engages.
            for (int i = 0; i < 6; i++) m.RegisterKill();
            Assert.AreEqual(3, m.Arena.FactoriesDestroyed);
            Assert.AreEqual(1, m.Arena.SubZonesCleared);
            Assert.IsTrue(m.Arena.Complete);
            Assert.IsTrue(m.Boss.Active);
            Assert.AreEqual("BIG BERMUDA", m.Boss.Name);
        }

        [Test]
        public void Model_KillsAfterEngageDrainBossAndDoNotOverAdvanceArena()
        {
            var m = new HudModel(subZonesTotal: 1, factoriesTotal: 2, killsPerFactory: 1,
                                 bossDamagePerKill: 0.5f);
            m.RegisterKill(); m.RegisterKill(); // 2 factories -> sub-zone -> boss engages
            Assert.IsTrue(m.Boss.Active);
            float before = m.Boss.HpNormalized;
            m.RegisterKill();                    // now drains boss, not the arena
            Assert.Less(m.Boss.HpNormalized, before);
            Assert.AreEqual(2, m.Arena.FactoriesDestroyed); // clamped, not exceeded
        }
    }
}
