using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>The maths that gets a beeline chaser around the lawn's cover (YT-68). A robot that
    /// presses into a prop forever is the failure this guards.</summary>
    public sealed class ObstacleSteeringTests
    {
        private static readonly Vector3 WallFacingSouth = new Vector3(0f, 0f, -1f); // wall's near face

        [Test]
        public void GlancingAWall_WalksAlongIt_NotIntoIt()
        {
            // Heading north-east into a wall that faces south: keep the eastward half, drop the rest.
            Vector3 dir = ObstacleSteering.SlideAlongWall(
                new Vector3(1f, 0f, 1f).normalized, WallFacingSouth, 1f);

            Assert.AreEqual(0f, Vector3.Dot(dir, WallFacingSouth), 1e-4, "still pushing into the wall");
            Assert.Greater(dir.x, 0.9f, "should have kept going the way it was already headed");
            Assert.AreEqual(1f, dir.magnitude, 1e-4);
        }

        [Test]
        public void HeadOn_CommitsToASide_InsteadOfStalling()
        {
            // Straight into the wall: sliding gives nothing, so it must pick a way around.
            Vector3 dir = ObstacleSteering.SlideAlongWall(Vector3.forward, WallFacingSouth, 1f);

            Assert.AreEqual(1f, dir.magnitude, 1e-4, "a stalled robot is the bug — it must still move");
            Assert.AreEqual(0f, dir.z, 1e-4, "and it must move ALONG the wall, not through it");
            Assert.AreNotEqual(0f, dir.x);
        }

        [Test]
        public void OppositeSigns_SendRobotsAroundOppositeSides()
        {
            Vector3 left = ObstacleSteering.SlideAlongWall(Vector3.forward, WallFacingSouth, 1f);
            Vector3 right = ObstacleSteering.SlideAlongWall(Vector3.forward, WallFacingSouth, -1f);

            Assert.Less(Vector3.Dot(left, right), -0.9f, "a pack should split around cover, not queue up");
        }

        [Test]
        public void PreferSign_IsStablePerRobot_ButSplitsThePack()
        {
            Assert.AreEqual(ObstacleSteering.PreferSignFor(42), ObstacleSteering.PreferSignFor(42),
                "the same robot must not flip-flop and jitter on the spot");
            Assert.AreNotEqual(ObstacleSteering.PreferSignFor(42), ObstacleSteering.PreferSignFor(43));
        }

        [Test]
        public void AWallBehindYou_IsNotInTheWay()
        {
            // Already walking away from it: the chase should be left completely alone.
            Vector3 desired = Vector3.back;
            Vector3 dir = ObstacleSteering.SlideAlongWall(desired, WallFacingSouth, 1f);
            Assert.AreEqual(desired, dir);
        }

        [Test]
        public void SteeringIsFlat_EvenWhenTheContactIsSloped()
        {
            // Contact normals off a box corner can have a Y component; the robot walks, it doesn't fly.
            Vector3 dir = ObstacleSteering.SlideAlongWall(
                new Vector3(1f, 0.5f, 1f), new Vector3(0f, 0.4f, -1f), 1f);
            Assert.AreEqual(0f, dir.y, 1e-4);
        }

        [Test]
        public void DegenerateNormal_LeavesTheChaseAlone()
        {
            Vector3 dir = ObstacleSteering.SlideAlongWall(Vector3.forward, Vector3.zero, 1f);
            Assert.AreEqual(Vector3.forward, dir);
        }
    }
}
