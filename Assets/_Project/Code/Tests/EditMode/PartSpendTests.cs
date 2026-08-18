using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Spending a banked part on a chosen owned track/ability (WV-228): a part is only actually
    /// consumed when the level-up it pays for actually happens — an empty bank, an unowned ability, or
    /// a track/ability already at its cap must all leave the bank untouched. MV-422: every track now
    /// starts at 0 (only <c>p_dmg</c> starts at 1) and each is gated by THE RIG's own reached-ness
    /// rule — see <see cref="RigStateTests"/> for the model's own contract.
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
            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Range), Is.EqualTo(0));
        }

        [Test]
        public void SpendingOnATrackRaisesItsLevelAndConsumesOnePart()
        {
            PickupWallet.AddPart();
            PickupWallet.AddPart();

            Assert.That(PartSpend.TrySpendOnTrack(WeaponTrackKind.Range), Is.True);

            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Range), Is.EqualTo(1));
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1), "exactly one part must be spent per level");
        }

        [Test]
        public void SpendingOnATrackAtItsCapFailsAndDoesNotSpendTheirPart()
        {
            // MV-422: p_spr (Spread)'s RIG parent is p_rng (Range) — must be reached before Spread
            // will accept any part at all.
            PickupWallet.AddPart();
            PartSpend.TrySpendOnTrack(WeaponTrackKind.Range);

            for (int i = 0; i < WeaponCatalog.MaxLevel(WeaponTrackKind.Spread); i++)
            {
                PickupWallet.AddPart();
                PartSpend.TrySpendOnTrack(WeaponTrackKind.Spread);
            }
            PickupWallet.AddPart();
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
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            int cap = WeaponCatalog.MaxLevel(AbilityKind.Teleport);
            for (int i = 1; i < cap; i++)
                PickupWallet.AddPart();
            for (int i = 1; i < cap; i++)
                PartSpend.TrySpendOnAbility(AbilityKind.Teleport);

            PickupWallet.AddPart();

            Assert.That(PartSpend.TrySpendOnAbility(AbilityKind.Teleport), Is.False, "must not level past the cap");
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1), "a failed spend must not cost a part");
        }

        // ---------------------------------------------------------------- MV-370/MV-422: Water Balloon tracks

        [Test]
        public void SpendingOnAWaterBalloonTrackFailsUntilBalloonIsOwned()
        {
            PickupWallet.AddPart();

            Assert.That(PartSpend.TrySpendOnWaterBalloonTrack(WaterBalloonTrackKind.Range), Is.False,
                "s_lob's RIG parent is s_bal — unreached until Water Balloon is drafted");
            Assert.That(WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.Range), Is.EqualTo(0));
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1), "a failed spend must not cost a part");
        }

        [Test]
        public void SpendingOnAWaterBalloonTrackRaisesItsLevelAndConsumesOnePartOnceOwned()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            PickupWallet.AddPart();
            PickupWallet.AddPart();

            Assert.That(PartSpend.TrySpendOnWaterBalloonTrack(WaterBalloonTrackKind.SplashArea), Is.True);

            Assert.That(WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.SplashArea), Is.EqualTo(1));
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1), "exactly one part must be spent per level");
        }

        [Test]
        public void SpendingOnAWaterBalloonTrackAtItsCapFailsAndDoesNotSpendTheirPart()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            WeaponSystemState.Acquire(AbilityKind.WaterBalloonAutoFire); // s_rte's own parent
            PickupWallet.AddPart();
            for (int i = 0; i < WeaponCatalog.MaxLevel(WaterBalloonTrackKind.RepeatFire); i++)
                PartSpend.TrySpendOnWaterBalloonTrack(WaterBalloonTrackKind.RepeatFire);
            int banked = PickupWallet.PartsBanked;

            Assert.That(PartSpend.TrySpendOnWaterBalloonTrack(WaterBalloonTrackKind.RepeatFire), Is.False, "must not level past the cap");
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(banked), "a spend that doesn't level up must not cost a part");
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
            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Range), Is.EqualTo(0));
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

        // ---------------------------------------------------------------- MV-374/MV-422: Cell Storage (e_cel)

        [Test]
        public void SpendingOnCellCapacityWithNoBankedPartsFails()
        {
            Assert.That(PartSpend.TrySpendOnCellCapacity(), Is.False);
            Assert.That(PickupWallet.PowerCellCapacityLevel, Is.EqualTo(0));
        }

        [Test]
        public void SpendingOnCellCapacityFailsUntilItIsDrafted_MV422()
        {
            // e_cel is a RIG cap now — a part can never perform its 0->1 unlock, only a Morphing
            // Module draft can (RigState.AcquireCap, exercised directly here since the draft
            // screen/shed-flow wiring is out of this ticket's scope).
            PickupWallet.AddPart();

            Assert.That(PartSpend.TrySpendOnCellCapacity(), Is.False,
                "unowned/locked items can't be upgraded (spec §5) — e_cel hasn't been drafted");
            Assert.That(PickupWallet.PowerCellCapacityLevel, Is.EqualTo(0));
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1), "a failed spend must not cost a part");
        }

        [Test]
        public void SpendingOnCellCapacityRaisesItsLevelAndConsumesOnePartOnceDrafted()
        {
            RigState.AcquireCap("e_cel");
            PickupWallet.AddPart();
            PickupWallet.AddPart();

            Assert.That(PartSpend.TrySpendOnCellCapacity(), Is.True);

            Assert.That(PickupWallet.PowerCellCapacityLevel, Is.EqualTo(2));
            Assert.That(PickupWallet.Capacity, Is.EqualTo(40));
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1), "exactly one part must be spent per level");
        }

        [Test]
        public void SpendingOnCellCapacityAtItsCapFailsAndDoesNotSpendTheirPart()
        {
            RigState.AcquireCap("e_cel");
            PickupWallet.AddPart();
            for (int i = 1; i < PickupWallet.PowerCellCapacityMaxLevel; i++)
            {
                PickupWallet.AddPart();
                PartSpend.TrySpendOnCellCapacity();
            }
            int banked = PickupWallet.PartsBanked;

            Assert.That(PartSpend.TrySpendOnCellCapacity(), Is.False, $"only {PickupWallet.PowerCellCapacityMaxLevel} levels exist");
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(banked), "a spend that doesn't level up must not cost a part");
        }

        [Test]
        public void BankedPowerCellsCannotSubstituteForPartsOnCellCapacity()
        {
            RigState.AcquireCap("e_cel");
            PickupWallet.AddPowerCell();
            PickupWallet.AddPowerCell();

            Assert.That(PartSpend.TrySpendOnCellCapacity(), Is.False, "power cells must never buy a capacity upgrade");
            Assert.That(PickupWallet.PowerCellCapacityLevel, Is.EqualTo(1), "the draft alone grants level 1, no part spent");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(2), "a rejected upgrade spend must not touch cells");
        }
    }
}
