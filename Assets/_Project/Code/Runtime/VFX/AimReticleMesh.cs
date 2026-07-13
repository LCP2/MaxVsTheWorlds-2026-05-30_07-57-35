using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The shape of a gadget's reach (YT-84), built from the gadget's actual numbers.
    ///
    /// It is a MESH rather than a texture on a quad, and that is the whole point of the ticket. A
    /// sprite of a cone is a picture of a weapon; a mesh generated from <c>range</c> and
    /// <c>coneHalfAngle</c> IS the weapon. The Water Blaster is a Spray, so it draws a wide arc; a
    /// Beam would draw a long thin one and a Lob a ring at its landing distance — and none of that
    /// needs new art, because the indicator was never a drawing. The moment a picture and a stat
    /// disagree, the picture is a lie the player has been taught to trust, and they will walk into
    /// range they don't have.
    ///
    /// Laid flat in local XZ pointing +Z, so the owner only has to yaw it. Pure — no scene, no
    /// materials, no clock.
    /// </summary>
    public static class AimReticleMesh
    {
        /// <summary>Where the fill sits, as a fraction of range: most of the wedge is a faint wash.</summary>
        private const float FillRing = 0.82f;

        /// <summary>How far past the edge the fade runs. Without it the outer boundary is a hard
        /// bright line, and a hard bright line at this camera aliases into a dashed one.</summary>
        private const float FadeRing = 1.03f;

        /// <summary>
        /// Build the wedge. <paramref name="halfAngleDeg"/> is HALF the total spread, matching
        /// <c>SprayHit.InCone</c>, so the drawing and the hit test read the same number the same way
        /// — a reticle that showed the full angle where the code meant the half would have promised
        /// twice the coverage the weapon has.
        /// </summary>
        public static Mesh Build(float range, float halfAngleDeg, int segments = 28)
        {
            range = Mathf.Max(0.01f, range);
            halfAngleDeg = Mathf.Clamp(halfAngleDeg, 1f, 180f);
            segments = Mathf.Max(3, segments);

            var verts = new List<Vector3>((segments + 1) * 3 + 1);
            var cols = new List<Color>((segments + 1) * 3 + 1);
            var tris = new List<int>(segments * 12);

            // Alpha lives in the vertex colours, so ONE shared material draws every weapon's reticle
            // whatever its shape. These are the relative weights; the material's own alpha scales the
            // lot up when the player actually aims (see AimReticle), which is what lets the thing be
            // a whisper while idle and still legible mid-fight without swapping anything.
            //
            // The outer boundary is the loudest part on purpose: the fill is nice, but the ONE fact
            // the player needs is where their reach ends.
            (float radius, float alpha)[] rings =
            {
                (FillRing * range, 0.30f),   // faint wash across the body of the wedge
                (range,            1.00f),   // the boundary — this is the information
                (FadeRing * range, 0f),      // feather it out, or the edge aliases into a dashed line
            };

            verts.Add(Vector3.zero);
            cols.Add(new Color(1f, 1f, 1f, 0.18f));   // dimmest at Max's feet: he can see himself

            int ringStart = verts.Count;
            foreach (var (r, a) in rings)
            {
                for (int i = 0; i <= segments; i++)
                {
                    float t = (float)i / segments;                      // 0..1 across the arc
                    float deg = Mathf.Lerp(-halfAngleDeg, halfAngleDeg, t);
                    float rad = deg * Mathf.Deg2Rad;
                    verts.Add(new Vector3(Mathf.Sin(rad) * r, 0f, Mathf.Cos(rad) * r));
                    cols.Add(new Color(1f, 1f, 1f, a));
                }
            }

            int perRing = segments + 1;
            int ringA = ringStart;                 // the fill ring
            int ringB = ringStart + perRing;       // the bright edge
            int ringC = ringStart + perRing * 2;   // the fade-out

            // Centre fan out to the fill ring.
            for (int i = 0; i < segments; i++)
            {
                tris.Add(0); tris.Add(ringA + i + 1); tris.Add(ringA + i);
            }

            // Bands between the rings.
            for (int band = 0; band < 2; band++)
            {
                int inner = band == 0 ? ringA : ringB;
                int outer = band == 0 ? ringB : ringC;
                for (int i = 0; i < segments; i++)
                {
                    tris.Add(inner + i); tris.Add(inner + i + 1); tris.Add(outer + i);
                    tris.Add(outer + i); tris.Add(inner + i + 1); tris.Add(outer + i + 1);
                }
            }

            var mesh = new Mesh { name = $"AimReticle {range:0.0}m {halfAngleDeg:0}deg" };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>How far the drawn wedge actually reaches — the fade runs a little past the
        /// weapon's range, and a test needs to know that's on purpose.</summary>
        public static float DrawnReach(float range) => range * FadeRing;
    }
}
