using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1355 — Orbitals' wider band + hit-on-crossing. REACH L1 (orbit radius 1.8 m), one stationary
    /// robot 2.3 m from Max at 20 degrees round the ring. That is 0.5 m outside the orbit line: inside
    /// the new band (1.8 + 0.7) but outside the old one (1.8 + 0.35). Orb 0 starts at 0 degrees; ONE
    /// <see cref="Orbitals.Tick"/> of 0.25 s at SPIN L1 (180 deg/s) carries it to 45 degrees, past the
    /// robot's angle, and its end point (~1.0 m from the robot) is outside the contact radius, so only
    /// a swept test can hit. The robot's resolved health must drop by POWER L1's 10, exactly once.
    /// Guards MV-1355; fails on the base commit (no hit at all: health unchanged).
    /// </summary>
    public sealed class MV1355OrbitalsSweepTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo CcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RobotEnemy.ResetRegistry();
            RigBoard.UseWorld(WorldCatalog.IndexOf(WorldIds.Mothership));
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RobotEnemy.ResetRegistry();
            RigBoard.ResetForTests();
        }

        [Test]
        public void OrbSweepingPastARobotInsideTheWideBandHitsItExactlyOnce_MV1355()
        {
            WeaponSystemState.SecondaryKind = SecondaryKind.Orbitals;
            RigState.RestoreSnapshot(new Dictionary<string, int> { { "s_orb", 1 }, { "s_rad", 1 } },
                new[] { "SECONDARY" });

            var maxGo = new GameObject("Max");
            var orbitals = maxGo.AddComponent<Orbitals>();

            float rad = 20f * Mathf.Deg2Rad;
            RobotEnemy robot = NewRobot(new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * 2.3f);
            try
            {
                Physics.SyncTransforms();
                float before = robot.HealthCurrent;

                orbitals.Tick(0.25f);

                Assert.That(before - robot.HealthCurrent, Is.EqualTo(10f).Within(0.001f),
                    "one swept tick must land POWER L1's 10 damage on the robot exactly once");
            }
            finally
            {
                Object.DestroyImmediate(robot.gameObject);
                Object.DestroyImmediate(maxGo);
            }
        }

        private static RobotEnemy NewRobot(Vector3 position)
        {
            var go = new GameObject("MV1355-Robot");
            var cc = go.AddComponent<CharacterController>();
            var robot = go.AddComponent<RobotEnemy>();
            CcField.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Rusher);
            RobotOnEnableMethod.Invoke(robot, null);
            go.transform.position = position;
            return robot;
        }
    }
}
