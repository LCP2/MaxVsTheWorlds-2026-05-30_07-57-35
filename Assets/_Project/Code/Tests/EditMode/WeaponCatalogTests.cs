using NUnit.Framework;
using UnityEngine;
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
        public void RangeTrack_MaxLevelReachIsTwiceBase_MV367()
        {
            // MV-367 cuts the max-level reach ~20% below MV-291's 2.5x-base ceiling (12.5 -> 10 for a
            // 5m base), landing at an even 2x base over the new 8-step (levels 1-9) cap.
            float baseReach = WaterBlaster.DefaultRange;
            float maxReach = WeaponCatalog.EffectiveRange(
                baseReach, WeaponCatalog.MaxLevel(WeaponTrackKind.Range), WeaponCatalog.DefaultRcdaRangePerLevel);

            Assert.That(maxReach, Is.EqualTo(baseReach * 2f).Within(0.05f),
                "MV-367: retuning the base reach or the per-level step must keep the max Range level at ~2x base (20% below MV-291's 2.5x)");
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
        public void SpreadTrack_BaseArcIsEightDegreesTotal_MV367()
        {
            // Lee, MV-367: "the initial beam ... much narrower so that it looks weak to begin with."
            // MV-301's ~16° total base already read as narrow; MV-367 halves it again to a ~8° total
            // arc. Power is untouched — a dead-ahead target stays inside the cone at any half-angle
            // above 0, see NarrowedBaseConeStillHitsADeadAheadTarget_MV367 below.
            Assert.That(WaterBlaster.DefaultConeHalfAngle * 2f, Is.EqualTo(8f).Within(0.01f),
                "MV-367: base spray must read as a much narrower ~8° total arc");
        }

        [Test]
        public void NarrowedBaseConeStillHitsADeadAheadTarget_MV367()
        {
            // What actually guarantees AC2 ("un-upgraded damage output is unchanged"): a target on the
            // aim axis (angle 0) stays inside the cone at any half-angle above 0, so narrowing the base
            // cone for looks does not cost a single dead-ahead enemy any time-to-kill, even though the
            // cone is mechanically narrower now too.
            Vector3 origin = Vector3.zero;
            Vector3 dir = Vector3.forward;
            Vector3 deadAhead = origin + dir * 3f;

            Assert.IsTrue(SprayHit.InCone(origin, dir, deadAhead, WaterBlaster.DefaultRange, WaterBlaster.DefaultConeHalfAngle),
                "a dead-ahead target must stay inside the base cone — this is what keeps level-1 TTK unchanged");
        }

        [Test]
        public void Level1DamagePerTickIsUnchanged_MV367()
        {
            // MV-367 narrows the cone for looks and cuts the max-level ceilings, but must not touch
            // per-tick damage — AC2 explicitly calls out TTK must be pinned before/after.
            Assert.That(WaterBlaster.DefaultDamagePerTick, Is.EqualTo(4f).Within(1e-5f),
                "MV-367 must not touch per-tick damage — only the cone width and the Range/Spread ceilings");
        }

        [Test]
        public void SpreadTrack_MaxLevelArcIsRoughlyFiftyThreeDegreesTotal_MV367()
        {
            // MV-367 cuts the max-level arc ~20% below MV-301's 66° total ceiling (66 * 0.8 = 52.8),
            // over the new 8-step (levels 1-9) cap.
            float baseHalfAngle = WaterBlaster.DefaultConeHalfAngle;
            float maxHalfAngle = WeaponCatalog.EffectiveConeHalfAngle(
                baseHalfAngle, WeaponCatalog.MaxLevel(WeaponTrackKind.Spread), WeaponCatalog.DefaultRcdaSpreadPerLevel);

            Assert.That(maxHalfAngle * 2f, Is.EqualTo(52.8f).Within(0.5f),
                "MV-367: retuning the base angle or the per-level step must keep the maxed Spread track ~20% below MV-301's 66° total");
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

        // ---------------------------------------------------------------- MV-357: ability draft-pick cards

        [Test]
        public void EveryAbilityHasANonEmptyGlyphAndEffectLine_MV357()
        {
            foreach (var kind in WeaponCatalog.AllAbilityKinds)
            {
                Assert.That(WeaponCatalog.Glyph(kind), Is.Not.Null.And.Not.Empty, $"{kind} has no card glyph");
                Assert.That(WeaponCatalog.EffectLine(kind), Is.Not.Null.And.Not.Empty, $"{kind} has no card effect line");
            }
        }
    }
}
