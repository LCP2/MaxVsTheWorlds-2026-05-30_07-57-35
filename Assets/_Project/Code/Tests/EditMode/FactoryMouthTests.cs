using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>Robots must pour OUT of the factory toward Max (YT-70) — never behind the shed, and
    /// never on top of the player. These pin the emission pattern that sells "kill the source".</summary>
    public sealed class FactoryMouthTests
    {
        private const float HalfAngle = 55f;
        private static readonly Vector3 MouthFacing = Vector3.back;   // factory's front face
        private static readonly Vector3 TowardMax = Vector3.back;     // Max is down-lawn (−Z)

        [Test]
        public void EveryRobotLeavesOnThePlayersSideOfTheFactory()
        {
            // The old ring spawned them all the way around, so some appeared BEHIND the shed and
            // walked through/around it. Every exit must now have a forward component toward Max.
            for (int i = 0; i < 200; i++)
            {
                Vector3 dir = FactoryMouth.ExitDirection(TowardMax, MouthFacing, i, HalfAngle);
                Assert.Greater(Vector3.Dot(dir, TowardMax.normalized), Mathf.Cos(HalfAngle * Mathf.Deg2Rad) - 1e-3f,
                    $"robot {i} left through the back of the shed");
            }
        }

        [Test]
        public void TheStreamFansOut_RatherThanFilingThroughOneSpot()
        {
            // A stream reads as a stream because consecutive robots use different parts of the mouth.
            bool sawLeft = false, sawRight = false, sawMiddle = false;
            for (int i = 0; i < 40; i++)
            {
                float f = FactoryMouth.FanOffset(i);
                Assert.GreaterOrEqual(f, -1f); Assert.LessOrEqual(f, 1f);
                if (f < -0.4f) sawLeft = true;
                else if (f > 0.4f) sawRight = true;
                else sawMiddle = true;
            }
            Assert.IsTrue(sawLeft && sawMiddle && sawRight, "the mouth isn't spreading across its width");
        }

        [Test]
        public void ConsecutiveRobotsDontStackOnTopOfEachOther()
        {
            for (int i = 0; i < 50; i++)
            {
                float a = FactoryMouth.FanOffset(i);
                float b = FactoryMouth.FanOffset(i + 1);
                Assert.Greater(Mathf.Abs(a - b), 0.1f, $"robots {i} and {i + 1} exit through the same slot");
            }
        }

        [Test]
        public void ExitDirectionIsFlatAndUnitLength()
        {
            // A robot walks; it does not launch. And a non-unit dir would corrupt the spawn radius.
            Vector3 dir = FactoryMouth.ExitDirection(new Vector3(3f, 9f, -4f), MouthFacing, 7, HalfAngle);
            Assert.AreEqual(0f, dir.y, 1e-4);
            Assert.AreEqual(1f, dir.magnitude, 1e-4);
        }

        [Test]
        public void ExitPointClearsTheFactoryBody()
        {
            // The Mower Hutch is 3 m across, so a 3.5 m radius must put the robot outside it —
            // otherwise it spawns inside its own factory and gets shoved out by the collider.
            var factory = new Vector3(0f, 1f, 15f);
            for (int i = 0; i < 50; i++)
            {
                Vector3 dir = FactoryMouth.ExitDirection(TowardMax, MouthFacing, i, HalfAngle);
                Vector3 p = FactoryMouth.ExitPoint(factory, dir, 3.5f, 1f);

                Assert.AreEqual(1f, p.y, 1e-4, "robot must appear on the ground");
                Assert.AreEqual(3.5f, Vector3.Distance(
                    new Vector3(p.x, 0f, p.z), new Vector3(factory.x, 0f, factory.z)), 1e-3);
                Assert.Greater(Mathf.Max(Mathf.Abs(p.x - factory.x), Mathf.Abs(p.z - factory.z)), 1.5f,
                    "robot spawned inside the factory's 3 m body");
            }
        }

        [Test]
        public void TheMouthTracksMax_SoTheStreamAlwaysFlowsAtHim()
        {
            // Max kites round to the far side; the mouth should follow him, not keep emitting
            // down-lawn into empty grass.
            Vector3 fromTheSide = Vector3.right;
            Vector3 dir = FactoryMouth.ExitDirection(fromTheSide, MouthFacing, 0, HalfAngle);
            Assert.Greater(Vector3.Dot(dir, Vector3.right), 0.5f);
        }

        [Test]
        public void NoTarget_FallsBackToTheFactorysOwnFace()
        {
            // Before Max is found (or if he's standing inside the factory), robots still walk out
            // the front — never in a degenerate or random direction.
            Vector3 dir = FactoryMouth.ExitDirection(Vector3.zero, MouthFacing, 3, HalfAngle);
            Assert.AreEqual(1f, dir.magnitude, 1e-4);
            Assert.Greater(Vector3.Dot(dir, MouthFacing), Mathf.Cos(HalfAngle * Mathf.Deg2Rad) - 1e-3f);
        }

        [Test]
        public void TargetDirectlyOverhead_IsStillFlattenedToAWalkableDirection()
        {
            Vector3 dir = FactoryMouth.ExitDirection(Vector3.up * 5f, MouthFacing, 1, HalfAngle);
            Assert.AreEqual(1f, dir.magnitude, 1e-4);
            Assert.AreEqual(0f, dir.y, 1e-4);
        }
    }
}
