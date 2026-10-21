using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1269 (one new EditMode test, MV-465 Rule 1): <c>ShipKit.BuildBulkheadPanel</c> (MV-1260) put
    /// every piece of bulkhead detail on the wall's vertical FACE — a chat play-check of f94cf65 on
    /// WebGL (20 Oct) found the bulkheads read as plain dark slabs, because at the 72-degree play camera
    /// a 1.5-3 m wall's face is nearly edge-on and the camera mostly sees the wall's TOP instead. Fails on
    /// base commit: there is no "Wall Top Cap" child anywhere under "Ship Wall Dressing", so every
    /// room-facing wall run's own cap coverage is 0% of its length, well under the ticket's 90% floor —
    /// see Logs/MV1269-failfirst.log, quoted in the fix comment.
    ///
    /// Run length and cap length are both read from the real built geometry's own <see cref="Renderer.bounds"/>
    /// (testing policy Tier 2, a resolved value) rather than any authored span — area 8's E/S walls each
    /// carry a gate that shortens the real built run below the authored span (same reasoning
    /// <see cref="MV1260ShipWallDressingTests"/> already documents), so the run length itself must come
    /// from what MV-1260's own "Bulkhead Panel" cells actually tiled, not from <c>WorldArea.WallSpan</c>.
    /// </summary>
    public sealed class MV1269ShipWallTopCapTests
    {
        private const float MinRunLength = 3f;     // ticket AC1: "every room-facing wall run longer than 3 m"
        private const float MinCapCoverage = 0.9f; // ticket AC1: "covers at least 90% of the run's length"

        private static readonly Wall[] AllWalls = { Wall.N, Wall.E, Wall.S, Wall.W };

        [Test]
        public void RealMothershipArea8_EveryLongRoomFacingWall_HasWallTopCapCovering90Percent_AndNoCubePrimitive()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
            Assert.IsNotNull(cfg, "the Mothership's own shipped config must load for this test to mean anything");

            WorldArea area8 = cfg.Area("a8");
            Assert.IsNotNull(area8, "area 'a8' must exist");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            var root = new GameObject("MV-1269 Mothership Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                ShipDressing.Dress(root.transform, map, cfg, built.Cover);

                Transform wallDressing = root.transform.Find("Ship Wall Dressing");
                Assert.IsNotNull(wallDressing, "ShipDressing must build a 'Ship Wall Dressing' host (MV-1260)");

                int longWallsChecked = 0;
                foreach (Wall wall in AllWalls)
                {
                    Transform wallGo = wallDressing.Find($"Wall {area8.id} {wall}");
                    Assert.IsNotNull(wallGo, $"area '{area8.id}' must carry a dressed {wall} wall");

                    bool alongX = wall == Wall.N || wall == Wall.S;
                    // "Panel Backing" (not the "Bulkhead Panel" wrapper transform itself, which carries
                    // no MeshRenderer of its own) tiles the wall's real run end to end (MV-1260).
                    float runLength = CombinedExtent(wallGo, "Panel Backing", alongX);
                    if (runLength <= MinRunLength) continue;
                    longWallsChecked++;

                    float capLength = CombinedExtent(wallGo, "Wall Top Cap", alongX);
                    float coverage = runLength > 0f ? capLength / runLength : 0f;
                    Assert.GreaterOrEqual(coverage, MinCapCoverage,
                        $"area '{area8.id}' {wall} wall ({runLength:F1}m real run) expected its wall-top cap " +
                        $"to cover >= {MinCapCoverage:P0} of the run, found {coverage:P0} ({capLength:F2}m of cap)");
                }

                Assert.Greater(longWallsChecked, 0,
                    "area 'a8' must have at least one room-facing wall run longer than 3 m for this test to mean anything");

                foreach (MeshFilter mf in wallDressing.GetComponentsInChildren<MeshFilter>(true))
                    Assert.AreNotEqual("Cube", mf.sharedMesh != null ? mf.sharedMesh.name : null,
                        $"'{mf.name}' uses the built-in Cube primitive mesh — the wall dressing must use generated meshes only");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>The combined world-space extent, along the wall's own run axis, of every descendant
        /// named <paramref name="childName"/> that carries a <see cref="MeshRenderer"/> — a resolved
        /// value read off the real built mesh bounds, not an authored or re-derived span.</summary>
        private static float CombinedExtent(Transform wallGo, string childName, bool alongX)
        {
            Bounds combined = default;
            bool any = false;
            foreach (Transform t in wallGo.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != childName) continue;
                var mr = t.GetComponent<MeshRenderer>();
                if (mr == null) continue;

                if (!any) { combined = mr.bounds; any = true; }
                else combined.Encapsulate(mr.bounds);
            }
            if (!any) return 0f;
            return alongX ? combined.size.x : combined.size.z;
        }
    }
}
