using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-765: <see cref="StormdrainKit"/>'s verticals were authored as absolute constants against an
    /// implied ~3.5 m wall (World 1's fence). Every world's actual <c>wallHeight</c> is 1.5 m
    /// (<c>world1/2/3_config.json</c>, <c>MapData.DefaultWallHeight</c>) and was never checked against
    /// that value — as merged (MV-755, <c>a63619c</c>), the high pipe run floats above the 1.5 m wall
    /// and the soffit hangs a 1.2 m slab into the room at waist height, hiding the fight.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1) asserting RESOLVED state (Tier 2): runs
    /// the real <see cref="StormdrainDressing.Dress"/> against World 2's own shipped geometry/cover and
    /// reads back <see cref="Renderer.bounds"/> — the engine's own resolved world-space AABB after every
    /// piece is built, not the authored constants themselves.
    ///
    /// MV-771 raised World 2's own <c>wallHeight</c> to 3.0 m, so the low-wall branch this test guards
    /// (no floating "Pipe High" run, nothing but a Standpipe clearing the wall) is forced onto the map
    /// after load rather than read from World 2's live config — the regression is about the proportional
    /// MATH at a low wall, not about which wall height World 2 happens to author today.
    /// </summary>
    public sealed class MV765KitProportionTests
    {
        [Test]
        public void StormdrainVerticals_ResolveWithinA1Point5mWall()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);
            map.wallHeight = 1.5f; // MV-771: World 2 itself now ships 3.0 m; force the low-wall branch under test.

            var host = new GameObject("MV765 host").transform;
            try
            {
                MapBuild build = MapRuntime.Build(map, host);
                StormdrainDressing.Dress(host, map, build.Cover);

                Transform dressingHost = host.Find("Stormdrain Dressing");
                Assert.IsNotNull(dressingHost, "the dressing host was never built");

                Renderer[] allRenderers = dressingHost.GetComponentsInChildren<Renderer>(true);

                // ---- nothing but a deliberate standpipe clears the wall by more than half a metre ----
                foreach (Renderer r in allRenderers)
                {
                    bool isStandpipePart = r.name == "Standpipe"
                        || r.GetComponentsInParent<Transform>(true).Any(t => t.name == "Standpipe");
                    if (isStandpipePart) continue;

                    Assert.LessOrEqual(r.bounds.max.y, 2.0f,
                        $"{r.name} (under {Path(r.transform, dressingHost)}) reaches {r.bounds.max.y:F2} m " +
                        "above a 1.5 m wall — only a Standpipe should clear 2.0 m");
                }

                // ---- wall-hung pieces stay within half a metre of the face they dress ----
                Transform wallsHost = dressingHost.Find("Walls");
                Assert.IsNotNull(wallsHost, "the wall-dressing host was never built");

                var faces = MapGeometry.Faces(map).Where(f => f.FacesRoom && f.Length >= 1.2f).ToList();
                Assert.IsNotEmpty(faces, "World 2's map must have at least one dressable wall face for this test to mean anything");

                foreach (Renderer r in wallsHost.GetComponentsInChildren<Renderer>(true))
                {
                    // Lamp lens/streak/pool are unlit Quads — deliberately wide-spread light effects
                    // (a floor pool is meant to spread, that is what "pool" means) with no collider and
                    // no relation to the soffit-shaped regression this guard targets. Skip by resolved
                    // mesh identity, not by name, so a genuinely solid piece can never slip through.
                    var meshFilter = r.GetComponent<MeshFilter>();
                    if (meshFilter != null && meshFilter.sharedMesh != null
                        && meshFilter.sharedMesh.name == "Quad") continue;

                    WallFace? face = OwningFace(faces, r.bounds);
                    Assert.IsTrue(face.HasValue,
                        $"{r.name} at {r.bounds.center} does not lie flat along any dressable wall face's own span");
                    float depth = PenetrationDepth(r.bounds, face.Value);
                    Assert.LessOrEqual(depth, 0.5f,
                        $"{r.name} reaches {depth:F2} m into the room from its wall face — " +
                        "a soffit-shaped regression would show up here");
                }

                // ---- the high pipe run is dropped entirely below the 2.2 m wall-height threshold ----
                int pipeHighCount = allRenderers.Count(r => r.name == "Pipe High");
                Assert.AreEqual(0, pipeHighCount,
                    "a 1.5 m wall must get zero 'Pipe High' runs — the run that used to float above it");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        /// <summary>The face a wall-hung renderer belongs to. Matching on the renderer's CENTER point
        /// alone mismatches a long, wall-spanning piece (a kerb or a pipe run the length of its own
        /// face): at a T-junction its centre can sit numerically close to an unrelated PERPENDICULAR
        /// face's infinite line, because that face's line happens to cross mid-span. So every corner of
        /// the renderer's XZ footprint must satisfy tangential containment and a generous perpendicular
        /// cap (3 m — comfortably above any legitimate post-fix depth) against the SAME face; a
        /// perpendicular face fails this because its own two long-axis corners sit metres from its line.</summary>
        private static WallFace? OwningFace(System.Collections.Generic.List<WallFace> faces, Bounds bounds)
        {
            var corners = new[]
            {
                new Vector2(bounds.min.x, bounds.min.z),
                new Vector2(bounds.min.x, bounds.max.z),
                new Vector2(bounds.max.x, bounds.min.z),
                new Vector2(bounds.max.x, bounds.max.z),
            };

            WallFace? best = null;
            float bestPerp = float.MaxValue;
            foreach (WallFace f in faces)
            {
                Vector2 ab = f.B - f.A;
                float len = ab.magnitude;
                if (len < 0.0001f) continue;
                Vector2 dir = ab / len;

                float maxPerp = 0f;
                bool allContained = true;
                foreach (Vector2 c in corners)
                {
                    float tangential = Vector2.Dot(c - f.A, dir);
                    if (tangential < -0.1f || tangential > len + 0.1f) { allContained = false; break; }

                    float perp = Mathf.Abs(Vector2.Dot(c - f.A, f.Out));
                    if (perp > 3f) { allContained = false; break; }
                    maxPerp = Mathf.Max(maxPerp, perp);
                }
                if (!allContained) continue;

                if (maxPerp < bestPerp) { bestPerp = maxPerp; best = f; }
            }
            return best;
        }

        /// <summary>How far, in metres, the renderer's world-space AABB extends past the wall face's
        /// line along that face's <see cref="WallFace.Out"/> normal (which points into the room). Out
        /// is always axis-aligned (<c>MapGeometry.Walls</c> only emits along-X/along-Z runs), so the
        /// comparison is a plain coordinate compare, sign-adjusted for which way Out points.</summary>
        private static float PenetrationDepth(Bounds b, WallFace face)
        {
            Vector2 n = face.Out;
            Vector2 a = face.A;
            if (Mathf.Abs(n.x) >= Mathf.Abs(n.y))
                return n.x > 0f ? b.max.x - a.x : a.x - b.min.x;
            return n.y > 0f ? b.max.z - a.y : a.y - b.min.z;
        }

        private static string Path(Transform t, Transform root)
        {
            var parts = new System.Collections.Generic.List<string>();
            while (t != null && t != root) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
