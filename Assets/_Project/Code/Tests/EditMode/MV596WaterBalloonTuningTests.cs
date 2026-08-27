using NUnit.Framework;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-596 (Lee, 26 Aug 2026: "balloon fire rate higher at base (+40%), damage less (-25%),
    /// swap SPLASH with AUTO-FIRE/FIRE RATE") — sole guard on this tuning pass; do not cull.
    /// AC1-AC4 in one test — the testing policy caps a ticket at one new EditMode test.
    /// </summary>
    public sealed class MV596WaterBalloonTuningTests
    {
        [SetUp]
        public void SetUp() => RigBoardLayout.ResetForTests();

        [TearDown]
        public void TearDown() => RigBoardLayout.ResetForTests();

        [Test]
        public void FasterBaseCooldown_LighterDamage_AndSplashAutoFireColumnsSwapped()
        {
            // ---------------------------------------------------------------- AC1: fire rate +40% at base
            float l1 = AbilityTuning.WaterBalloonCooldownSeconds(1,
                WeaponCatalog.DefaultWaterBalloonCooldownSeconds, AbilityTuning.DefaultWaterBalloonRepeatFirePerLevel);
            Assert.That(l1, Is.EqualTo(6.43f).Within(0.01f),
                "the base (level 1) cooldown must be the old 9s divided by 1.4 (+40% fire rate)");

            float l3 = AbilityTuning.WaterBalloonCooldownSeconds(3,
                WeaponCatalog.DefaultWaterBalloonCooldownSeconds, AbilityTuning.DefaultWaterBalloonRepeatFirePerLevel);
            Assert.That(l3, Is.EqualTo(l1 * 0.6f).Within(0.001f),
                "the per-level curve itself must be untouched — level 3 is still 0.6x the base");

            // ---------------------------------------------------------------- AC2: damage -25%
            // Two archetypes with different max health (EnemyArchetype.Rusher/Bruiser) so the assertion
            // proves the cut is proportional, not a fluke of one number.
            float rusherDamage = AbilityTuning.WaterBalloonDamage(32f, AbilityTuning.DefaultWaterBalloonDamagePct);
            Assert.That(rusherDamage, Is.EqualTo(32f * 0.375f).Within(0.001f),
                "a balloon hit must remove exactly 37.5% of the rusher's own max health");

            float bruiserDamage = AbilityTuning.WaterBalloonDamage(68f, AbilityTuning.DefaultWaterBalloonDamagePct);
            Assert.That(bruiserDamage, Is.EqualTo(68f * 0.375f).Within(0.001f),
                "a balloon hit must remove exactly 37.5% of the bruiser's own (different) max health");

            // ---------------------------------------------------------------- AC3 + AC4: column swap
            RigAbilityLayout splash = null, lob = null, autoFire = null, fireRate = null;
            foreach (var ab in RigBoardLayout.Abilities)
            {
                if (ab.Id == "s_spl") splash = ab;
                else if (ab.Id == "s_lob") lob = ab;
                else if (ab.Id == "s_aut") autoFire = ab;
                else if (ab.Id == "s_rte") fireRate = ab;
            }
            Assert.That(splash, Is.Not.Null, "fixture: s_spl must exist in the resolved layout");
            Assert.That(lob, Is.Not.Null, "fixture: s_lob must exist in the resolved layout");
            Assert.That(autoFire, Is.Not.Null, "fixture: s_aut must exist in the resolved layout");
            Assert.That(fireRate, Is.Not.Null, "fixture: s_rte must exist in the resolved layout");

            Assert.Greater(splash.X, lob.X, "AC3: SPLASH must resolve to the RIGHT of LOB after the swap");
            Assert.Less(autoFire.X, lob.X, "AC3: AUTO-FIRE must resolve to the LEFT of LOB after the swap");
            Assert.That(fireRate.X, Is.EqualTo(autoFire.X).Within(0.01f),
                "AC4: FIRE RATE must still resolve directly beneath AUTO-FIRE after the swap");
        }
    }
}
