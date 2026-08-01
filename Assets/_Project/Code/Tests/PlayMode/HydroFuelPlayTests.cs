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
        public IEnumerator AtZeroCellsTheSprayStalls_AndCollectingACellRestoresIt()
        {
            // No cells at all.
            Blaster.SetFiring(true);
            yield return null;
            yield return null;
            Assert.That(Blaster.IsEmitting, Is.False,
                "the spray must stall with an empty power-cell reserve — collect more to keep firing");

            PickupWallet.AddPowerCell();
            yield return Spray(0.2f);
            Assert.That(Blaster.IsEmitting, Is.True, "collecting a cell restores the spray");
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
    }
}
