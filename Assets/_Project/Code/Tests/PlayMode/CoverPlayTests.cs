using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Cover, against real colliders and real raycasts (YT-83). The EditMode tests prove the
    /// perception maths; these prove the thing the player actually experiences — that a robot loses
    /// you behind a tree, commits to where you WERE, and finds you again when you step out.
    /// </summary>
    public sealed class CoverPlayTests
    {
        private GameObject _cover;
        private GameObject _max;
        private GameObject _robot;

        /// <summary>A solid block on the cover layer, standing between Max and the robot.</summary>
        private GameObject Wall(Vector3 at, Vector3 size)
        {
            _cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cover.name = "Test Cover";
            _cover.transform.position = at;
            _cover.transform.localScale = size;
            CoverLayer.Assign(_cover);
            return _cover;
        }

        private GameObject Max(Vector3 at)
        {
            _max = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _max.name = "Max";
            _max.tag = "Player";                 // RobotEnemy finds him by tag
            _max.transform.position = at;
            return _max;
        }

        private RobotEnemy Robot(Vector3 at)
        {
            _robot = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _robot.name = "Robot";
            _robot.transform.position = at;
            _robot.AddComponent<CharacterController>();
            var e = _robot.AddComponent<RobotEnemy>();
            e.Apply(EnemyArchetype.Rusher);
            return e;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in new[] { _cover, _max, _robot })
                if (go != null) Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ARobotSeesMaxAcrossAnEmptyLawn()
        {
            Max(new Vector3(0f, 1f, 0f));
            var robot = Robot(new Vector3(0f, 0.7f, 8f));
            yield return null;
            yield return null;

            Assert.IsTrue(robot.Sight.HasSight,
                "nothing is between them and it still can't see him — the sight-line is broken, " +
                "which would mean the robots never chase at all");
        }

        [UnityTest]
        public IEnumerator CoverBreaksTheSightLine_AndTheRobotStopsKnowingWhereMaxIs()
        {
            // The whole ticket, in one test. Max at the origin, robot 8 m up-field, a solid block
            // planted squarely between them.
            Max(new Vector3(0f, 1f, 0f));
            Wall(new Vector3(0f, 1.5f, 4f), new Vector3(4f, 3f, 1f));
            var robot = Robot(new Vector3(0f, 0.7f, 8f));

            yield return null;
            Physics.SyncTransforms();
            yield return null;

            Assert.IsFalse(robot.Sight.HasSight,
                "the robot can see Max straight through a solid block — cover is still decoration");
        }

        [UnityTest]
        public IEnumerator LosingHimBehindCover_TheRobotCommitsToWhereHeWas_AndMaxGetsAway()
        {
            // Sight first, so it has a trail worth going stale.
            var max = Max(new Vector3(0f, 1f, 0f));
            var robot = Robot(new Vector3(0f, 0.7f, 8f));
            yield return null;
            yield return null;
            Assert.IsTrue(robot.Sight.HasSight, "precondition: it has him");

            // Now drop cover between them, and Max runs for it.
            Wall(new Vector3(0f, 1.5f, 4f), new Vector3(6f, 3f, 1f));
            Physics.SyncTransforms();
            yield return null;

            Vector3 whereHeWas = robot.Sight.LastKnown;
            max.transform.position = new Vector3(12f, 1f, -6f);   // gone
            Physics.SyncTransforms();
            yield return null;
            yield return null;

            Assert.IsFalse(robot.Sight.HasSight);
            Assert.AreEqual(whereHeWas, robot.Sight.LastKnown,
                "the robot updated its memory of Max while it could not see him — it is still " +
                "omniscient, and hiding does nothing");

            float toStaleSpot = Vector3.Distance(robot.Sight.LastKnown, whereHeWas);
            float toActualMax = Vector3.Distance(robot.Sight.LastKnown, max.transform.position);
            Assert.Less(toStaleSpot, toActualMax,
                "it is heading for the real Max rather than the empty patch of lawn he left behind");
        }

        [UnityTest]
        public IEnumerator SteppingBackOutOfCoverIsSeenAgain_SoContactCanBeRemade()
        {
            var max = Max(new Vector3(0f, 1f, 0f));
            Wall(new Vector3(0f, 1.5f, 4f), new Vector3(4f, 3f, 1f));
            var robot = Robot(new Vector3(0f, 0.7f, 8f));
            yield return null;
            Physics.SyncTransforms();
            yield return null;
            Assert.IsFalse(robot.Sight.HasSight, "precondition: he is hidden");

            // Step out from behind the block, into the open.
            max.transform.position = new Vector3(9f, 1f, 0f);
            Physics.SyncTransforms();
            yield return null;
            yield return null;

            Assert.IsTrue(robot.Sight.HasSight,
                "Max walked back into the open and the robot never noticed — it would search an " +
                "empty spot forever and the fight would just stop");
        }

        [UnityTest]
        public IEnumerator TheBlasterCannotSprayThroughCover()
        {
            // Symmetry, and the reason it matters: if cover broke THEIR sight but not YOUR spray,
            // hiding would be strictly dominant — stand behind the tree and kill everything in
            // perfect safety. Cover has to cost you your shot too, or it's a turret nest.
            var robotGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            robotGo.transform.position = new Vector3(0f, 0.7f, 5f);
            _robot = robotGo;

            Wall(new Vector3(0f, 1.5f, 2.5f), new Vector3(4f, 3f, 1f));
            Physics.SyncTransforms();
            yield return null;

            Vector3 maxEye = new Vector3(0f, 1f, 0f);
            Assert.IsFalse(
                LineOfSight.Clear(maxEye, robotGo.transform.position, robotGo.transform),
                "the blaster can wash a robot through a solid block — hiding behind cover would be " +
                "a free kill box");
        }

        [UnityTest]
        public IEnumerator YouCanAlwaysShootTheThingYouAreAimingAt_EvenWhenItIsItselfCover()
        {
            // The Mower Hutch is on the cover layer (so the shed reads true) AND is the thing Max
            // must destroy to win. Cast naively and the factory blocks the sight-line to itself, the
            // blaster can never hurt it, and cover has made the run unwinnable.
            var factory = GameObject.CreatePrimitive(PrimitiveType.Cube);
            factory.name = "Factory";
            factory.transform.position = new Vector3(0f, 1f, 5f);
            factory.transform.localScale = new Vector3(3f, 2f, 3f);
            CoverLayer.Assign(factory);
            _cover = factory;
            Physics.SyncTransforms();
            yield return null;

            Assert.IsTrue(
                LineOfSight.Clear(new Vector3(0f, 1f, 0f), factory.transform.position, factory.transform),
                "the factory is blocking the shot at the factory — the win condition is unreachable");
        }
    }
}
