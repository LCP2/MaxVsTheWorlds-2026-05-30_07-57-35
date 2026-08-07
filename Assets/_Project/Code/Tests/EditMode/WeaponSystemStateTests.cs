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
        public void RangeCapsHigherThanSpread()
        {
            Assert.That(WeaponCatalog.MaxLevel(WeaponTrackKind.Range), Is.EqualTo(6));
            Assert.That(WeaponCatalog.MaxLevel(WeaponTrackKind.Spread), Is.EqualTo(4));
        }

        // ---------------------------------------------------------------- abilities

        [Test]
        public void AcquireGrantsLevel1()
        {
            Assert.That(WeaponSystemState.Acquire(AbilityKind.Dash), Is.True);
            Assert.That(WeaponSystemState.IsAcquired(AbilityKind.Dash), Is.True);
            Assert.That(WeaponSystemState.AbilityLevel(AbilityKind.Dash), Is.EqualTo(1));
            CollectionAssert.Contains(new System.Collections.Generic.List<AbilityKind>(WeaponSystemState.Acquired), AbilityKind.Dash);
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
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            int cap = WeaponCatalog.MaxLevel(AbilityKind.WaterBalloon);
            for (int i = 1; i < cap; i++)
                Assert.That(WeaponSystemState.LevelUpAbility(AbilityKind.WaterBalloon), Is.True);

            Assert.That(WeaponSystemState.AbilityLevel(AbilityKind.WaterBalloon), Is.EqualTo(cap));
            Assert.That(WeaponSystemState.LevelUpAbility(AbilityKind.WaterBalloon), Is.False, "must not level past the cap");
        }

        [Test]
        public void DashIsASingleUnlockWithNoFurtherLevels()
        {
            WeaponSystemState.Acquire(AbilityKind.Dash);
            Assert.That(WeaponSystemState.LevelUpAbility(AbilityKind.Dash), Is.False,
                "Dash caps at L1 — acquiring it is the whole upgrade");
        }

        [Test]
        public void AcquiredAndUnacquiredPartitionAllSixAbilities()
        {
            WeaponSystemState.Acquire(AbilityKind.Speed);
            WeaponSystemState.Acquire(AbilityKind.Dash);

            CollectionAssert.AreEquivalent(new[] { AbilityKind.Speed, AbilityKind.Dash }, WeaponSystemState.Acquired);
            CollectionAssert.AreEquivalent(
                new[] { AbilityKind.WaterBalloon, AbilityKind.Teleport, AbilityKind.WeaponCooldown },
                WeaponSystemState.Unacquired);
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
            Assert.That(WeaponCatalog.BaseCooldownSeconds(AbilityKind.WaterBalloon), Is.GreaterThan(0f));
            Assert.That(WeaponCatalog.BaseCooldownSeconds(AbilityKind.Dash), Is.GreaterThan(0f));
            Assert.That(WeaponCatalog.BaseCooldownSeconds(AbilityKind.Teleport), Is.GreaterThan(0f));
        }

        [Test]
        public void EffectiveCooldownIsTheBaseWithoutWeaponCooldownOwned()
        {
            Assert.That(WeaponSystemState.EffectiveCooldownSeconds(AbilityKind.Dash),
                Is.EqualTo(WeaponCatalog.BaseCooldownSeconds(AbilityKind.Dash)).Within(1e-4f),
                "Weapon Cooldown not owned (L0) must not shorten anything");
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
            WeaponSystemState.Acquire(AbilityKind.Dash);

            WeaponSystemState.Reset();

            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Range), Is.EqualTo(1));
            Assert.That(WeaponSystemState.IsAcquired(AbilityKind.Dash), Is.False);
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
        public void CatalogListsAllFiveAbilitiesAndTwoTracks()
        {
            Assert.That(WeaponCatalog.AllAbilityKinds.Length, Is.EqualTo(5));
            Assert.That(WeaponCatalog.AllTrackKinds.Length, Is.EqualTo(2));
        }

        [Test]
        public void AbilityCapsMatchTheSpec()
        {
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.WaterBalloon), Is.EqualTo(3));
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.Speed), Is.EqualTo(4));
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.Dash), Is.EqualTo(1));
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.Teleport), Is.EqualTo(2));
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.WeaponCooldown), Is.EqualTo(5));
        }
    }
}
