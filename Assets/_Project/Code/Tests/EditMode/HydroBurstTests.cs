using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The Hydro burst/cooldown state machine (YT-215): assembling the harness + condenser unlocks the
    /// button, pressing it frees Max for a timed window, then it snaps back onto a cooldown — a
    /// resource, not the old permanent untether.
    /// </summary>
    public sealed class HydroBurstTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            HydroBurst.Reset();
            UpgradeState.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void NotReadyOrActiveBeforeAssembly()
        {
            Assert.That(HydroBurst.Ready, Is.False, "no harness/condenser — nothing to press");
            Assert.That(HydroBurst.Active, Is.False);

            HydroBurst.Trigger();
            Assert.That(HydroBurst.Active, Is.False, "triggering without assembly must be a no-op");
        }

        [Test]
        public void TriggerStartsABurst_ReadyOnceAssembled()
        {
            Assemble();
            Assert.That(HydroBurst.Ready, Is.True, "assembled, never used — must be ready");

            HydroBurst.Trigger();
            Assert.That(HydroBurst.Active, Is.True, "triggering while ready must start the burst");
            Assert.That(HydroBurst.RemainingSeconds, Is.EqualTo(HydroBurst.Seconds).Within(1e-5f));
            Assert.That(HydroBurst.Ready, Is.False, "mid-burst must not also read as ready");
        }

        [Test]
        public void BurstEndsIntoCooldown_ThenBecomesReadyAgain()
        {
            Assemble();
            DevTuning.HydroBurstSeconds = 1f;
            DevTuning.HydroBurstCooldown = 2f;

            HydroBurst.Trigger();
            HydroBurst.Tick(0.6f);
            Assert.That(HydroBurst.Active, Is.True, "0.6s into a 1s burst must still be active");

            HydroBurst.Tick(0.5f);   // crosses the 1s burst boundary
            Assert.That(HydroBurst.Active, Is.False, "the burst must end at its authored length");
            Assert.That(HydroBurst.Ready, Is.False, "the leash just snapped back — cooldown must have started");
            Assert.That(HydroBurst.CooldownNormalized, Is.GreaterThan(0.9f), "cooldown should be freshly full");

            HydroBurst.Tick(2f);   // the full cooldown and then some
            Assert.That(HydroBurst.Ready, Is.True, "cooldown elapsed — must be pressable again");
            Assert.That(HydroBurst.CooldownNormalized, Is.EqualTo(0f));
        }

        [Test]
        public void TriggerDuringCooldownIsANoOp()
        {
            Assemble();
            DevTuning.HydroBurstSeconds = 1f;
            DevTuning.HydroBurstCooldown = 5f;

            HydroBurst.Trigger();
            HydroBurst.Tick(1f);   // ends the burst, starts the cooldown
            Assert.That(HydroBurst.Active, Is.False);

            HydroBurst.Trigger();   // mashing the button mid-cooldown
            Assert.That(HydroBurst.Active, Is.False, "a burst must not restart while on cooldown");
        }

        [Test]
        public void ResetDropsAnInProgressBurstAndCooldown()
        {
            Assemble();
            HydroBurst.Trigger();
            HydroBurst.Tick(0.1f);

            HydroBurst.Reset();
            Assert.That(HydroBurst.Active, Is.False);
            Assert.That(HydroBurst.CooldownNormalized, Is.EqualTo(0f));
        }

        private static void Assemble()
        {
            UpgradeState.Install(PartKind.AugmentationHarness);
            UpgradeState.Install(PartKind.Hydro);
        }
    }
}
