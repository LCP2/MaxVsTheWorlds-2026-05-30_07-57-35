using NUnit.Framework;
using UnityEngine;
using MaxWorlds.CameraRig;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>Unit tests for the fixed-angle camera follow offset (YT-46): the pitch stays
    /// fixed while distance scales the pull-back.</summary>
    public sealed class CameraRigTests
    {
        [Test]
        public void ComputeOffset_MatchesTheOriginal72DegreeRig()
        {
            // Original rig offset was (0, 13, -4.22) ≈ 13.67 m along the 72° ray.
            var o = FixedAngleCameraRig.ComputeOffset(13.669f, 72f);
            Assert.AreEqual(0f, o.x, 1e-3);
            Assert.AreEqual(13.0f, o.y, 0.05f);
            Assert.AreEqual(-4.22f, o.z, 0.05f);
        }

        [Test]
        public void ComputeOffset_KeepsPitchWhenDistanceChanges()
        {
            // height:back ratio must equal tan(pitch) at any distance (angle unchanged).
            float tan = Mathf.Tan(72f * Mathf.Deg2Rad);
            foreach (float d in new[] { 10f, 13.669f, 20.5f, 30f })
            {
                var o = FixedAngleCameraRig.ComputeOffset(d, 72f);
                Assert.AreEqual(tan, o.y / -o.z, 1e-3, $"pitch drifted at distance {d}");
            }
        }

        [Test]
        public void ComputeOffset_FartherDistanceIsFartherAway()
        {
            float near = FixedAngleCameraRig.ComputeOffset(13.669f, 72f).magnitude;
            float far = FixedAngleCameraRig.ComputeOffset(20.5f, 72f).magnitude;
            Assert.Greater(far, near);
            Assert.AreEqual(20.5f, far, 1e-2);      // distance == offset length
            Assert.AreEqual(13.669f, near, 1e-2);
        }
    }
}
