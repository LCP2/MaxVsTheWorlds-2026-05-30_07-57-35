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
        public void TheSplashRadiusIsTheSpecMultipleOfTheLargeRobotsFootprint()
        {
            // Spec §6a: "an area ≈ 2× the large robot's footprint" — waterBalloonSplashMult defaults to 2.
            Assert.That(AbilityTuning.WaterBalloonSplashRadius(0.55f, 2f), Is.EqualTo(1.1f).Within(1e-5f));
        }

        [Test]
        public void TheSplashRadiusNeverGoesNegative()
        {
            Assert.That(AbilityTuning.WaterBalloonSplashRadius(-1f, -1f), Is.EqualTo(0f).Within(1e-5f));
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
            Assert.Greater(l2, l1, "level 2 (the cap) must blink farther than level 1");
        }

        [Test]
        public void TeleportsAuthoredRangeClearsDashsShortBurst()
        {
            // Dash covers dashSpeed * dashDuration = 18 * 0.18 = 3.24m (PlayerController). Teleport
            // must read as a clearly longer-range tool at both its levels (MV-292 AC1), not a
            // re-skinned Dash.
            const float dashDistance = 18f * 0.18f;
            float teleportL1 = AbilityTuning.TeleportDistance(1,
                AbilityTuning.DefaultTeleportBaseDistance, AbilityTuning.DefaultTeleportDistancePerLevel);

            Assert.Greater(teleportL1, dashDistance * 2f,
                "Teleport's L1 distance must clearly exceed Dash's short reposition burst");
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
    }
}
