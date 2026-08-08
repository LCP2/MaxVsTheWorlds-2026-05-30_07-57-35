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
        public void RangeTrack_MaxLevelReachIsRoughlyTwoAndAHalfTimesBase_MV291()
        {
            float baseReach = WaterBlaster.DefaultRange;
            float maxReach = WeaponCatalog.EffectiveRange(
                baseReach, WeaponCatalog.MaxLevel(WeaponTrackKind.Range), WeaponCatalog.DefaultRcdaRangePerLevel);

            Assert.That(maxReach, Is.EqualTo(baseReach * 2.5f).Within(0.05f),
                "MV-291: retuning the base reach or the per-level step must keep the max Range level at ~2.5x base");
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
        public void SpreadTrack_BaseArcIsSixteenDegreesTotal_MV301()
        {
            // MV-289's ~45° total base already read as a wide fan at 0 Spread upgrades, leaving the
            // Spread track nothing to sell — MV-301 re-narrows the un-upgraded stream to a focused
            // ~16° total arc so widening it is something a player earns.
            Assert.That(WaterBlaster.DefaultConeHalfAngle * 2f, Is.EqualTo(16f).Within(0.01f),
                "MV-301: base spray must read as a narrow, focused ~16° total arc");
        }

        [Test]
        public void SpreadTrack_MaxLevelArcIsSixtySixDegreesTotal_MV301()
        {
            float baseHalfAngle = WaterBlaster.DefaultConeHalfAngle;
            float maxHalfAngle = WeaponCatalog.EffectiveConeHalfAngle(
                baseHalfAngle, WeaponCatalog.MaxLevel(WeaponTrackKind.Spread), WeaponCatalog.DefaultRcdaSpreadPerLevel);

            Assert.That(maxHalfAngle * 2f, Is.EqualTo(66f).Within(0.5f),
                "MV-301: retuning the base angle or the per-level step must keep the maxed Spread track at ~66° total");
        }

        [Test]
        public void DamageLevelOneIsTheBaseDamageUnmodified()
        {
            Assert.That(WeaponCatalog.EffectiveDamagePerTick(4f, 1, 0.2f), Is.EqualTo(4f).Within(1e-5f),
                "level 1 is every track's starting level — it must not add anything yet (MV-291)");
        }

        [Test]
        public void EachDamageLevelAddsTheSamePerLevelStep()
        {
            float l1 = WeaponCatalog.EffectiveDamagePerTick(4f, 1, 0.2f);
            float l2 = WeaponCatalog.EffectiveDamagePerTick(4f, 2, 0.2f);
            float l6 = WeaponCatalog.EffectiveDamagePerTick(4f, 6, 0.2f);
            Assert.That(l2 - l1, Is.EqualTo(4f * 0.2f).Within(1e-5f), "the first upgrade must already be a visible step (MV-291)");
            Assert.That(l6 - l1, Is.EqualTo(4f * 0.2f * 5f).Within(1e-5f), "5 even steps above L1 up to the L6 cap");
        }

        [Test]
        public void DamageTrack_MaxLevelIsRoughlyTwiceBase_MV291()
        {
            float baseDamage = WaterBlaster.DefaultDamagePerTick;
            float maxDamage = WeaponCatalog.EffectiveDamagePerTick(
                baseDamage, WeaponCatalog.MaxLevel(WeaponTrackKind.Damage), WeaponCatalog.DefaultRcdaDamagePerLevel);

            Assert.That(maxDamage, Is.EqualTo(baseDamage * 2f).Within(0.05f),
                "MV-291: retuning the base damage or the per-level step must keep the maxed Damage track at ~2x base");
        }

        [Test]
        public void DepletionRateLevelOneIsTheBaseDrainUnmodified()
        {
            Assert.That(WeaponCatalog.EffectiveDrainPerSecond(10f, 1, 0.15f), Is.EqualTo(10f).Within(1e-5f),
                "level 1 is every track's starting level — it must not slow the drain yet (MV-299)");
        }

        [Test]
        public void EachDepletionRateLevelSlowsTheDrainFurther()
        {
            float l1 = WeaponCatalog.EffectiveDrainPerSecond(10f, 1, 0.15f);
            float l2 = WeaponCatalog.EffectiveDrainPerSecond(10f, 2, 0.15f);
            float l6 = WeaponCatalog.EffectiveDrainPerSecond(10f, 6, 0.15f);
            Assert.Less(l2, l1, "level 2 must drain slower than level 1");
            Assert.Less(l6, l2, "level 6 (the cap) must drain slower still");
        }

        [Test]
        public void DepletionRateTrack_MaxLevelDrainsAtQuarterBase_MV299()
        {
            float baseDrain = BlasterTuning.EnergyPerSecond;
            float maxDrain = WeaponCatalog.EffectiveDrainPerSecond(
                baseDrain, WeaponCatalog.MaxLevel(WeaponTrackKind.DepletionRate), WeaponCatalog.DefaultRcdaDepletionRatePerLevel);

            Assert.That(maxDrain, Is.EqualTo(baseDrain * 0.25f).Within(0.01f),
                "MV-299: retuning the per-level step must keep the maxed Depletion Rate track at ~25% of base drain (4x sustained fire)");
        }

        [Test]
        public void DepletionRateNeverDrainsFasterThanBase()
        {
            // Sanity: a track that's meant to slow the drain must never speed it up at any level.
            for (int level = 1; level <= WeaponCatalog.MaxLevel(WeaponTrackKind.DepletionRate); level++)
            {
                float drain = WeaponCatalog.EffectiveDrainPerSecond(
                    10f, level, WeaponCatalog.DefaultRcdaDepletionRatePerLevel);
                Assert.That(drain, Is.LessThanOrEqualTo(10f), $"level {level} drained faster than base");
                Assert.That(drain, Is.GreaterThan(0f), $"level {level} drained to zero or negative — the tank must never stop draining outright");
            }
        }
    }
}
