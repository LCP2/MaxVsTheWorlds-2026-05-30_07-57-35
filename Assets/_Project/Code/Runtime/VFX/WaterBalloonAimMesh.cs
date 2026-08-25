using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The Water Balloon's aim visuals (v0.5 recut spec §6a, WV-241): a lob arc plus a landing circle
    /// at wherever it lands. Built from the ability's own numbers — <see cref="MaxWorlds.Weapons.AbilityTuning.WaterBalloonDistance"/>
    /// for how far, <see cref="MaxWorlds.Weapons.AbilityTuning.WaterBalloonSplashRadius"/> for how big
    /// the splash reads — the same rule <see cref="AimReticleMesh"/> set for the Water Blaster's own
    /// reticle: the picture IS the weapon, not a drawing of it, so a Water Balloon level-up (which is
    /// purely a distance increase, spec §6a) has to visibly lengthen the arc or the upgrade is invisible.
    ///
    /// The landing circle is not new geometry — <see cref="AimReticleMesh"/> already predicted it
    /// ("a Lob would draw a ring at its landing distance"): a 360° wedge IS a ring, so
    /// <see cref="BuildLandingCircle"/> is a thin wrapper over <see cref="AimReticleMesh.Build"/> rather
    /// than a second copy of the same fill/edge/fade ramp.
    ///
    /// Pure — no scene, no materials, no clock. Whoever wires the joystick (WV-240) positions/rotates
    /// these by the aim direction and moves the landing circle's centre; this only answers "what shape".
    /// </summary>
    public static class WaterBalloonAimMesh
    {
        /// <summary>The arc's apex height, as a fraction of the throw distance — a lob has to visibly
        /// rise, not skim the lawn like a direct-fire shot.</summary>
        public const float ApexHeightFraction = 0.35f;

        /// <summary>Half the ribbon's width, world metres — thin enough to read as a trajectory line,
        /// not a wall.</summary>
        public const float HalfWidth = 0.06f;

        /// <summary>
        /// Build the lob arc: a flat ribbon tracing a parabola from Max's feet (local origin) out to
        /// the landing point at (0, 0, <paramref name="distance"/>), laid flat in local XZ pointing +Z
        /// the same way <see cref="AimReticleMesh"/> does, so the owner only has to yaw it toward the
        /// aim direction.
        /// </summary>
        /// <summary><paramref name="reuse"/> (MV-545): see <see cref="AimReticleMesh.Build"/>'s own doc
        /// comment — pass the previous frame's mesh back in to overwrite it in place instead of leaking
        /// a fresh one every drag frame.</summary>
        public static Mesh Build(float distance, int segments = 20, Mesh reuse = null)
        {
            distance = Mathf.Max(0.01f, distance);
            segments = Mathf.Max(2, segments);

            var verts = new List<Vector3>((segments + 1) * 2);
            var cols = new List<Color>((segments + 1) * 2);
            var tris = new List<int>(segments * 6);

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float alpha = Mathf.Lerp(0.35f, 0.9f, t);   // brightens toward the landing point

                var center = LocalPositionOnArc(distance, t);
                verts.Add(center + Vector3.left * HalfWidth);
                verts.Add(center + Vector3.right * HalfWidth);
                cols.Add(new Color(1f, 1f, 1f, alpha));
                cols.Add(new Color(1f, 1f, 1f, alpha));
            }

            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = i * 2 + 1, c = (i + 1) * 2, d = (i + 1) * 2 + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }

            var mesh = reuse != null ? reuse : new Mesh();
            mesh.name = $"WaterBalloonArc {distance:0.0}m";
            if (reuse != null) mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The landing circle: the splash's true footprint, drawn as a ring at the arc's landing point
        /// — literally <see cref="AimReticleMesh"/>'s wedge fully opened to 180° half-angle, so a Water
        /// Balloon's ring uses the exact same fill/edge/fade language as the blaster's own reticle
        /// rather than a second hand-tuned ramp.
        /// </summary>
        public static Mesh BuildLandingCircle(float radius, int segments = 40, Mesh reuse = null) =>
            AimReticleMesh.Build(radius, 180f, segments, reuse);

        /// <summary>
        /// The same parabola <see cref="Build"/> draws, evaluated at a single fraction <paramref name="t"/>
        /// of the throw (0 = Max's feet, 1 = the landing point) — local space, +Z forward, flat XZ.
        ///
        /// Shared with <see cref="WaterBalloonThrowVfx"/>, which actually flies a body along this exact
        /// curve once the balloon is thrown (MV-334): the preview arc and the real flight must trace the
        /// same shape, or a player who aimed along the ribbon would watch the balloon land somewhere the
        /// picture never promised.
        /// </summary>
        public static Vector3 LocalPositionOnArc(float distance, float t)
        {
            distance = Mathf.Max(0.01f, distance);
            t = Mathf.Clamp01(t);
            float apexHeight = distance * ApexHeightFraction;
            float z = t * distance;
            float y = 4f * apexHeight * t * (1f - t);   // parabola, 0 at both ends, peak at t=0.5
            return new Vector3(0f, y, z);
        }
    }
}
