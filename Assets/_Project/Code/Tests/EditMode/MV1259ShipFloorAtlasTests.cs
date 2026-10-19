using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1259 AC1: the Mothership's deck floor used to be a flat, unwired slab
    /// (<see cref="ShipKit.BuildDeckFloor"/>, called only for the Hangar Bay's own 16x11 m launch-pad
    /// patch, per <see cref="ShipDressing"/>'s own header) — never a real per-area floor. Fails on the
    /// base commit: <see cref="ShipKit.BuildAtlasFloor"/> and <see cref="ShipDressing"/>'s 4-argument
    /// <c>Dress</c> overload do not exist there yet.
    ///
    /// Loads the REAL shipped Mothership config through the production path
    /// (<see cref="WorldLibrary.Load"/> -&gt; <see cref="WorldMapLoader.TryLoad"/> -&gt;
    /// <see cref="MapRuntime.Build"/> -&gt; <see cref="ShipDressing.Dress"/>, same idiom
    /// <c>MV1232HangarBayTests</c>/<c>MV1256ReefDeckDetailTests</c> already use for their own worlds),
    /// then asserts RESOLVED values only (testing policy Rule 2, Tier 2) off area 1's own built floor
    /// mesh: its world bounds cover at least 98% of area 1's own authored footprint, its UVs resolve to
    /// at least 6 distinct atlas plate cells, and no <see cref="MeshFilter"/> under that floor uses
    /// Unity's built-in "Cube" or "Plane" primitive mesh.
    /// </summary>
    public sealed class MV1259ShipFloorAtlasTests
    {
        [Test]
        public void Area1Floor_TilesItsFootprintWithAtLeastSixDistinctPlateVariants()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
            Assert.IsNotNull(cfg, "the Mothership's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapZone area1 = map.zones.FirstOrDefault(z => z.AreaIndex == 1);
            Assert.IsNotNull(area1, "expected a real area 1 (Hangar Bay) zone in the shipped config");

            var root = new GameObject("MV1259 Mothership Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                ShipDressing.Dress(root.transform, map, cfg, built.Cover);

                Transform floorHost = root.transform.Find("Ship Floors/Atlas Floor A1");
                Assert.IsNotNull(floorHost, "expected a built 'Atlas Floor A1' host for area 1");

                Transform plates = floorHost.Find("Plates");
                Assert.IsNotNull(plates, "expected area 1's floor to carry a 'Plates' mesh");

                Mesh mesh = plates.GetComponent<MeshFilter>()?.sharedMesh;
                Assert.IsNotNull(mesh, "area 1's floor mesh was never built");

                // AC1: the mesh's own resolved world bounds cover at least 98% of area 1's authored footprint.
                Rect footprint = area1.Footprint;
                float footprintArea = footprint.width * footprint.height;
                Bounds b = mesh.bounds;
                float meshArea = b.size.x * b.size.z;
                Assert.GreaterOrEqual(meshArea, footprintArea * 0.98f,
                    $"area 1's floor mesh covers {meshArea:F1} m2, below 98% of its {footprintArea:F1} m2 footprint " +
                    $"(mesh bounds {b.size}, footprint {footprint})");

                // AC1: at least 6 distinct atlas plate variants, read off the mesh's own resolved UVs
                // (never an authored constant) -- each quad's 4 UVs sit inside exactly one of the atlas's
                // 4x2 = 8 cells, so the (col,row) of its first vertex identifies which cell it used.
                Vector2[] uv = mesh.uv;
                Assert.IsNotEmpty(uv, "area 1's floor mesh has no UVs");
                var cells = new HashSet<int>();
                for (int i = 0; i + 3 < uv.Length; i += 4)
                {
                    int col = Mathf.Clamp(Mathf.FloorToInt(uv[i].x * 4f), 0, 3);
                    int row = Mathf.Clamp(Mathf.FloorToInt(uv[i].y * 2f), 0, 1);
                    cells.Add(row * 4 + col);
                }
                Assert.GreaterOrEqual(cells.Count, 6,
                    $"expected at least 6 distinct plate variants across area 1's floor, found {cells.Count} " +
                    $"({string.Join(",", cells)})");

                // AC1: no MeshFilter under area 1's floor uses a built-in Cube or Plane primitive mesh.
                foreach (MeshFilter mf in floorHost.GetComponentsInChildren<MeshFilter>(true))
                {
                    string meshName = mf.sharedMesh != null ? mf.sharedMesh.name : "";
                    Assert.IsFalse(meshName == "Cube" || meshName == "Plane",
                        $"{mf.name} uses the built-in '{meshName}' primitive mesh");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
