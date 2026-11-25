using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1448 (the one new test, per CLAUDE.md Rule 1): the rocket economy. The Shoulder Rack's rocket
    /// count (<see cref="PickupWallet.PowerCellsSecondary"/>) is capped per world
    /// (<see cref="WorldDefinition.RocketCapacity"/>), a world start levels it into the band
    /// <c>clamp(current, DevStartCells, RocketCapacity)</c>, and a Rocket Crate (+10) is lit and
    /// collectable only while all 10 fit.
    ///
    /// Tier 2 (resolved values): every assertion reads the live wallet count, a placed pickup's live
    /// lit/active state, or the HUD counter's resolved text - never an authored constant asserted back.
    ///
    /// Fails to compile on main before this ticket: <see cref="PickupWallet"/> has no
    /// <c>RocketCapacity</c>, <see cref="PickupKind"/> no <c>RocketCrate</c>, <see cref="WorldCatalog"/>
    /// no <c>ApplyWorldStartLevelling</c> (CS1061/CS0117).
    /// </summary>
    public sealed class MV1448RocketEconomyTests
    {
        private string _dir;
        private GameObject _player;
        private GameObject _hud;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1448-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            PickupWallet.Reset();
            RocketCrateCensus.Reset();
            Pickup.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hud != null) Object.DestroyImmediate(_hud);
            if (_player != null) Object.DestroyImmediate(_player);
            foreach (PickupDirector d in Object.FindObjectsByType<PickupDirector>(FindObjectsInactive.Include))
                Object.DestroyImmediate(d.gameObject);
            SaveSystem.ResetForTests();
            PickupWallet.Reset();
            RocketCrateCensus.Reset();
            Pickup.ResetRegistry();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void RackIsCappedPerWorld_WorldStartLevels_CrateLightsOnlyWhenTenFit_AndHudReadsNOverCap()
        {
            int city = WorldCatalog.IndexOf(WorldIds.City);
            PickupDirector director = PickupDirector.EnsureInstalled();
            _player = new GameObject("Player") { tag = "Player" };
            _player.transform.position = Vector3.zero;

            // --- World-start levelling: 200 -> the City's capacity, 3 -> its floor.
            PickupWallet.SetPowerCellSecondary(200);
            WorldCatalog.ApplyWorldStartLevelling(city);
            Assert.AreEqual(30, PickupWallet.PowerCellsSecondary, "starting the City with 200 rockets levels down to its capacity");
            PickupWallet.SetPowerCellSecondary(3);
            WorldCatalog.ApplyWorldStartLevelling(city);
            Assert.AreEqual(20, PickupWallet.PowerCellsSecondary, "starting the City with 3 rockets levels up to its floor");

            // --- A kill-dropped rocket: collected below capacity, left on the ground at capacity.
            PickupWallet.SetPowerCellSecondary(25);
            Pickup drop = director.PlacePickupAt(PickupKind.PowerCellSecondary, Vector3.zero);
            director.Tick(0.016f);
            Assert.AreEqual(26, PickupWallet.PowerCellsSecondary, "a rocket below capacity is collected");
            Assert.IsFalse(drop.gameObject.activeSelf, "a collected rocket leaves the ground");

            PickupWallet.SetPowerCellSecondary(30);
            Pickup full = director.PlacePickupAt(PickupKind.PowerCellSecondary, Vector3.zero);
            director.Tick(0.016f);
            Assert.AreEqual(30, PickupWallet.PowerCellsSecondary, "no source raises the count above the world's capacity");
            Assert.IsTrue(full.gameObject.activeSelf, "a rocket at capacity stays on the ground, unclaimed");
            full.transform.position = new Vector3(300f, 0f, 300f);   // out of Max's reach for the crate steps below

            // --- A Rocket Crate: lit and collectable at 20 (gives 30), dim and inert at 21.
            PickupWallet.SetPowerCellSecondary(20);
            Pickup lit = director.PlaceRocketCrate(Vector3.zero, "mv1448_lit");
            director.Tick(0.016f);
            Assert.AreEqual(30, PickupWallet.PowerCellsSecondary, "a lit crate banks +10");
            Assert.IsFalse(lit.gameObject.activeSelf, "a collected crate leaves the ground");

            PickupWallet.SetPowerCellSecondary(21);
            Pickup dim = director.PlaceRocketCrate(Vector3.zero, "mv1448_dim");
            director.Tick(0.016f);
            Assert.IsFalse(dim.IsCrateLit, "at 21 of 30 a crate would overflow, so it reads dim");
            Assert.AreEqual(21, PickupWallet.PowerCellsSecondary, "walking over a dim crate does nothing");
            Assert.IsTrue(dim.gameObject.activeSelf, "a dim crate stays where it is");

            PickupWallet.SetPowerCellSecondary(20);
            director.Tick(0.016f);   // re-lights, and Max is standing on it, so it collects
            Assert.AreEqual(30, PickupWallet.PowerCellsSecondary, "the crate re-lights as soon as the count drops low enough");

            // --- Checkpoint: a collected crate is not present again after a restore.
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "MV1448", WorldIndex = city });
            SaveSystem.CaptureCheckpoint(0, areaIndex: 3, worldIndex: city);
            RocketCrateCensus.Reset();   // a resume rebuilds the level: nothing yet knows what was collected
            _player.transform.position = new Vector3(500f, 0f, 500f);   // keep Max off the rebuilt crate
            Pickup rebuilt = director.PlaceRocketCrate(new Vector3(0f, 0f, 0f), "mv1448_dim");
            Assert.IsNotNull(rebuilt, "setup: a rebuilt level places the crate before the restore runs");
            Assert.IsTrue(SaveSystem.RestoreCheckpoint(0));
            Assert.IsFalse(rebuilt.gameObject.activeSelf, "a crate collected before the checkpoint is not present after the restore");

            // --- HUD: the rocket counter reads n/cap.
            WorldCatalog.ApplyWorldStartLevelling(city);
            PickupWallet.SetPowerCellSecondary(30);
            _hud = new GameObject("HUD");
            var hud = _hud.AddComponent<HudController>();
            Invoke(hud, "Awake");
            Invoke(hud, "OnEnable");
            try
            {
                Text counter = _hud.GetComponentsInChildren<Text>(true)
                    .FirstOrDefault(t => t.transform.parent != null && t.transform.parent.name == "Power Cells");
                Assert.IsNotNull(counter, "setup: the rocket counter must build");
                Assert.AreEqual("30/30", counter.text);
            }
            finally
            {
                Invoke(hud, "OnDisable");
            }
        }

        private static void Invoke(Object component, string methodName) =>
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(component, null);
    }
}
