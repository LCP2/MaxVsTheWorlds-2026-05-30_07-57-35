using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-941 — Lee's TestFlight 0.9.9 report: dying sent Max back an area, but robots respawned in
    /// every earlier area, including the one he was returned to. Root cause:
    /// <see cref="WorldRunner.Continue"/> used to call <see cref="AreaAccumulationDirector.RestoreArea"/>
    /// on the death area, which wipes every live robot there — survivors included — and re-solves a
    /// brand-new authored composition. That is the opposite of Lee's rule ("if you destroyed those
    /// robots, they stay destroyed"): a robot still alive at the moment of death got despawned anyway,
    /// and every robot already destroyed before death came back. The fix drops that call entirely — a
    /// death no longer touches the arena's robot population at all.
    ///
    /// Builds a real four-area world through <see cref="MapRuntime.Build"/> (same idiom as
    /// <see cref="MV438DeathOverlayTests"/>) rather than mocking <see cref="AreaAccumulationDirector"/>,
    /// clears areas 1-3 by hand (simulating the player having already killed every robot there), leaves
    /// one of area4's two robots alive at the moment of death, then drives the real death/Continue
    /// sequence and reads back which robots are still alive, per area.
    /// </summary>
    public sealed class MV941DestroyedGarrisonStaysDestroyedTests
    {
        private GameObject _root;
        private GameObject _playerGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            DeathRunState.Reset();
            RobotEnemy.ResetRegistry();
            Sentinel.ResetRegistry();
            Time.timeScale = 1f;
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            var overlay = Object.FindFirstObjectByType<DeathOverlay>();
            if (overlay != null) Object.DestroyImmediate(overlay.gameObject);

            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_root != null) Object.DestroyImmediate(_root);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            Sentinel.ResetRegistry();
            DeathRunState.Reset();
            DevTuning.Reset();
        }

        /// <summary>Stub entry plus four ordinary combat areas stacked N/S, each with an authored
        /// rusher-only composition (area4 gets two, so one can be killed before death and one can still
        /// be alive when Max falls). No garrisonDensity on any area — DensityShare(null) is 0, so
        /// Garrison never pre-places anything extra and each area's live count is exactly its authored
        /// rusher count, deterministically.</summary>
        private static WorldConfig FourAreaWorld() => new WorldConfig
        {
            world = "MV-941 Test World",
            dials = new WorldDials
            {
                areaCount = 4, baseThreat = 1f, threatGrowth = 0f,
                pacingRhythm = new[] { 1f, 1f, 1f, 1f }, toughnessCurve = new WorldToughnessCurve(), powerupCadence = 1,
                band = new WorldBand(),
            },
            enemyTypes = new WorldEnemyTypes
            {
                small = new WorldEnemyTypeEntry { thv = 1f }, large = new WorldEnemyTypeEntry { thv = 1f },
                heavy = new WorldEnemyTypeEntry { thv = 1f }, brute = new WorldEnemyTypeEntry { thv = 1f },
            },
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", index = 0, role = "entry",
                    origin = new WorldAreaOrigin { x = -2f, z = -6f }, size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    id = "a1", index = 1, role = "normal", name = "Area One",
                    origin = new WorldAreaOrigin { x = -10f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                    composition = new WorldComposition { rusher = 1 },
                },
                new WorldArea
                {
                    id = "a2", index = 2, role = "normal", name = "Area Two",
                    origin = new WorldAreaOrigin { x = -10f, z = 20f }, size = new WorldAreaSize { w = 20f, d = 20f },
                    composition = new WorldComposition { rusher = 1 },
                },
                new WorldArea
                {
                    id = "a3", index = 3, role = "normal", name = "Area Three",
                    origin = new WorldAreaOrigin { x = -10f, z = 40f }, size = new WorldAreaSize { w = 20f, d = 20f },
                    composition = new WorldComposition { rusher = 1 },
                },
                new WorldArea
                {
                    id = "a4", index = 4, role = "normal", name = "Area Four",
                    origin = new WorldAreaOrigin { x = -10f, z = 60f }, size = new WorldAreaSize { w = 20f, d = 20f },
                    composition = new WorldComposition { rusher = 2 },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
                new WorldGate
                {
                    id = "g1", width = 3f, opensWith = "primary",
                    from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a2", wall = "S", pos = 0.5f },
                },
                new WorldGate
                {
                    id = "g2", width = 3f, opensWith = "primary",
                    from = new WorldGateEndpoint { area = "a2", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a3", wall = "S", pos = 0.5f },
                },
                new WorldGate
                {
                    id = "g3", width = 3f, opensWith = "primary",
                    from = new WorldGateEndpoint { area = "a3", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a4", wall = "S", pos = 0.5f },
                },
            },
        };

        private static void InvokeOnPlayerDied(WorldRunner runner) =>
            typeof(WorldRunner).GetMethod("OnPlayerDied", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(runner, null);

        private static RobotEnemy[] LiveRobotsInZone(MapData map, string zoneId)
        {
            MapZone zone = map.Zone(zoneId);
            var list = new List<RobotEnemy>();
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
            {
                if (r == null || !r.IsAlive) continue;
                MapZone at = map.ZoneAt(r.transform.position.x, r.transform.position.z);
                if (at != null && at.id == zone.id) list.Add(r);
            }
            return list.ToArray();
        }

        [Test]
        public void DeathInArea4_LeavesAreas1To3Empty_AndKeepsOnlyArea4sSurvivor()
        {
            WorldConfig cfg = FourAreaWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            _root = new GameObject("WorldRunner Test Root");
            MapBuild built = MapRuntime.Build(map, _root.transform);

            var areaGo = new GameObject("Area Accumulation");
            areaGo.transform.SetParent(_root.transform);
            var areaDirector = areaGo.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);
            areaDirector.Configure(map, built.Cover); // fills area1

            areaDirector.EnterArea(2);
            areaDirector.EnterArea(3);
            areaDirector.EnterArea(4);

            Assert.That(LiveRobotsInZone(map, "area1").Length, Is.EqualTo(1), "precondition: area1's authored rusher is up");
            Assert.That(LiveRobotsInZone(map, "area2").Length, Is.EqualTo(1), "precondition: area2's authored rusher is up");
            Assert.That(LiveRobotsInZone(map, "area3").Length, Is.EqualTo(1), "precondition: area3's authored rusher is up");
            RobotEnemy[] area4Robots = LiveRobotsInZone(map, "area4");
            Assert.That(area4Robots.Length, Is.EqualTo(2), "precondition: area4's authored two rushers are up");

            // Simulate the player having already cleared areas 1-3 on the way to area4.
            foreach (RobotEnemy r in LiveRobotsInZone(map, "area1")) r.Despawn();
            foreach (RobotEnemy r in LiveRobotsInZone(map, "area2")) r.Despawn();
            foreach (RobotEnemy r in LiveRobotsInZone(map, "area3")) r.Despawn();

            // Simulate area4 already half-cleared before the death that actually kills Max.
            RobotEnemy destroyedBeforeDeath = area4Robots[0];
            RobotEnemy aliveAtDeath = area4Robots[1];
            destroyedBeforeDeath.Despawn();
            Assert.IsFalse(destroyedBeforeDeath.IsAlive, "precondition: one area4 robot is destroyed before Max dies");
            Assert.IsTrue(aliveAtDeath.IsAlive, "precondition: the other area4 robot is still alive at the moment of death");

            var runner = _root.AddComponent<WorldRunner>();
            runner.Configure(cfg, map, built, areaDirector);

            MapZone area4Zone = map.Zone("area4");
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = area4Zone.Center;

            InvokeOnPlayerDied(runner);
            Assert.IsTrue(runner.HasPendingRespawn, "precondition: the death must leave a respawn pending");

            runner.Continue();

            Assert.That(LiveRobotsInZone(map, "area1").Length, Is.EqualTo(0),
                "MV-941: area1's already-destroyed garrison must not come back after a death two areas later");
            Assert.That(LiveRobotsInZone(map, "area2").Length, Is.EqualTo(0),
                "MV-941: area2's already-destroyed garrison must not come back after a death two areas later");
            Assert.That(LiveRobotsInZone(map, "area3").Length, Is.EqualTo(0),
                "MV-941: area3's already-destroyed garrison must not come back after a death in the area right after it");

            Assert.IsFalse(destroyedBeforeDeath.IsAlive,
                "MV-941: a robot already destroyed in the death area before Max fell must stay destroyed");
            Assert.IsTrue(aliveAtDeath.IsAlive,
                "MV-941: a robot still alive at the moment of death must remain alive, not be wiped and replaced");
            Assert.That(LiveRobotsInZone(map, "area4").Length, Is.EqualTo(1),
                "MV-941: the death area must keep exactly the one survivor, not a freshly re-solved composition");
        }
    }
}
