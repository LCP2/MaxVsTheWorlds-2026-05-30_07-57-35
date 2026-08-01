using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Hose;
using MaxWorlds.Pickups;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The legacy parts applied to the live game (YT-133): installing one re-fits the weapon or the
    /// player on the spot. The Hydro sub-assembly (YT-215) still unlocks its burst button, though
    /// WV-233 removed the leash it used to release.
    /// </summary>
    public sealed class UpgradePartsPlayTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            UpgradeState.Reset();
            HydroBurst.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.Destroy(go);
            _spawned.Clear();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.Destroy(d.gameObject);
            foreach (var t in Object.FindObjectsByType<Tap>(FindObjectsSortMode.None))
                Object.Destroy(t.gameObject);
            yield return null;
            UpgradeState.Reset();   // critical: don't leak installs into other test classes
            HydroBurst.Reset();
            PickupWallet.Reset();
            DevTuning.Reset();
            Time.timeScale = 1f;
            yield return null;
        }

        private WaterBlaster NewBlaster()
        {
            var go = new GameObject("Max");
            _spawned.Add(go);
            return go.AddComponent<WaterBlaster>();
        }

        [UnityTest]
        public IEnumerator BeamNozzleNarrowsTheLiveBlaster()
        {
            var blaster = NewBlaster();
            yield return null;
            float baseCone = blaster.ConeHalfAngle;
            float baseRange = blaster.Range;

            UpgradeState.Install(PartKind.BeamNozzle);
            yield return null;

            Assert.That(blaster.ConeHalfAngle, Is.LessThan(baseCone), "the beam nozzle should narrow the cone");
            Assert.That(blaster.Range, Is.EqualTo(baseRange).Within(0.01f), "the beam nozzle keeps the same length");
        }

        [UnityTest]
        public IEnumerator PowerNozzleNarrowsAndLengthens()
        {
            var blaster = NewBlaster();
            yield return null;
            float baseCone = blaster.ConeHalfAngle;
            float baseRange = blaster.Range;

            UpgradeState.Install(PartKind.PowerNozzle);
            yield return null;

            Assert.That(blaster.ConeHalfAngle, Is.LessThan(baseCone), "the power nozzle narrows too");
            Assert.That(blaster.Range, Is.GreaterThan(baseRange + 0.5f), "the power nozzle lengthens the reach");
        }

        [UnityTest]
        public IEnumerator TheHarnessGrowsTheTank()
        {
            var blaster = NewBlaster();
            yield return null;
            float baseMax = blaster.Energy.Max;

            UpgradeState.Install(PartKind.AugmentationHarness);
            yield return null;   // the blaster re-fits off UpgradeState.Changed

            Assert.That(blaster.Energy.Max, Is.GreaterThan(baseMax),
                "the augmentation harness must enlarge the water tank");
        }

        [UnityTest]
        public IEnumerator AssemblingHydroUnlocksTheBurstButton_ButDoesNotAutoTrigger()
        {
            // Since WV-233 detached the hose from taps entirely there is no leash left for the sub-
            // assembly to cut — assembling it only unlocks the burst button (YT-215's "prize", now a
            // cosmetic countdown pending its own retirement); it must never trigger by itself.
            UpgradeState.Install(PartKind.Hydro);
            yield return null;
            Assert.That(HydroBurst.Ready, Is.False,
                "the condenser alone has no mount to clip into — the sub-assembly isn't complete yet");

            UpgradeState.Install(PartKind.AugmentationHarness);   // the mount — completes the sub-assembly
            yield return null;

            Assert.That(HydroBurst.Active, Is.False, "assembly alone must not start a burst");
            Assert.That(HydroBurst.Ready, Is.True, "assembled and never used — the burst must now be pressable");
        }

        // The drop table exhausting after five/seven distinct parts, and the screen's dismiss auto-
        // installing whatever was banked, are the "dropped-part-decides" mechanics WV-228 replaces —
        // see RobotDropPlayTests.PartsKeepDroppingPastTheOldSevenPartCap and UpgradeEffectsPlayTests
        // (which still exercises Open/Continue's effect application per part) for their replacements.
    }
}
