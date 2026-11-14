using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1364: World 3 a1's two west-wall observation windows each built an ocean backdrop whose four layers
    /// (cyan fish, green jellyfish, purple manta, whale) were unrotated 6 m x 3 m quads, so they ran east-west
    /// straight through the west wall and stood in the room as stacked flat coloured slabs. Asserts a RESOLVED
    /// value (Tier 2): each layer renderer's world bounds, built through the real BuildWindows path, must not
    /// extend more than 0.05 m past the wall line into any area's floor rectangle.
    /// </summary>
    public sealed class MV1364OceanBackdropStaysOutsideRoomTests
    {
        private const float Tolerance = 0.05f;

        [Test]
        public void World3OceanBackdropLayers_DoNotExtendIntoAnyAreaPastTheWallLine()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV1364 World3 Probe Root");
            try
            {
                MapRuntime.Build(map, host.transform);
                var wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
                wm.Apply(WorldCatalog.Get(2).Palette);
                ReefKit.DressHull(host.transform);

                var walls = ReefKit.CollectStructuralWalls(host.transform);
                ReefAreaKit.BuildWindows(host.transform, cfg, walls, map.wallHeight, map.wallThickness);

                int layers = 0;
                float worst = 0f;
                string worstName = null;
                foreach (OceanLayer layer in host.GetComponentsInChildren<OceanLayer>(true))
                {
                    var rend = layer.GetComponent<Renderer>();
                    Assert.IsNotNull(rend, "every ocean layer carries a renderer");
                    layers++;
                    Bounds b = rend.bounds;
                    foreach (WorldArea area in cfg.areas)
                    {
                        if (area == null) continue;
                        float overlapX = Mathf.Min(b.max.x, area.XMax) - Mathf.Max(b.min.x, area.XMin);
                        float overlapZ = Mathf.Min(b.max.z, area.ZMax) - Mathf.Max(b.min.z, area.ZMin);
                        if (overlapX < 0f || overlapZ < 0f) continue; // a flat quad has zero thickness, so touching counts
                        // How far the layer's footprint reaches into this area's floor rectangle.
                        float intrusion = Mathf.Max(overlapX, overlapZ);
                        if (intrusion > worst) { worst = intrusion; worstName = $"{layer.Kind} into {area.id}"; }
                    }
                }

                Assert.Greater(layers, 0, "World 3 authors windows, so at least one ocean layer must be built");
                Assert.LessOrEqual(worst, Tolerance,
                    $"an ocean backdrop layer reaches {worst:F2} m into a room past its wall line ({worstName}); " +
                    $"limit {Tolerance} m");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
