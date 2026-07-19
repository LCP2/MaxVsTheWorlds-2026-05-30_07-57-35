using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The coil a dead robot throws out (YT-101) — a helical tube, built once and shared by every
    /// spring in the game.
    ///
    /// It has to be a real coil. The cheap version of this effect is a stretched cylinder, and a
    /// stretched cylinder at gameplay zoom is a grey tic-tac: it reads as "a bit fell off", which is
    /// the one thing this effect must not read as. The whole joke is that the robot's INSIDES came
    /// out, and a spring is only funny if you can tell it is a spring. The coil silhouette is the
    /// entire payload, so that is what gets the triangles.
    ///
    /// Built in a unit-ish local space — roughly 1 m tall, 1 m across — and scaled down per spring by
    /// the transform, so one mesh serves every size. Authored around the origin (y from -H/2 to +H/2)
    /// so a spring spins about its own middle rather than swinging around an end.
    ///
    /// Cheap on purpose: a four-sided tube. At thirty metres up a spring is a handful of pixels, and
    /// nobody has ever been able to count the sides on a tube at that distance — but they can tell a
    /// spiral from a stick, which is the only read that matters.
    /// </summary>
    public static class SpringMesh
    {
        /// <summary>How many times the wire goes around. Three reads unmistakably as a coil; more
        /// turns at this pixel size just fill the middle in and it goes back to being a stick.</summary>
        public const int Turns = 3;

        /// <summary>Points along the helical path. 36 over three turns is twelve per turn — enough that
        /// the curve is a curve and not a polygon.</summary>
        public const int PathSegments = 36;

        /// <summary>Sides on the wire's cross-section. Four: see the class note.</summary>
        public const int Sides = 4;

        private const float CoilRadius = 0.42f;   // how far the wire sits from the axis
        private const float WireRadius = 0.12f;   // the thickness of the wire itself
        private const float Height = 1.0f;        // end to end, before the per-spring transform scale

        private static Mesh _shared;

        /// <summary>The one spring mesh. Built on first ask, then handed out forever.</summary>
        public static Mesh Shared
        {
            get
            {
                if (_shared == null) _shared = Build();
                return _shared;
            }
        }

        /// <summary>
        /// Build the coil. Public so a test can check it without needing a scene, a robot, or a death.
        /// </summary>
        public static Mesh Build()
        {
            int rings = PathSegments + 1;
            var verts = new Vector3[rings * Sides];

            // Two triangles per side per gap between rings, plus a flat cap at each end. The caps
            // matter more than they sound: the tube is open at both ends, backfaces are culled, and
            // an uncapped spring seen from above — which is the ONLY angle this game has — is a
            // coil with two holes punched through it.
            var tris = new int[(PathSegments * Sides * 2 + (Sides - 2) * 2) * 3];

            for (int i = 0; i < rings; i++)
            {
                float t = (float)i / PathSegments;
                float angle = t * Turns * Mathf.PI * 2f;
                float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);

                Vector3 centre = new Vector3(ca * CoilRadius, t * Height - Height * 0.5f, sa * CoilRadius);

                // A frame to sweep the cross-section around. The radial direction is the obvious
                // "outward", and the tangent is the analytic derivative of the helix — using the
                // real tangent rather than plain "up" is what keeps the wire's thickness even
                // instead of pinching where the coil is steepest.
                Vector3 radial = new Vector3(ca, 0f, sa);
                Vector3 tangent = new Vector3(-sa * CoilRadius * Turns * Mathf.PI * 2f,
                                              Height,
                                              ca * CoilRadius * Turns * Mathf.PI * 2f).normalized;
                Vector3 binormal = Vector3.Cross(tangent, radial).normalized;

                for (int s = 0; s < Sides; s++)
                {
                    float th = (float)s / Sides * Mathf.PI * 2f;
                    verts[i * Sides + s] = centre
                                         + radial * (Mathf.Cos(th) * WireRadius)
                                         + binormal * (Mathf.Sin(th) * WireRadius);
                }
            }

            int w = 0;
            for (int i = 0; i < PathSegments; i++)
            {
                for (int s = 0; s < Sides; s++)
                {
                    int a = i * Sides + s;
                    int b = i * Sides + (s + 1) % Sides;
                    int c = a + Sides;
                    int d = b + Sides;

                    tris[w++] = a; tris[w++] = c; tris[w++] = b;
                    tris[w++] = b; tris[w++] = c; tris[w++] = d;
                }
            }

            // Caps: a fan across the first ring, and the last one wound the other way so it faces out.
            int last = PathSegments * Sides;
            for (int s = 1; s < Sides - 1; s++)
            {
                tris[w++] = 0; tris[w++] = s + 1; tris[w++] = s;
                tris[w++] = last; tris[w++] = last + s; tris[w++] = last + s + 1;
            }

            var mesh = new Mesh { name = "SpringCoil" };
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
