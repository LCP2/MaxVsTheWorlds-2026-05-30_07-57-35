using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1264 AC1 (one new EditMode test, MV-465 Rule 1): the Mothership's single map-spanning
    /// "Map Floor" slab (<see cref="MapGeometry.Floor"/>, top always at world y = 0) sat right under
    /// every atlas floor <see cref="ShipKit.BuildAtlasFloor"/> builds on top of it — at the base
    /// commit's y = 0.001 that was near-coplanar, so the map floor's own flat
    /// <c>Palette.GroundBase</c> won the z-fight on a real device and every plate/seam/decal MV-1259
    /// drew was invisible (Lee, TestFlight 0.17.0, 20 Oct 2026). Fails on base commit: the map floor's
    /// renderer is still enabled under area 1's atlas floor, and the atlas floor's own top sits only
    /// 0.001 above it, nowhere near the 0.009 margin this test demands — see Logs/MV1264-failfirst.log,
    /// quoted in the fix comment.
    ///
    /// Loads the REAL shipped Mothership config through the production path (same idiom
    /// <c>MV1259ShipFloorAtlasTests</c>/<c>MV1260ShipWallDressingTests</c> already use), then asserts
    /// RESOLVED values only (testing policy Rule 2, Tier 2): every real <see cref="StructuralFloor"/>
    /// renderer whose own world-space XZ footprint overlaps area 1's built atlas floor is disabled
    /// (collider left enabled, so walkability is untouched), and the atlas floor's own resolved top is
    /// at least 0.009 m above that floor's own resolved top.
    /// </summary>
    public sealed class MV1264ShipFloorHiddenUnderAtlasTests
    {
        [Test]
        public void RealMothershipArea1_MapFloorUnderTheAtlasFloorIsHidden_ColliderKept_AndAtlasFloorStandsClearAboveIt()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
            Assert.IsNotNull(cfg, "the Mothership's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject("MV1264 Mothership Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                ShipDressing.Dress(root.transform, map, cfg, built.Cover);

                Transform floorHost = root.transform.Find("Ship Floors/Atlas Floor A1");
                Assert.IsNotNull(floorHost, "expected a built 'Atlas Floor A1' host for area 1");
                Transform plates = floorHost.Find("Plates");
                Assert.IsNotNull(plates, "expected area 1's floor to carry a 'Plates' mesh");

                var atlasRenderer = plates.GetComponent<Renderer>();
                Assert.IsNotNull(atlasRenderer, "area 1's floor plates must carry a Renderer to resolve world bounds from");

                Bounds atlasBounds = atlasRenderer.bounds;
                var atlasXz = new Rect(atlasBounds.min.x, atlasBounds.min.z, atlasBounds.size.x, atlasBounds.size.z);

                int checkedOverlapping = 0;
                float lowestMapFloorTop = float.PositiveInfinity;

                foreach (StructuralFloor floor in root.GetComponentsInChildren<StructuralFloor>(true))
                {
                    var renderer = floor.GetComponent<Renderer>();
                    Assert.IsNotNull(renderer, $"'{floor.name}' carries StructuralFloor but no Renderer");

                    Bounds floorBounds = renderer.bounds;
                    var floorXz = new Rect(floorBounds.min.x, floorBounds.min.z, floorBounds.size.x, floorBounds.size.z);
                    if (!floorXz.Overlaps(atlasXz)) continue;

                    checkedOverlapping++;
                    Assert.IsFalse(renderer.enabled,
                        $"'{floor.name}' renderer overlaps area 1's atlas floor footprint and must be disabled " +
                        "so the atlas floor is what actually renders there");

                    var collider = floor.GetComponent<Collider>();
                    Assert.IsNotNull(collider, $"'{floor.name}' must keep a collider for walkability");
                    Assert.IsTrue(collider.enabled, $"'{floor.name}' collider must stay enabled even though its renderer is hidden");

                    lowestMapFloorTop = Mathf.Min(lowestMapFloorTop, floorBounds.max.y);
                }

                Assert.Greater(checkedOverlapping, 0,
                    "expected at least one real map-floor renderer whose bounds overlap area 1's atlas floor");

                float atlasTop = atlasBounds.max.y;
                Assert.GreaterOrEqual(atlasTop - lowestMapFloorTop, 0.009f,
                    $"atlas floor top ({atlasTop:F4}) must stand at least 0.009 above the hidden map floor's own " +
                    $"top ({lowestMapFloorTop:F4}) so it can never lose a z-fight to it again");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
