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
    /// The five legacy parts applied to the live game (YT-133): installing one re-fits the weapon or
    /// the player on the spot, and the Hydro burst (YT-215) frees Max from the leash for a timed
    /// window then snaps it back.
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
        public IEnumerator AssemblingHydroAloneDoesNotUntetherMax()
        {
            var tap = Tap.Create("Tap", Vector3.zero);
            _spawned.Add(tap.gameObject);

            var max = new GameObject("Max");
            _spawned.Add(max);
            max.AddComponent<CharacterController>();
            var tether = max.AddComponent<HoseTether>();
            max.transform.position = Vector3.zero;
            tether.SetTap(tap);
            yield return null;

            UpgradeState.Install(PartKind.Hydro);
            yield return null;
            Assert.That(tether.Tap, Is.Not.Null,
                "the condenser alone has no mount to clip into — Max should stay tethered");

            UpgradeState.Install(PartKind.AugmentationHarness);   // the mount — completes the sub-assembly
            yield return null;

            // Assembling (YT-215) only unlocks the burst BUTTON now — it must not, by itself, cut the
            // leash the way the old permanent untether did.
            Assert.That(HydroBurst.Active, Is.False, "assembly alone must not start a burst");

            max.transform.position = new Vector3(0f, 1f, 100f);
            yield return null;

            float dist = new Vector2(max.transform.position.x, max.transform.position.z).magnitude;
            Assert.That(dist, Is.LessThanOrEqualTo(HoseTether.AuthoredLength + 0.5f),
                "with no burst active the leash must still clamp him, even fully assembled");
        }

        [UnityTest]
        public IEnumerator TriggeringTheBurstUntethersMax_ThenSnapsBackOnTimeout()
        {
            DevTuning.HydroBurstSeconds = 0.2f;     // short, so the test doesn't wait 10 real seconds
            DevTuning.HydroBurstCooldown = 0.1f;

            var tap = Tap.Create("Tap", Vector3.zero);
            _spawned.Add(tap.gameObject);

            var max = new GameObject("Max");
            _spawned.Add(max);
            max.AddComponent<CharacterController>();
            var tether = max.AddComponent<HoseTether>();
            max.transform.position = Vector3.zero;
            tether.SetTap(tap);
            yield return null;

            UpgradeState.Install(PartKind.Hydro);
            UpgradeState.Install(PartKind.AugmentationHarness);
            yield return null;

            HydroBurst.Trigger();
            yield return null;   // LateUpdate sees Active and detaches

            // Bolt far past the leash. Bursting, nothing reels him in.
            max.transform.position = new Vector3(0f, 1f, 100f);
            yield return null;

            float dist = new Vector2(max.transform.position.x, max.transform.position.z).magnitude;
            Assert.That(dist, Is.GreaterThan(HoseTether.AuthoredLength + 5f),
                "while the burst is active the leash must be gone — Max roams free");
            Assert.That(tether.Tap, Is.Null, "the hose should have detached from the tap while out of range");

            // Wait out the (shortened) burst so it snaps back.
            float t = 0f;
            while (HydroBurst.Active && t < 2f) { t += Time.deltaTime; yield return null; }
            Assert.That(HydroBurst.Active, Is.False, "the burst must end on its own");

            // He's still 100 m out — past PlugRange of the only tap — when the timer runs out. The
            // snap-back must re-anchor him to the nearest tap regardless of range and clamp him back
            // in, with no manual walk-back required and no softlock (YT-215 acceptance).
            yield return null;
            yield return null;

            Assert.That(tether.Tap, Is.Not.Null, "the burst ending must re-leash Max to a tap, not leave him stranded");
            float distAfter = new Vector2(max.transform.position.x, max.transform.position.z).magnitude;
            Assert.That(distAfter, Is.LessThanOrEqualTo(HoseTether.AuthoredLength + 0.5f),
                "once the burst ends the leash must clamp him back in immediately, with no softlock");
        }

        // The drop table exhausting after five/seven distinct parts, and the screen's dismiss auto-
        // installing whatever was banked, are the "dropped-part-decides" mechanics WV-228 replaces —
        // see RobotDropPlayTests.PartsKeepDroppingPastTheOldSevenPartCap and UpgradeEffectsPlayTests
        // (which still exercises Open/Continue's effect application per part) for their replacements.
    }
}
