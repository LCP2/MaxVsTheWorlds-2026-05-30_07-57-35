using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1054: World 3 read as a flat dark void past its own edge — <see cref="ReefKit.BuildOceanBackdrop"/>
    /// existed (MV-713) but only ever built the four small per-window parallax layers, and nothing in a
    /// World 3 load placed a true exterior backdrop at all. Fails on 12419fa (the commit before this fix):
    /// <c>ReefKit</c> carries no <c>BuildOceanVoid</c> method, so this does not compile there.
    ///
    /// Asserts RESOLVED state only, through the same real load path MV745World3HullDressingTests and
    /// MV1053ReefDeckFloorTests already prove out (<see cref="MapRuntime.Build"/> -&gt;
    /// <see cref="WorldMaterials.Apply"/> -&gt; <see cref="ReefKit.DressHull"/>): the backdrop's own
    /// resolved renderer bounds against the floor's own resolved renderer bounds — never an authored
    /// constant compared to itself.
    /// </summary>
    public sealed class MV1054OceanVoidTests
    {
        [Test]
        public void World3OceanVoid_SpansMapBoundsInflatedBy40m_WellBelowFloor_NoCollider()
        {
            WorldConfig cfg3 = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg3, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg3, out MapData map3, out string reason3), reason3);

            var root3 = new GameObject("MV1054 World3 Probe Root");
            try
            {
                // Exactly BackyardPath.Awake's own order: geometry, the world's palette sweep, then the
                // Reef-only hull pass this ticket adds the void backdrop to.
                MapRuntime.Build(map3, root3.transform);
                var wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
                wm.Apply(WorldCatalog.Get(2).Palette);
                ReefKit.DressHull(root3.transform);

                Renderer floor = FindNamed(root3.transform, "Map Floor");
                Assert.IsNotNull(floor, "expected World 3's built map to include its floor slab");
                Bounds floorBounds = floor.bounds;

                GameObject voidGo = ReefKit.BuildOceanVoid(root3.transform);
                Assert.IsNotNull(voidGo, "BuildOceanVoid must build something against World 3's own built floor");

                Renderer voidRenderer = voidGo.GetComponent<Renderer>();
                Assert.IsNotNull(voidRenderer, "the ocean void must carry its own renderer");
                Bounds voidBounds = voidRenderer.bounds;

                Bounds inflated = new Bounds(floorBounds.center, floorBounds.size + new Vector3(80f, 0f, 80f));

                Assert.That(voidBounds.min.x, Is.LessThanOrEqualTo(inflated.min.x),
                    $"the ocean void ({voidBounds.min.x:F1}) must reach at least 40 m past the map's -X edge ({inflated.min.x:F1})");
                Assert.That(voidBounds.max.x, Is.GreaterThanOrEqualTo(inflated.max.x),
                    $"the ocean void ({voidBounds.max.x:F1}) must reach at least 40 m past the map's +X edge ({inflated.max.x:F1})");
                Assert.That(voidBounds.min.z, Is.LessThanOrEqualTo(inflated.min.z),
                    $"the ocean void ({voidBounds.min.z:F1}) must reach at least 40 m past the map's -Z edge ({inflated.min.z:F1})");
                Assert.That(voidBounds.max.z, Is.GreaterThanOrEqualTo(inflated.max.z),
                    $"the ocean void ({voidBounds.max.z:F1}) must reach at least 40 m past the map's +Z edge ({inflated.max.z:F1})");

                Assert.That(voidBounds.max.y, Is.LessThanOrEqualTo(floorBounds.max.y - 4f),
                    $"the ocean void's top ({voidBounds.max.y:F2}) must sit at least 4 m below the floor's own top ({floorBounds.max.y:F2})");

                Assert.IsNull(voidGo.GetComponent<Collider>(), "the ocean void must carry no collider");
            }
            finally
            {
                Object.DestroyImmediate(root3);
            }
        }

        private static Renderer FindNamed(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.GetComponent<Renderer>();
            return null;
        }
    }
}
