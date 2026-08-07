using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>The Blinker's flank point (MV-293) has to actually BE a flank — the right distance
    /// from Max, and off to a side rather than a re-tread of the approach it was already on.</summary>
    public sealed class BlinkerTeleportTests
    {
        [Test]
        public void LandsExactlyTheAskedDistanceFromTheTarget()
        {
            Vector3 p = BlinkerTeleport.FlankPoint(Vector3.zero, new Vector3(0f, 0f, -5f), 3f, 1f);
            Assert.AreEqual(3f, Vector3.Distance(Vector3.zero, p), 1e-4f);
        }

        [Test]
        public void DoesNotLandOnTheApproachLineItWasAlreadyOn()
        {
            // Approaching from directly behind (south of Max); a flank point must not simply be
            // further along, or closer along, that same line — that would just be a shorter walk.
            Vector3 approach = new Vector3(0f, 0f, -5f);
            Vector3 p = BlinkerTeleport.FlankPoint(Vector3.zero, approach, 3f, 1f);

            Vector3 approachDir = approach.normalized;
            Vector3 landingDir = p.normalized;
            float dot = Vector3.Dot(approachDir, landingDir);
            Assert.Less(dot, 0.9f, "the landing spot is basically still in front — that isn't a flank");
        }

        [Test]
        public void OppositeSigns_LandOnOppositeSides()
        {
            Vector3 approach = new Vector3(0f, 0f, -5f);
            Vector3 left = BlinkerTeleport.FlankPoint(Vector3.zero, approach, 3f, -1f);
            Vector3 right = BlinkerTeleport.FlankPoint(Vector3.zero, approach, 3f, 1f);

            Assert.Greater(Vector3.Distance(left, right), 1f, "the two signs should land in different spots");
        }

        [Test]
        public void AttackerStandingOnTheTarget_StillProducesAValidPoint()
        {
            // Degenerate case: no meaningful "current side" to rotate away from.
            Vector3 p = BlinkerTeleport.FlankPoint(Vector3.zero, Vector3.zero, 3f, 1f);
            Assert.AreEqual(3f, Vector3.Distance(Vector3.zero, p), 1e-3f);
        }
    }
}
