using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// A thin, dashed annulus (MV-1337) — flat in local XZ, built from an explicit inner/outer
    /// radius rather than a soft quad texture.
    ///
    /// <see cref="GroundRing"/>'s single quad + <see cref="VfxMaterials.Ring"/> texture was built for
    /// small telegraphs a couple of metres across; its soft falloff is a fixed FRACTION of the quad's
    /// size, so scaling that quad up to a 10+ m radius turns the falloff into a blurred band metres
    /// wide — the Assembly Cradle's "big red haze" (Lee, TestFlight v0.23.0). A dashed annulus has a
    /// real geometric width (outer radius minus inner radius) that does not grow with the ring's own
    /// radius, so it reads as a thin line at any size.
    ///
    /// Pure — no scene, no materials, no clock. Same shape as <see cref="AimReticleMesh"/>.
    /// </summary>
    public static class DashedRingMesh
    {
        /// <summary>Build a standalone mesh. <paramref name="lineWidth"/> is the full radial width of
        /// the band (outer radius minus inner radius); <paramref name="dashCount"/> dashes are spaced
        /// evenly around the circle with a gap between each.</summary>
        public static Mesh Build(float radius, float lineWidth, int dashCount, float dashFraction = 0.6f, int segmentsPerDash = 2)
        {
            var mesh = new Mesh { name = $"DashedRing {radius:0.0}m x{dashCount}" };
            BuildInto(mesh, radius, lineWidth, dashCount, dashFraction, segmentsPerDash);
            return mesh;
        }

        /// <summary>Same ring as <see cref="Build"/>, written into an already-existing
        /// <paramref name="target"/> mesh instead of allocating a new one — same reasoning as
        /// <see cref="AimReticleMesh.BuildInto"/>: a caller that only ever moves with the same
        /// radius/width/dash count (<see cref="MaxWorlds.Factories.AssemblyCradle"/>'s proximity ring,
        /// shown every frame while alive) can reuse one retained mesh forever.</summary>
        public static void BuildInto(Mesh target, float radius, float lineWidth, int dashCount, float dashFraction = 0.6f, int segmentsPerDash = 2)
        {
            radius = Mathf.Max(0.01f, radius);
            lineWidth = Mathf.Max(0.001f, lineWidth);
            dashCount = Mathf.Max(1, dashCount);
            dashFraction = Mathf.Clamp01(dashFraction);
            segmentsPerDash = Mathf.Max(1, segmentsPerDash);

            float inner = radius - lineWidth * 0.5f;
            float outer = radius + lineWidth * 0.5f;
            float stepAngle = Mathf.PI * 2f / dashCount;
            float dashAngle = stepAngle * dashFraction;

            var verts = new List<Vector3>(dashCount * (segmentsPerDash + 1) * 2);
            var tris = new List<int>(dashCount * segmentsPerDash * 6);

            for (int d = 0; d < dashCount; d++)
            {
                float centreAngle = stepAngle * d;
                float startAngle = centreAngle - dashAngle * 0.5f;
                int baseIndex = verts.Count;

                for (int s = 0; s <= segmentsPerDash; s++)
                {
                    float t = (float)s / segmentsPerDash;
                    float angle = startAngle + dashAngle * t;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);
                    verts.Add(new Vector3(cos * inner, 0f, sin * inner));
                    verts.Add(new Vector3(cos * outer, 0f, sin * outer));
                }

                for (int s = 0; s < segmentsPerDash; s++)
                {
                    int i0 = baseIndex + s * 2;       // inner, near edge
                    int i1 = i0 + 1;                  // outer, near edge
                    int i2 = i0 + 2;                  // inner, far edge
                    int i3 = i0 + 3;                  // outer, far edge
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i1); tris.Add(i2); tris.Add(i3);
                }
            }

            target.Clear();
            target.SetVertices(verts);
            target.SetTriangles(tris, 0);
            target.RecalculateBounds();
        }
    }
}
