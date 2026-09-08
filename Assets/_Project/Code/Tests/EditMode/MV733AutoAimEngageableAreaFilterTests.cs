using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-733: with AUTO-FIRE on, the Water Balloon threw at any robot <c>RobotEnemy.IsAlive</c>
    /// accepted — including a Dormant robot asleep behind a wall, a Submerged Grate Lurker mid-cycle,
    /// and any robot in a DIFFERENT area, since the scan applied no area filter at all. Every one of
    /// those throws lands on nothing and still spends a cell, and auto-fire just re-arms and repeats
    /// until the bank is dry.
    ///
    /// <see cref="WaterBalloonJoystickControl.BuildAutoAimTargets"/> (private static — invoked here by
    /// reflection, same idiom <c>MV611DormantAreaGateTests</c> uses for a private instance method) is
    /// the pulled-out, pure candidate scan: engageable AND same-area, both conditions. Fails to compile
    /// on the pre-fix commit — <c>RobotEnemy.IsEngageable</c> and <c>BuildAutoAimTargets</c> did not
    /// exist before this ticket.
    /// </summary>
    public sealed class MV733AutoAimEngageableAreaFilterTests
    {
        private GameObject _pathGo;
        private readonly List<GameObject> _robotGos = new List<GameObject>();

        [SetUp]
        public void SetUp() => EnemyNavigation.Reset();

        [TearDown]
        public void TearDown()
        {
            EnemyNavigation.Reset();
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
            foreach (GameObject go in _robotGos)
                if (go != null) Object.DestroyImmediate(go);
            _robotGos.Clear();
        }

        /// <summary>Two 10x10 m areas side by side along Z, matching the "area&lt;N&gt;" id convention
        /// <see cref="AreaAccumulationDirector.AreaIndexOf"/> parses — same shape as
        /// <c>MV611DormantAreaGateTests.FourAreasInARow</c>.</summary>
        private static MapData TwoAreasInARow(out MapZone area1, out MapZone area2)
        {
            area1 = new MapZone { id = "area1", x = 0f, z = 5f, width = 10f, depth = 10f };
            area2 = new MapZone { id = "area2", x = 0f, z = 15f, width = 10f, depth = 10f };
            return new MapData { zones = new[] { area1, area2 } };
        }

        private void InstallMap(MapData map)
        {
            _pathGo = new GameObject("MV733-auto-aim-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            FieldInfo mapField = typeof(BackyardPath).GetField("_map",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            mapField.SetValue(path, map);
        }

        private RobotEnemy NewRobot(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            e.ResetState();   // EditMode has no Awake/OnEnable lifecycle — init explicitly
            _robotGos.Add(go);
            return e;
        }

        private RobotEnemy NewChaseRobot(string name, Vector3 position) => NewRobot(name, position);

        private RobotEnemy NewDormantRobot(string name, Vector3 position)
        {
            RobotEnemy e = NewRobot(name, position);
            e.BeginDormant();
            return e;
        }

        /// <summary>A Grate Lurker's own SUBMERGED state (MV-688) — <see cref="RobotEnemy.BeginDormant"/>
        /// routes a Lurker-kind robot into <c>BeginSubmerged</c> rather than plain Dormant.</summary>
        private RobotEnemy NewSubmergedRobot(string name, Vector3 position)
        {
            RobotEnemy e = NewRobot(name, position);
            PropertyInfo kindProp = typeof(RobotEnemy).GetProperty("Kind", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(kindProp, "RobotEnemy.Kind went missing");
            kindProp.SetValue(e, EnemyKind.Lurker);
            e.BeginDormant();
            Assert.AreEqual(RobotEnemy.State.Submerged, e.Current, "sanity: a Lurker must land in Submerged, not Dormant");
            return e;
        }

        private static List<Vector3> InvokeBuildAutoAimTargets(Vector3 playerPosition, IReadOnlyList<RobotEnemy> robots)
        {
            MethodInfo method = typeof(WaterBalloonJoystickControl).GetMethod("BuildAutoAimTargets",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "WaterBalloonJoystickControl.BuildAutoAimTargets went missing (MV-733)");

            var targets = new List<Vector3>();
            method.Invoke(null, new object[] { playerPosition, robots, targets });
            return targets;
        }

        [Test]
        public void AutoAimScan_ExcludesDormantSubmergedAndOutOfAreaRobots()
        {
            MapData map = TwoAreasInARow(out MapZone area1, out MapZone area2);
            InstallMap(map);

            var playerPosition = new Vector3(0f, 0f, area1.z);

            RobotEnemy chaseSameArea = NewChaseRobot("chase-same-area", new Vector3(2f, 0f, area1.z));
            RobotEnemy dormantSameArea = NewDormantRobot("dormant-same-area", new Vector3(1f, 0f, area1.z + 2f));
            RobotEnemy submergedSameArea = NewSubmergedRobot("submerged-same-area", new Vector3(1f, 0f, area1.z - 2f));
            RobotEnemy chaseOtherArea = NewChaseRobot("chase-other-area", new Vector3(0f, 0f, area2.z));

            var allFour = new List<RobotEnemy> { chaseSameArea, dormantSameArea, submergedSameArea, chaseOtherArea };

            List<Vector3> candidates = InvokeBuildAutoAimTargets(playerPosition, allFour);

            Assert.AreEqual(1, candidates.Count,
                "only the Chase robot standing in the player's own area may survive the scan");
            Assert.AreEqual(chaseSameArea.transform.position, candidates[0],
                "the surviving candidate must be the in-area Chase robot, not the Dormant, Submerged " +
                "or out-of-area one");

            var withoutTheOnlyEngageableRobot = new List<RobotEnemy> { dormantSameArea, submergedSameArea, chaseOtherArea };
            List<Vector3> emptyCandidates = InvokeBuildAutoAimTargets(playerPosition, withoutTheOnlyEngageableRobot);

            Assert.AreEqual(0, emptyCandidates.Count,
                "with the only engageable, in-area robot removed, nothing may survive the scan");

            bool found = WaterBalloonAutoAim.TryFindBestDirection(
                playerPosition, throwDistance: 10f, splashRadius: 2f, emptyCandidates, out _);
            Assert.IsFalse(found, "no candidates survived the scan — auto-fire must not throw or spend a cell");
        }
    }
}
