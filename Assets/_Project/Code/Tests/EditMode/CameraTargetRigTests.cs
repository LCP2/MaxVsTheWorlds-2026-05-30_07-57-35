using NUnit.Framework;
using UnityEngine;
using MaxWorlds.CameraRig;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The camera's movement-direction look-ahead lead (MV-332): it must bias toward wherever
    /// Max is actually moving — including retreat, away from an oncoming threat — and compensate
    /// the narrow (top/bottom) axis of a landscape screen so retreating from robots spawned
    /// above/below doesn't box him against the frame.
    /// </summary>
    public sealed class CameraTargetRigTests
    {
        private const float LookAhead = 3f;
        private const float Deadzone = 0.15f;
        private const float LandscapeAspect = 16f / 9f;

        [Test]
        public void BelowTheDeadzone_NoLeadIsApplied()
        {
            var lead = CameraTargetRig.ComputeLead(
                new Vector3(0.1f, 0f, 0f), LookAhead, LandscapeAspect, Deadzone);
            Assert.AreEqual(Vector3.zero, lead);
        }

        [Test]
        public void MovingEast_LeadsFullDistanceOnTheWideAxis()
        {
            var lead = CameraTargetRig.ComputeLead(
                new Vector3(5f, 0f, 0f), LookAhead, LandscapeAspect, Deadzone);
            Assert.AreEqual(LookAhead, lead.x, 1e-4f);
            Assert.AreEqual(0f, lead.z, 1e-4f);
        }

        [Test]
        public void MovingNorth_TowardTheTopOfScreen_LeadsLessThanTheWideAxisWouldOnALandscapeScreen()
        {
            // The bug this pins: robots at the top/bottom of a landscape screen sit on the world-Z
            // axis, which the frame shows far less of than world-X. A full-distance lead there eats
            // a disproportionate slice of that little room, boxing Max against the edge.
            var lead = CameraTargetRig.ComputeLead(
                new Vector3(0f, 0f, 5f), LookAhead, LandscapeAspect, Deadzone);
            Assert.AreEqual(0f, lead.x, 1e-4f);
            Assert.Less(lead.z, LookAhead, "the narrow (top/bottom) axis should lead by less than the wide axis");
            Assert.AreEqual(LookAhead / LandscapeAspect, lead.z, 1e-4f);
        }

        [Test]
        public void RetreatingSouth_AwayFromANorthernThreat_StillGetsANegativeLead()
        {
            // AC: the bias must follow retreat too, not only advance — a robot chasing Max from the
            // north must produce lead AWAY from it (negative Z) exactly as advancing toward one would
            // produce lead toward it (positive Z). Direction alone decides it.
            var lead = CameraTargetRig.ComputeLead(
                new Vector3(0f, 0f, -5f), LookAhead, LandscapeAspect, Deadzone);
            Assert.Less(lead.z, 0f, "retreating south should lead the camera south, not sit inert");
            Assert.AreEqual(-LookAhead / LandscapeAspect, lead.z, 1e-4f);
        }

        [Test]
        public void OnASquareScreen_BothAxesLeadEqually()
        {
            // The landscape compensation is specifically for a landscape aspect — prove it isn't a
            // blanket Z reduction by checking a 1:1 screen gets no compensation at all.
            var eastLead = CameraTargetRig.ComputeLead(new Vector3(5f, 0f, 0f), LookAhead, 1f, Deadzone);
            var northLead = CameraTargetRig.ComputeLead(new Vector3(0f, 0f, 5f), LookAhead, 1f, Deadzone);
            Assert.AreEqual(eastLead.x, northLead.z, 1e-4f);
        }

        [Test]
        public void DiagonalRetreat_BiasesBothAxesTowardTheMovementDirection()
        {
            var lead = CameraTargetRig.ComputeLead(
                new Vector3(-4f, 0f, -3f), LookAhead, LandscapeAspect, Deadzone);
            Assert.Less(lead.x, 0f, "westward component should lead west");
            Assert.Less(lead.z, 0f, "southward component should lead south");
        }

        [Test]
        public void ZeroOrNegativeAspect_FallsBackToNoCompensation()
        {
            // Defensive: a degenerate aspect (e.g. Screen.height read as 0 before the window is
            // sized) must not divide by zero or invert the sign of the lead.
            var lead = CameraTargetRig.ComputeLead(new Vector3(0f, 0f, 5f), LookAhead, 0f, Deadzone);
            Assert.AreEqual(LookAhead, lead.z, 1e-4f);
        }
    }
}
