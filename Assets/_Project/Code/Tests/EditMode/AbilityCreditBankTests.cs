using NUnit.Framework;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-358: a shed pickup banks a buildable ability credit instead of granting/offering anything on
    /// the spot — the credit only turns into an ability once the player spends it from the Abilities
    /// screen's BUILD ABILITY button.
    /// </summary>
    public sealed class AbilityCreditBankTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => AbilityCreditBank.Reset();

        [Test]
        public void StartsWithNoCreditsBanked()
        {
            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(0));
        }

        [Test]
        public void BankIncrementsTheBankedCount()
        {
            AbilityCreditBank.Bank();
            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(1));

            AbilityCreditBank.Bank();
            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(2));
        }

        [Test]
        public void TrySpendConsumesOneBankedCreditAndSucceeds()
        {
            AbilityCreditBank.Bank();
            AbilityCreditBank.Bank();

            Assert.That(AbilityCreditBank.TrySpend(), Is.True);
            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(1));
        }

        [Test]
        public void TrySpendFailsAndDoesNotGoNegativeWhenNothingIsBanked()
        {
            Assert.That(AbilityCreditBank.TrySpend(), Is.False);
            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(0));
        }

        [Test]
        public void ChangedFiresWithTheNewCountOnBankAndSpend()
        {
            int lastReported = -1;
            void Handler(int reported) => lastReported = reported;

            AbilityCreditBank.Changed += Handler;
            try
            {
                AbilityCreditBank.Bank();
                Assert.That(lastReported, Is.EqualTo(1), "Changed should report the count right after banking");

                AbilityCreditBank.TrySpend();
                Assert.That(lastReported, Is.EqualTo(0), "Changed should report the count right after spending");
            }
            finally
            {
                AbilityCreditBank.Changed -= Handler;   // static event — must detach or it leaks into later tests
            }
        }

        [Test]
        public void ResetClearsBankedCreditsBackToZero()
        {
            AbilityCreditBank.Bank();
            AbilityCreditBank.Bank();

            AbilityCreditBank.Reset();

            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(0));
        }
    }
}
