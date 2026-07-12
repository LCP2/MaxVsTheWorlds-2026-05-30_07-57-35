using NUnit.Framework;
using MaxWorlds.Combat;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>The power fantasy (YT-67). Levelling used to fill a bar and change a number while
    /// Max stayed exactly as strong as before; these pin the escalation that fixes that, and the
    /// pace at which it has to arrive.</summary>
    public sealed class PowerRampTests
    {
        [Test]
        public void Level1_IsTheBaseline_NotABuff()
        {
            Assert.AreEqual(1f, PowerRamp.DamageMultiplier(1), 1e-4);
            Assert.AreEqual(1f, PowerRamp.FireRateMultiplier(1), 1e-4);
            Assert.AreEqual(1f, PowerRamp.DpsMultiplier(1), 1e-4);
        }

        [Test]
        public void EveryLevelMakesMaxStrictlyStronger()
        {
            for (int level = 2; level <= 12; level++)
                Assert.Greater(PowerRamp.DpsMultiplier(level), PowerRamp.DpsMultiplier(level - 1),
                    $"level {level} granted nothing — that's the bug this ticket exists to fix");
        }

        [Test]
        public void TheBoostAlternates_SoEachLevelUpHasSomethingSpecificToShout()
        {
            Assert.AreEqual(PowerBoost.FireRate, PowerRamp.BoostAt(2));
            Assert.AreEqual(PowerBoost.Damage, PowerRamp.BoostAt(3));
            Assert.AreEqual(PowerBoost.FireRate, PowerRamp.BoostAt(4));
            Assert.AreEqual(PowerBoost.Damage, PowerRamp.BoostAt(5));
        }

        [Test]
        public void TheBoostThatIsAnnounced_IsTheBoostThatIsBanked()
        {
            // The popup must not lie. A level that shouts "+DAMAGE" must be the level whose damage
            // multiplier actually moved.
            for (int level = 2; level <= 10; level++)
            {
                bool damageMoved = PowerRamp.DamageMultiplier(level) > PowerRamp.DamageMultiplier(level - 1);
                bool rateMoved = PowerRamp.FireRateMultiplier(level) > PowerRamp.FireRateMultiplier(level - 1);

                if (PowerRamp.BoostAt(level) == PowerBoost.Damage)
                {
                    Assert.IsTrue(damageMoved, $"level {level} shouts DAMAGE but damage didn't change");
                    Assert.IsFalse(rateMoved);
                }
                else
                {
                    Assert.IsTrue(rateMoved, $"level {level} shouts FIRE RATE but fire rate didn't change");
                    Assert.IsFalse(damageMoved);
                }
            }
        }

        [Test]
        public void BoostLabel_NamesTheReward()
        {
            StringAssert.Contains("FIRE RATE", PowerRamp.BoostLabel(2));
            StringAssert.Contains("DAMAGE", PowerRamp.BoostLabel(3));
        }

        [Test]
        public void ByLevelFive_TheRampIsUnmistakable()
        {
            // The ticket's actual goal: "by 90 seconds in, the player should notice they're visibly
            // mowing down more than they were at the start." A few percent would not be noticed.
            Assert.Greater(PowerRamp.DpsMultiplier(5), 1.6f);
        }

        [Test]
        public void TheRampDoesNotRunAway()
        {
            // Strong, but not so strong that the slice trivialises itself before the boss.
            Assert.Less(PowerRamp.DpsMultiplier(10), 5f);
        }

        [Test]
        public void NonsenseLevels_ClampToTheBaseline_RatherThanGoingNegative()
        {
            foreach (int level in new[] { 0, -1, -99 })
            {
                Assert.AreEqual(1f, PowerRamp.DamageMultiplier(level), 1e-4);
                Assert.AreEqual(1f, PowerRamp.FireRateMultiplier(level), 1e-4);
            }
        }

        // --- The pace the ticket asks for -------------------------------------------------------

        [Test]
        public void TheFirstBoostLandsInTheFirstFewKills()
        {
            // "Make the XP-to-level curve quick enough that the first boost lands in the first
            // ~20-30s." Robots spawn roughly every 1.2-1.8s, so a handful of kills IS that window.
            var model = new HudModel();

            int kills = 0;
            while (model.Xp.Level < 2 && kills < 50)
            {
                model.RegisterKill();
                kills++;
            }

            Assert.AreEqual(2, model.Xp.Level);
            Assert.LessOrEqual(kills, 6, $"took {kills} kills to reach level 2 — too slow to hook");
        }
    }
}
