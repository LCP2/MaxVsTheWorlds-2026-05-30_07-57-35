using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The banked-drops tally behind the HUD counter and the weapons area (YT-131, recut WV-228):
    /// power cells and parts both accumulate as a plain count and fire a change event so the HUD
    /// reacts rather than polls. Parts carry no identity — they're universal upgrade tokens.
    /// </summary>
    public sealed class PickupWalletTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            PickupWallet.Reset();
            // This suite is about the wallet's own counters/capacity math, not MV-457's shed/category-
            // lock gate (RigStateTests owns that) — force every category open so a root ability (e_cel)
            // this file drafts directly stays reached, as it always was before MV-457.
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
            DevTuning.Reset();
        }

        [Test]
        public void PowerCellsAccumulate()
        {
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0));
            PickupWallet.AddPowerCell();
            PickupWallet.AddPowerCell();
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(2), "banked cells must add up — it's a running currency");
        }

        [Test]
        public void BankingACellFiresTheChangeEventWithTheNewTotal()
        {
            int seen = -1;
            void Handler(int n) => seen = n;
            PickupWallet.PowerCellsChanged += Handler;
            try
            {
                PickupWallet.AddPowerCell();
                Assert.That(seen, Is.EqualTo(1), "the HUD counter binds this event; it must carry the new total");
            }
            finally { PickupWallet.PowerCellsChanged -= Handler; }
        }

        [Test]
        public void PartsAccumulateAsABankedCount()
        {
            PickupWallet.AddPart();
            PickupWallet.AddPart();
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(2),
                "each collected part banks as a fungible upgrade token (WV-228)");
        }

        [Test]
        public void CollectingAPartFiresTheChangeEvent()
        {
            int seen = -1;
            void Handler(int n) => seen = n;
            PickupWallet.PartsChanged += Handler;
            try
            {
                PickupWallet.AddPart();
                Assert.That(seen, Is.EqualTo(1), "the flashing edge icon is raised off this event");
            }
            finally { PickupWallet.PartsChanged -= Handler; }
        }

        [Test]
        public void SpendingAPartDecrementsTheBank_AndIsANoOpWhenEmpty()
        {
            Assert.That(PickupWallet.TrySpendPart(), Is.False, "there's nothing to spend yet");
            PickupWallet.AddPart();
            Assert.That(PickupWallet.TrySpendPart(), Is.True);
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(0), "spending the only banked part clears it");
            Assert.That(PickupWallet.TrySpendPart(), Is.False, "and can't be spent below zero");
        }

        [Test]
        public void PowerCellsCanBeSpent_AndStopAtEmpty()
        {
            PickupWallet.AddPowerCell();
            PickupWallet.AddPowerCell();
            Assert.That(PickupWallet.TrySpendPowerCell(), Is.True);
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(1), "spending a cell decrements the reserve (YT-137)");
            Assert.That(PickupWallet.TrySpendPowerCell(), Is.True);
            Assert.That(PickupWallet.TrySpendPowerCell(), Is.False, "can't spend below zero — Hydro stalls here");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0));
        }

        [Test]
        public void TheReserveIsCappedAtCapacity()
        {
            for (int i = 0; i < PickupWallet.Capacity + 10; i++) PickupWallet.AddPowerCell();
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(PickupWallet.Capacity),
                "collecting past the cap is wasted — the meter has a full mark (YT-137)");
        }

        [Test]
        public void AddPowerCellReturnsWhetherItActuallyBanked()
        {
            // MV-439: the caller (PickupDirector.Collect) must be able to tell a refused add from a
            // banked one — that's the one missing bit that let a cell get destroyed at the ceiling.
            for (int i = 0; i < PickupWallet.Capacity; i++)
                Assert.That(PickupWallet.AddPowerCell(), Is.True, "below capacity, every add must bank");

            Assert.That(PickupWallet.PowerCells, Is.EqualTo(PickupWallet.Capacity));
            Assert.That(PickupWallet.AddPowerCell(), Is.False, "at capacity, nothing banked — MV-439");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(PickupWallet.Capacity), "a refused add must not change the count");
        }

        [Test]
        public void SetPowerCellsClampsToCapacityAndFiresChange()
        {
            int seen = -1;
            void Handler(int n) => seen = n;
            PickupWallet.PowerCellsChanged += Handler;
            try
            {
                PickupWallet.SetPowerCells(PickupWallet.Capacity + 50);
                Assert.That(PickupWallet.PowerCells, Is.EqualTo(PickupWallet.Capacity),
                    "a save slot (YT-151) restoring past the cap must clamp, same as AddPowerCell");
                Assert.That(seen, Is.EqualTo(PickupWallet.Capacity));
            }
            finally { PickupWallet.PowerCellsChanged -= Handler; }
        }

        [Test]
        public void ResetClearsBothTallies()
        {
            PickupWallet.AddPowerCell();
            PickupWallet.AddPart();
            PickupWallet.Reset();
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0));
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- MV-374/MV-422: Cell Storage (e_cel)

        [Test]
        public void CapacityStartsAtTheBase20()
        {
            Assert.That(PickupWallet.PowerCellCapacityLevel, Is.EqualTo(0));
            Assert.That(PickupWallet.Capacity, Is.EqualTo(20), "a fresh run starts at the base, not the old flat 30");
        }

        [Test]
        public void LevelUpFailsUntilCellStorageIsDrafted_MV422()
        {
            // e_cel is a RIG cap now — a part/LevelUpCellCapacity can never perform its 0->1 unlock,
            // only a Morphing Module draft can (RigState.AcquireCap, exercised directly here since the
            // draft screen/shed-flow wiring is out of this ticket's scope).
            Assert.That(PickupWallet.LevelUpCellCapacity(), Is.False);
            Assert.That(PickupWallet.PowerCellCapacityLevel, Is.EqualTo(0));
        }

        [Test]
        public void DraftingCellStorageGrantsLevelOneAtThirtyCapacity_MV422()
        {
            RigState.AcquireCap("e_cel");
            Assert.That(PickupWallet.PowerCellCapacityLevel, Is.EqualTo(1));
            Assert.That(PickupWallet.Capacity, Is.EqualTo(30));
        }

        [Test]
        public void LevelingUpCellCapacityAdds10PerLevelUpToFourLevels()
        {
            RigState.AcquireCap("e_cel"); // level 1, capacity 30
            Assert.That(PickupWallet.LevelUpCellCapacity(), Is.True);
            Assert.That(PickupWallet.Capacity, Is.EqualTo(40));
            Assert.That(PickupWallet.LevelUpCellCapacity(), Is.True);
            Assert.That(PickupWallet.Capacity, Is.EqualTo(50));
            Assert.That(PickupWallet.LevelUpCellCapacity(), Is.True);
            Assert.That(PickupWallet.Capacity, Is.EqualTo(60), "e_cel's RIG maxLevel is 4 — 4 levels at +10 each cap out at 60");
        }

        [Test]
        public void LevelingUpCellCapacityPastTheCapFails()
        {
            RigState.AcquireCap("e_cel");
            for (int i = 1; i < PickupWallet.PowerCellCapacityMaxLevel; i++) PickupWallet.LevelUpCellCapacity();
            Assert.That(PickupWallet.LevelUpCellCapacity(), Is.False, $"only {PickupWallet.PowerCellCapacityMaxLevel} levels exist");
            Assert.That(PickupWallet.Capacity, Is.EqualTo(60));
        }

        [Test]
        public void LevelingUpCellCapacityFiresCapacityChangedWithTheNewCapacity()
        {
            RigState.AcquireCap("e_cel");
            int seen = -1;
            void Handler(int n) => seen = n;
            PickupWallet.CapacityChanged += Handler;
            try
            {
                PickupWallet.LevelUpCellCapacity();
                Assert.That(seen, Is.EqualTo(40), "the HUD/weapons screen redraw off this event even though the count didn't move");
            }
            finally { PickupWallet.CapacityChanged -= Handler; }
        }

        [Test]
        public void RaisingCapacityDoesNotWasteAlreadyBankedCellsHeadroom()
        {
            RigState.AcquireCap("e_cel"); // capacity 30 already, before filling
            for (int i = 0; i < PickupWallet.Capacity; i++) PickupWallet.AddPowerCell();
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(30), "full at the drafted cap");

            PickupWallet.LevelUpCellCapacity();
            PickupWallet.AddPowerCell();
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(31), "the new headroom must be collectible immediately");
        }

        [Test]
        public void ResetClearsCellCapacityLevelBackToTheBase()
        {
            RigState.AcquireCap("e_cel");
            PickupWallet.LevelUpCellCapacity();
            PickupWallet.Reset();
            Assert.That(PickupWallet.PowerCellCapacityLevel, Is.EqualTo(0));
            Assert.That(PickupWallet.Capacity, Is.EqualTo(20));
        }
    }
}
