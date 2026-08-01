using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The Water Balloon's arc + landing circle (WV-241, spec §6a). The one thing that has to be true:
    /// Water Balloon's ENTIRE upgrade is throw distance, so a level-up that doesn't visibly lengthen the
    /// arc is an upgrade the player can't see they got.
    /// </summary>
    public sealed class WaterBalloonAimMeshTests
    {
        private static void Free(Mesh m) => Object.DestroyImmediate(m);

        [Test]
        public void TheArcReachesExactlyAsFarAsTheThrow()
        {
            var m = WaterBalloonAimMesh.Build(6f);
            try
            {
                Assert.AreEqual(6f, m.bounds.max.z, 0.05f,
                    "the arc's landing point does not match the throw distance");
            }
            finally { Free(m); }
        }

        [Test]
        public void ALevelUpDrawsAVisiblyLongerArc()
        {
            float l1 = AbilityTuning.WaterBalloonDistance(1, 4f, 1.5f);
            float l3 = AbilityTuning.WaterBalloonDistance(3, 4f, 1.5f);
            var near = WaterBalloonAimMesh.Build(l1);
            var far = WaterBalloonAimMesh.Build(l3);
            try
            {
                Assert.Greater(far.bounds.max.z, near.bounds.max.z,
                    "levelling Water Balloon didn't lengthen the drawn arc — the only stat this " +
                    "ability upgrades is invisible");
            }
            finally { Free(near); Free(far); }
        }

        [Test]
        public void TheArcActuallyRisesInTheAir_ItIsALobNotADirectShot()
        {
            var m = WaterBalloonAimMesh.Build(8f);
            try
            {
                Assert.Greater(m.bounds.max.y, 0.5f, "the arc never leaves the ground — it reads as a slide, not a lob");
            }
            finally { Free(m); }
        }

        [Test]
        public void TheArcStartsAndEndsAtGroundLevel()
        {
            var m = WaterBalloonAimMesh.Build(5f);
            try
            {
                var verts = m.vertices;
                float startY = 0f, endY = 0f;
                foreach (var v in verts)
                {
                    if (Mathf.Abs(v.z) < 0.01f) startY = Mathf.Max(startY, v.y);
                    if (Mathf.Abs(v.z - 5f) < 0.01f) endY = Mathf.Max(endY, v.y);
                }
                Assert.AreEqual(0f, startY, 0.01f, "the throw doesn't start at Max's feet");
                Assert.AreEqual(0f, endY, 0.01f, "the arc doesn't land at ground level");
            }
            finally { Free(m); }
        }

        [Test]
        public void TheLandingCircleIsTheSplashsTrueRadius()
        {
            float radius = AbilityTuning.WaterBalloonSplashRadius(0.55f, 2f);
            var m = WaterBalloonAimMesh.BuildLandingCircle(radius);
            try
            {
                Assert.AreEqual(AimReticleMesh.DrawnReach(radius), m.bounds.max.z, 0.05f,
                    "the landing circle's edge doesn't match the splash's real radius");
                // Fully opened (180°) — a ring reaches equally far on every side, not just forward.
                Assert.AreEqual(m.bounds.max.z, -m.bounds.min.z, 0.05f,
                    "the landing circle isn't a full ring — it should reach as far behind as ahead");
                Assert.AreEqual(m.bounds.max.x, -m.bounds.min.x, 0.05f,
                    "the landing circle isn't symmetric left/right");
            }
            finally { Free(m); }
        }

        [Test]
        public void ABiggerSplashDrawsABiggerLandingCircle()
        {
            var small = WaterBalloonAimMesh.BuildLandingCircle(0.6f);
            var big = WaterBalloonAimMesh.BuildLandingCircle(1.4f);
            try
            {
                Assert.Greater(big.bounds.size.x, small.bounds.size.x * 1.5f,
                    "the splash radius changed and the ring didn't follow it");
            }
            finally { Free(small); Free(big); }
        }

        [Test]
        public void ADegenerateThrowDoesNotProduceABrokenMesh()
        {
            foreach (float distance in new[] { 0f, -5f, 1e6f })
            {
                var m = WaterBalloonAimMesh.Build(distance);
                try
                {
                    Assert.Greater(m.vertexCount, 0);
                    foreach (var v in m.vertices)
                        Assert.IsFalse(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z),
                            $"distance {distance} produced NaN geometry");
                }
                finally { Free(m); }
            }
        }
    }
}
