using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The weapon/ability backbone (WV-230), now a thin enum-typed layer over THE RIG's unified node
    /// model (MV-422, see <see cref="RigStateTests"/> for the model's own rules). Run start: only
    /// <c>p_dmg</c> (RCDA Damage) is owned, at Level 1 — every other track/ability starts at 0 and is
    /// gated by <see cref="RigState.IsReached"/>. <see cref="AbilityKind.WeaponCooldown"/> is retired
    /// (no RIG node) and can never be acquired.
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
        public void FreshStateOwnsOnlyDamageAtLevelOne()
        {
            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Damage), Is.EqualTo(1),
                "p_dmg is THE RIG's one run-start exception");
            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Range), Is.EqualTo(0));
            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Spread), Is.EqualTo(0));
            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.DepletionRate), Is.EqualTo(0));
        }

        [Test]
        public void RangeAndFlowAreImmediatelySpendableAtRunStart_ButSpreadIsNot()
        {
            // p_rng and p_flw (DepletionRate) are both direct children of p_dmg, which is already at
            // L1 at run start — reached immediately. p_spr's parent is p_rng, still at 0, so it is
            // NOT reached until a part is spent on Range first.
            Assert.That(WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range), Is.True);
            Assert.That(WeaponSystemState.LevelUpTrack(WeaponTrackKind.DepletionRate), Is.True);
            WeaponSystemState.Reset();
            Assert.That(WeaponSystemState.LevelUpTrack(WeaponTrackKind.Spread), Is.False,
                "Spread must not be spendable until Range has raised it to reached");
        }

        [Test]
        public void FreshStateHasNoAbilitiesAcquired()
        {
            foreach (var kind in WeaponCatalog.AllAbilityKinds)
            {
                Assert.That(WeaponSystemState.IsAcquired(kind), Is.False, $"{kind} must not be owned at run start");
                Assert.That(WeaponSystemState.AbilityLevel(kind), Is.EqualTo(0));
            }

            // MV-380/MV-422: WaterBalloonAutoFire is a prerequisite chain — Unacquired never offers it
            // until WaterBalloon itself is owned, so at a fresh run it's excluded from the pool even
            // though nothing is acquired yet.
            var expectedUnacquired = new System.Collections.Generic.List<AbilityKind>(WeaponCatalog.AllAbilityKinds);
            expectedUnacquired.Remove(AbilityKind.WaterBalloonAutoFire);
            CollectionAssert.AreEquivalent(expectedUnacquired, WeaponSystemState.Unacquired);
            Assert.That(WeaponSystemState.Acquired, Is.Empty);
        }

        [Test]
        public void WeaponCooldownCanNeverBeAcquired_RetiredByMV422()
        {
            Assert.That(WeaponSystemState.Acquire(AbilityKind.WeaponCooldown), Is.False,
                "WeaponCooldown has no node in the canonical rig_board.json — MV-422 retires it");
            Assert.That(WeaponSystemState.IsAcquired(AbilityKind.WeaponCooldown), Is.False);
            CollectionAssert.DoesNotContain(
                new System.Collections.Generic.List<AbilityKind>(WeaponSystemState.Unacquired), AbilityKind.WeaponCooldown);
        }

        // ---------------------------------------------------------------- tracks

        [Test]
        public void LevelUpTrackIncrementsByOne()
        {
            Assert.That(WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range), Is.True);
            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Range), Is.EqualTo(1));
        }

        [Test]
        public void LevelUpTrackStopsAtItsCap()
        {
            for (int i = 0; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Damage) - 1; i++)
                Assert.That(WeaponSystemState.LevelUpTrack(WeaponTrackKind.Damage), Is.True);

            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Damage), Is.EqualTo(WeaponCatalog.MaxLevel(WeaponTrackKind.Damage)));
            Assert.That(WeaponSystemState.LevelUpTrack(WeaponTrackKind.Damage), Is.False, "must not level past the cap");
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
            Assert.That(WeaponCatalog.MaxLevel(WeaponTrackKind.Range), Is.EqualTo(9));
            Assert.That(WeaponCatalog.MaxLevel(WeaponTrackKind.Spread), Is.EqualTo(9));
        }

        // ---------------------------------------------------------------- Water Balloon tracks (MV-370/MV-422)

        [Test]
        public void WaterBalloonTracksAreNotReachedUntilBalloonIsOwned()
        {
            Assert.That(WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.Range), Is.False,
                "s_lob's parent is s_bal — unreached until Water Balloon is acquired");
            Assert.That(WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.Range), Is.EqualTo(0));
        }

        [Test]
        public void LevelUpWaterBalloonTrackIncrementsByOneOnceOwned()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);

            Assert.That(WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.Range), Is.True);
            Assert.That(WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.Range), Is.EqualTo(1));
        }

        [Test]
        public void LevelUpWaterBalloonTrackStopsAtItsCap()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            int cap = WeaponCatalog.MaxLevel(WaterBalloonTrackKind.SplashArea);
            for (int i = 0; i < cap; i++)
                Assert.That(WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.SplashArea), Is.True);

            Assert.That(WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.SplashArea), Is.EqualTo(cap));
            Assert.That(WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.SplashArea), Is.False,
                "must not level past the cap");
        }

        [Test]
        public void RepeatFireTrackIsNotReachedUntilAutoFireIsOwned()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            Assert.That(WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.RepeatFire), Is.False,
                "s_rte's parent is s_aut (Auto-Fire), not s_bal — Balloon alone isn't enough");

            WeaponSystemState.Acquire(AbilityKind.WaterBalloonAutoFire);
            Assert.That(WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.RepeatFire), Is.True);
        }

        [Test]
        public void EachWaterBalloonTrackLevelsIndependently()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.Range);

            Assert.That(WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.Range), Is.EqualTo(1));
            Assert.That(WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.SplashArea), Is.EqualTo(0),
                "leveling Range must not touch Splash Area");
        }

        [Test]
        public void WaterBalloonEffectiveCooldownShortensAsRepeatFireLevelsUp()
        {
            // The formula's own Mathf.Max(1, level) clamp reads level 0 and level 1 identically (both
            // "not shortened yet", same as every other track pre-MV-422) — level 2 is the first level
            // that actually moves the cooldown.
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            WeaponSystemState.Acquire(AbilityKind.WaterBalloonAutoFire);
            WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.RepeatFire); // to L1
            float baseCooldown = WeaponSystemState.WaterBalloonEffectiveCooldownSeconds();

            WeaponSystemState.LevelUpWaterBalloonTrack(WaterBalloonTrackKind.RepeatFire); // to L2

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
        public void AcquiredAndUnacquiredPartitionAllSixAbilities()
        {
            WeaponSystemState.Acquire(AbilityKind.Speed);

            CollectionAssert.AreEquivalent(new[] { AbilityKind.Speed }, WeaponSystemState.Acquired);
            // WaterBalloonAutoFire stays out of Unacquired too — its prerequisite (WaterBalloon) isn't
            // owned yet.
            CollectionAssert.AreEquivalent(
                new[] { AbilityKind.Teleport, AbilityKind.WaterBalloon, AbilityKind.ForceField, AbilityKind.Sentinels },
                WeaponSystemState.Unacquired);
        }

        [Test]
        public void WaterBalloonAutoFireIsNotOfferedUntilWaterBalloonIsAcquired_MV380()
        {
            CollectionAssert.DoesNotContain(
                new System.Collections.Generic.List<AbilityKind>(WeaponSystemState.Unacquired), AbilityKind.WaterBalloonAutoFire);

            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);

            CollectionAssert.Contains(
                new System.Collections.Generic.List<AbilityKind>(WeaponSystemState.Unacquired), AbilityKind.WaterBalloonAutoFire);
        }

        [Test]
        public void WaterBalloonAutoFireEnabledDefaultsTrueAndTogglesWithAnEvent()
        {
            Assert.That(WeaponSystemState.WaterBalloonAutoFireEnabled, Is.True, "MV-373's payoff is on by default once unlocked");

            int fired = 0;
            System.Action handler = () => fired++;
            WeaponSystemState.Changed += handler;
            try
            {
                WeaponSystemState.WaterBalloonAutoFireEnabled = false;
                Assert.That(WeaponSystemState.WaterBalloonAutoFireEnabled, Is.False);
                Assert.That(fired, Is.EqualTo(1), "a real change must fire Changed so the HUD toggle updates");

                WeaponSystemState.WaterBalloonAutoFireEnabled = false;
                Assert.That(fired, Is.EqualTo(1), "setting the same value again must not re-fire Changed");
            }
            finally
            {
                WeaponSystemState.Changed -= handler;
            }
        }

        [Test]
        public void ResetRestoresWaterBalloonAutoFireEnabledToItsDefault()
        {
            WeaponSystemState.WaterBalloonAutoFireEnabled = false;
            WeaponSystemState.Reset();

            Assert.That(WeaponSystemState.WaterBalloonAutoFireEnabled, Is.True);
        }

        [Test]
        public void AcquiredListsAbilitiesInAcquisitionOrderNotCatalogOrder_MV333()
        {
            // ForceField is last in WeaponCatalog.AllAbilityKinds but granted first here — it must
            // still come out first, and Speed (first in catalog order) must land second, not
            // displace it.
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            WeaponSystemState.Acquire(AbilityKind.Speed);

            CollectionAssert.AreEqual(
                new[] { AbilityKind.Sentinels, AbilityKind.Speed }, WeaponSystemState.Acquired);
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
            Assert.That(WeaponCatalog.WaterBalloonBaseCooldownSeconds(), Is.GreaterThan(0f));
        }

        [Test]
        public void EffectiveCooldownIsAlwaysTheBase_WeaponCooldownRetiredByMV422()
        {
            Assert.That(WeaponSystemState.EffectiveCooldownSeconds(AbilityKind.Teleport),
                Is.EqualTo(WeaponCatalog.BaseCooldownSeconds(AbilityKind.Teleport)).Within(1e-4f),
                "WeaponCooldown can never be acquired any more, so this multiplier is always a no-op (1x)");
        }

        [Test]
        public void WaterBalloonEffectiveCooldownIsTheBaseAtRepeatFireLevel0()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            WeaponSystemState.Acquire(AbilityKind.WaterBalloonAutoFire);

            Assert.That(WeaponSystemState.WaterBalloonEffectiveCooldownSeconds(),
                Is.EqualTo(WeaponCatalog.WaterBalloonBaseCooldownSeconds()).Within(1e-4f),
                "Repeat Fire at its starting L0 must not shorten anything yet");
        }

        // ---------------------------------------------------------------- reset / events

        [Test]
        public void ResetClearsTracksAndAbilities()
        {
            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range);
            WeaponSystemState.Acquire(AbilityKind.Speed);

            WeaponSystemState.Reset();

            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Range), Is.EqualTo(0));
            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Damage), Is.EqualTo(1));
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
        public void CatalogListsAllSixAbilitiesFourTracksAndThreeWaterBalloonTracks()
        {
            Assert.That(WeaponCatalog.AllAbilityKinds.Length, Is.EqualTo(6),
                "MV-422 retired WeaponCooldown — no RIG node for a global cooldown-reduction ability");
            Assert.That(WeaponCatalog.AllTrackKinds.Length, Is.EqualTo(4), "MV-299 reinstated Depletion Rate as the fourth track");
            Assert.That(WeaponCatalog.AllWaterBalloonTrackKinds.Length, Is.EqualTo(3), "MV-370: Range, Splash Area, Repeat Fire — unchanged by MV-380");
        }

        [Test]
        public void AbilityCapsMatchTheSpec()
        {
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.Speed), Is.EqualTo(4));
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.Teleport), Is.EqualTo(4), "MV-339 widened Teleport from 2 levels to 4");
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.ForceField), Is.EqualTo(5), "MV-422: e_ff's RIG maxLevel rose to 5");
        }

        [Test]
        public void WaterBalloonAndAutoFireAreBooleanUnlocksCappedAtOne_MV380()
        {
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.WaterBalloon), Is.EqualTo(1),
                "the throw's own magnitudes live on WaterBalloonTrackKind, not a leveled ability");
            Assert.That(WeaponCatalog.MaxLevel(AbilityKind.WaterBalloonAutoFire), Is.EqualTo(1));
        }

        [Test]
        public void WaterBalloonTrackCapsMatchTheSpec_MV370()
        {
            foreach (var kind in WeaponCatalog.AllWaterBalloonTrackKinds)
                Assert.That(WeaponCatalog.MaxLevel(kind), Is.EqualTo(3), $"{kind} keeps the old single track's 3-level cap");
        }
    }
}
