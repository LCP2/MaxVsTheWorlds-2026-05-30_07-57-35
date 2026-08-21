using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The banked-drops tally behind the HUD counter and the weapons area (YT-131, recut WV-228,
    /// MV-515): power cells and Supercells both accumulate as a plain count and fire a change event so
    /// the HUD reacts rather than polls. A Supercell carries no identity — it's a banked 10-cell
    /// top-up, cashed in explicitly via <see cref="PickupWallet.TryCashSupercell"/>.
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
        public void SupercellsAccumulateAsABankedCount()
        {
            PickupWallet.AddSupercell();
            PickupWallet.AddSupercell();
            Assert.That(PickupWallet.SupercellsBanked, Is.EqualTo(2),
                "each collected Supercell banks as a fungible token (WV-228, MV-515)");
        }

        [Test]
        public void CollectingASupercellFiresTheChangeEvent()
        {
            int seen = -1;
            void Handler(int n) => seen = n;
            PickupWallet.SupercellsChanged += Handler;
            try
            {
                PickupWallet.AddSupercell();
                Assert.That(seen, Is.EqualTo(1), "the flashing edge icon is raised off this event");
            }
            finally { PickupWallet.SupercellsChanged -= Handler; }
        }

        [Test]
        public void CashingASupercellAddsTenCellsAndIsANoOpWhenEmpty()
        {
            Assert.That(PickupWallet.TryCashSupercell(), Is.False, "there's nothing to cash yet");
            PickupWallet.AddSupercell();
            Assert.That(PickupWallet.TryCashSupercell(), Is.True);
            Assert.That(PickupWallet.SupercellsBanked, Is.EqualTo(0), "cashing the only banked Supercell clears it");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(PickupWallet.SupercellCellValue),
                "cashing must add exactly SupercellCellValue cells");
            Assert.That(PickupWallet.TryCashSupercell(), Is.False, "and can't be cashed below zero");
        }

        /// <summary>MV-515 AC3, verbatim: at 5/20 cells cashing succeeds and leaves 15/20 with one
        /// fewer Supercell; at 15/20 cells (room for only 5 more, short of the full 10-cell top-up) it
        /// refuses, changing neither cells nor the Supercell count — the same no-waste principle MV-439
        /// established for cell pickups. Testing policy (MV-465): the one new test this ticket adds,
        /// proven to fail on 1b5c5892686445ec623abbfe7288329880c6830b (main HEAD before this ticket)
        /// since neither <see cref="PickupWallet.TryCashSupercell"/> nor
        /// <see cref="PickupWallet.SupercellCellValue"/> exist there.</summary>
        [Test]
        public void CashingASupercellRequiresRoomForTheFullTenCellTopUp_MV515()
        {
            PickupWallet.AddSupercell();
            PickupWallet.SetPowerCells(5);   // capacity 20 by default — room for 10 more

            Assert.That(PickupWallet.TryCashSupercell(), Is.True, "5/20 has room for the full 10-cell top-up");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(15));
            Assert.That(PickupWallet.SupercellsBanked, Is.EqualTo(0));

            PickupWallet.AddSupercell();
            // Now at 15/20 — only 5 cells of headroom, short of the full 10-cell top-up.
            Assert.That(PickupWallet.TryCashSupercell(), Is.False,
                "MV-515: never partially fill and never silently discard — refuse below a full top-up's worth of room");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(15), "a refused cash-in must not change the cell count");
            Assert.That(PickupWallet.SupercellsBanked, Is.EqualTo(1), "a refused cash-in must not spend the Supercell");
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
            PickupWallet.AddSupercell();
            PickupWallet.Reset();
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0));
            Assert.That(PickupWallet.SupercellsBanked, Is.EqualTo(0));
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
