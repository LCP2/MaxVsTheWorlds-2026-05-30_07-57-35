using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1094: Lee, on device (TestFlight v0.11.7), World 3 area 10 (Drowned Mess Hall) and area 11
    /// (Salvage Winch Room) read as missing perimeter walls -- the floor simply ends on a dark void with
    /// only a faint line. Root cause (read in code, not re-derived): <see cref="ReefKit"/>'s
    /// BuildObservationGlass (MV-1054) swapped every outer-edge <see cref="StructuralWall"/>'s material
    /// to <see cref="WorldMaterials.M_GlassOcean"/>, a translucent pane that reads as no wall at all on a
    /// phone. The fix withdraws that swap: an outer-edge wall now keeps the same
    /// <see cref="WorldMaterials.M_ShipWall"/> every interior wall wears, with only a dark foot frame and
    /// cyan top strip overlay kept so the map edge still reads as distinct.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values (Rule 2, Tier 2)
    /// through the real per-world build path (<c>WorldMapLoader</c>/<c>MapRuntime</c>/
    /// <c>WorldMaterials</c>/<c>ReefKit</c> -- the same entry points
    /// <see cref="MV745World3HullDressingTests"/> already uses) against all three shipped worlds: every
    /// outer-edge wall's renderer is enabled and its resolved shared material is the SAME OBJECT an
    /// interior wall of that world wears, opaque (render queue below Transparent, base alpha 1.0); and a
    /// completeness sweep over <see cref="MapGeometry.Walls"/> -- the solver's own "every edge that needs
    /// a wall" list, with gate doorways and shared-footprint deck overlays already excluded -- finds a
    /// built wall carrying both a collider and an enabled renderer for every single segment, naming the
    /// zone and wall line for any it does not.
    ///
    /// Fails on base commit d30d293 for World 3: the outer-edge wall's resolved material is
    /// <c>WorldMaterials.M_GlassOcean</c>, a different object from the interior wall's
    /// <c>WorldMaterials.M_ShipWall</c> -- see the fix comment for the captured failure output.
    /// </summary>
    public sealed class MV1094EdgeWallsSolidTests
    {
        [Test]
        public void OuterEdgeWalls_AreSolidLikeInteriorWalls_AndEveryEdgeIsCovered()
        {
            AssertWorld(WorldLibrary.World1, worldIndex: 0);
            AssertWorld(WorldLibrary.World2, worldIndex: 1);
            AssertWorld(WorldLibrary.World3, worldIndex: 2);
        }

        private static void AssertWorld(string worldKey, int worldIndex)
        {
            int worldNumber = worldIndex + 1;   // BiomePalette.ForWorld is 0-based; messages read "World 1/2/3"

            WorldConfig cfg = WorldLibrary.Load(worldKey);
            Assert.IsNotNull(cfg, $"World {worldNumber}'s own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject($"MV1094 World{worldNumber} Probe Root");
            try
            {
                // Exactly BackyardPath.Awake's own order: geometry, then the world's palette sweep, then
                // the Reef-only pass (World 3 only -- the only one that re-touches a wall's material).
                MapRuntime.Build(map, root.transform);
                var wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
                wm.Apply(WorldCatalog.Get(worldIndex).Palette);
                if (worldIndex >= 2) ReefKit.DressHull(root.transform);

                List<StructuralWall> walls = AllWallComponents(root.transform);
                Assert.IsNotEmpty(walls, $"World {worldNumber}'s own map must solve to some walls");

                Renderer interiorReference = null;
                foreach (StructuralWall w in walls)
                {
                    if (w.IsOuterEdge) continue;
                    Renderer r = w.GetComponent<Renderer>();
                    if (r != null) { interiorReference = r; break; }
                }
                Assert.IsNotNull(interiorReference,
                    $"World {worldNumber} precondition: at least one interior (non-outer-edge) wall must exist to compare against");

                int outerEdgeCount = 0;
                foreach (StructuralWall w in walls)
                {
                    if (!w.IsOuterEdge) continue;
                    outerEdgeCount++;

                    Renderer r = w.GetComponent<Renderer>();
                    Assert.IsNotNull(r, $"World {worldNumber} outer-edge wall '{w.name}' carries no renderer");
                    Assert.IsTrue(r.enabled, $"World {worldNumber} outer-edge wall '{w.name}' has a disabled renderer");
                    Assert.AreSame(interiorReference.sharedMaterial, r.sharedMaterial,
                        $"World {worldNumber} outer-edge wall '{w.name}' must wear the SAME material object an interior wall wears, not a separate pane");
                    AssertOpaque(r.sharedMaterial, $"World {worldNumber} outer-edge wall '{w.name}'");
                }
                Assert.Greater(outerEdgeCount, 0,
                    $"World {worldNumber} precondition: its own map must solve at least one outer-edge wall");

                AssertEveryEdgeIsCovered(map, root.transform, worldNumber);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AssertOpaque(Material m, string label)
        {
            Assert.IsNotNull(m, $"{label}: has no material to check for opacity");
            Assert.Less(m.renderQueue, (int)RenderQueue.Transparent,
                $"{label}: material '{m.name}' renders in the transparent queue ({m.renderQueue}), not opaque");
            if (m.HasProperty("_BaseColor"))
                Assert.AreEqual(1f, m.GetColor("_BaseColor").a, 0.001f,
                    $"{label}: material '{m.name}' base colour alpha is not fully opaque");
        }

        /// <summary>AC item 4: every edge <see cref="MapGeometry.Walls"/> itself solved for (gate
        /// doorways and shared-footprint deck overlays already excluded by that method) must have come
        /// out the other end of <see cref="MapRuntime.Build"/> as a real wall with a collider and an
        /// enabled, opaque renderer. Matched by name: <c>MapRuntime.Build</c> names each built wall
        /// GameObject exactly <see cref="WallSegment.Name"/>, so a segment with no matching built wall
        /// means the build step itself dropped it.</summary>
        private static void AssertEveryEdgeIsCovered(MapData map, Transform root, int worldNumber)
        {
            var builtByName = new Dictionary<string, StructuralWall>();
            foreach (StructuralWall w in root.GetComponentsInChildren<StructuralWall>(true))
                builtByName[w.name] = w;

            foreach (WallSegment seg in MapGeometry.Walls(map))
            {
                MapZone zone = map.ZoneAt(seg.Center.x, seg.Center.z);
                string where = $"World {worldNumber}, area '{(zone != null ? zone.id : "(map boundary)")}', wall line '{seg.Name}'";

                Assert.IsTrue(builtByName.TryGetValue(seg.Name, out StructuralWall wall),
                    $"{where}: the map solver wants a wall here but none was built");

                Collider collider = wall.GetComponent<Collider>();
                Renderer renderer = wall.GetComponent<Renderer>();
                Assert.IsNotNull(collider, $"{where}: built wall carries no collider");
                Assert.IsNotNull(renderer, $"{where}: built wall carries no renderer");
                Assert.IsTrue(renderer.enabled, $"{where}: built wall's renderer is disabled");
                AssertOpaque(renderer.sharedMaterial, where);
            }
        }

        private static List<StructuralWall> AllWallComponents(Transform root)
        {
            var walls = new List<StructuralWall>();
            foreach (StructuralWall w in root.GetComponentsInChildren<StructuralWall>(true))
                if (w.GetComponent<Renderer>() != null) walls.Add(w);
            return walls;
        }
    }
}
