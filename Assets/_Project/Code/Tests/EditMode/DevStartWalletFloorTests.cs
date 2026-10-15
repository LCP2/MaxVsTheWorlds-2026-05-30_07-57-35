using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1185 (the one new test, per CC_AUTONOMY's testing policy): a DEV jump (a slot-screen W-button
    /// start, or LAST AREA's <see cref="WorldRunner.JumpToFinaleArea"/>) used to leave
    /// <see cref="PickupWallet"/> empty -- Lee then fought a world's last area with no Power Cells for
    /// rockets and arrived in the next world with nothing. <see cref="HomeScreen.StartSlotWorld"/> and
    /// <see cref="WorldRunner.JumpToFinaleArea"/> now both floor the wallet to
    /// <see cref="WorldCatalog.ApplyDevStartFloor"/>'s per-world authored floor, never reducing an
    /// existing higher balance.
    ///
    /// Tier 2 (resolved values): the Parts assertion reads <see cref="PickupWallet.Capacity"/> live,
    /// AFTER the rig has been applied by <see cref="HomeScreen.StartDevWorld"/> -- never a hardcoded
    /// magic number -- so it also stands as a live check that the City's baked
    /// <see cref="WorldDefinition.DevStartParts"/> literal still matches 50% of what the rig actually
    /// resolves to today.
    ///
    /// Fails to compile on main HEAD before this ticket: <see cref="WorldDefinition"/> carries no
    /// <c>DevStartParts</c>/<c>DevStartCells</c> fields and <see cref="WorldCatalog"/> has no
    /// <c>ApplyDevStartFloor</c> (CS1061/CS0117).
    /// </summary>
    public sealed class DevStartWalletFloorTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1185-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            DevTuning.Reset();
            WeaponSystemState.Reset();   // also resets RigState and RigBoard back to World 1
            RigFusionState.Reset();
            // Same reset set MV1057DevFinalAreaTests uses before it builds a real world — this test
            // builds the City's own map too, so the static census registries must start clean or a
            // later test in the same batch run (a13's garrison count, MV-1027/MV-1066) sees doubled
            // registrations left over from here.
            RobotEnemy.ResetRegistry();
            FactoryCensus.Reset();
            BossCensus.Reset();
            Pickup.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            WeaponSystemState.Reset();
            RigBoard.ResetForTests();
            RigFusionState.Reset();
            DevTuning.Reset();
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            RobotEnemy.ResetRegistry();
            FactoryCensus.Reset();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void DevStartFloorsTheWallet_AndJumpToFinaleAreaNeverReducesIt()
        {
            int cityIndex = WorldCatalog.IndexOf(WorldIds.City);

            HomeScreen.StartDevWorld(0, cityIndex);

            Assert.AreEqual(20, PickupWallet.PowerCellsSecondary,
                "the City's own DevStartCells floor is 20");
            Assert.AreEqual(PickupWallet.Capacity / 2, PickupWallet.PowerCells,
                "the City's own DevStartParts floor is half the resolved post-rig Capacity, read live");

            PickupWallet.SetPowerCellSecondary(40);

            // Drive the real engine entry point, same idiom as MV1057DevFinalAreaTests: a real
            // WorldConfig/MapRuntime build for the City, then WorldRunner.JumpToFinaleArea on it.
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.KeyForIndex(cityIndex));
            Assert.IsNotNull(cfg, "setup failure: the City's own config must load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV-1185 Probe Root");
            GameObject playerGo = null, areaGo = null, runnerGo = null;
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                areaGo = new GameObject("Area Accumulation");
                var areaDirector = areaGo.AddComponent<AreaAccumulationDirector>();
                areaDirector.ConfigureWorld(cfg, cityIndex);
                areaDirector.Configure(map, built.Cover);

                runnerGo = new GameObject("WorldRunner Test Root");
                var runner = runnerGo.AddComponent<WorldRunner>();
                runner.Configure(cfg, map, built, areaDirector);

                MapZone area1 = map.Zone("area1");
                Assert.IsNotNull(area1, "setup failure: the City must have an area1 zone to cold-boot the player in");
                playerGo = new GameObject("Player") { tag = "Player" };
                playerGo.transform.position = area1.Center;

                bool jumped = runner.JumpToFinaleArea();
                Assert.IsTrue(jumped, "MV-1185: JumpToFinaleArea must succeed for the City");

                Assert.AreEqual(40, PickupWallet.PowerCellsSecondary,
                    "MV-1185: the DEV-start floor must never reduce an existing higher balance");
            }
            finally
            {
                if (runnerGo != null) Object.DestroyImmediate(runnerGo);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (areaGo != null) Object.DestroyImmediate(areaGo);
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }
    }
}
