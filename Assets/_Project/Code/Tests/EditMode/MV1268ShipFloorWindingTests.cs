using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1268 AC1: <see cref="ShipKit.BuildPlateMesh"/>/<c>AddFlatQuad</c>'s private helpers wound every
    /// atlas-floor plate and wear decal quad counter-clockwise seen from above (x right, z up) — Unity's
    /// clockwise-front convention treats that as back-facing, so URP/Lit's cull-back default culled every
    /// one of them from the play camera, leaving only the camera background visible between the seams.
    /// Fails on base commit (before this ticket's winding fix): every triangle under area 2's own
    /// floor-level dressing winds with a NEGATIVE y winding normal.
    ///
    /// Builds the REAL shipped Mothership config through the production path (<see cref="WorldLibrary.Load"/>
    /// -&gt; <see cref="WorldMapLoader.TryLoad"/> -&gt; <see cref="MapRuntime.Build"/> -&gt;
    /// <see cref="ShipDressing.Dress"/>, same idiom <c>MV1259ShipFloorAtlasTests</c> already uses), then
    /// asserts a RESOLVED value only (testing policy Rule 2, Tier 2): every triangle's own world-space
    /// winding normal (cross of its first two edges, in Unity's clockwise-front convention) under "Ship
    /// Floors" area 2's own floor-level dressing has y &gt; 0 — i.e. faces up, not down.
    ///
    /// Only checked for a MESH whose own vertices all share one world Y (a genuinely flat floor-level
    /// quad sheet — exactly what <c>BuildPlateMesh</c>/<c>AddFlatQuad</c> build): a real box (the deck
    /// seams, their glow lines) has a legitimate bottom face whose own winding normal is negative-y by
    /// design — it faces away from the floor it sits on, into ground nobody ever sees — so asserting y
    /// &gt; 0 on ITS triangles too would fail on correct, already-working geometry this ticket never
    /// touched.</summary>
    public sealed class MV1268ShipFloorWindingTests
    {
        [Test]
        public void Area2FloorMeshes_EveryTriangleWindsFaceUp()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
            Assert.IsNotNull(cfg, "the Mothership's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapZone area2 = map.zones.FirstOrDefault(z => z.AreaIndex == 2 && z.level == 0);
            Assert.IsNotNull(area2, "expected a real area 2 zone in the shipped Mothership config");

            var root = new GameObject("MV1268 Mothership Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                ShipDressing.Dress(root.transform, map, cfg, built.Cover);

                Transform floorsHost = root.transform.Find("Ship Floors");
                Assert.IsNotNull(floorsHost, "expected a built 'Ship Floors' host");

                Transform area2Floor = floorsHost.Find("Atlas Floor A2");
                Assert.IsNotNull(area2Floor, "expected a built 'Atlas Floor A2' host for area 2's own floor-level dressing");

                int triangleCount = 0;
                foreach (MeshFilter mf in area2Floor.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = mf.sharedMesh;
                    if (mesh == null) continue;

                    Vector3[] verts = mesh.vertices;
                    int[] tris = mesh.triangles;
                    Matrix4x4 toWorld = mf.transform.localToWorldMatrix;
                    Vector3[] worldVerts = new Vector3[verts.Length];
                    for (int v = 0; v < verts.Length; v++) worldVerts[v] = toWorld.MultiplyPoint3x4(verts[v]);

                    // A genuinely flat floor-level quad sheet (Plates/Wear Decals) has every vertex at
                    // one world Y. A real box (a seam, its glow line) does not, and its legitimate bottom
                    // face is supposed to wind face-down -- skip anything that isn't flat, so this test
                    // only ever asserts on the geometry this ticket's winding fix actually touched.
                    bool isFlat = true;
                    for (int v = 1; v < worldVerts.Length; v++)
                        if (Mathf.Abs(worldVerts[v].y - worldVerts[0].y) > 0.0001f) { isFlat = false; break; }
                    if (!isFlat) continue;

                    for (int i = 0; i + 2 < tris.Length; i += 3)
                    {
                        Vector3 p0 = worldVerts[tris[i]];
                        Vector3 p1 = worldVerts[tris[i + 1]];
                        Vector3 p2 = worldVerts[tris[i + 2]];
                        Vector3 windingNormal = Vector3.Cross(p1 - p0, p2 - p0);

                        Assert.Greater(windingNormal.y, 0f,
                            $"{mf.name} triangle {i / 3} winds with y={windingNormal.y:F4} -- faces down, " +
                            "so URP/Lit's cull-back default culls it from the play camera");
                        triangleCount++;
                    }
                }

                Assert.Greater(triangleCount, 0, "area 2's floor-level dressing built no triangles to check");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
