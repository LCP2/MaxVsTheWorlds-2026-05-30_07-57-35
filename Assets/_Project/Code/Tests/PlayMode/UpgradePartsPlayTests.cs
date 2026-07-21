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
using MaxWorlds.UI;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The five parts applied to the live game (YT-133): installing one re-fits the weapon or the
    /// player on the spot, the Hydro device cuts the leash, the drop table hands out five distinct
    /// parts, and the upgrade screen's dismiss is what installs the effect.
    /// </summary>
    public sealed class UpgradePartsPlayTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            UpgradeState.Reset();
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
        public IEnumerator TheHydroDeviceUntethersMax()
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
            yield return null;   // LateUpdate sees Untethered and detaches

            // Bolt far past the old leash. Untethered, nothing reels him in.
            max.transform.position = new Vector3(0f, 1f, 100f);
            yield return null;

            float dist = new Vector2(max.transform.position.x, max.transform.position.z).magnitude;
            Assert.That(dist, Is.GreaterThan(HoseTether.AuthoredLength + 5f),
                "with the Hydro device installed the leash must be gone — Max roams free");
            Assert.That(tether.Tap, Is.Null, "the hose should have detached from the tap");
        }

        [UnityTest]
        public IEnumerator TheDropTableGivesFiveDistinctPartsThenNoMore()
        {
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.Destroy(d.gameObject);
            yield return null;

            var dir = new GameObject("PickupDirector");
            _spawned.Add(dir);
            dir.AddComponent<PickupDirector>();
            yield return null;

            DevTuning.PartDropInterval = 1f;   // one part per kill, so 6 kills exercises the whole table
            for (int i = 0; i < 6; i++)   // six tough kills; only five parts exist
                DropSignals.EmitRobotDied(new Vector3(i * 3f, 0f, 0f), EnemyKind.Bruiser);
            yield return null;

            var parts = new HashSet<PartKind>();
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None))
                if (p.gameObject.activeInHierarchy && p.Kind == PickupKind.Part) parts.Add(p.Part);

            Assert.That(parts.Count, Is.EqualTo(5), "the level must drop all five parts, each exactly once");
        }

        [UnityTest]
        public IEnumerator DismissingTheUpgradeScreenInstallsTheEffect()
        {
            foreach (var s in Object.FindObjectsByType<UpgradeScreen>(FindObjectsSortMode.None))
                Object.Destroy(s.gameObject);
            yield return null;

            var screenGo = new GameObject("UpgradeScreen");
            _spawned.Add(screenGo);
            var screen = screenGo.AddComponent<UpgradeScreen>();
            yield return null;

            PickupWallet.AddPart(PartKind.PowerNozzle);
            screen.Open(UpgradeCatalog.For(PartKind.PowerNozzle));
            yield return null;
            Assert.That(UpgradeState.IsInstalled(PartKind.PowerNozzle), Is.False, "not installed until dismissed");

            screen.Continue();
            yield return null;

            Assert.That(UpgradeState.IsInstalled(PartKind.PowerNozzle), Is.True,
                "dismissing the screen must install the part's effect");
            Assert.That(PickupWallet.PartsPending, Is.EqualTo(0), "and take it off the pending queue");
        }
    }
}
