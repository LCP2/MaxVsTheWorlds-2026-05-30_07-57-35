using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Water Balloon's "stop" (WV-231, spec §6a) — <see cref="RobotEnemy.ApplyHalt"/> freezes the
    /// state machine in place for a duration, then lets it resume chasing. Against real Update()
    /// ticks, not a mock, since the freeze/resume is exactly the kind of thing that only shows up
    /// when the clock actually runs.
    /// </summary>
    public sealed class RobotHaltPlayTests
    {
        private GameObject _max;
        private GameObject _robot;
        private RobotEnemy _enemy;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _max = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _max.name = "Max";
            _max.tag = "Player";
            _max.transform.position = new Vector3(0f, 1f, 12f);   // far enough to stay in Chase, not Lunge

            _robot = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _robot.name = "Robot";
            _robot.transform.position = Vector3.zero;
            _robot.AddComponent<CharacterController>();
            _enemy = _robot.AddComponent<RobotEnemy>();
            _enemy.Apply(EnemyArchetype.Rusher);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_robot != null) Object.Destroy(_robot);
            if (_max != null) Object.Destroy(_max);
            yield return null;
        }

        /// <summary>Horizontal-plane distance only — there's no ground collider in this fixture, so
        /// <c>ApplyGravity</c> (deliberately still active while halted) makes the robot fall through
        /// empty space regardless of halt state. That vertical drift isn't "chasing"; only XZ is.</summary>
        private static float FlatDistance(Vector3 a, Vector3 b) =>
            Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        [UnityTest]
        public IEnumerator AHaltedRobotDoesNotAdvanceTowardMax()
        {
            _enemy.ApplyHalt(1f);
            Vector3 before = _robot.transform.position;

            yield return new WaitForSeconds(0.3f);

            Assert.That(_enemy.IsHalted, Is.True, "should still be inside the halt window");
            Assert.That(FlatDistance(_robot.transform.position, before), Is.LessThan(0.05f),
                "a halted robot must not chase while frozen");
        }

        [UnityTest]
        public IEnumerator TheRobotResumesChasingOnceTheHaltExpires()
        {
            _enemy.ApplyHalt(0.2f);
            Vector3 frozenAt = _robot.transform.position;

            yield return new WaitForSeconds(0.6f);

            Assert.That(_enemy.IsHalted, Is.False, "the halt must have expired by now");
            Assert.That(FlatDistance(_robot.transform.position, frozenAt), Is.GreaterThan(0.05f),
                "once the halt ends the robot must resume closing the distance");
        }

        [UnityTest]
        public IEnumerator ReApplyingAShorterHaltDoesNotCutTheLongerOneShort()
        {
            _enemy.ApplyHalt(1f);
            _enemy.ApplyHalt(0.1f);   // must not shorten the existing halt

            yield return new WaitForSeconds(0.5f);

            Assert.That(_enemy.IsHalted, Is.True, "a shorter re-application must not cut an existing halt short");
        }
    }
}
