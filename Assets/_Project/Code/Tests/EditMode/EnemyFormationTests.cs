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

        [Test]
        public void DoorwayApproach_FansFiveDistinctRobots_AtRange_ThenConvergesUpClose()
        {
            // MV-449: at 6m from a doorway mouth, five robots on distinct lanes should spread out
            // (not queue single-file); at 1m they should all be converging on the mouth itself.
            Vector3 mouth = new Vector3(0f, 0f, 6f);
            Vector3 from = Vector3.zero;
            int[] ids = { 0, 1, 2, 3, 4 }; // lanes -1, -0.5, 0, 0.5, 1 (Bias)

            var farPoints = new Vector3[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                farPoints[i] = EnemyFormation.ApproachPoint(mouth, from, ids[i],
                    EnemyFormation.DoorwaySpread, EnemyFormation.DoorwayFullSpreadAt);
            }

            for (int i = 0; i < ids.Length; i++)
            for (int j = i + 1; j < ids.Length; j++)
            {
                if (!Mathf.Approximately(EnemyFormation.Bias(ids[i]), EnemyFormation.Bias(ids[j])))
                    Assert.Greater(Vector3.Distance(farPoints[i], farPoints[j]), 0.01f,
                        $"robots {ids[i]} and {ids[j]} should land at distinct approach points");
            }

            float lateralSpread = 0f;
            foreach (Vector3 p in farPoints)
                lateralSpread = Mathf.Max(lateralSpread, Mathf.Abs(Vector3.Dot(p - mouth, Vector3.right)));

            Assert.LessOrEqual(lateralSpread, EnemyFormation.DoorwaySpread,
                "no robot should swing past DoorwaySpread off the direct line");

            Vector3 nearMouth = new Vector3(0f, 0f, 6f);
            Vector3 nearFrom = new Vector3(0f, 0f, 5f); // same approach direction, 1m out from the mouth
            foreach (int id in ids)
            {
                Vector3 closePoint = EnemyFormation.ApproachPoint(nearMouth, nearFrom, id,
                    EnemyFormation.DoorwaySpread, EnemyFormation.DoorwayFullSpreadAt);
                Assert.Less(Vector3.Distance(closePoint, nearMouth), 0.3f,
                    $"robot {id} should be within 0.3m of the mouth at 1m out");
            }
        }

        [Test]
        public void DoorwayOverload_UsesGivenSpread_NotTheFlankerAwareDefaults()
        {
            // MV-449: at a doorway everyone funnels the same way regardless of IsFlanker — the
            // doorway overload must not silently fall back to Spread/FlankSpread.
            Assert.IsTrue(EnemyFormation.IsFlanker(6));
            Assert.IsFalse(EnemyFormation.IsFlanker(11));
            Assert.AreEqual(EnemyFormation.Bias(6), EnemyFormation.Bias(11));

            Vector3 goal = new Vector3(0f, 0f, 8f);
            Vector3 from = Vector3.zero;

            Vector3 flankerDoorway = EnemyFormation.ApproachPoint(goal, from, 6,
                EnemyFormation.DoorwaySpread, EnemyFormation.DoorwayFullSpreadAt);
            Vector3 ordinaryDoorway = EnemyFormation.ApproachPoint(goal, from, 11,
                EnemyFormation.DoorwaySpread, EnemyFormation.DoorwayFullSpreadAt);

            Assert.AreEqual(Vector3.Distance(flankerDoorway, goal), Vector3.Distance(ordinaryDoorway, goal), 0.001f,
                "a flanker and an ordinary robot on the same lane must swing identically at a doorway");
        }
    }
}
