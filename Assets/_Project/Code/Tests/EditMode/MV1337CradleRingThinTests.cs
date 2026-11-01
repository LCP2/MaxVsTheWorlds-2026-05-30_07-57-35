using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Factories;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1337 — Lee (iPhone, TestFlight v0.23.0, World 5): "Big red haze." The Assembly Cradle's
    /// proximity ring (MV-1297) was built with <see cref="GroundRing.Create"/> — a single quad using
    /// the soft <see cref="VfxMaterials.Ring"/> texture made for small telegraphs. Scaled to
    /// <see cref="AssemblyCradle.ProximityRadius"/>'s 10 m radius (20 m across), that texture's soft
    /// falloff becomes a blurred band metres wide — the haze in Lee's screenshot. Fails to compile on
    /// base commit 4af831f (quoted in this ticket's fix comment): no <c>DashedRingMesh</c>,
    /// <c>AssemblyCradle.ProximityRingLineWidth</c>, or <c>AssemblyCradle.ProximityRingDashCount</c>
    /// exist there.
    ///
    /// Tier 2 (resolved value): reads the actual vertex radii <see cref="DashedRingMesh.Build"/>
    /// produces for the cradle's own authored radius/width/dash count — never a literal re-read of an
    /// authored constant against itself.
    /// </summary>
    // Guards MV-1337
    public sealed class MV1337CradleRingThinTests
    {
        [Test]
        public void TheProximityRingsBandIsThin_NotAMetresWideHaze()
        {
            Mesh mesh = DashedRingMesh.Build(
                AssemblyCradle.ProximityRadius,
                AssemblyCradle.ProximityRingLineWidth,
                AssemblyCradle.ProximityRingDashCount);
            try
            {
                float minR = float.MaxValue, maxR = 0f;
                foreach (Vector3 v in mesh.vertices)
                {
                    float r = new Vector2(v.x, v.z).magnitude;
                    minR = Mathf.Min(minR, r);
                    maxR = Mathf.Max(maxR, r);
                }

                float band = maxR - minR;
                Assert.AreEqual(AssemblyCradle.ProximityRingLineWidth, band, 0.02f,
                    "the ring's own geometry is not a thin band at the cradle's authored width");
                Assert.Less(band, 0.5f,
                    "the ring reads as a blurred metres-wide haze, not a thin line — MV-1337's own defect");

                // Every vertex must sit ON the ring at ProximityRadius, not sprayed across the arena —
                // a dashed annulus whose dashes drifted inward/outward would read as a wobble, not a
                // ring at the cradle's own authored radius.
                Assert.AreEqual(AssemblyCradle.ProximityRadius, (minR + maxR) * 0.5f, 0.05f,
                    "the ring is not centred on the cradle's own proximity radius");
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
