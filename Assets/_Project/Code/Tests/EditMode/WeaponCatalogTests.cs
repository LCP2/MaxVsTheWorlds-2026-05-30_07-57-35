using NUnit.Framework;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-263: the RCDA Range/Spread tracks' pure effect formulas, tested independently of any live
    /// track state — same shape as <see cref="AbilityTuningTests"/> for the ability backbone.
    /// </summary>
    public sealed class WeaponCatalogTests
    {
        [Test]
        public void RangeLevelOneIsTheBaseReachUnmodified()
        {
            Assert.That(WeaponCatalog.EffectiveRange(4.5f, 1, 0.6f), Is.EqualTo(4.5f).Within(1e-5f),
                "level 1 is every track's starting level — it must not add anything yet");
        }

        [Test]
        public void EachRangeLevelAddsTheSamePerLevelStep()
        {
            float l1 = WeaponCatalog.EffectiveRange(4.5f, 1, 0.6f);
            float l2 = WeaponCatalog.EffectiveRange(4.5f, 2, 0.6f);
            float l6 = WeaponCatalog.EffectiveRange(4.5f, 6, 0.6f);
            Assert.That(l2 - l1, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(l6 - l1, Is.EqualTo(0.6f * 5f).Within(1e-5f), "5 steps above L1 up to the L6 cap");
        }

        [Test]
        public void SpreadLevelOneIsTheBaseHalfAngleUnmodified()
        {
            Assert.That(WeaponCatalog.EffectiveConeHalfAngle(48f, 1, 0.08f), Is.EqualTo(48f).Within(1e-5f),
                "level 1 is every track's starting level — it must not widen anything yet");
        }

        [Test]
        public void EachSpreadLevelWidensTheConeFurther()
        {
            float l1 = WeaponCatalog.EffectiveConeHalfAngle(48f, 1, 0.08f);
            float l2 = WeaponCatalog.EffectiveConeHalfAngle(48f, 2, 0.08f);
            float l4 = WeaponCatalog.EffectiveConeHalfAngle(48f, 4, 0.08f);
            Assert.Greater(l2, l1, "level 2 must be wider than level 1");
            Assert.Greater(l4, l2, "level 4 (the cap) must be wider still");
            Assert.That(l4, Is.EqualTo(48f * 1.24f).Within(1e-4f), "3 steps above L1 at 8%/level = +24%");
        }
    }
}
