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
            Assert.That(PartSpend.TrySpendOnTrack(WeaponTrackKind.Capacity), Is.False);
            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Capacity), Is.EqualTo(1));
        }

        [Test]
        public void SpendingOnATrackRaisesItsLevelAndConsumesOnePart()
        {
            PickupWallet.AddPart();
            PickupWallet.AddPart();

            Assert.That(PartSpend.TrySpendOnTrack(WeaponTrackKind.Capacity), Is.True);

            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Capacity), Is.EqualTo(2));
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
            WeaponSystemState.Acquire(AbilityKind.Dash);   // caps at L1 — a single unlock
            PickupWallet.AddPart();

            Assert.That(PartSpend.TrySpendOnAbility(AbilityKind.Dash), Is.False);
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1), "a failed spend must not cost a part");
        }
    }
}
