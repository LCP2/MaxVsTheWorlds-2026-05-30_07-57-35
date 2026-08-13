using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>The Blinker squad jump's landing point (MV-366) has two hard rules a solo blink never
    /// needed: never on the north/south axis through Max, and always closer to Max than whatever
    /// robots are not in the jump.</summary>
    public sealed class BlinkerSquadTeleportTests
    {
        private static float AngleFromNorthSouthAxis(Vector3 targetPos, Vector3 landing)
        {
            Vector3 dir = landing - targetPos;
            dir.y = 0f;
            float fromNorth = Mathf.Abs(Vector3.SignedAngle(Vector3.forward, dir.normalized, Vector3.up));
            return Mathf.Min(fromNorth, 180f - fromNorth); // distance to the NEAREST of north/south
        }

        [Test]
        public void GroupFlankPoint_NeverLandsInsideTheNorthSouthDeadZone()
        {
            // Sweep a wide spread of approach directions, including ones that would land the raw
            // solo-style flank point right on (or very near) the north/south axis.
            for (int deg = 0; deg < 360; deg += 15)
            {
                Vector3 approach = Quaternion.AngleAxis(deg, Vector3.up) * Vector3.forward * 5f;
                foreach (float sign in new[] { -1f, 1f })
                {
                    Vector3 p = BlinkerSquadTeleport.GroupFlankPoint(Vector3.zero, approach, 3f, sign);
                    float clearance = AngleFromNorthSouthAxis(Vector3.zero, p);
                    Assert.GreaterOrEqual(clearance, BlinkerSquadTeleport.NorthSouthExclusionDeg - 0.01f,
                        $"deg={deg} sign={sign} landed too close to due north/south");
                }
            }
        }

        [Test]
        public void GroupFlankPoint_LandsExactlyTheAskedDistanceFromTheTarget()
        {
            Vector3 p = BlinkerSquadTeleport.GroupFlankPoint(Vector3.zero, new Vector3(0f, 0f, -5f), 3f, 1f);
            Assert.AreEqual(3f, Vector3.Distance(Vector3.zero, p), 1e-3f);
        }

        [Test]
        public void CanLandCloserThanPack_FalseWhenThePackIsAlreadyRightOnTopOfMax()
        {
            Assert.IsFalse(BlinkerSquadTeleport.CanLandCloserThanPack(1f));
        }

        [Test]
        public void CanLandCloserThanPack_TrueWhenThePackIsWellSpreadOut()
        {
            Assert.IsTrue(BlinkerSquadTeleport.CanLandCloserThanPack(20f));
        }

        [Test]
        public void LandingDistance_IsStrictlyNearerThanThePack()
        {
            float distance = BlinkerSquadTeleport.LandingDistance(nearestPackDistance: 10f, preferredDistance: 1.9f);
            Assert.Less(distance, 10f);
            Assert.AreEqual(1.9f, distance, 1e-4f, "preferred range should win when the pack leaves room for it");
        }

        [Test]
        public void LandingDistance_ClampsToThePackWhenPreferredRangeWouldOvershoot()
        {
            // Pack's nearest robot is only just outside the minimum landing distance — the squad must
            // still land closer than it, even though that's tighter than its preferred attack range.
            float distance = BlinkerSquadTeleport.LandingDistance(nearestPackDistance: 3f, preferredDistance: 1.9f);
            Assert.Less(distance, 3f);
            Assert.GreaterOrEqual(distance, BlinkerSquadTeleport.MinLandingDistance);
        }

        [Test]
        public void NearestPackDistance_IgnoresParticipantsAndInfinityWhenNoneRemain()
        {
            var distances = new List<float> { 2f, 5f, 8f };
            var participant = new List<bool> { true, false, true };
            Assert.AreEqual(5f, BlinkerSquadTeleport.NearestPackDistance(distances, participant));

            var allParticipants = new List<bool> { true, true, true };
            Assert.IsTrue(float.IsPositiveInfinity(BlinkerSquadTeleport.NearestPackDistance(distances, allParticipants)));
        }
    }
}
