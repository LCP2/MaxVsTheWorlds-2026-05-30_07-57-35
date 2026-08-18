using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The maths MV-434 uses to keep a robot's body from ending a tick inside Max's own.
    /// <c>EnemySpawner</c>/<c>AreaAccumulationDirector</c>'s <c>Physics.IgnoreCollision</c> (MV-321)
    /// leaves nothing physical to do it — this pure clamp is the whole guarantee. Must fail on
    /// <c>dadd9f1</c>, where <see cref="EnemyBodySeparation"/> does not exist at all.
    /// </summary>
    public sealed class EnemyBodySeparationTests
    {
        [Test]
        public void MinDistance_SumsBothRadiiAndTheMargin()
        {
            float min = EnemyBodySeparation.MinDistance(0.55f, 0.5f, 0.15f);
            Assert.AreEqual(1.2f, min, 1e-5f);
        }

        [Test]
        public void AlreadyClear_LeavesPositionUnchanged()
        {
            Vector3 robot = new Vector3(5f, 0f, 0f);
            Vector3 corrected = EnemyBodySeparation.Clamp(robot, Vector3.zero, 1.2f);
            Assert.AreEqual(robot, corrected);
        }

        [Test]
        public void InsideMinDistance_PushedOutToExactlyMinDistance()
        {
            Vector3 robot = new Vector3(0.3f, 0f, 0.4f); // 0.5 m from the player — inside 1.2 m
            Vector3 corrected = EnemyBodySeparation.Clamp(robot, Vector3.zero, 1.2f);
            Assert.AreEqual(1.2f, Vector3.Distance(corrected, Vector3.zero), 1e-4f);
        }

        [Test]
        public void PushedOutAlongTheSameLine_NeverToTheFarSideOfThePlayer()
        {
            Vector3 robot = new Vector3(0.3f, 0f, 0.4f);
            Vector3 corrected = EnemyBodySeparation.Clamp(robot, Vector3.zero, 1.2f);
            float alignment = Vector3.Dot(robot.normalized, corrected.normalized);
            Assert.Greater(alignment, 0.999f,
                "pushing a robot clear must not flip it through the player to the opposite side");
        }

        [Test]
        public void PlayerAdvancingIntoAStationaryRobot_DisplacesItOutward_NotThrough()
        {
            // The robot's own position never changes between these two calls — only the player's
            // does, the same as a stationary robot in Telegraph/Recover being walked into.
            Vector3 robot = new Vector3(2f, 0f, 0f);
            Assert.AreEqual(robot, EnemyBodySeparation.Clamp(robot, Vector3.zero, 1.2f),
                "still 2 m clear of the player — must not move yet");

            Vector3 playerNow = new Vector3(1.5f, 0f, 0f); // closed the gap to 0.5 m
            Vector3 corrected = EnemyBodySeparation.Clamp(robot, playerNow, 1.2f);

            Assert.AreNotEqual(robot, corrected, "the player closing the gap must displace the robot");
            Assert.Greater(corrected.x, playerNow.x,
                "displaced OUTWARD, on the same side it started on — never through the player");
            Assert.AreEqual(1.2f, Vector3.Distance(corrected, playerNow), 1e-4f);
        }

        [Test]
        public void CoincidentPositions_PickADeterministicDirection_NotANaN()
        {
            Vector3 corrected = EnemyBodySeparation.Clamp(Vector3.zero, Vector3.zero, 1.2f);
            Assert.IsFalse(float.IsNaN(corrected.x) || float.IsNaN(corrected.y) || float.IsNaN(corrected.z));
            Assert.AreEqual(1.2f, corrected.magnitude, 1e-4f);
        }

        [Test]
        public void YIsPassedThrough_SeparationIsGroundPlaneOnly()
        {
            Vector3 robot = new Vector3(0.1f, 3f, 0.1f);
            Vector3 corrected = EnemyBodySeparation.Clamp(robot, Vector3.zero, 1.2f);
            Assert.AreEqual(3f, corrected.y, 1e-5f);
        }
    }
}
