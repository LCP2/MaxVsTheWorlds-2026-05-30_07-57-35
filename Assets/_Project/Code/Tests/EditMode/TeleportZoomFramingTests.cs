using NUnit.Framework;
using UnityEngine;
using MaxWorlds.CameraRig;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Pure geometry for the MV-371 teleport camera zoom: how far back the camera must sit so a
    /// given selectable radius is fully on screen. The load-bearing claim is the linear-scaling
    /// shortcut <see cref="TeleportZoomFraming.DistanceForVisibleRadius"/> relies on — verified
    /// directly here rather than trusted from the class doc comment.
    /// </summary>
    public sealed class TeleportZoomFramingTests
    {
        private const float ShippingPitch = 64.88f; // MV-468
        private const float ShippingFov = 40f;
        private const float LandscapeAspect = 16f / 9f;

        [Test]
        public void VisibleRadius_ScalesLinearlyWithDistance()
        {
            float unit = TeleportZoomFraming.SafeVisibleRadius(1f, ShippingPitch, ShippingFov, LandscapeAspect);
            float atFive = TeleportZoomFraming.SafeVisibleRadius(5f, ShippingPitch, ShippingFov, LandscapeAspect);
            float atTwenty = TeleportZoomFraming.SafeVisibleRadius(20f, ShippingPitch, ShippingFov, LandscapeAspect);

            Assert.AreEqual(unit * 5f, atFive, 1e-3f);
            Assert.AreEqual(unit * 20f, atTwenty, 1e-3f);
        }

        [Test]
        public void VisibleRadius_IsZeroAtZeroDistance()
        {
            Assert.AreEqual(0f, TeleportZoomFraming.SafeVisibleRadius(0f, ShippingPitch, ShippingFov, LandscapeAspect));
        }

        [Test]
        public void DistanceForVisibleRadius_IsTheExactInverseOfSafeVisibleRadius()
        {
            foreach (float radius in new[] { 2f, 8f, 12f, 20f, 30f })
            {
                float distance = TeleportZoomFraming.DistanceForVisibleRadius(radius, ShippingPitch, ShippingFov, LandscapeAspect);
                float achieved = TeleportZoomFraming.SafeVisibleRadius(distance, ShippingPitch, ShippingFov, LandscapeAspect);
                Assert.AreEqual(radius, achieved, 1e-2f, $"radius {radius} round-tripped to {achieved}");
            }
        }

        [Test]
        public void PullingBackShowsMoreGround_NotLess()
        {
            float near = TeleportZoomFraming.SafeVisibleRadius(15f, ShippingPitch, ShippingFov, LandscapeAspect);
            float far = TeleportZoomFraming.SafeVisibleRadius(30f, ShippingPitch, ShippingFov, LandscapeAspect);
            Assert.Greater(far, near);
        }

        [Test]
        public void ZeroDesiredRadius_NeedsNoDistance()
        {
            Assert.AreEqual(0f, TeleportZoomFraming.DistanceForVisibleRadius(0f, ShippingPitch, ShippingFov, LandscapeAspect));
        }

        [Test]
        public void AtTheShippingLens_TheNearEdgeTowardTheCameraIsTheTightestConstraint()
        {
            // The rig sits mostly ABOVE Max and only a little back (cos 72 degrees is small), so the
            // ground just behind him — toward the camera, bottom of screen — runs out of frame sooner
            // than ahead of him or to either side. Getting this backwards would under-zoom exactly the
            // direction a player is most likely to aim a retreat blink toward.
            float pitch = ShippingPitch * Mathf.Deg2Rad;
            float sin = Mathf.Sin(pitch), cos = Mathf.Cos(pitch);
            float vHalf = ShippingFov * 0.5f * Mathf.Deg2Rad;
            float tanV = Mathf.Tan(vHalf);

            // Ray toward the bottom of screen (near/south) hits the ground closer to Max than the ray
            // toward the top of screen (far/north) does, at the same unit distance.
            Vector3 camPos = new Vector3(0f, sin, -cos);
            Vector3 nearDir = new Vector3(0f, -sin - cos * tanV, cos - sin * tanV);
            Vector3 farDir = new Vector3(0f, -sin + cos * tanV, cos + sin * tanV);

            float tNear = -camPos.y / nearDir.y;
            float tFar = -camPos.y / farDir.y;
            float nearZ = camPos.z + tNear * nearDir.z;
            float farZ = camPos.z + tFar * farDir.z;

            Assert.Less(Mathf.Abs(nearZ), Mathf.Abs(farZ),
                "the near/south edge should be the tighter of the two front-back constraints");
        }
    }
}
