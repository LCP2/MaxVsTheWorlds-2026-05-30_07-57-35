using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1210 (the one new test, Rule 1): fires one LIGHT MISSILE ball at the origin, heading +Z, with
    /// one alive robot placed directly BEHIND it at (0, 0, -2) — the single candidate within range, so
    /// rule 3's "smallest angle to heading" pick must take it even at a 180-degree angle. Drives the
    /// ball's own explicit-dt <see cref="LightMissileBall.Tick"/> at dt = 1/60 with TRACKING at level 0
    /// (170 deg/s base) and proves, on RESOLVED values (MV-465 Tier 2): (a) the ball's heading never
    /// turns more than that tick's own <see cref="LightMissileBall.CurrentTurnRateDegPerSec"/> times dt,
    /// and (b) it registers a hit on the robot (a measured health loss) before 3.0 s airborne.
    ///
    /// Fails on base commit 9220e37 (main HEAD before this ticket) — it cannot compile: LIGHT MISSILE
    /// (<see cref="LightMissileBall"/>) does not exist there at all.
    /// </summary>
    public sealed class MV1210LightMissileHomingTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo CcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly FieldInfo HealthField = typeof(RobotEnemy).GetField("_health", NonPublicInstance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        private GameObject _robotGo;

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            LightMissileBall.DestroyPoolForTests();
            if (_robotGo != null) Object.DestroyImmediate(_robotGo);
        }

        [Test]
        public void BallNeverSnapsAndHomesOntoTheOnlyCandidateEvenDirectlyBehindIt()
        {
            RobotEnemy robot = NewRegisteredEnemy(new Vector3(0f, 0f, -2f));
            _robotGo = robot.gameObject;
            HealthField.SetValue(robot, 1000f); // generous headroom -- the damage must never clamp at 0
            float healthBefore = robot.HealthCurrent;
            Physics.SyncTransforms();

            const float trackingBaseLevel0 = 170f; // rig_board.ship.json p_trk startLevel 0
            LightMissileBall ball = LightMissileBall.Spawn(Vector3.zero, Vector3.forward,
                MaxWorlds.Combat.LightMissile.DefaultSpeed, 30f, trackingBaseLevel0, 0);
            Assert.IsNotNull(ball, "test precondition: the pool must have room for one ball");

            const float step = 1f / 60f;
            const float budgetSeconds = 3f;
            int maxSteps = Mathf.CeilToInt(budgetSeconds / step);

            for (int i = 0; i < maxSteps && ball.IsActiveBall; i++)
            {
                Vector3 headingBefore = ball.Direction;
                ball.Tick(step);
                if (!ball.IsActiveBall) break;

                float turnedDeg = Vector3.Angle(headingBefore, ball.Direction);
                float allowedDeg = ball.CurrentTurnRateDegPerSec * step + 0.05f; // small float slack
                Assert.LessOrEqual(turnedDeg, allowedDeg,
                    $"tick {i}: heading turned {turnedDeg:0.000} deg, more than this tick's own " +
                    $"CurrentTurnRateDegPerSec ({ball.CurrentTurnRateDegPerSec:0.0}) x dt ({step:0.0000}) allows");
            }

            Assert.IsFalse(ball.IsActiveBall,
                "the ball must have registered its hit well within the 3.0s budget");

            float damage = healthBefore - robot.HealthCurrent;
            Assert.AreEqual(30f, damage, 0.01f,
                "the robot must take exactly the ball's one authored hit (30 damage), before 3.0s airborne");
        }

        private static RobotEnemy NewRegisteredEnemy(Vector3 position)
        {
            var go = new GameObject("MV1210 Robot");
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            go.transform.position = position;
            e.Apply(EnemyArchetype.Rusher);
            RobotEnemyOnEnable.Invoke(e, null);
            return e;
        }
    }
}
