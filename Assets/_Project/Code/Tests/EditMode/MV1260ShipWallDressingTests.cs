using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1260 (one new EditMode test, MV-465 Rule 1): <c>ShipKit.BuildBulkheadPanel</c> (MV-1230) was
    /// never wired into <see cref="ShipDressing.Dress"/> — the real Mothership's walls built as plain
    /// slabs, no panels, no robot racks. Fails on base commit (pre-MV-1260): <c>ShipDressing.Dress</c>
    /// never calls <c>ShipAreaKit.BuildWallDressing</c> at all, so the real area 8's north wall carries
    /// zero "Bulkhead Panel" descendants and zero "Robot Rack Frame" descendants, and the "Ship Wall
    /// Dressing" host the fix adds does not exist yet — see Logs/MV1260-failfirst.log, quoted in the fix
    /// comment.
    ///
    /// AC1's exact wording: "every room-facing wall [has] at least one bulkhead panel per 3.5 m of its
    /// length" and "at least one robot rack on its north wall" and "no MeshFilter under the wall dressing
    /// uses the built-in Cube mesh". Covers all four of area 8's sides (N/E/S/W), not the north wall
    /// alone — a verifier pass on an earlier draft of this test correctly flagged that "every room-facing
    /// wall" was unproven when only the north wall was checked.
    ///
    /// The exact per-3.5 m density figure is asserted precisely only on the north wall: it is the one
    /// side <c>mothership_config.json</c> authors gate-free, so its full 56 m authored span IS its real
    /// built run with no gate gap shortening it. a8's E and S walls each carry a gate (<c>g7</c>/<c>g8</c>,
    /// each 3 m wide, <c>g7</c> at the S wall's own midpoint) that splits the real built wall into a
    /// shorter segment than the authored span — replicating exactly where that split lands would mean
    /// duplicating <c>ShipAreaKit</c>'s own private wall-matching logic in test code. Rather than assert a
    /// precise count this test cannot honestly derive, E/S/W are held to the AC's own weaker floor
    /// ("every ... wall" gets at least one) — never zero panels — which is still a real, fail-first-provable
    /// claim this test enforces on the base commit (where no wall anywhere gets dressed at all).
    /// </summary>
    public sealed class MV1260ShipWallDressingTests
    {
        private const float PanelPitchAc = 3.5f;   // ticket's own AC1 wording: "per 3.5 m of its length"

        private static readonly Wall[] AllWalls = { Wall.N, Wall.E, Wall.S, Wall.W };

        [Test]
        public void RealMothershipArea8_EveryWallGetsBulkheadPanels_NorthWallGetsARobotRack_AndNoCubePrimitive()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
            Assert.IsNotNull(cfg, "the Mothership's own shipped config must load for this test to mean anything");

            WorldArea area8 = cfg.Area("a8");
            Assert.IsNotNull(area8, "area 'a8' must exist");
            float northWallLength = area8.WallSpan(Wall.N).Length;

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            var root = new GameObject("MV-1260 Mothership Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                ShipDressing.Dress(root.transform, map, cfg, built.Cover);

                Transform wallDressing = root.transform.Find("Ship Wall Dressing");
                Assert.IsNotNull(wallDressing, "ShipDressing must build a 'Ship Wall Dressing' host (MV-1260)");

                foreach (Wall wall in AllWalls)
                {
                    Transform wallGo = wallDressing.Find($"Wall {area8.id} {wall}");
                    Assert.IsNotNull(wallGo, $"area '{area8.id}' must carry a dressed {wall} wall");

                    int panelCount = 0, rackCount = 0;
                    foreach (Transform t in wallGo.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "Bulkhead Panel") panelCount++;
                        if (t.name == "Robot Rack Frame") rackCount++;
                    }

                    if (wall == Wall.N)
                    {
                        int expectedMinPanels = Mathf.FloorToInt(northWallLength / PanelPitchAc);
                        Assert.GreaterOrEqual(panelCount, expectedMinPanels,
                            $"area '{area8.id}' north wall ({northWallLength:F1}m) expected >= {expectedMinPanels} " +
                            $"bulkhead panels (one per {PanelPitchAc}m), found {panelCount}");
                        Assert.GreaterOrEqual(rackCount, 1,
                            $"area '{area8.id}' north wall must carry at least one robot rack, found {rackCount}");
                    }
                    else
                    {
                        Assert.GreaterOrEqual(panelCount, 1,
                            $"area '{area8.id}' {wall} wall must carry at least one bulkhead panel, found {panelCount}");
                    }
                }

                foreach (MeshFilter mf in wallDressing.GetComponentsInChildren<MeshFilter>(true))
                    Assert.AreNotEqual("Cube", mf.sharedMesh != null ? mf.sharedMesh.name : null,
                        $"'{mf.name}' uses the built-in Cube primitive mesh — the wall dressing must use generated meshes only");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
