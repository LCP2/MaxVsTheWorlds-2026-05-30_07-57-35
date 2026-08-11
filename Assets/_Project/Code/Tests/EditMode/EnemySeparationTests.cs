using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>The maths that keeps a chasing pack from clumping into a single stack (MV-321). A
    /// robot pressed shoulder-to-shoulder against its neighbours, with nothing steering it away
    /// until they physically collide, is the failure this guards.</summary>
    public sealed class EnemySeparationTests
    {
        [Test]
        public void NoNeighbours_ProducesNoPush()
        {
            Vector3 push = EnemySeparation.Push(Vector3.zero, new List<Vector3>(), 2f);
            Assert.AreEqual(Vector3.zero, push);
        }

        [Test]
        public void NeighbourBeyondMinDistance_ProducesNoPush()
        {
            var others = new List<Vector3> { new Vector3(5f, 0f, 0f) };
            Vector3 push = EnemySeparation.Push(Vector3.zero, others, 2f);
            Assert.AreEqual(Vector3.zero, push);
        }

        [Test]
        public void NeighbourInsideMinDistance_PushesDirectlyAway()
        {
            // Neighbour 1m east; self should be pushed west.
            var others = new List<Vector3> { new Vector3(1f, 0f, 0f) };
            Vector3 push = EnemySeparation.Push(Vector3.zero, others, 2f);

            Assert.Greater(push.magnitude, 0f, "a crowding neighbour must produce some push");
            Assert.Less(push.x, 0f, "should push away from the neighbour, not toward it");
            Assert.AreEqual(0f, push.z, 1e-4f);
        }

        [Test]
        public void CloserNeighbour_PushesHarder()
        {
            var far = new List<Vector3> { new Vector3(1.8f, 0f, 0f) };
            var near = new List<Vector3> { new Vector3(0.2f, 0f, 0f) };

            Vector3 pushFar = EnemySeparation.Push(Vector3.zero, far, 2f);
            Vector3 pushNear = EnemySeparation.Push(Vector3.zero, near, 2f);

            Assert.Greater(pushNear.magnitude, pushFar.magnitude,
                "a robot almost on top of you should push harder than one just inside range");
        }

        [Test]
        public void MultipleCrowdingNeighbours_Accumulate()
        {
            // Both neighbours crowding from roughly the same side (not opposite each other, which
            // would cancel out and defeat the point of this test).
            var one = new List<Vector3> { new Vector3(1f, 0f, 0f) };
            var two = new List<Vector3> { new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f) };

            Vector3 pushOne = EnemySeparation.Push(Vector3.zero, one, 2f);
            Vector3 pushTwo = EnemySeparation.Push(Vector3.zero, two, 2f);

            Assert.Greater(pushTwo.magnitude, pushOne.magnitude,
                "a second crowding robot should add to the push, not be ignored");
        }

        [Test]
        public void PushIsFlat_EvenWhenNeighbourHeightDiffers()
        {
            var others = new List<Vector3> { new Vector3(1f, 3f, 0f) }; // way above/below on Y
            Vector3 push = EnemySeparation.Push(Vector3.zero, others, 2f);
            Assert.AreEqual(0f, push.y, 1e-4f, "separation is a ground-plane steering term, not vertical");
        }

        [Test]
        public void CoincidentNeighbour_IsIgnored_NotANaN()
        {
            // Exactly the same spot: no well-defined direction to push in, so skip it rather than
            // dividing by zero.
            var others = new List<Vector3> { Vector3.zero };
            Vector3 push = EnemySeparation.Push(Vector3.zero, others, 2f);
            Assert.AreEqual(Vector3.zero, push);
        }

        [Test]
        public void Steer_WithNoPush_LeavesDesiredDirectionUnchanged()
        {
            Vector3 desired = new Vector3(1f, 0f, 1f).normalized;
            Vector3 result = EnemySeparation.Steer(desired, Vector3.zero);
            Assert.AreEqual(desired, result);
        }

        [Test]
        public void Steer_BlendsPushIntoDesiredDirection_AndStaysUnitLength()
        {
            Vector3 desired = Vector3.forward;
            Vector3 push = new Vector3(1f, 0f, 0f);
            Vector3 result = EnemySeparation.Steer(desired, push);

            Assert.AreEqual(1f, result.magnitude, 1e-4f);
            Assert.Greater(result.x, 0f, "the push should bend the steering toward it, not cancel it");
            Assert.Greater(result.z, 0f, "but the robot should still be making progress toward its goal");
        }

        [Test]
        public void Steer_WhenPushExactlyCancelsDesired_FallsBackToDesired_NotZero()
        {
            Vector3 desired = Vector3.forward;
            Vector3 push = -Vector3.forward;
            Vector3 result = EnemySeparation.Steer(desired, push);

            Assert.AreEqual(desired, result, "a stalled chaser is the bug — it must still have a direction to move in");
        }
    }
}
