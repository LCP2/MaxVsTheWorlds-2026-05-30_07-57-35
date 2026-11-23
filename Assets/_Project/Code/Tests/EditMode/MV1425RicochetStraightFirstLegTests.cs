using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1425 (the one new test, Rule 1): a disc fired along +X at the maximum AIMING level (200 deg/s,
    /// 12 m lookahead) with an alive robot 5 m ahead and 30 degrees off the heading and a Cover wall 10 m
    /// ahead must hold its resolved heading dead straight (dot with +X >= 0.9999) on every tick before
    /// its first contact, then turn toward the robot after the bounce.
    ///
    /// Fails on base e5c2395: Steer runs from the first tick, so the heading bends toward the robot
    /// before the wall is reached.
    /// </summary>
    public sealed class MV1425RicochetStraightFirstLegTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo DirectionField =
            typeof(RicochetDisc).GetField("_direction", NonPublicInstance);
        private static readonly FieldInfo CcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        private GameObject _wall;
        private GameObject _robotGo;

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            RicochetDisc.DestroyPoolForTests();
            if (_wall != null) Object.DestroyImmediate(_wall);
            if (_robotGo != null) Object.DestroyImmediate(_robotGo);
        }

        [Test]
        public void DiscFliesDeadStraightUntilItsFirstBounceThenBendsTowardTheRobot()
        {
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.transform.position = new Vector3(10f, 1f, 0f);
            _wall.transform.localScale = new Vector3(0.2f, 2f, 20f);
            CoverLayer.Assign(_wall);

            _robotGo = new GameObject("MV1425 Robot");
            var cc = _robotGo.AddComponent<CharacterController>();
            var robot = _robotGo.AddComponent<RobotEnemy>();
            CcField.SetValue(robot, cc);
            float rad = 30f * Mathf.Deg2Rad;
            _robotGo.transform.position = new Vector3(5f * Mathf.Cos(rad), 0f, 5f * Mathf.Sin(rad));
            robot.Apply(EnemyArchetype.Rusher);
            RobotEnemyOnEnable.Invoke(robot, null);
            Physics.SyncTransforms();

            LogAssert.ignoreFailingMessages = true; // one-time VfxMaterials shader log, as in MV1195
            RicochetDisc disc;
            try
            {
                disc = RicochetDisc.Spawn(Vector3.zero, Vector3.right, Ricochet.DefaultSpeed, bounces: 2,
                    powerLevel: 1, aimLevel: 4, damage: 12f, bankBonusPerBounce: 0.2f,
                    turnRateDegPerSec: 200f, lookaheadMetres: 12f, Ricochet.DefaultRadius,
                    Ricochet.DefaultLifetimeSeconds);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
            Assert.IsNotNull(disc, "test precondition: the pool must have room for one disc");

            const float step = 1f / 60f;
            int guard = 0;
            while (disc.IsActiveDisc && disc.BouncesTaken == 0 && guard++ < 300)
            {
                disc.Tick(step);
                if (disc.BouncesTaken > 0) break;
                var dir = (Vector3)DirectionField.GetValue(disc);
                Assert.GreaterOrEqual(Vector3.Dot(dir, Vector3.right), 0.9999f,
                    $"before the first contact the heading must stay +X; was {dir} at tick {guard}");
            }
            Assert.AreEqual(1, disc.BouncesTaken, "the disc must bank off the wall");

            for (int i = 0; i < 12 && disc.IsActiveDisc; i++) disc.Tick(step);
            var after = (Vector3)DirectionField.GetValue(disc);
            Assert.Greater(after.z, 0.01f,
                $"after the bounce AIMING must turn the heading toward the robot (+Z side); was {after}");
        }
    }
}
