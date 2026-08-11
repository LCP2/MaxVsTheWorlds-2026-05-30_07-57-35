using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>The maths behind how a pack arrives (YT-93) and, for a stable minority, flanks
    /// instead of leaning straight in (MV-322). Pure maths, no transforms — what the pack does is a
    /// thing a test can assert.</summary>
    public sealed class EnemyFormationTests
    {
        [Test]
        public void IsFlanker_IsStablePerRobot()
        {
            Assert.AreEqual(EnemyFormation.IsFlanker(17), EnemyFormation.IsFlanker(17),
                "the same robot must not flip-flop between flanking and not");
        }

        [Test]
        public void IsFlanker_IsOnlyASubset_NotEveryRobot()
        {
            bool sawFlanker = false, sawNonFlanker = false;
            for (int id = 0; id < 30; id++)
            {
                if (EnemyFormation.IsFlanker(id)) sawFlanker = true;
                else sawNonFlanker = true;
            }

            Assert.IsTrue(sawFlanker, "nobody flanks — the ticket asked for SOME robots to");
            Assert.IsTrue(sawNonFlanker, "everybody flanks — the rest of the pack should still lean in");
        }

        [Test]
        public void FlankerApproach_SwingsWiderThanTheOrdinaryFan_AtTheSameLaneAndRange()
        {
            // id 6 is a flanker, id 11 is not, and both land in the same lane (id % 5 == 1) — so any
            // difference in how far they swing off the direct line is the flank behaviour, not a
            // different lane pick.
            Assert.IsTrue(EnemyFormation.IsFlanker(6));
            Assert.IsFalse(EnemyFormation.IsFlanker(11));
            Assert.AreEqual(EnemyFormation.Bias(6), EnemyFormation.Bias(11));

            Vector3 goal = new Vector3(0f, 0f, 8f);
            Vector3 from = Vector3.zero;

            Vector3 flankerPoint = EnemyFormation.ApproachPoint(goal, from, 6);
            Vector3 ordinaryPoint = EnemyFormation.ApproachPoint(goal, from, 11);

            float flankerOffset = Vector3.Distance(flankerPoint, goal);
            float ordinaryOffset = Vector3.Distance(ordinaryPoint, goal);

            Assert.Greater(flankerOffset, ordinaryOffset,
                "a flanker should swing noticeably wider than the ordinary fan's lean");
        }

        [Test]
        public void FlankerApproach_StillCommitsToTheGoal_UpClose()
        {
            // Right on top of the goal, even a flanker's swing collapses to nothing — a flank is an
            // approach shape, not a permanent refusal to close.
            Vector3 point = EnemyFormation.ApproachPoint(Vector3.forward * 0.2f, Vector3.zero, 6);
            Assert.Less(Vector3.Distance(point, Vector3.forward * 0.2f), 0.1f);
        }

        [Test]
        public void FlankerApproach_KeepsTheSameSideAsItsLane()
        {
            // Flanking widens the swing; it must not flip which side of the goal a robot commits to.
            Vector3 goal = new Vector3(0f, 0f, 8f);
            Vector3 from = Vector3.zero;

            Vector3 ordinaryPoint = EnemyFormation.ApproachPoint(goal, from, 11);
            Vector3 flankerPoint = EnemyFormation.ApproachPoint(goal, from, 6);

            float ordinarySide = Vector3.Dot(ordinaryPoint - goal, Vector3.right);
            float flankerSide = Vector3.Dot(flankerPoint - goal, Vector3.right);

            Assert.AreEqual(Mathf.Sign(ordinarySide), Mathf.Sign(flankerSide),
                "widening the swing should not send the robot around the opposite side");
        }
    }
}
