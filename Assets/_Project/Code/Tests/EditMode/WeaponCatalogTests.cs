using NUnit.Framework;
using MaxWorlds.Combat;
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
        public void RangeTrack_MaxLevelReachIsRoughlyThreeTimesBase_MV280()
        {
            float baseReach = WaterBlaster.DefaultRange;
            float maxReach = WeaponCatalog.EffectiveRange(
                baseReach, WeaponCatalog.MaxLevel(WeaponTrackKind.Range), WeaponCatalog.DefaultRcdaRangePerLevel);

            Assert.That(maxReach, Is.EqualTo(baseReach * 3f).Within(0.05f),
                "MV-280: retuning the base reach or the per-level step must keep the max Range level at ~3x base");
        }

        [Test]
        public void SpreadLevelOneIsTheBaseHalfAngleUnmodified()
        {
            Assert.That(WeaponCatalog.EffectiveConeHalfAngle(5f, 1, 3f), Is.EqualTo(5f).Within(1e-5f),
                "level 1 is every track's starting level — it must not widen anything yet");
        }

        [Test]
        public void EachSpreadLevelWidensTheConeFurther()
        {
            float l1 = WeaponCatalog.EffectiveConeHalfAngle(5f, 1, 3f);
            float l2 = WeaponCatalog.EffectiveConeHalfAngle(5f, 2, 3f);
            float l4 = WeaponCatalog.EffectiveConeHalfAngle(5f, 4, 3f);
            Assert.Greater(l2, l1, "level 2 must be wider than level 1");
            Assert.Greater(l4, l2, "level 4 (the cap) must be wider still");
            Assert.That(l4, Is.EqualTo(50f).Within(1e-4f), "3 steps above L1 at 300%/level = 10x base");
        }

        [Test]
        public void SpreadTrack_BaseArcIsTenDegreesTotal_MV281()
        {
            Assert.That(WaterBlaster.DefaultConeHalfAngle * 2f, Is.EqualTo(10f).Within(0.01f),
                "MV-281: base spray must read as a narrow ~10° total arc");
        }

        [Test]
        public void SpreadTrack_MaxLevelArcIsOneHundredDegreesTotal_MV281()
        {
            float baseHalfAngle = WaterBlaster.DefaultConeHalfAngle;
            float maxHalfAngle = WeaponCatalog.EffectiveConeHalfAngle(
                baseHalfAngle, WeaponCatalog.MaxLevel(WeaponTrackKind.Spread), WeaponCatalog.DefaultRcdaSpreadPerLevel);

            Assert.That(maxHalfAngle * 2f, Is.EqualTo(100f).Within(0.5f),
                "MV-281: retuning the base angle or the per-level step must keep the maxed Spread track at ~100° total");
        }
    }
}
