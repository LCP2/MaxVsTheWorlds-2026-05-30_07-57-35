using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Hose;
using MaxWorlds.Pickups;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The Hydro device runs on power cells (YT-137). Untethered, it burns the reserve as it sprays;
    /// on a tap it uses the YT-106 water economy and leaves the cells alone; at empty it can't sustain.
    /// </summary>
    public sealed class HydroFuelPlayTests
    {
        private GameObject _max;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            UpgradeState.Reset();
            PickupWallet.Reset();
            DevTuning.Reset();
            // No stray taps/directors — this test controls whether Max is "on a tap".
            foreach (var t in Object.FindObjectsByType<Tap>(FindObjectsSortMode.None)) Object.Destroy(t.gameObject);
            foreach (var d in Object.FindObjectsByType<HoseDirector>(FindObjectsSortMode.None)) Object.Destroy(d.gameObject);
            yield return null;

            _max = new GameObject("Max");   // NOT tagged Player, so the HoseDirector won't wire it
            _max.AddComponent<WaterBlaster>();
            _max.AddComponent<HoseTether>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_max != null) Object.Destroy(_max);
            yield return null;
            foreach (var t in Object.FindObjectsByType<Tap>(FindObjectsSortMode.None)) Object.Destroy(t.gameObject);
            UpgradeState.Reset();
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
        public IEnumerator UntetheredHydroBurnsCellsAsItSprays()
        {
            UpgradeState.Install(PartKind.AugmentationHarness);   // the mount — completes the sub-assembly (YT-165)
            UpgradeState.Install(PartKind.Hydro);
            DevTuning.HydroDrainRate = 40f;   // fast, so the test drains in a moment
            FillCells(10);
            yield return null;

            yield return Spray(0.5f);

            Assert.That(PickupWallet.PowerCells, Is.LessThan(10),
                "spraying on the Hydro condenser must burn power cells");
        }

        [UnityTest]
        public IEnumerator OnATapItLeavesTheCellsAlone()
        {
            var tap = Tap.Create("Tap", _max.transform.position);   // Max is standing on the tap
            UpgradeState.Install(PartKind.AugmentationHarness);   // the mount — completes the sub-assembly (YT-165)
            UpgradeState.Install(PartKind.Hydro);
            DevTuning.HydroDrainRate = 40f;
            FillCells(10);
            yield return null;   // the tether plugs into the tap by proximity

            Assert.That(_max.GetComponent<HoseTether>().OnTap, Is.True, "Max should be plugged into the tap");
            yield return Spray(0.5f);

            Assert.That(PickupWallet.PowerCells, Is.EqualTo(10),
                "on a tap the YT-106 economy supplies the water — power cells must not drain");
            Object.Destroy(tap.gameObject);
        }

        [UnityTest]
        public IEnumerator AtZeroCellsHydroStalls_AndCollectingACellRestoresIt()
        {
            UpgradeState.Install(PartKind.AugmentationHarness);   // the mount — completes the sub-assembly (YT-165)
            UpgradeState.Install(PartKind.Hydro);   // no cells at all
            yield return null;

            Blaster.SetFiring(true);
            yield return null;
            yield return null;
            Assert.That(Blaster.IsEmitting, Is.False,
                "the Hydro spray must stall with an empty power-cell reserve — collect more or re-tap");

            PickupWallet.AddPowerCell();
            yield return Spray(0.2f);
            Assert.That(Blaster.IsEmitting, Is.True, "collecting a cell restores the Hydro spray");
        }

        [UnityTest]
        public IEnumerator WithCellsTheHydroTankStaysSupplied()
        {
            UpgradeState.Install(PartKind.AugmentationHarness);   // the mount — completes the sub-assembly (YT-165)
            UpgradeState.Install(PartKind.Hydro);
            DevTuning.HydroDrainRate = 0.01f;   // barely drains, so cells last through the test
            FillCells(20);
            yield return null;

            yield return Spray(0.6f);

            Assert.That(Blaster.Energy.Normalized, Is.GreaterThan(0.8f),
                "while cells remain the condenser keeps the tank supplied — unlimited-feeling water");
        }
    }
}
