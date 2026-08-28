using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-611 — <c>RobotEnemy.TickDormant</c> ran its frustum test (<c>IsOnScreen</c>,
    /// <c>GeometryUtility.CalculateFrustumPlanes</c> plus an AABB test) for every dormant robot every
    /// frame, however many areas behind the player it was placed — exactly the residue population the
    /// ticket names (concealed knots, garrison never looked at, stragglers run past), which only grows
    /// as the player advances. A robot two or more areas behind the player's own can never be the one
    /// the fixed ~72 degree top-down camera falls on, so the test is skipped entirely rather than run
    /// only to read false forever.
    ///
    /// Same reflection idiom as <c>MV603IndividualWakeTests</c>: Unity does not run Awake/OnEnable for a
    /// plain MonoBehaviour outside Play mode, so <c>ResetState</c>/<c>TickDormant</c> are invoked
    /// directly.
    ///
    /// Must fail to COMPILE on the pre-fix commit: <c>RobotEnemy.IsWellBehindPlayer</c> and its test
    /// instrumentation <c>_frustumTestCount</c> did not exist before this ticket — the same "fails on
    /// the base commit" the project's testing policy accepts, per <c>ZoneRouteGridTests</c>' own doc
    /// comment precedent.
    /// </summary>
    public sealed class MV611DormantAreaGateTests
    {
        private GameObject _pathGo;
        private GameObject _playerGo;

        [SetUp]
        public void SetUp() => EnemyNavigation.Reset();

        [TearDown]
        public void TearDown()
        {
            EnemyNavigation.Reset();
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        /// <summary>Four 10x10 m areas in a row along Z, matching the "area&lt;N&gt;" id convention
        /// <see cref="AreaAccumulationDirector.AreaIndexOf"/> parses.</summary>
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
            _pathGo = new GameObject("MV611-dormant-gate-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            FieldInfo mapField = typeof(BackyardPath).GetField("_map",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            mapField.SetValue(path, map);
        }

        private static RobotEnemy NewDormantRobot(Vector3 position)
        {
            var go = new GameObject("MV611-dormant-robot");
            go.transform.position = position;
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            e.ResetState();   // EditMode has no Awake/OnEnable lifecycle — init explicitly, seeds _playerTarget
            e.BeginDormant();
            return e;
        }

        private static void InvokeTickDormant(RobotEnemy e) =>
            typeof(RobotEnemy).GetMethod("TickDormant", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, null);

        private static int FrustumTestCount(RobotEnemy e)
        {
            FieldInfo field = typeof(RobotEnemy).GetField("_frustumTestCount",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "RobotEnemy._frustumTestCount went missing — the instrumentation this test guards (MV-611)");
            return (int)field.GetValue(e);
        }

        [Test]
        public void TickDormant_RobotWellBehindThePlayer_NeverRunsTheFrustumTest()
        {
            MapData map = FourAreasInARow(out MapZone area1, out MapZone area4);
            InstallMap(map);

            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = new Vector3(0f, 0f, area4.z);   // deep in area4

            RobotEnemy farBehind = NewDormantRobot(new Vector3(0f, 0f, area1.z));   // 3 areas behind
            RobotEnemy sameArea = NewDormantRobot(new Vector3(0f, 0f, area4.z));    // the player's own area

            try
            {
                InvokeTickDormant(farBehind);
                Assert.AreEqual(0, FrustumTestCount(farBehind),
                    "a robot two or more areas behind the player must skip the frustum test entirely, " +
                    "not merely read it as false");
                Assert.AreEqual(RobotEnemy.State.Dormant, farBehind.Current,
                    "sanity: skipping the test must never itself wake the robot");

                InvokeTickDormant(sameArea);
                Assert.Greater(FrustumTestCount(sameArea), 0,
                    "a robot in the player's own area must still run its wake test normally — this " +
                    "gate must never suppress the check for a robot the player could actually see");
            }
            finally
            {
                Object.DestroyImmediate(farBehind.gameObject);
                Object.DestroyImmediate(sameArea.gameObject);
            }
        }
    }
}
