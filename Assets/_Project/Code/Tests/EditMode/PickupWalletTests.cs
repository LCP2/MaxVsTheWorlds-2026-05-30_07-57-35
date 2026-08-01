using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.Pickups;

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
        public void Clear() { PickupWallet.Reset(); DevTuning.Reset(); }

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
    }
}
