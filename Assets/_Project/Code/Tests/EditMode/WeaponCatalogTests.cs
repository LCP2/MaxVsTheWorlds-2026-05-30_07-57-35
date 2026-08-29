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
        public void SpreadTrack_BaseArcIsSixteenDegreesTotal_MV379()
        {
            // MV-367 narrowed the base cone to make the beam "look weak to begin with," but the cone
            // feeds the hit test too, so that also made robots harder to hit (Lee's playtest, MV-379).
            // MV-379 restores the pre-MV-367 8°-half-angle/16°-total base and moves the "looks weaker"
            // job onto WaterVfx's visual-only dials instead — see WaterVfx.Init's doc. Power is
            // untouched either way: a dead-ahead target stays inside the cone at any half-angle above
            // 0, see NarrowedBaseConeStillHitsADeadAheadTarget_MV367 below.
            Assert.That(WaterBlaster.DefaultConeHalfAngle * 2f, Is.EqualTo(16f).Within(0.01f),
                "MV-379: base spray must be restored to its pre-MV-367 ~16° total arc");
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
        public void SpreadTrack_MaxLevelArc_MV379_MV597()
        {
            // MV-379 restored the base half-angle from MV-367's 4° to the pre-MV-367 8°, without
            // retuning the per-level step (DefaultRcdaSpreadPerLevel is untouched). MV-597 then cut
            // Spread's own cap from 9 to 4 levels (the main over-power lever, Lee's playtest): the
            // maxed arc is now 8 * (1 + 0.7*3) = 24.8° half-angle, 49.6° total.
            float baseHalfAngle = WaterBlaster.DefaultConeHalfAngle;
            float maxHalfAngle = WeaponCatalog.EffectiveConeHalfAngle(
                baseHalfAngle, WeaponCatalog.MaxLevel(WeaponTrackKind.Spread), WeaponCatalog.DefaultRcdaSpreadPerLevel);

            Assert.That(maxHalfAngle * 2f, Is.EqualTo(49.6f).Within(0.5f),
                "MV-597: capping Spread at 4 levels must land the maxed arc at 49.6° total, not the old 105.6°");
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
        public void DamageTrack_MaxLevelIs1Point6xBase_MV597()
        {
            // MV-291 originally capped Damage at 6 levels (~2x base). MV-597 cut the cap to 4 levels
            // (Lee's playtest: maxed Damage+Spread+Flow was over-powered) without touching the
            // per-level step, landing the new ceiling at 1.6x base instead.
            float baseDamage = WaterBlaster.DefaultDamagePerTick;
            float maxDamage = WeaponCatalog.EffectiveDamagePerTick(
                baseDamage, WeaponCatalog.MaxLevel(WeaponTrackKind.Damage), WeaponCatalog.DefaultRcdaDamagePerLevel);

            Assert.That(maxDamage, Is.EqualTo(baseDamage * 1.6f).Within(0.05f),
                "MV-597: capping Damage at 4 levels must land the maxed track at 1.6x base (6.4 dmg/tick), not the old 2x (8)");
        }

        // ---------------------------------------------------------------- MV-368: drain scales with output

        [Test]
        public void DrainOutputScale_IsOneAtTheAuthoredBase()
        {
            float baseReach = WaterBlaster.DefaultRange;
            float baseCone = WaterBlaster.DefaultConeHalfAngle;
            Assert.That(WeaponCatalog.DrainOutputScale(baseReach, baseReach, baseCone, baseCone), Is.EqualTo(1f).Within(1e-5f),
                "an un-upgraded weapon (reach and cone both at base) must not scale the drain at all — AC2");
        }

        [Test]
        public void DrainOutputScale_RisesWithReach()
        {
            float baseReach = WaterBlaster.DefaultRange;
            float baseCone = WaterBlaster.DefaultConeHalfAngle;
            float atBase = WeaponCatalog.DrainOutputScale(baseReach, baseReach, baseCone, baseCone);
            float widerReach = WeaponCatalog.DrainOutputScale(baseReach * 2f, baseReach, baseCone, baseCone);
            Assert.Greater(widerReach, atBase, "more reach must drain the tank faster — more water is covering more ground");
        }

        [Test]
        public void DrainOutputScale_RisesWithCone()
        {
            float baseReach = WaterBlaster.DefaultRange;
            float baseCone = WaterBlaster.DefaultConeHalfAngle;
            float atBase = WeaponCatalog.DrainOutputScale(baseReach, baseReach, baseCone, baseCone);
            float widerCone = WeaponCatalog.DrainOutputScale(baseReach, baseReach, baseCone * 2f, baseCone);
            Assert.Greater(widerCone, atBase, "a wider spray must drain the tank faster — more water is coming out");
        }

        [Test]
        public void MaxedRangeAndSpread_DrainsMarkedlyFasterThanBase_MV368()
        {
            // The ticket's headline AC: a fully upgraded weapon (Range + Spread both maxed, no nozzle)
            // must empty the tank noticeably faster than an un-upgraded one, with no Depletion spend.
            float baseReach = WaterBlaster.DefaultRange;
            float baseCone = WaterBlaster.DefaultConeHalfAngle;
            float maxReach = WeaponCatalog.EffectiveRange(
                baseReach, WeaponCatalog.MaxLevel(WeaponTrackKind.Range), WeaponCatalog.DefaultRcdaRangePerLevel);
            float maxCone = WeaponCatalog.EffectiveConeHalfAngle(
                baseCone, WeaponCatalog.MaxLevel(WeaponTrackKind.Spread), WeaponCatalog.DefaultRcdaSpreadPerLevel);

            float baseDrain = WeaponCatalog.EffectiveDrainPerSecond(
                BlasterTuning.EnergyPerSecond, 0, WeaponCatalog.DefaultRcdaDepletionRatePerLevel,
                WeaponCatalog.DrainOutputScale(baseReach, baseReach, baseCone, baseCone));
            float maxedDrain = WeaponCatalog.EffectiveDrainPerSecond(
                BlasterTuning.EnergyPerSecond, 0, WeaponCatalog.DefaultRcdaDepletionRatePerLevel,
                WeaponCatalog.DrainOutputScale(maxReach, baseReach, maxCone, baseCone));

            Assert.That(maxedDrain, Is.GreaterThan(baseDrain * 2f), "a maxed weapon must drain markedly faster than an un-upgraded one");
        }

        [Test]
        public void UndraftedEnduranceAtBaseOutput_MatchesTodaysBaseDrainExactly_MV368()
        {
            // AC2: a completely fresh weapon (Range/Spread at base, Endurance not yet drafted — RigState
            // level 0, MV-597) must reproduce today's drain exactly. MV-597 makes level 1 pay out once
            // drafted, so "unaffected" now means level 0, not level 1.
            float baseReach = WaterBlaster.DefaultRange;
            float baseCone = WaterBlaster.DefaultConeHalfAngle;
            float drain = WeaponCatalog.EffectiveDrainPerSecond(
                BlasterTuning.EnergyPerSecond, 0, WeaponCatalog.DefaultRcdaDepletionRatePerLevel,
                WeaponCatalog.DrainOutputScale(baseReach, baseReach, baseCone, baseCone));

            Assert.That(drain, Is.EqualTo(BlasterTuning.EnergyPerSecond).Within(1e-4f));
        }

        [Test]
        public void EnduranceTrack_StillOffsetsTheIncreasedDrainAtMaxedOutput_MV368()
        {
            // AC3: spending on Endurance must measurably cut the drain even when the weapon's
            // output is maxed, and AC4: it must never become literally free to run.
            float baseReach = WaterBlaster.DefaultRange;
            float baseCone = WaterBlaster.DefaultConeHalfAngle;
            float maxReach = WeaponCatalog.EffectiveRange(
                baseReach, WeaponCatalog.MaxLevel(WeaponTrackKind.Range), WeaponCatalog.DefaultRcdaRangePerLevel);
            float maxCone = WeaponCatalog.EffectiveConeHalfAngle(
                baseCone, WeaponCatalog.MaxLevel(WeaponTrackKind.Spread), WeaponCatalog.DefaultRcdaSpreadPerLevel);
            float outputScale = WeaponCatalog.DrainOutputScale(maxReach, baseReach, maxCone, baseCone);

            float undraftedEndurance = WeaponCatalog.EffectiveDrainPerSecond(
                BlasterTuning.EnergyPerSecond, 0, WeaponCatalog.DefaultRcdaDepletionRatePerLevel, outputScale);
            float maxedDepletionSpend = WeaponCatalog.EffectiveDrainPerSecond(
                BlasterTuning.EnergyPerSecond, WeaponCatalog.MaxLevel(WeaponTrackKind.Capacity),
                WeaponCatalog.DefaultRcdaDepletionRatePerLevel, outputScale);

            Assert.Less(maxedDepletionSpend, undraftedEndurance, "Endurance must still buy back sustain against a maxed-output weapon");
            Assert.Greater(maxedDepletionSpend, 0f, "even a maxed Endurance track must leave a real, positive cost to run");
        }

        // ---------------------------------------------------------------- MV-379: visual-only strength fraction

        [Test]
        public void VisualStrengthFraction_IsZeroAtTheStartingLevel()
        {
            Assert.That(WeaponCatalog.VisualStrengthFraction(1, 9), Is.EqualTo(0f).Within(1e-5f),
                "level 1 is every track's starting level — the visual must read at its weakest here");
        }

        [Test]
        public void VisualStrengthFraction_IsOneAtTheMaxLevel()
        {
            Assert.That(WeaponCatalog.VisualStrengthFraction(9, 9), Is.EqualTo(1f).Within(1e-5f),
                "a maxed track must read the full, un-scaled-down visual");
        }

        [Test]
        public void VisualStrengthFraction_RisesMonotonicallyBetweenTheEndpoints()
        {
            float mid = WeaponCatalog.VisualStrengthFraction(5, 9);
            Assert.That(mid, Is.GreaterThan(0f));
            Assert.That(mid, Is.LessThan(1f));

            float lower = WeaponCatalog.VisualStrengthFraction(3, 9);
            float higher = WeaponCatalog.VisualStrengthFraction(7, 9);
            Assert.That(higher, Is.GreaterThan(lower), "a higher track level must never read a weaker visual than a lower one");
        }

        [Test]
        public void VisualStrengthFraction_NeverDividesByZeroForASingleLevelTrack()
        {
            Assert.That(WeaponCatalog.VisualStrengthFraction(1, 1), Is.EqualTo(1f),
                "a track with no levels above 1 must not throw or read NaN — it just reads full strength");
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

        // ---------------------------------------------------------------- MV-394: Repeat Fire relabel

        [Test]
        public void RepeatFireTrackDisplaysAsAutoFireRate_MV394()
        {
            Assert.That(WeaponCatalog.DisplayName(WaterBalloonTrackKind.RepeatFire), Is.EqualTo("AUTO FIRE RATE"),
                "MV-394: the track's mechanics/enum are unchanged, only its on-screen label");
        }

        // ---------------------------------------------------------------- MV-409: Water Balloon grid card removed

        [Test]
        public void WaterBalloonNeverShowsInTheAbilitiesGrid_MV409()
        {
            Assert.IsFalse(WeaponCatalog.ShowsInAbilitiesGrid(AbilityKind.WaterBalloon),
                "MV-409: Water Balloon's own WATER BALLOON section is the sole UI for it — no duplicate grid card");
        }

        [Test]
        public void EveryOtherAbilityStillShowsInTheGrid_MV409()
        {
            foreach (var kind in WeaponCatalog.AllAbilityKinds)
            {
                if (kind == AbilityKind.WaterBalloon) continue;
                Assert.IsTrue(WeaponCatalog.ShowsInAbilitiesGrid(kind), $"{kind} must still get a grid card");
            }
        }
    }
}
