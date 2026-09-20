using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-869: World 2 ran at 15-21 fps against a 60 target and nothing in the build said how many
    /// actors were alive — <c>RobotEnemy.ActiveCount</c> already existed and nothing surfaced it. This
    /// proves the new readout line is read off the LIVE registries (<see cref="RobotEnemy.Active"/>,
    /// <see cref="Sentinel.Active"/>), never authored counts — Tier 1 of the project's own testing
    /// policy is exactly the trap this ticket exists to avoid.
    ///
    /// Must fail to COMPILE on base commit 68d962b: <c>MaxWorlds.Enemies.PopulationReadout</c> did not
    /// exist before this ticket. (HEAD at the time of writing, d8a6610, is identical to 68d962b in
    /// every file this ticket touches — <c>git log --oneline 68d962b..d8a6610</c> against
    /// RobotEnemy.cs/Replicator.cs/Bootstrap.cs/Mv503DiagnosticOverlay.cs/FactoryCensus.cs returns
    /// nothing — so a compile check with only this ticket's production changes reverted is the same
    /// proof against either commit.) Same "fails on the base commit" acceptance the project's testing
    /// policy already uses for a compile failure — see <c>MV611DormantAreaGateTests</c>' own doc
    /// comment precedent. Same reflection-driven-OnEnable idiom as <c>MV611DormantAreaGateTests</c> /
    /// <c>MV625CrossAreaBossDeathTests</c>: Unity does not run Awake/OnEnable for a plain MonoBehaviour
    /// outside Play mode.
    /// </summary>
    public sealed class MV869PopulationReadoutTests
    {
        private GameObject _pathGo;
        private GameObject _playerGo;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            Sentinel.ResetRegistry();
            FactoryCensus.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            Sentinel.ResetRegistry();
            FactoryCensus.Reset();
            foreach (GameObject go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        /// <summary>Same four-areas-in-a-row map shape as <c>MV611DormantAreaGateTests</c> — area4 is
        /// the player's own area, area1 is three areas behind (past the MV-611 WellBehindAreaSlack of
        /// two), area3/area4 are not.</summary>
        private static MapData FourAreasInARow(out MapZone area1, out MapZone area4)
        {
            area1 = new MapZone { id = "area1", x = 0f, z = 5f, width = 10f, depth = 10f };
            var area2 = new MapZone { id = "area2", x = 0f, z = 15f, width = 10f, depth = 10f };
            var area3 = new MapZone { id = "area3", x = 0f, z = 25f, width = 10f, depth = 10f };
            area4 = new MapZone { id = "area4", x = 0f, z = 35f, width = 10f, depth = 10f };
            return new MapData { zones = new[] { area1, area2, area3, area4 } };
        }

        private void InstallMap(MapData map)
        {
            _pathGo = new GameObject("MV869-population-readout-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            FieldInfo mapField = typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            mapField.SetValue(path, map);
        }

        private static void InvokeOnEnable(RobotEnemy e) =>
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, null);

        private RobotEnemy NewRobot(Vector3 position)
        {
            var go = new GameObject("MV869-robot");
            _spawned.Add(go);
            go.transform.position = position;
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            InvokeOnEnable(e); // registers into RobotEnemy.Active, resets state, seeds _playerTarget by tag
            return e;
        }

        [Test]
        public void BuildLine_ReadsThePopulationOffTheLiveRegistries_NotAuthoredCounts()
        {
            MapData map = FourAreasInARow(out MapZone area1, out MapZone area4);
            InstallMap(map);

            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = new Vector3(0f, 0f, area4.z);

            for (int i = 0; i < 2; i++) NewRobot(new Vector3(0f, 0f, area4.z)); // awake (default Chase)

            for (int i = 0; i < 3; i++)
                NewRobot(new Vector3(0f, 0f, area4.z)).BeginDormant(); // dormant, player's own area — not well behind

            for (int i = 0; i < 4; i++)
                NewRobot(new Vector3(0f, 0f, area1.z)).BeginDormant(); // dormant, 3 areas behind — well behind

            var sentinelGo = new GameObject("MV869-sentinel");
            _spawned.Add(sentinelGo);
            sentinelGo.AddComponent<Sentinel>().Init(area4.Center, 200f, range: 7f, fireInterval: 0.6f,
                moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);

            string line = PopulationReadout.BuildLine();

            Assert.AreEqual("robots 9 (awake 2 dorm 3 behind 4)  sent 1  repl 0", line,
                "the resolved line must come off RobotEnemy.Active/Sentinel.Active live, never an authored count");
        }
    }
}
