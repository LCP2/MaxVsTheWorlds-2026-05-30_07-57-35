using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-955 (Lee): "Max can teleport out of the playable area... fall through the world" at World 1
    /// g20 could not be reproduced in EditMode, and Lee plays on TestFlight with no console open. This
    /// ticket makes every <see cref="FallRecoveryState"/> recovery (MV-946/MV-952) write what it saw
    /// into <see cref="FallEventLog"/>, so a live fall names its own cause without anyone reproducing it
    /// with a console open.
    ///
    /// Must FAIL on the pre-fix commit: base <c>RobotEnemy</c> never calls <c>FallEventLog.Record</c>
    /// from its own recovery path (the type doesn't exist there at all), so
    /// <c>FallEventLog.Events.Count</c> stays 0 through this whole test instead of reaching 1.
    ///
    /// Tier 2 (resolved value): asserts the buffer's actual recorded contents (entity kind, gate id,
    /// dt) — never a log string, never a presence check. Same reflection-driven <c>Tick</c> idiom as
    /// <c>MV952RobotFallRecoveryTests</c> (RobotEnemy.Tick never runs outside Play mode).
    /// </summary>
    public sealed class MV955FallEventLogTests
    {
        private GameObject _pathGo;
        private GameObject _floorGo;
        private RobotEnemy _robot;

        private static readonly MethodInfo TickMethod =
            typeof(RobotEnemy).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo TargetField =
            typeof(RobotEnemy).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PlayerTargetField =
            typeof(RobotEnemy).GetField("_playerTarget", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
            FallEventLog.Reset();
            CharacterControllerMotion.ResetRecentWindow();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
            FallEventLog.Reset();
            CharacterControllerMotion.ResetRecentWindow();
            if (_robot != null) Object.DestroyImmediate(_robot.gameObject);
            if (_floorGo != null) Object.DestroyImmediate(_floorGo);
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
        }

        private void InstallMap(MapData map)
        {
            _pathGo = new GameObject("MV955-robot-fall-event-log-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            FieldInfo mapField = typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            mapField.SetValue(path, map);
        }

        private static void TickOnce(RobotEnemy e, float dt) => TickMethod.Invoke(e, new object[] { dt });

        [Test]
        public void RobotPushedBelowTheFloorNextToAGate_RecordsOneFallEventNamingTheGate()
        {
            // Same remote-coordinate hygiene as MV952RobotFallRecoveryTests — keeps this test's own
            // floor/gate geometry structurally isolated from whatever another EditMode test in the same
            // shared batch-mode scene left behind near the origin.
            const float ox = -73512f, oz = 46209f;
            const string gateId = "g20";

            var map = new MapData
            {
                zones = new[] { new MapZone { id = "area1", x = ox, z = oz, width = 20f, depth = 20f, level = 0 } },
                entities = new[] { new MapEntity { id = gateId, kind = "gate", x = ox, z = oz } },
            };
            InstallMap(map);

            _floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floorGo.transform.position = new Vector3(ox, -0.05f, oz);
            _floorGo.transform.localScale = new Vector3(20f, 0.1f, 20f);
            Physics.SyncTransforms();

            var go = new GameObject("MV955-robot");
            go.transform.position = new Vector3(ox, 0f, oz);
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.5f;
            cc.center = Vector3.up * (cc.height * 0.5f);
            _robot = go.AddComponent<RobotEnemy>();
            CcField.SetValue(_robot, cc);
            _robot.ResetState();
            TargetField.SetValue(_robot, null);
            PlayerTargetField.SetValue(_robot, null);

            const float dt = 1f / 60f;

            for (int i = 0; i < 30 && !(cc.isGrounded && Mathf.Abs(go.transform.position.y) <= 0.2f); i++)
                TickOnce(_robot, dt);
            Assert.IsTrue(cc.isGrounded, "sanity: the robot must actually be resting on the real floor collider");

            // Push it below the floor right next to the gate (MV-952's own mechanism-agnostic setup).
            cc.enabled = false;
            go.transform.position = new Vector3(ox, -5f, oz);
            cc.enabled = true;
            Physics.SyncTransforms();

            const int framesInOneSecond = 60;
            for (int i = 0; i < framesInOneSecond && FallEventLog.Events.Count == 0; i++)
                TickOnce(_robot, dt);

            Assert.AreEqual(1, FallEventLog.Events.Count,
                "MV-955: recovering a robot from a fall must record exactly one FallEventLog entry");

            FallEventRecord ev = FallEventLog.Events[0];
            Assert.AreEqual("robot", ev.EntityKind, "MV-955: the recorded entity kind must name the robot");
            Assert.AreEqual(gateId, ev.NearestGateId, "MV-955: the recorded entry must name the nearest gate id");
            Assert.Greater(ev.DeltaTime, 0f, "MV-955: the recorded entry must carry a non-zero dt");
        }
    }
}
