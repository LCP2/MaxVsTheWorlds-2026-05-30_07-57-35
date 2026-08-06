using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The hose runs on power cells, always (YT-137, generalised to every primary shot by WV-233 once
    /// the hose detached from taps entirely): it burns the reserve as it sprays; at empty it can't
    /// sustain.
    /// </summary>
    public sealed class HydroFuelPlayTests
    {
        private GameObject _max;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            UpgradeState.Reset();
            HydroBurst.Reset();
            PickupWallet.Reset();
            DevTuning.Reset();
            yield return null;

            _max = new GameObject("Max");
            _max.AddComponent<WaterBlaster>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_max != null) Object.Destroy(_max);
            yield return null;
            UpgradeState.Reset();
            HydroBurst.Reset();
            PickupWallet.Reset();
            DevTuning.Reset();
        }

        private WaterBlaster Blaster => _max.GetComponent<WaterBlaster>();

        private static void FillCells(int n) { for (int i = 0; i < n; i++) PickupWallet.AddPowerCell(); }

        private IEnumerator Spray(float seconds)
        {
            float t = 0f;
            while (t < seconds) { Blaster.SetFiring(true); t += Time.deltaTime; yield return null; }
        }

        [UnityTest]
        public IEnumerator SprayingBurnsCells()
        {
            DevTuning.PrimaryCellsPerMin = 2400f;   // fast, so the test drains in a moment
            FillCells(10);
            yield return null;

            yield return Spray(0.5f);

            Assert.That(PickupWallet.PowerCells, Is.LessThan(10),
                "spraying must burn power cells — the hose is always self-supplied now (WV-233)");
        }

        [UnityTest]
        public IEnumerator AtZeroCellsTheSprayIsWeakenedNeverBlocked_AndCollectingACellRestoresFullPower()
        {
            // MV-243: a fresh run always starts at 0 power cells (PickupWallet.Reset()). If firing were
            // gated on cells like it used to be, the primary weapon could never fire from t=0 — and
            // since kills are the only way to earn cells, that was a permanent, unrecoverable deadlock.
            // Firing must still emit at 0 cells; it only hits softer until Max collects one.
            Blaster.SetFiring(true);
            yield return null;
            yield return null;
            Assert.That(Blaster.IsEmitting, Is.True,
                "the spray must still emit with an empty power-cell reserve — a fresh run starts at " +
                "0 cells, so gating emission on cells deadlocks the weapon forever");
            Assert.That(Blaster.IsWeakened, Is.True, "0 cells must weaken the stream, not silence it");

            PickupWallet.AddPowerCell();
            yield return Spray(0.2f);
            Assert.That(Blaster.IsEmitting, Is.True, "still emitting once a cell is collected");
            Assert.That(Blaster.IsWeakened, Is.False, "collecting a cell restores full power");
        }

        [UnityTest]
        public IEnumerator WithCellsTheTankStaysSupplied()
        {
            DevTuning.PrimaryCellsPerMin = 0.6f;   // barely drains, so cells last through the test
            FillCells(20);
            yield return null;

            yield return Spray(0.6f);

            Assert.That(Blaster.Energy.Normalized, Is.GreaterThan(0.8f),
                "while cells remain the tank stays supplied — unlimited-feeling water");
        }

        [UnityTest]
        public IEnumerator AtZeroCells_TheTankRecoversOnItsOwnRegenClock_AfterRunningDry()
        {
            // MV-266: a fresh run starts at 0 cells (SetUp's PickupWallet.Reset()). Drain the tank dry,
            // then let go of the trigger — the tank must recover on its own (BlasterTuning's
            // RegenPerSec/RegenDelay), the same as it always has with cells in hand. Before the fix,
            // nothing ever advanced natural regen at 0 cells, so a dry tank stayed dry forever and the
            // run was unwinnable before the first kill could earn a cell.
            // 1400/s * the default 0.1s tick = 140 energy/tick, exactly the tank's size (BlasterTuning.
            // MaxEnergy): affordable for one tick (spends it to 0) but not the next, so it empties
            // in a single tick rather than getting stuck CanSpend-false on a per-tick cost bigger than
            // the whole tank (which would never spend anything at all).
            DevTuning.BlasterDrainPerSecond = 1400f;
            Blaster.RefreshDevTuning();

            yield return Spray(0.15f);
            Assert.That(Blaster.Energy.Normalized, Is.LessThan(0.05f), "expected the tank to be run dry");

            Blaster.SetFiring(false);
            for (int i = 0; i < 60; i++) yield return null;   // let the regen delay + refill clock run

            Assert.That(Blaster.Energy.Normalized, Is.GreaterThan(0.05f),
                "an empty tank at 0 cells must still climb back up on its own regen clock, not stay dead");
        }

        [UnityTest]
        public IEnumerator WaterDepleteRate_Low_LeavesTheTankClearlyFullerThanHigh()
        {
            // MV-266: the "Water deplete rate" tuning slider (BlasterDrainPerSecond) must actually move
            // real in-game consumption — a low setting drains clearly slower than a high one. This is
            // the exact resource Lee watched run dry, and the exact slider he moved with no measurable
            // effect before this fix (the tank was force-refilled to full every frame it held any cells,
            // and never advanced its own regen clock once it didn't — see the recovery test above).
            DevTuning.BlasterDrainPerSecond = 1f;
            Blaster.RefreshDevTuning();

            yield return Spray(0.5f);

            Assert.That(Blaster.Energy.Normalized, Is.GreaterThan(0.9f),
                "a low deplete rate (1/s against a 140 tank) must barely dent the tank over 0.5s");
        }

        [UnityTest]
        public IEnumerator WaterDepleteRate_High_DrainsTheTankClearlyFasterThanLow()
        {
            DevTuning.BlasterDrainPerSecond = 100f;
            Blaster.RefreshDevTuning();

            yield return Spray(0.5f);

            Assert.That(Blaster.Energy.Normalized, Is.LessThan(0.7f),
                "a high deplete rate (100/s against a 140 tank) must visibly drain the tank over 0.5s — " +
                "if this and the low-rate test both pass, the slider is proven wired to real consumption");
        }
    }
}
