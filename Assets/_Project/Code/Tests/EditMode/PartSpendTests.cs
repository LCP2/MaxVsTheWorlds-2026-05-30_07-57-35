using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Spending a banked part on a chosen owned track/ability (WV-228): a part is only actually
    /// consumed when the level-up it pays for actually happens — an empty bank, an unowned ability, or
    /// a track/ability already at its cap must all leave the bank untouched.
    /// </summary>
    public sealed class PartSpendTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            PickupWallet.Reset();
            WeaponSystemState.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void SpendingOnATrackWithNoBankedPartsFails()
        {
            Assert.That(PartSpend.TrySpendOnTrack(WeaponTrackKind.Range), Is.False);
            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Range), Is.EqualTo(1));
        }

        [Test]
        public void SpendingOnATrackRaisesItsLevelAndConsumesOnePart()
        {
            PickupWallet.AddPart();
            PickupWallet.AddPart();

            Assert.That(PartSpend.TrySpendOnTrack(WeaponTrackKind.Range), Is.True);

            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Range), Is.EqualTo(2));
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1), "exactly one part must be spent per level");
        }

        [Test]
        public void SpendingOnATrackAtItsCapFailsAndDoesNotSpendTheirPart()
        {
            PickupWallet.AddPart();
            for (int i = 1; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Spread); i++)
                PartSpend.TrySpendOnTrack(WeaponTrackKind.Spread);
            int banked = PickupWallet.PartsBanked;

            Assert.That(PartSpend.TrySpendOnTrack(WeaponTrackKind.Spread), Is.False, "must not level past the cap");
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(banked), "a spend that doesn't level up must not cost a part");
        }

        [Test]
        public void SpendingOnAnUnacquiredAbilityFails_UnownedItemsCannotBeUpgraded()
        {
            PickupWallet.AddPart();

            Assert.That(PartSpend.TrySpendOnAbility(AbilityKind.Speed), Is.False,
                "unowned/locked items can't be upgraded (spec §5)");
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1), "a failed spend must not cost a part");
            Assert.That(WeaponSystemState.AbilityLevel(AbilityKind.Speed), Is.EqualTo(0));
        }

        [Test]
        public void SpendingOnAnOwnedAbilityRaisesItsLevelAndConsumesOnePart()
        {
            WeaponSystemState.Acquire(AbilityKind.Speed);
            PickupWallet.AddPart();

            Assert.That(PartSpend.TrySpendOnAbility(AbilityKind.Speed), Is.True);

            Assert.That(WeaponSystemState.AbilityLevel(AbilityKind.Speed), Is.EqualTo(2));
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(0));
        }

        [Test]
        public void SpendingOnAnOwnedAbilityAtItsCapFailsAndDoesNotSpendTheirPart()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            int cap = WeaponCatalog.MaxLevel(AbilityKind.WaterBalloon);
            for (int i = 1; i < cap; i++)
                PickupWallet.AddPart();
            for (int i = 1; i < cap; i++)
                PartSpend.TrySpendOnAbility(AbilityKind.WaterBalloon);

            PickupWallet.AddPart();

            Assert.That(PartSpend.TrySpendOnAbility(AbilityKind.WaterBalloon), Is.False, "must not level past the cap");
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1), "a failed spend must not cost a part");
        }

        // MV-274: power cells are exclusively primary/ability fuel — they must never buy an upgrade,
        // even when the bank is flush with cells and empty of parts.

        [Test]
        public void BankedPowerCellsCannotSubstituteForPartsOnATrack()
        {
            PickupWallet.AddPowerCell();
            PickupWallet.AddPowerCell();

            Assert.That(PartSpend.TrySpendOnTrack(WeaponTrackKind.Range), Is.False,
                "power cells must never buy a track upgrade");
            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Range), Is.EqualTo(1));
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(2), "a rejected upgrade spend must not touch cells");
        }

        [Test]
        public void BankedPowerCellsCannotSubstituteForPartsOnAnAbility()
        {
            WeaponSystemState.Acquire(AbilityKind.Speed);
            PickupWallet.AddPowerCell();

            Assert.That(PartSpend.TrySpendOnAbility(AbilityKind.Speed), Is.False,
                "power cells must never buy an ability upgrade");
            Assert.That(WeaponSystemState.AbilityLevel(AbilityKind.Speed), Is.EqualTo(1));
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(1), "a rejected upgrade spend must not touch cells");
        }

        [Test]
        public void SpendingOnATrackLeavesPowerCellsUntouched()
        {
            PickupWallet.AddPart();
            PickupWallet.AddPowerCell();
            PickupWallet.AddPowerCell();

            Assert.That(PartSpend.TrySpendOnTrack(WeaponTrackKind.Range), Is.True);

            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(0), "the track upgrade must consume the part");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(2), "a part spend must never also spend cells");
        }
    }
}
