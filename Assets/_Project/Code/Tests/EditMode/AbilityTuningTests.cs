using NUnit.Framework;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The Weapon Cooldown ability's pure reduction formula (WV-230) — same shape as
    /// <c>CellEconomyTuning.EfficiencyMultiplier</c>, tested independently of any live ability state.
    /// </summary>
    public sealed class AbilityTuningTests
    {
        [Test]
        public void LevelZeroAppliesNoReduction()
        {
            Assert.That(AbilityTuning.CooldownMultiplier(0, 0.1f), Is.EqualTo(1f).Within(1e-5f),
                "not owned (level 0) must not shorten anything");
        }

        [Test]
        public void EachLevelShavesOffTheReductionFraction()
        {
            Assert.That(AbilityTuning.CooldownMultiplier(1, 0.1f), Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(AbilityTuning.CooldownMultiplier(5, 0.1f), Is.EqualTo(0.5f).Within(1e-5f),
                "a maxed L5 ability at the authored 10%/level should halve every cooldown");
        }

        [Test]
        public void LevelIsClampedToTheFiveLevelCap()
        {
            Assert.That(AbilityTuning.CooldownMultiplier(99, 0.1f),
                Is.EqualTo(AbilityTuning.CooldownMultiplier(5, 0.1f)).Within(1e-5f),
                "the ability only goes to L5 — a runaway level must not drain the multiplier past that");
        }

        [Test]
        public void TheMultiplierNeverGoesNegative()
        {
            Assert.That(AbilityTuning.CooldownMultiplier(5, 1f), Is.EqualTo(0f).Within(1e-5f),
                "an oversized reduction-per-level must clamp at 0x, not swing a cooldown negative");
        }

        [Test]
        public void WaterBalloonLevelOneThrowsTheBaseDistance()
        {
            Assert.That(AbilityTuning.WaterBalloonDistance(1, 4f, 1.5f), Is.EqualTo(4f).Within(1e-5f),
                "level 1 is the ability's starting point — it should throw exactly the base distance");
        }

        [Test]
        public void EachWaterBalloonLevelThrowsFurther()
        {
            // Spec §6a: "level = throw DISTANCE (further each level)" — the whole upgrade is this number.
            float l1 = AbilityTuning.WaterBalloonDistance(1, 4f, 1.5f);
            float l2 = AbilityTuning.WaterBalloonDistance(2, 4f, 1.5f);
            float l3 = AbilityTuning.WaterBalloonDistance(3, 4f, 1.5f);
            Assert.Greater(l2, l1, "level 2 must throw further than level 1");
            Assert.Greater(l3, l2, "level 3 must throw further than level 2");
            Assert.That(l3 - l2, Is.EqualTo(l2 - l1).Within(1e-5f), "the per-level step should be constant");
        }

        [Test]
        public void TheSplashRadiusIsTheSpecMultipleOfTheLargeRobotsFootprintAtLevel1()
        {
            // Spec §6a: "an area ≈ 2× the large robot's footprint" — waterBalloonSplashMult defaults to
            // 2. Level 1 is the Splash Area track's starting level (MV-370) — it must not widen anything yet.
            Assert.That(AbilityTuning.WaterBalloonSplashRadius(0.55f, 1, 2f, 0.3f), Is.EqualTo(1.1f).Within(1e-5f));
        }

        [Test]
        public void EachSplashAreaLevelWidensTheSplashFurther_MV370()
        {
            float l1 = AbilityTuning.WaterBalloonSplashRadius(0.55f, 1, 2f, 0.3f);
            float l2 = AbilityTuning.WaterBalloonSplashRadius(0.55f, 2, 2f, 0.3f);
            float l3 = AbilityTuning.WaterBalloonSplashRadius(0.55f, 3, 2f, 0.3f);
            Assert.Greater(l2, l1, "level 2 must splash wider than level 1");
            Assert.Greater(l3, l2, "level 3 must splash wider than level 2");
        }

        [Test]
        public void TheSplashRadiusNeverGoesNegative()
        {
            Assert.That(AbilityTuning.WaterBalloonSplashRadius(-1f, 1, -1f, 0.3f), Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void RepeatFireLevelOneIsTheBaseCooldownUnmodified_MV370()
        {
            Assert.That(AbilityTuning.WaterBalloonCooldownSeconds(1, 9f, 0.2f), Is.EqualTo(9f).Within(1e-5f),
                "level 1 is the track's starting point — it should not shorten the cooldown yet");
        }

        [Test]
        public void EachRepeatFireLevelShortensTheCooldownFurther_MV370()
        {
            float l1 = AbilityTuning.WaterBalloonCooldownSeconds(1, 9f, 0.2f);
            float l2 = AbilityTuning.WaterBalloonCooldownSeconds(2, 9f, 0.2f);
            float l3 = AbilityTuning.WaterBalloonCooldownSeconds(3, 9f, 0.2f);
            Assert.Less(l2, l1, "level 2 must throw more often than level 1");
            Assert.Less(l3, l2, "level 3 must throw more often than level 2");
        }

        [Test]
        public void RepeatFireCooldownNeverDropsBelowTheFortyPercentFloor_MV370()
        {
            float cooldown = AbilityTuning.WaterBalloonCooldownSeconds(99, 9f, 0.2f);
            Assert.That(cooldown, Is.EqualTo(9f * 0.4f).Within(1e-4f),
                "an oversized level must clamp at the 40% floor, not swing the cooldown to zero");
        }

        // ---------------------------------------------------------------- Teleport (MV-292)

        [Test]
        public void TeleportLevelOneBlinksTheBaseDistance()
        {
            Assert.That(AbilityTuning.TeleportDistance(1, 8f, 4f), Is.EqualTo(8f).Within(1e-5f),
                "level 1 is the ability's starting point — it should blink exactly the base distance");
        }

        [Test]
        public void TeleportLevelTwoBlinksFartherThanLevelOne()
        {
            float l1 = AbilityTuning.TeleportDistance(1, 8f, 4f);
            float l2 = AbilityTuning.TeleportDistance(2, 8f, 4f);
            Assert.Greater(l2, l1, "level 2 must blink farther than level 1");
        }

        [Test]
        public void TeleportHasFourDistinctFartherLevels()
        {
            // MV-339: Teleport widened from 2 upgrade levels to 4 — each level must read as a felt
            // difference from the one before it, all the way to the new cap.
            float l1 = AbilityTuning.TeleportDistance(1, 8f, 4f);
            float l2 = AbilityTuning.TeleportDistance(2, 8f, 4f);
            float l3 = AbilityTuning.TeleportDistance(3, 8f, 4f);
            float l4 = AbilityTuning.TeleportDistance(4, 8f, 4f);

            Assert.Greater(l2, l1);
            Assert.Greater(l3, l2);
            Assert.Greater(l4, l3);
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.Teleport), Is.EqualTo(4));
        }

        // ---------------------------------------------------------------- Speed (WV-231)

        [Test]
        public void SpeedLevelZeroIsNoMultiplier()
        {
            Assert.That(AbilityTuning.SpeedMultiplier(0, 0.15f), Is.EqualTo(1f).Within(1e-5f),
                "not owned (level 0) must not change Max's walk speed");
        }

        [Test]
        public void EachSpeedLevelAddsTheSamePerLevelFraction()
        {
            Assert.That(AbilityTuning.SpeedMultiplier(1, 0.15f), Is.EqualTo(1.15f).Within(1e-5f));
            Assert.That(AbilityTuning.SpeedMultiplier(4, 0.15f), Is.EqualTo(1.60f).Within(1e-5f),
                "a maxed L4 Speed at the authored 15%/level should be +60%");
        }

        // ---------------------------------------------------------------- Force Field (MV-361)

        [Test]
        public void ForceFieldLevelOneIsThePinnedSixtyDamageCap()
        {
            // DECISION #5: "the field absorbs incoming damage up to a 60-damage cap" — level 1 is
            // the ability's starting point, so this must land exactly on the DECISION's own number.
            Assert.That(AbilityTuning.ForceFieldAbsorbCap(1, 60f, 30f), Is.EqualTo(60f).Within(1e-5f));
        }

        [Test]
        public void EachForceFieldLevelRaisesTheAbsorbCapFurther()
        {
            float l1 = AbilityTuning.ForceFieldAbsorbCap(1, 60f, 30f);
            float l2 = AbilityTuning.ForceFieldAbsorbCap(2, 60f, 30f);
            float l3 = AbilityTuning.ForceFieldAbsorbCap(3, 60f, 30f);
            Assert.Greater(l2, l1, "level 2 must absorb more than level 1");
            Assert.Greater(l3, l2, "level 3 must absorb more than level 2");
            Assert.That(l3 - l2, Is.EqualTo(l2 - l1).Within(1e-5f), "the per-level step should be constant");
        }

        [Test]
        public void OnlyLevelThreeForceFieldPopsDealDamage()
        {
            // DECISION #4: pop damage "stays exactly where the Upgrade track already scoped it,
            // level 3" — levels 1-2 must pop as a visual burst only, no damage/knockback.
            Assert.IsFalse(AbilityTuning.ForceFieldPopDealsDamage(1));
            Assert.IsFalse(AbilityTuning.ForceFieldPopDealsDamage(2));
            Assert.IsTrue(AbilityTuning.ForceFieldPopDealsDamage(3));
        }

        [Test]
        public void ForceFieldAbsorbsUpToTheRemainingCapAndLeaksNothingWhenUnderIt()
        {
            var (absorbed, leaked) = AbilityTuning.ForceFieldAbsorb(20f, 60f);
            Assert.That(absorbed, Is.EqualTo(20f).Within(1e-5f));
            Assert.That(leaked, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void ForceFieldLeaksTheOverflowOnceAHitExceedsTheRemainingCap()
        {
            // A hit bigger than what's left must eat exactly the remainder and leak the rest through
            // to PlayerHealth — the field never blocks more than it actually has left.
            var (absorbed, leaked) = AbilityTuning.ForceFieldAbsorb(50f, 30f);
            Assert.That(absorbed, Is.EqualTo(30f).Within(1e-5f));
            Assert.That(leaked, Is.EqualTo(20f).Within(1e-5f));
        }

        [Test]
        public void ForceFieldAbsorbsNothingOnceTheCapIsAlreadyExhausted()
        {
            var (absorbed, leaked) = AbilityTuning.ForceFieldAbsorb(15f, 0f);
            Assert.That(absorbed, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(leaked, Is.EqualTo(15f).Within(1e-5f));
        }

        [Test]
        public void ForceFieldIsAFiveLevelTrack_MV422()
        {
            // MV-422's RIG restructure raised e_ff's own maxLevel from 3 to 5 (radius now levels too).
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.ForceField), Is.EqualTo(5));
        }
    }
}
