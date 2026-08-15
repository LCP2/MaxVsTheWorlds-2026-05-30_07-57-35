using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-362's pure/state layer: Sentinel tracks start owned at Level 1 like Water Balloon's, but
    /// spending on one — unlike Water Balloon — is gated on <see cref="AbilityKind.Sentinels"/> being
    /// acquired first; the Gunner's damage fraction always stays below 1.0 (the DECISION's "always
    /// weaker than Max's CURRENT primary" enforced structurally); and the Deployment Count track's
    /// level IS the slot count.
    /// </summary>
    public sealed class SentinelSystemTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            DevTuning.Reset();
        }

        // ---------------------------------------------------------------- WeaponSystemState

        [Test]
        public void FreshStateHasEverySentinelTrackAtLevel1()
        {
            foreach (var kind in WeaponCatalog.AllSentinelTrackKinds)
                Assert.That(WeaponSystemState.SentinelTrackLevel(kind), Is.EqualTo(1),
                    $"{kind} must start owned at L1, same as a Water Balloon track");
        }

        [Test]
        public void SentinelsStartsUnacquired()
        {
            Assert.That(WeaponSystemState.IsAcquired(AbilityKind.Sentinels), Is.False);
        }

        [Test]
        public void LevelingASentinelTrackFailsUntilSentinelsIsAcquired()
        {
            Assert.That(WeaponSystemState.LevelUpSentinelTrack(SentinelTrackKind.WallStrength), Is.False,
                "unowned/locked items can't be upgraded (spec §5) — Sentinels isn't acquired yet");
            Assert.That(WeaponSystemState.SentinelTrackLevel(SentinelTrackKind.WallStrength), Is.EqualTo(1),
                "a failed level-up must not move the level");
        }

        [Test]
        public void LevelingASentinelTrackSucceedsOnceAcquired()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);

            Assert.That(WeaponSystemState.LevelUpSentinelTrack(SentinelTrackKind.DeploymentCount), Is.True);
            Assert.That(WeaponSystemState.SentinelTrackLevel(SentinelTrackKind.DeploymentCount), Is.EqualTo(2));
        }

        [Test]
        public void ASentinelTrackCannotLevelPastItsCap()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            int cap = WeaponCatalog.MaxLevel(SentinelTrackKind.DeploymentCount);
            for (int i = 1; i < cap; i++)
                Assert.That(WeaponSystemState.LevelUpSentinelTrack(SentinelTrackKind.DeploymentCount), Is.True);

            Assert.That(WeaponSystemState.SentinelTrackLevel(SentinelTrackKind.DeploymentCount), Is.EqualTo(cap));
            Assert.That(WeaponSystemState.LevelUpSentinelTrack(SentinelTrackKind.DeploymentCount), Is.False,
                "already at the cap");
        }

        [Test]
        public void ResetPutsEverySentinelTrackBackToLevel1AndForgetsAcquisition()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            WeaponSystemState.LevelUpSentinelTrack(SentinelTrackKind.GunnerPower);

            WeaponSystemState.Reset();

            Assert.That(WeaponSystemState.IsAcquired(AbilityKind.Sentinels), Is.False);
            Assert.That(WeaponSystemState.SentinelTrackLevel(SentinelTrackKind.GunnerPower), Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- PartSpend

        [Test]
        public void PartSpendOnSentinelTrackFailsWithNoBankedPart()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            Assert.That(PartSpend.TrySpendOnSentinelTrack(SentinelTrackKind.WallStrength), Is.False);
        }

        [Test]
        public void PartSpendOnSentinelTrackFailsWhenUnacquiredAndDoesNotConsumeTheBankedPart()
        {
            PickupWallet.AddPart();
            Assert.That(PartSpend.TrySpendOnSentinelTrack(SentinelTrackKind.WallStrength), Is.False);
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1), "a failed spend must not consume the part");
        }

        [Test]
        public void PartSpendOnSentinelTrackSpendsExactlyOnePartOnSuccess()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            PickupWallet.AddPart();
            PickupWallet.AddPart();

            Assert.That(PartSpend.TrySpendOnSentinelTrack(SentinelTrackKind.GunnerPower), Is.True);
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1));
            Assert.That(WeaponSystemState.SentinelTrackLevel(SentinelTrackKind.GunnerPower), Is.EqualTo(2));
        }

        // ---------------------------------------------------------------- AbilityTuning math

        [Test]
        public void WallMaxHpGrowsLinearlyPerLevel()
        {
            Assert.That(AbilityTuning.SentinelWallMaxHp(1, 200f, 40f), Is.EqualTo(200f).Within(1e-4f));
            Assert.That(AbilityTuning.SentinelWallMaxHp(4, 200f, 40f), Is.EqualTo(320f).Within(1e-4f));
        }

        [Test]
        public void GunnerPowerFractionNeverReachesOrExceedsOne()
        {
            for (int level = 1; level <= WeaponCatalog.MaxLevel(SentinelTrackKind.GunnerPower); level++)
            {
                float fraction = AbilityTuning.SentinelGunnerPowerFraction(
                    level, AbilityTuning.DefaultSentinelGunnerPowerFraction, AbilityTuning.DefaultSentinelGunnerPowerFractionPerLevel);
                Assert.That(fraction, Is.LessThan(1f),
                    $"level {level} must stay strictly below Max's own current primary output");
            }
        }

        [Test]
        public void GunnerDamagePerShotIsAlwaysBelowThePrimaryItIsAFractionOf()
        {
            const float primaryDamage = 8f; // an arbitrary "Max's current primary tick damage"
            for (int level = 1; level <= WeaponCatalog.MaxLevel(SentinelTrackKind.GunnerPower); level++)
            {
                float shot = AbilityTuning.SentinelGunnerDamagePerShot(
                    primaryDamage, level,
                    AbilityTuning.DefaultSentinelGunnerPowerFraction, AbilityTuning.DefaultSentinelGunnerPowerFractionPerLevel);
                Assert.That(shot, Is.LessThan(primaryDamage),
                    "the DECISION: sentinel damage must never catch up to Max's own current primary");
            }
        }

        [Test]
        public void DeploymentSlotsEqualsTheTrackLevel()
        {
            Assert.That(AbilityTuning.SentinelDeploymentSlots(1), Is.EqualTo(1));
            Assert.That(AbilityTuning.SentinelDeploymentSlots(4), Is.EqualTo(4));
        }
    }
}
