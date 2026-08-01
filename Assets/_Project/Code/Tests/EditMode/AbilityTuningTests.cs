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
    }
}
