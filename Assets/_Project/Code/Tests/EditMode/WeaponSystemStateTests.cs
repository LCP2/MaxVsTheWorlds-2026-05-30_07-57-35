using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The weapon/ability backbone (WV-230): the RCDA primary's tracks start owned at Level 1,
    /// the abilities start unowned at Level 0 and are granted (not leveled) one at a time, both
    /// respect their catalog caps, and the Weapon Cooldown ability shortens every other active
    /// ability's cooldown. (Capacity/Weapon Efficiency tracks and the Power Efficiency ability were
    /// retired by MV-290.)
    /// </summary>
    public sealed class WeaponSystemStateTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
        }

        // ---------------------------------------------------------------- fresh state

        [Test]
        public void FreshStateHasEveryTrackAtLevel1()
        {
            foreach (var kind in WeaponCatalog.AllTrackKinds)
                Assert.That(WeaponSystemState.TrackLevel(kind), Is.EqualTo(1), $"{kind} must start owned at L1");
        }

        [Test]
        public void FreshStateHasNoAbilitiesAcquired()
        {
            foreach (var kind in WeaponCatalog.AllAbilityKinds)
            {
                Assert.That(WeaponSystemState.IsAcquired(kind), Is.False, $"{kind} must not be owned at run start");
                Assert.That(WeaponSystemState.AbilityLevel(kind), Is.EqualTo(0));
            }

            CollectionAssert.AreEquivalent(WeaponCatalog.AllAbilityKinds, WeaponSystemState.Unacquired);
            Assert.That(WeaponSystemState.Acquired, Is.Empty);
        }

        [Test]
        public void FreshStateHasEveryWaterBalloonTrackAtLevel1_MV370()
        {
            foreach (var kind in WeaponCatalog.AllWaterBalloonTrackKinds)
                Assert.That(WeaponSystemState.WaterBalloonTrackLevel(kind), Is.EqualTo(1),
                    $"{kind} must start owned at L1 — a primary add-on, not a shed find");
        }

        // ---------------------------------------------------------------- tracks

        [Test]
        public void LevelUpTrackIncrementsByOne()
        {
            Assert.That(WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range), Is.True);
            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Range), Is.EqualTo(2));
        }

        [Test]
        public void LevelUpTrackStopsAtItsCap()
        {
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Spread); i++)
                Assert.That(WeaponSystemState.LevelUpTrack(WeaponTrackKind.Spread), Is.True);

            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Spread), Is.EqualTo(WeaponCatalog.MaxLevel(WeaponTrackKind.Spread)));
            Assert.That(WeaponSystemState.LevelUpTrack(WeaponTrackKind.Spread), Is.False, "must not level past the cap");
            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Spread), Is.EqualTo(WeaponCatalog.MaxLevel(WeaponTrackKind.Spread)));
        }

        [Test]
        public void DamageAndDepletionRateCapAtSixLevels_MV291()
        {
            Assert.That(WeaponCatalog.MaxLevel(WeaponTrackKind.Damage), Is.EqualTo(6));
            Assert.That(WeaponCatalog.MaxLevel(WeaponTrackKind.DepletionRate), Is.EqualTo(6), "MV-299");
        }

        [Test]
        public void RangeAndSpreadCapAtNineLevels_MV367()
        {
            // MV-367: Range and Spread get 3 more steps than Damage/DepletionRate so a lower ceiling
            // still reads as steady, frequent growth rather than two giant jumps to godhood.
            Assert.That(WeaponCatalog.MaxLevel(WeaponTrackKind.Range), Is.EqualTo(9));
            Assert.That(WeaponCatalog.MaxLevel(WeaponTrackKind.Spread), Is.EqualTo(9));
        }

        // ---------------------------------------------------------------- Water Balloon tracks (MV-370)

        [Test]
        public void LevelUpWaterBalloonTrackIncrementsByOne()
        {
            Assert.That(WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.Range), Is.True);
            Assert.That(WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.Range), Is.EqualTo(2));
        }

        [Test]
        public void LevelUpWaterBalloonTrackStopsAtItsCap()
        {
            int cap = WeaponCatalog.MaxLevel(WaterBalloonTrackKind.SplashArea);
            for (int i = 1; i < cap; i++)
                Assert.That(WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.SplashArea), Is.True);

            Assert.That(WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.SplashArea), Is.EqualTo(cap));
            Assert.That(WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.SplashArea), Is.False,
                "must not level past the cap");
        }

        [Test]
        public void EachWaterBalloonTrackLevelsIndependently()
        {
            WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.Range);

            Assert.That(WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.Range), Is.EqualTo(2));
            Assert.That(WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.SplashArea), Is.EqualTo(1),
                "leveling Range must not touch Splash Area");
            Assert.That(WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.RepeatFire), Is.EqualTo(1),
                "leveling Range must not touch Repeat Fire");
        }

        [Test]
        public void WaterBalloonEffectiveCooldownShortensAsRepeatFireLevelsUp()
        {
            float baseCooldown = WeaponSystemState.WaterBalloonEffectiveCooldownSeconds();

            WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.RepeatFire);

            Assert.That(WeaponSystemState.WaterBalloonEffectiveCooldownSeconds(), Is.LessThan(baseCooldown),
                "a Repeat Fire level must shorten the throw cooldown");
        }

        // ---------------------------------------------------------------- abilities

        [Test]
        public void AcquireGrantsLevel1()
        {
            Assert.That(WeaponSystemState.Acquire(AbilityKind.Speed), Is.True);
            Assert.That(WeaponSystemState.IsAcquired(AbilityKind.Speed), Is.True);
            Assert.That(WeaponSystemState.AbilityLevel(AbilityKind.Speed), Is.EqualTo(1));
            CollectionAssert.Contains(new System.Collections.Generic.List<AbilityKind>(WeaponSystemState.Acquired), AbilityKind.Speed);
        }

        [Test]
        public void AcquiringAnAlreadyOwnedAbilityIsANoOp()
        {
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            WeaponSystemState.LevelUpAbility(AbilityKind.Teleport);   // now L2
            Assert.That(WeaponSystemState.Acquire(AbilityKind.Teleport), Is.False, "re-granting must not reset the level");
            Assert.That(WeaponSystemState.AbilityLevel(AbilityKind.Teleport), Is.EqualTo(2));
        }

        [Test]
        public void UnacquiredAbilityCannotBeLeveledUp()
        {
            Assert.That(WeaponSystemState.LevelUpAbility(AbilityKind.Speed), Is.False,
                "unowned/locked items can't be upgraded (spec §5)");
            Assert.That(WeaponSystemState.AbilityLevel(AbilityKind.Speed), Is.EqualTo(0));
        }

        [Test]
        public void LevelUpAbilityStopsAtItsCap()
        {
            WeaponSystemState.Acquire(AbilityKind.Speed);
            int cap = WeaponCatalog.MaxLevel(AbilityKind.Speed);
            for (int i = 1; i < cap; i++)
                Assert.That(WeaponSystemState.LevelUpAbility(AbilityKind.Speed), Is.True);

            Assert.That(WeaponSystemState.AbilityLevel(AbilityKind.Speed), Is.EqualTo(cap));
            Assert.That(WeaponSystemState.LevelUpAbility(AbilityKind.Speed), Is.False, "must not level past the cap");
        }

        [Test]
        public void AcquiredAndUnacquiredPartitionAllThreeAbilities_MV370()
        {
            WeaponSystemState.Acquire(AbilityKind.Speed);

            CollectionAssert.AreEquivalent(new[] { AbilityKind.Speed }, WeaponSystemState.Acquired);
            CollectionAssert.AreEquivalent(
                new[] { AbilityKind.Teleport, AbilityKind.WeaponCooldown },
                WeaponSystemState.Unacquired);
        }

        [Test]
        public void AcquiredListsAbilitiesInAcquisitionOrderNotCatalogOrder_MV333()
        {
            // WeaponCooldown is last in WeaponCatalog.AllAbilityKinds but granted first here — it must
            // still come out first, and Speed (first in catalog order) must land second, not
            // displace it.
            WeaponSystemState.Acquire(AbilityKind.WeaponCooldown);
            WeaponSystemState.Acquire(AbilityKind.Speed);

            CollectionAssert.AreEqual(
                new[] { AbilityKind.WeaponCooldown, AbilityKind.Speed }, WeaponSystemState.Acquired);
        }

        [Test]
        public void AcquiringAFurtherAbilityDoesNotReorderAlreadyAcquiredOnes_MV333()
        {
            WeaponSystemState.Acquire(AbilityKind.Speed);
            var beforeSecondAcquire = new System.Collections.Generic.List<AbilityKind>(WeaponSystemState.Acquired);

            WeaponSystemState.Acquire(AbilityKind.Teleport);
            var afterSecondAcquire = new System.Collections.Generic.List<AbilityKind>(WeaponSystemState.Acquired);

            Assert.That(afterSecondAcquire[0], Is.EqualTo(beforeSecondAcquire[0]),
                "the first-acquired ability's slot must not move when a second is granted");
            Assert.That(afterSecondAcquire[1], Is.EqualTo(AbilityKind.Teleport));
        }

        // ---------------------------------------------------------------- cooldowns

        [Test]
        public void PassiveAbilitiesHaveZeroBaseCooldown()
        {
            Assert.That(WeaponCatalog.BaseCooldownSeconds(AbilityKind.Speed), Is.EqualTo(0f));
            Assert.That(WeaponCatalog.BaseCooldownSeconds(AbilityKind.WeaponCooldown), Is.EqualTo(0f));
        }

        [Test]
        public void ActiveAbilitiesHaveAPositiveBaseCooldown()
        {
            Assert.That(WeaponCatalog.BaseCooldownSeconds(AbilityKind.Teleport), Is.GreaterThan(0f));
        }

        [Test]
        public void WaterBalloonHasAPositiveBaseCooldown_MV370()
        {
            // MV-370: Water Balloon's base cooldown moved off the AbilityKind switch when it left the
            // shed-drop pool — WeaponCatalog.WaterBalloonBaseCooldownSeconds() is its replacement.
            Assert.That(WeaponCatalog.WaterBalloonBaseCooldownSeconds(), Is.GreaterThan(0f));
        }

        [Test]
        public void EffectiveCooldownIsTheBaseWithoutWeaponCooldownOwned()
        {
            Assert.That(WeaponSystemState.EffectiveCooldownSeconds(AbilityKind.Teleport),
                Is.EqualTo(WeaponCatalog.BaseCooldownSeconds(AbilityKind.Teleport)).Within(1e-4f),
                "Weapon Cooldown not owned (L0) must not shorten anything");
        }

        [Test]
        public void WaterBalloonEffectiveCooldownIsTheBaseAtRepeatFireLevel1_MV370()
        {
            Assert.That(WeaponSystemState.WaterBalloonEffectiveCooldownSeconds(),
                Is.EqualTo(WeaponCatalog.WaterBalloonBaseCooldownSeconds()).Within(1e-4f),
                "Repeat Fire at its starting L1 must not shorten anything yet");
        }

        [Test]
        public void EffectiveCooldownShrinksAsWeaponCooldownLevelsUp()
        {
            float baseCooldown = WeaponCatalog.BaseCooldownSeconds(AbilityKind.Teleport);
            WeaponSystemState.Acquire(AbilityKind.WeaponCooldown);

            float atL1 = WeaponSystemState.EffectiveCooldownSeconds(AbilityKind.Teleport);
            Assert.That(atL1, Is.LessThan(baseCooldown), "L1 Weapon Cooldown must shorten the base cooldown");

            for (int i = 1; i < WeaponCatalog.MaxLevel(AbilityKind.WeaponCooldown); i++)
                WeaponSystemState.LevelUpAbility(AbilityKind.WeaponCooldown);

            float atMax = WeaponSystemState.EffectiveCooldownSeconds(AbilityKind.Teleport);
            Assert.That(atMax, Is.LessThan(atL1), "a maxed Weapon Cooldown must shorten it further than L1");
            Assert.That(atMax, Is.GreaterThanOrEqualTo(0f), "the multiplier must never take a cooldown negative");
        }

        [Test]
        public void WeaponCooldownDoesNotAffectPassiveAbilitiesZeroCooldown()
        {
            WeaponSystemState.Acquire(AbilityKind.WeaponCooldown);
            for (int i = 1; i < WeaponCatalog.MaxLevel(AbilityKind.WeaponCooldown); i++)
                WeaponSystemState.LevelUpAbility(AbilityKind.WeaponCooldown);

            Assert.That(WeaponSystemState.EffectiveCooldownSeconds(AbilityKind.Speed), Is.EqualTo(0f));
        }

        // ---------------------------------------------------------------- reset / events

        [Test]
        public void ResetClearsTracksAndAbilities()
        {
            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range);
            WeaponSystemState.Acquire(AbilityKind.Speed);

            WeaponSystemState.Reset();

            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Range), Is.EqualTo(1));
            Assert.That(WeaponSystemState.IsAcquired(AbilityKind.Speed), Is.False);
        }

        [Test]
        public void ChangedFiresOnAcquireLevelUpAndReset()
        {
            int fired = 0;
            System.Action handler = () => fired++;
            WeaponSystemState.Changed += handler;
            try
            {
                WeaponSystemState.Acquire(AbilityKind.Speed);
                WeaponSystemState.LevelUpAbility(AbilityKind.Speed);
                WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range);
                WeaponSystemState.Reset();

                Assert.That(fired, Is.EqualTo(4));
            }
            finally
            {
                WeaponSystemState.Changed -= handler;
            }
        }

        // ---------------------------------------------------------------- catalog

        [Test]
        public void CatalogListsAllThreeAbilitiesFourTracksAndThreeWaterBalloonTracks_MV370()
        {
            Assert.That(WeaponCatalog.AllAbilityKinds.Length, Is.EqualTo(3), "MV-370 removed Water Balloon");
            Assert.That(WeaponCatalog.AllTrackKinds.Length, Is.EqualTo(4), "MV-299 reinstated Depletion Rate as the fourth track");
            Assert.That(WeaponCatalog.AllWaterBalloonTrackKinds.Length, Is.EqualTo(3), "MV-370: Range, Splash Area, Repeat Fire");
        }

        [Test]
        public void AbilityCapsMatchTheSpec()
        {
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.Speed), Is.EqualTo(4));
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.Teleport), Is.EqualTo(4), "MV-339 widened Teleport from 2 levels to 4");
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.WeaponCooldown), Is.EqualTo(5));
        }

        [Test]
        public void WaterBalloonTrackCapsMatchTheSpec_MV370()
        {
            foreach (var kind in WeaponCatalog.AllWaterBalloonTrackKinds)
                Assert.That(WeaponCatalog.MaxLevel(kind), Is.EqualTo(3), $"{kind} keeps the old single track's 3-level cap");
        }
    }
}
