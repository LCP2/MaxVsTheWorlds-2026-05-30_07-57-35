using NUnit.Framework;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The banked-drops tally behind the HUD counter and the upgrade flow (YT-131): power cells
    /// accumulate, parts accumulate as pending upgrades, and both fire a change event so the HUD
    /// reacts rather than polls.
    /// </summary>
    public sealed class PickupWalletTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => PickupWallet.Reset();

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
        public void PartsAccumulateAsPending()
        {
            PickupWallet.AddPart();
            PickupWallet.AddPart();
            Assert.That(PickupWallet.PartsPending, Is.EqualTo(2),
                "each collected part is a pending upgrade until the upgrade screen (YT-132) spends it");
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
        public void SpendingAPartDecrementsPending_AndIsANoOpWhenEmpty()
        {
            Assert.That(PickupWallet.SpendPart(), Is.False, "there's nothing to spend yet");
            PickupWallet.AddPart();
            Assert.That(PickupWallet.SpendPart(), Is.True);
            Assert.That(PickupWallet.PartsPending, Is.EqualTo(0), "spending the only pending part clears it");
            Assert.That(PickupWallet.SpendPart(), Is.False, "and can't be spent below zero");
        }

        [Test]
        public void ResetClearsBothTallies()
        {
            PickupWallet.AddPowerCell();
            PickupWallet.AddPart();
            PickupWallet.Reset();
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0));
            Assert.That(PickupWallet.PartsPending, Is.EqualTo(0));
        }
    }
}
