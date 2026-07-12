using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>Unit tests for the Water Blaster's spray cone hit-test (YT-64).</summary>
    public sealed class SprayHitTests
    {
        private static readonly Vector3 Origin = Vector3.zero;
        private static readonly Vector3 Aim = Vector3.forward; // +Z

        [Test]
        public void HitsStraightAheadInRange()
        {
            Assert.IsTrue(SprayHit.InCone(Origin, Aim, new Vector3(0, 0, 4f), 6f, 35f));
        }

        [Test]
        public void MissesBeyondRange()
        {
            Assert.IsFalse(SprayHit.InCone(Origin, Aim, new Vector3(0, 0, 8f), 6f, 35f));
        }

        [Test]
        public void MissesBehind()
        {
            Assert.IsFalse(SprayHit.InCone(Origin, Aim, new Vector3(0, 0, -3f), 6f, 35f));
        }

        [Test]
        public void HitsInsideTheArcButNotOutside()
        {
            // At 4 m ahead, ±35° covers ~±2.8 m of lateral offset (tan 35° ≈ 0.70).
            Assert.IsTrue(SprayHit.InCone(Origin, Aim, new Vector3(2.0f, 0, 4f), 6f, 35f), "inside the arc");
            Assert.IsFalse(SprayHit.InCone(Origin, Aim, new Vector3(4.0f, 0, 4f), 6f, 35f), "outside the arc");
        }

        [Test]
        public void WiderConeCatchesMore()
        {
            var side = new Vector3(4.0f, 0, 4f); // ~45° off-axis
            Assert.IsFalse(SprayHit.InCone(Origin, Aim, side, 6f, 35f));
            Assert.IsTrue(SprayHit.InCone(Origin, Aim, side, 6f, 50f));
        }

        [Test]
        public void PointBlankAlwaysHits()
        {
            Assert.IsTrue(SprayHit.InCone(Origin, Aim, new Vector3(0.01f, 0, 0.01f), 6f, 35f));
        }

        [Test]
        public void IgnoresHeightDifference()
        {
            // A robot standing a little higher than the muzzle is still in the planar cone.
            Assert.IsTrue(SprayHit.InCone(Origin, Aim, new Vector3(0, 1.5f, 4f), 6f, 35f));
        }
    }
}
