using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The three form builders every character in the game is made of (MV-451).
    ///
    /// WHY THIS EXISTS. Until now Max and the robots were assembled from Unity primitives — cubes,
    /// spheres and cylinders. That reads, in Lee's words, as "glued together from cardboard boxes and
    /// tubes then painted". The reason is structural, not a matter of tuning: a primitive has a
    /// constant cross-section and an axis-aligned box has hard abutting seams, so no arrangement of
    /// them ever looks designed. Generated geometry fixes it at the source — every surface can taper,
    /// chamfer and flow into the next.
    ///
    /// PERFORMANCE. This is also strictly cheaper than what it replaces. A mesh depends only on its
    /// parameters, so it is built once, cached here, and shared by every instance of that kind: one
    /// Rusher and twenty Rushers cost the same geometry. The primitive rigs, by contrast, needed a
    /// renderer per part per robot.
    ///
    /// The maths is mirrored exactly in the design tool (`robot-gen-mesh.html`,
    /// `max-gen-mesh.html`). Change a builder here and the render stops matching the game — change
    /// both, or change neither.
    /// </summary>
    public static class CharacterMeshes
    {
        private static readonly Dictionary<int, Mesh> Cache = new Dictionary<int, Mesh>(64);

        /// <summary>Drop every cached mesh. Only for a domain reload / test teardown — in play there
        /// is never a reason to rebuild geometry that depends solely on its parameters.</summary>
        public static void ClearCache()
        {
            foreach (Mesh m in Cache.Values)
                if (m != null) Object.DestroyImmediate(m);
            Cache.Clear();
        }

        /// <summary>How many distinct meshes have actually been built. The whole performance claim of
        /// this class is "once per kind, not once per robot", and this is how a test says so.</summary>
        public static int Built => Cache.Count;

        // ------------------------------------------------------------------ Lathe

        /// <summary>
        /// Revolve a profile around Y. The profile is (radius, height) pairs read bottom to top.
        ///
        /// This is the builder that kills the stack-of-cylinders read: a body, a dome, a tapered drum
        /// and a bezel ring are all one continuous surface with no seam anywhere along the silhouette.
        /// Smooth-shaded, because these are meant to read as turned or moulded forms.
        /// </summary>
        public static Mesh Lathe(Vector2[] profile, int segments = 24)
        {
            int key = Hash(1, segments, profile);
            if (Cache.TryGetValue(key, out Mesh hit)) return hit;

            var verts = new List<Vector3>(profile.Length * (segments + 1) + 2);
            var tris = new List<int>();

            for (int r = 0; r < profile.Length; r++)
                for (int s = 0; s <= segments; s++)
                {
                    float a = (float)s / segments * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * profile[r].x, profile[r].y, Mathf.Sin(a) * profile[r].x));
                }

            int w = segments + 1;
            for (int r = 0; r < profile.Length - 1; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = r * w + s, b = a + 1, c = a + w, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }

            AddCap(verts, tris, profile, 0, segments, w, true);
            AddCap(verts, tris, profile, profile.Length - 1, segments, w, false);

            return Store(key, verts, tris, smooth: true);
        }

        private static void AddCap(List<Vector3> verts, List<int> tris, Vector2[] profile,
                                   int row, int segments, int w, bool flip)
        {
            if (profile[row].x < 1e-5f) return;   // already a point; nothing to cap
            int centre = verts.Count;
            verts.Add(new Vector3(0f, profile[row].y, 0f));
            for (int s = 0; s < segments; s++)
            {
                int a = row * w + s, b = a + 1;
                tris.Add(centre);
                tris.Add(flip ? a : b);
                tris.Add(flip ? b : a);
            }
        }

        // ------------------------------------------------------------------ Prism

        /// <summary>
        /// An N-sided tapered housing, chamfered at both ends, optionally twisted along its length.
        ///
        /// Flat-shaded on purpose: a housing wants readable planes and a hard edge highlight. Six or
        /// eight sides with a taper reads as machined; four sides with no taper is a cardboard box,
        /// which is the thing this whole class exists to avoid. <paramref name="chamfer"/> is the
        /// fraction of the height eaten by the bevel at each end, and it is what stops the ends
        /// looking cut off.
        /// </summary>
        public static Mesh Prism(int sides, float rBottom, float rTop, float height,
                                 float chamfer = 0.14f, float twistDegrees = 0f)
        {
            int key = Hash(2, sides, new[]
            {
                new Vector2(rBottom, rTop), new Vector2(height, chamfer), new Vector2(twistDegrees, 0f)
            });
            if (Cache.TryGetValue(key, out Mesh hit)) return hit;

            float c = height * chamfer;
            var profile = new[]
            {
                new Vector2(rBottom * 0.72f, 0f),
                new Vector2(rBottom, c),
                new Vector2(rTop, height - c),
                new Vector2(rTop * 0.72f, height),
            };

            var verts = new List<Vector3>();
            var tris = new List<int>();
            float twist = twistDegrees * Mathf.Deg2Rad;

            for (int r = 0; r < profile.Length; r++)
                for (int s = 0; s <= sides; s++)
                {
                    float a = (float)s / sides * Mathf.PI * 2f
                            + profile[r].y / height * twist
                            + Mathf.PI / sides;
                    verts.Add(new Vector3(Mathf.Cos(a) * profile[r].x,
                                          profile[r].y - height * 0.5f,
                                          Mathf.Sin(a) * profile[r].x));
                }

            int w = sides + 1;
            for (int r = 0; r < profile.Length - 1; r++)
                for (int s = 0; s < sides; s++)
                {
                    int a = r * w + s, b = a + 1, cc = a + w, d = cc + 1;
                    tris.Add(a); tris.Add(cc); tris.Add(b);
                    tris.Add(b); tris.Add(cc); tris.Add(d);
                }

            for (int i = 0; i < 2; i++)
            {
                int row = i == 0 ? 0 : profile.Length - 1;
                bool flip = i == 0;
                int centre = verts.Count;
                verts.Add(new Vector3(0f, profile[row].y - height * 0.5f, 0f));
                for (int s = 0; s < sides; s++)
                {
                    int a = row * w + s, b = a + 1;
                    tris.Add(centre);
                    tris.Add(flip ? a : b);
                    tris.Add(flip ? b : a);
                }
            }

            return Store(key, verts, tris, smooth: false);
        }

        /// <summary>A ball — a lathed hemisphere profile revolved, not a primitive sphere. Joints,
        /// eye lenses and hair tufts all want one, and routing it through the same builder keeps the
        /// "no primitives anywhere on a character" rule true rather than nearly true.</summary>
        public static Mesh Sphere(int segments = 16)
        {
            var profile = new Vector2[segments / 2 + 1];
            for (int i = 0; i < profile.Length; i++)
            {
                float t = Mathf.PI * i / (profile.Length - 1);
                profile[i] = new Vector2(Mathf.Sin(t) * 0.5f, -Mathf.Cos(t) * 0.5f);
            }
            return Lathe(profile, segments);
        }

        // ------------------------------------------------------------------ Beam

        /// <summary>A tapered limb, built along +Y and oriented by the caller. A leg that thins toward
        /// the foot and an arm that thins toward the hand are most of what separates a designed
        /// character from a bundle of tubes.</summary>
        public static Mesh Beam(float length, float rBottom, float rTop, int sides = 8)
            => Prism(sides, rBottom, rTop, length, 0.10f);

        // ------------------------------------------------------------------ Ring (MV-786)

        /// <summary>A flat washer: a hollow disc of <paramref name="thickness"/> between
        /// <paramref name="innerRadius"/> and <paramref name="outerRadius"/>, centred on the origin and
        /// lying flat in XZ. A rust rim proud of a hopper's mouth, a bezel — anywhere a shape needs to
        /// read as an annulus rather than a solid disc. Flat-shaded, same reasoning as
        /// <see cref="Prism"/>: a rim wants a hard machined edge, not a blurred one.</summary>
        public static Mesh Ring(float innerRadius, float outerRadius, float thickness = 0.05f, int segments = 24)
        {
            int key = Hash(4, segments, new[] { new Vector2(innerRadius, outerRadius), new Vector2(thickness, 0f) });
            if (Cache.TryGetValue(key, out Mesh hit)) return hit;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            float hh = thickness * 0.5f;

            AddAnnulusCap(verts, tris, innerRadius, outerRadius, hh, segments, topFacing: true);
            AddAnnulusCap(verts, tris, innerRadius, outerRadius, -hh, segments, topFacing: false);
            AddCylinderWall(verts, tris, outerRadius, hh, -hh, segments, outward: true);
            AddCylinderWall(verts, tris, innerRadius, hh, -hh, segments, outward: false);

            return Store(key, verts, tris, smooth: false);
        }

        private static void AddAnnulusCap(List<Vector3> verts, List<int> tris, float rInner, float rOuter,
                                          float y, int segments, bool topFacing)
        {
            int start = verts.Count;
            for (int s = 0; s <= segments; s++)
            {
                float a = (float)s / segments * Mathf.PI * 2f;
                float cx = Mathf.Cos(a), cz = Mathf.Sin(a);
                verts.Add(new Vector3(cx * rInner, y, cz * rInner));
                verts.Add(new Vector3(cx * rOuter, y, cz * rOuter));
            }
            for (int s = 0; s < segments; s++)
            {
                int a = start + s * 2, b = a + 2;
                int ai = a + 1, bi = b + 1;
                if (topFacing)
                {
                    tris.Add(a); tris.Add(b); tris.Add(ai);
                    tris.Add(ai); tris.Add(b); tris.Add(bi);
                }
                else
                {
                    tris.Add(a); tris.Add(ai); tris.Add(b);
                    tris.Add(ai); tris.Add(bi); tris.Add(b);
                }
            }
        }

        private static void AddCylinderWall(List<Vector3> verts, List<int> tris, float r,
                                            float yTop, float yBottom, int segments, bool outward)
        {
            int start = verts.Count;
            for (int s = 0; s <= segments; s++)
            {
                float a = (float)s / segments * Mathf.PI * 2f;
                float cx = Mathf.Cos(a), cz = Mathf.Sin(a);
                verts.Add(new Vector3(cx * r, yTop, cz * r));
                verts.Add(new Vector3(cx * r, yBottom, cz * r));
            }
            for (int s = 0; s < segments; s++)
            {
                int a = start + s * 2, b = a + 2;
                int at = a, ab = a + 1, bt = b, bb = b + 1;
                if (outward)
                {
                    tris.Add(at); tris.Add(bt); tris.Add(ab);
                    tris.Add(ab); tris.Add(bt); tris.Add(bb);
                }
                else
                {
                    tris.Add(at); tris.Add(ab); tris.Add(bt);
                    tris.Add(bt); tris.Add(ab); tris.Add(bb);
                }
            }
        }

        // ------------------------------------------------------------------ Bevelled box (MV-778)

        /// <summary>
        /// A box of <paramref name="size"/> with all 12 edges chamfered inward by
        /// <paramref name="bevel"/> metres — the environment's answer to <see cref="Prism"/>: a Unity
        /// primitive cube has a perfectly sharp 90 degree edge that catches no light, so every wall,
        /// kerb and cover block built from one reads as a flat silhouette. This is the fix, applied to
        /// the box shape instead of the tapered-housing shape.
        ///
        /// Six shrunk faces, twelve flat bevel strips and eight corner triangles, each with its own
        /// vertices — never shared across facets, which is what makes the shading HARD rather than
        /// smoothed (a smoothed chamfer reads as a blurry cube, not a machined edge). The bounds still
        /// come out to exactly <paramref name="size"/>: every corner triangle has one vertex sitting on
        /// the box's true extreme in one axis, so the chamfer cuts in, it never inflates the box.
        /// </summary>
        public static Mesh Bevelled(Vector3 size, float bevel)
        {
            int key = Hash(3, 0, new[] { new Vector2(size.x, size.y), new Vector2(size.z, bevel) });
            if (Cache.TryGetValue(key, out Mesh hit)) return hit;

            float hx = size.x * 0.5f, hy = size.y * 0.5f, hz = size.z * 0.5f;
            float b = Mathf.Clamp(bevel, 0f, Mathf.Min(hx, Mathf.Min(hy, hz)) * 0.99f);

            var verts = new List<Vector3>(96);
            var tris = new List<int>(144);

            // 6 flat faces, each shrunk by b on the two axes perpendicular to its own normal.
            AddQuad(verts, tris,
                new Vector3(hx, -(hy - b), -(hz - b)), new Vector3(hx, -(hy - b), (hz - b)),
                new Vector3(hx, (hy - b), (hz - b)), new Vector3(hx, (hy - b), -(hz - b)),
                new Vector3(1f, 0f, 0f));
            AddQuad(verts, tris,
                new Vector3(-hx, -(hy - b), -(hz - b)), new Vector3(-hx, -(hy - b), (hz - b)),
                new Vector3(-hx, (hy - b), (hz - b)), new Vector3(-hx, (hy - b), -(hz - b)),
                new Vector3(-1f, 0f, 0f));
            AddQuad(verts, tris,
                new Vector3(-(hx - b), hy, -(hz - b)), new Vector3((hx - b), hy, -(hz - b)),
                new Vector3((hx - b), hy, (hz - b)), new Vector3(-(hx - b), hy, (hz - b)),
                new Vector3(0f, 1f, 0f));
            AddQuad(verts, tris,
                new Vector3(-(hx - b), -hy, -(hz - b)), new Vector3((hx - b), -hy, -(hz - b)),
                new Vector3((hx - b), -hy, (hz - b)), new Vector3(-(hx - b), -hy, (hz - b)),
                new Vector3(0f, -1f, 0f));
            AddQuad(verts, tris,
                new Vector3(-(hx - b), -(hy - b), hz), new Vector3((hx - b), -(hy - b), hz),
                new Vector3((hx - b), (hy - b), hz), new Vector3(-(hx - b), (hy - b), hz),
                new Vector3(0f, 0f, 1f));
            AddQuad(verts, tris,
                new Vector3(-(hx - b), -(hy - b), -hz), new Vector3((hx - b), -(hy - b), -hz),
                new Vector3((hx - b), (hy - b), -hz), new Vector3(-(hx - b), (hy - b), -hz),
                new Vector3(0f, 0f, -1f));

            // 12 edge bevels — one per (free axis, pair of signs on the other two axes).
            foreach (float sx in Signs)
                foreach (float sz in Signs)   // free axis Y: between the X face and the Z face
                    AddQuad(verts, tris,
                        new Vector3(sx * hx, -(hy - b), sz * (hz - b)), new Vector3(sx * hx, (hy - b), sz * (hz - b)),
                        new Vector3(sx * (hx - b), hy, sz * hz), new Vector3(sx * (hx - b), -(hy - b), sz * hz),
                        new Vector3(sx, 0f, sz));

            foreach (float sx in Signs)
                foreach (float sy in Signs)   // free axis Z: between the X face and the Y face
                    AddQuad(verts, tris,
                        new Vector3(sx * hx, sy * (hy - b), -(hz - b)), new Vector3(sx * hx, sy * (hy - b), (hz - b)),
                        new Vector3(sx * (hx - b), sy * hy, (hz - b)), new Vector3(sx * (hx - b), sy * hy, -(hz - b)),
                        new Vector3(sx, sy, 0f));

            foreach (float sy in Signs)
                foreach (float sz in Signs)   // free axis X: between the Y face and the Z face
                    AddQuad(verts, tris,
                        new Vector3(-(hx - b), sy * hy, sz * (hz - b)), new Vector3((hx - b), sy * hy, sz * (hz - b)),
                        new Vector3((hx - b), sy * (hy - b), sz * hz), new Vector3(-(hx - b), sy * (hy - b), sz * hz),
                        new Vector3(0f, sy, sz));

            // 8 corner triangles — one per octant, connecting the three faces that meet there.
            foreach (float sx in Signs)
                foreach (float sy in Signs)
                    foreach (float sz in Signs)
                        AddTri(verts, tris,
                            new Vector3(sx * hx, sy * (hy - b), sz * (hz - b)),
                            new Vector3(sx * (hx - b), sy * hy, sz * (hz - b)),
                            new Vector3(sx * (hx - b), sy * (hy - b), sz * hz),
                            new Vector3(sx, sy, sz));

            return Store(key, verts, tris, smooth: false);
        }

        /// <summary>MV-778's default chamfer: proportional to an object's smallest dimension, clamped
        /// so a small prop gets a bevel that actually reads and a 6 m wall does not get a 70 cm one.</summary>
        public static float DefaultBevel(Vector3 size) =>
            Mathf.Clamp(Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.12f, 0.015f, 0.06f);

        private static readonly float[] Signs = { -1f, 1f };

        /// <summary>Appends a quad as two triangles, picking whichever winding makes the triangles'
        /// own cross-product normal agree with <paramref name="outward"/> — so every facet culls and
        /// lights correctly regardless of which order its four corners were written in above.</summary>
        private static void AddQuad(List<Vector3> verts, List<int> tris,
                                    Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f;
            if (!flip)
            {
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }
            else
            {
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            }
        }

        /// <summary>Same idea as <see cref="AddQuad"/>, for the three-vertex corner facets.</summary>
        private static void AddTri(List<Vector3> verts, List<int> tris,
                                   Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f;
            if (!flip) { tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); }
            else { tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); }
        }

        // ------------------------------------------------------------------ plumbing

        private static Mesh Store(int key, List<Vector3> verts, List<int> tris, bool smooth)
        {
            var mesh = new Mesh { name = $"CharacterMesh_{key:X8}", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);

            if (smooth) SmoothNormals(mesh, verts, tris);
            else mesh.RecalculateNormals();

            mesh.RecalculateBounds();
            mesh.UploadMeshData(markNoLongerReadable: false);   // tests read vertices back
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>Area-weighted vertex normals. <c>RecalculateNormals</c> splits on the angle
        /// threshold and would facet a lathed body, which is exactly the look this class is replacing.</summary>
        private static void SmoothNormals(Mesh mesh, List<Vector3> verts, List<int> tris)
        {
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                Vector3 face = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                normals[a] += face; normals[b] += face; normals[c] += face;
            }
            for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].normalized;
            mesh.normals = normals;
        }

        private static int Hash(int kind, int n, IReadOnlyList<Vector2> data)
        {
            unchecked
            {
                int h = (17 * 31 + kind) * 31 + n;
                for (int i = 0; i < data.Count; i++)
                {
                    h = h * 31 + Mathf.RoundToInt(data[i].x * 10000f);
                    h = h * 31 + Mathf.RoundToInt(data[i].y * 10000f);
                }
                return h;
            }
        }
    }
}
