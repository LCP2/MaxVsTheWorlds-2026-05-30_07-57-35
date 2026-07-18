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

        // --- the door: where a robot APPEARS, before it walks out (YT-100) --------------------

        /// <summary>The Mower Hutch body, as the map builds it: a cube scaled (3, 2, 3).</summary>
        private static readonly Vector3 Body = new Vector3(3f, 2f, 3f);
        private const float Radius = 0.4f;    // a robot's collider radius

        [Test]
        public void TheDoorIsAgainstTheFactoryWall_NotOutOnTheLawn()
        {
            // The whole of YT-100: a robot must be born at the building, and walk out. Appearing at
            // the exit point — a clear 3.5 m out, past the shed, on open grass — is what read as
            // popping into existence beside the factory rather than coming out of it.
            var factory = new Vector3(0f, 1f, 15f);
            for (int i = 0; i < 50; i++)
            {
                Vector3 dir = FactoryMouth.ExitDirection(TowardMax, MouthFacing, i, HalfAngle);
                Vector3 door = FactoryMouth.DoorPoint(factory, dir, Body, Radius, 3.5f, 1f);
                Vector3 exit = FactoryMouth.ExitPoint(factory, dir, 3.5f, 1f);

                float toDoor = Flat(door, factory);
                Assert.Less(toDoor, Flat(exit, factory),
                    $"robot {i} appears no closer to the factory than the spot it walks out to — " +
                    "there is no emergence left to see");

                // Against the wall, but not embedded in it. The body's half-depth is 1.5 m, so the
                // near face is 1.5 m out along an axis and further across a corner.
                Assert.Greater(toDoor, 1.5f, $"robot {i} is born inside the factory's 3 m body");
                Assert.Less(toDoor, 1.5f + Radius + 1.2f,
                    $"robot {i} is born {toDoor:0.0} m out — that is not a doorway");
            }
        }

        [Test]
        public void TheRobotClearsTheWallItIsBornAgainst()
        {
            // Its own body must not overlap the factory's collider: an interpenetrating
            // CharacterController is ejected on its first move, which would fire the robot out of
            // the shed instead of walking it out.
            for (int i = 0; i < 50; i++)
            {
                Vector3 dir = FactoryMouth.ExitDirection(TowardMax, MouthFacing, i, HalfAngle);
                Vector3 door = FactoryMouth.DoorPoint(Vector3.zero, dir, Body, Radius, 3.5f, 0f);

                // Distance from the box surface, measured on the axis the robot left by.
                float outside = Mathf.Max(Mathf.Abs(door.x) - 1.5f, Mathf.Abs(door.z) - 1.5f);
                Assert.Greater(outside, 0f, $"robot {i} overlaps the factory body");
            }
        }

        [Test]
        public void TheDoorIsOnTheLineTheRobotWalksOut_SoItLeavesInAStraightLine()
        {
            // Door and exit share a bearing from the factory: the emergence is a walk straight out
            // of the mouth, not a sidestep.
            for (int i = 0; i < 20; i++)
            {
                Vector3 dir = FactoryMouth.ExitDirection(TowardMax, MouthFacing, i, HalfAngle);
                Vector3 door = FactoryMouth.DoorPoint(Vector3.zero, dir, Body, Radius, 3.5f, 0f);
                Vector3 exit = FactoryMouth.ExitPoint(Vector3.zero, dir, 3.5f, 0f);

                Vector3 outward = exit - door; outward.y = 0f;
                Assert.Greater(Vector3.Dot(outward.normalized, dir), 0.999f,
                    $"robot {i} does not walk straight out of the door it appeared in");
            }
        }

        [Test]
        public void ADoorBeyondTheExitPointIsClampedBack_SoNothingEmergesBackwards()
        {
            // A big enough robot (or a small enough spawn radius) would otherwise put the door
            // further out than the place it is walking to, and the emergence would run inwards.
            Vector3 dir = Vector3.back;
            Vector3 door = FactoryMouth.DoorPoint(Vector3.zero, dir, Body, Radius, maxRadius: 1.0f, y: 0f);
            Assert.AreEqual(1.0f, Flat(door, Vector3.zero), 1e-3,
                "the door should be clamped to the exit point, never past it");
        }

        [Test]
        public void TheDoorMovesToWhicheverFaceTheRobotLeavesBy()
        {
            // There is no authored door on the greybox body, and that is the feature: the mouth is
            // wherever the stream is pointing, so it swings round the building as Max moves.
            Vector3 north = FactoryMouth.DoorPoint(Vector3.zero, Vector3.forward, Body, Radius, 3.5f, 0f);
            Vector3 east = FactoryMouth.DoorPoint(Vector3.zero, Vector3.right, Body, Radius, 3.5f, 0f);

            Assert.Greater(north.z, 1.5f, "leaving north should put the door on the north face");
            Assert.AreEqual(0f, north.x, 1e-3);
            Assert.Greater(east.x, 1.5f, "leaving east should put the door on the east face");
            Assert.AreEqual(0f, east.z, 1e-3);
        }

        private static float Flat(Vector3 a, Vector3 b) =>
            Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

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
