using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The power-cell economy recut (WV-227): cells accumulate from pickups and deplete as gear is
    /// used, an empty reserve weakens Max, and the Power Efficiency formula is ready for a real
    /// ability level once WV-230/231 add one.
    /// </summary>
    public sealed class CellEconomyTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            PickupWallet.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void LevelZeroAppliesNoReduction()
        {
            Assert.That(CellEconomyTuning.EfficiencyMultiplier(0, 0.1f), Is.EqualTo(1f).Within(1e-5f),
                "no ability owned (level 0) must not touch the drain at all");
        }

        [Test]
        public void EachLevelShavesOffTheReductionFraction()
        {
            Assert.That(CellEconomyTuning.EfficiencyMultiplier(1, 0.1f), Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(CellEconomyTuning.EfficiencyMultiplier(5, 0.1f), Is.EqualTo(0.5f).Within(1e-5f),
                "a maxed L5 ability at the authored 10%/level should halve the drain");
        }

        [Test]
        public void LevelIsClampedToTheFiveLevelCap()
        {
            Assert.That(CellEconomyTuning.EfficiencyMultiplier(99, 0.1f),
                Is.EqualTo(CellEconomyTuning.EfficiencyMultiplier(5, 0.1f)).Within(1e-5f),
                "the ability only goes to L5 — a runaway level must not drain the multiplier past that");
        }

        [Test]
        public void TheMultiplierNeverGoesNegative()
        {
            Assert.That(CellEconomyTuning.EfficiencyMultiplier(5, 1f), Is.EqualTo(0f).Within(1e-5f),
                "an oversized reduction-per-level must clamp at 0x, not swing the drain negative");
        }

        [Test]
        public void MaxIsNotWeakenedWithCellsInReserve()
        {
            PickupWallet.AddPowerCell();
            Assert.That(PlayerHealth.IsWeakened, Is.False);
        }

        [Test]
        public void MaxIsWeakenedAtZeroCells()
        {
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0), "precondition: a fresh run starts empty");
            Assert.That(PlayerHealth.IsWeakened, Is.True,
                "at 0 cells Max must read as weakened until he collects more");
        }

        [Test]
        public void CollectingACellUnWeakensHimImmediately()
        {
            Assert.That(PlayerHealth.IsWeakened, Is.True);
            PickupWallet.AddPowerCell();
            Assert.That(PlayerHealth.IsWeakened, Is.False, "a single collected cell must clear it right away");
        }

        // WeakenedMaxTakesExtraDamage (TakeDamage against a live PlayerHealth) lives in
        // PlayMode: constructing PlayerHealth needs its Awake to have run (to seed _health from
        // Max), and a plain EditMode [Test] has no frame to yield for that — see
        // CellEconomyPlayTests.
    }
}
