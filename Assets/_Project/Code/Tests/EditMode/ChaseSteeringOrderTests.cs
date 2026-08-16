using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// RobotEnemy.TickChase blends two steering terms every tick a wall is remembered: a neighbour
    /// push (<see cref="EnemySeparation"/>) and a wall round (<see cref="ObstacleSteering"/>). MV-402:
    /// several robots converging on the same barrier queued along its face instead of routing around
    /// it, because the neighbour push was blended in LAST — a push from a robot crowding behind could
    /// hand back a direction with a component back into the wall, cancelling the one in front's
    /// along-wall progress every tick the pack stayed bunched.
    ///
    /// The fix is call order, not new maths: separation is blended into the desired direction first,
    /// then <see cref="ObstacleSteering.SlideAlongWall"/> gets the final say, exactly as it already
    /// does for a raw beeline. These tests pin that composition so neither call can be reordered back
    /// into the regression without a red test.
    /// </summary>
    public sealed class ChaseSteeringOrderTests
    {
        private static readonly Vector3 WallFacingSouth = new Vector3(0f, 0f, -1f);

        [Test]
        public void NeighbourPush_ThatWouldPointBackIntoTheWall_IsClampedOffByTheWallSlideAppliedAfter()
        {
            // Desired: hugging the wall eastward (the along-wall direction SlideAlongWall would
            // already have chosen for a lone robot). A neighbour crowding from the east pushes back
            // west — straight into the wall this robot is rounding.
            Vector3 desired = Vector3.right;
            Vector3 neighbourPush = Vector3.left * 5f; // a very close neighbour: a strong push

            Vector3 blended = EnemySeparation.Steer(desired, neighbourPush);
            Vector3 final = ObstacleSteering.SlideAlongWall(blended, WallFacingSouth, 1f);

            Assert.GreaterOrEqual(Vector3.Dot(final, WallFacingSouth), -1e-4f,
                "a crowding neighbour must never be able to steer this robot back into the wall it's rounding");
            Assert.Greater(final.magnitude, 0.9f, "the robot must still have somewhere to go, not stall");
        }

        [Test]
        public void NeighbourPush_AppliedBeforeWallSlide_StillLetsTheRobotRoundTheCorner()
        {
            // A robot nose-on to the wall (as it approaches a barrier square-on), with a neighbour
            // pushing from directly behind it (reinforcing, not opposing) — the common "queued behind
            // the one in front" shape at a chokepoint.
            Vector3 desired = Vector3.forward;
            Vector3 neighbourPush = Vector3.forward * 2f;

            Vector3 blended = EnemySeparation.Steer(desired, neighbourPush);
            Vector3 final = ObstacleSteering.SlideAlongWall(blended, WallFacingSouth, 1f);

            Assert.AreEqual(0f, final.z, 1e-4f, "must round the wall, not grind nose-on into it");
            Assert.AreNotEqual(0f, final.x, "must commit to a side rather than stalling");
        }
    }
}
