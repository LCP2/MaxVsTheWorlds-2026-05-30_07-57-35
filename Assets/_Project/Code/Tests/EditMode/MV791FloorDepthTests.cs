using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-791: World 2's cast bays (<see cref="StormdrainKit.BuildBay"/>, MV-784) land their top face at
    /// exactly y = 0 — the same plane <see cref="MapGeometry.Floor"/>'s single "Map Floor" slab already
    /// occupies. Two coplanar renderers, one depth buffer: which one wins is decided by floating-point
    /// noise and flickers as the camera moves (Lee, on device, build edd9c3c: "there seem to be lines
    /// running left to right, and at times the cobbled floor disappears totally"). The crack/stain lifts
    /// (3-4 mm) sit in the same trap at this world's ~26 m camera distance on WebGL.
    ///
    /// ONE test (testing policy MV-465, Rule 1), asserting RESOLVED bounds only (Rule 2, Tier 2), against
    /// World 2's own shipped map through the real <see cref="MapRuntime"/> → <see cref="StormdrainDressing"/>
    /// pipeline. Fails on base commit edd9c3c with:
    ///
    /// <c>'MV791 host/Map: World 2 — Stormdrain/Map Floor' and 'MV791 host/Stormdrain
    /// Dressing/Floor Composition/Bay' top faces are only 0.0 mm apart at a shared XZ point
    /// Expected: greater than or equal to 0.00800000038f
    /// But was:  0.0f</c>
    /// </summary>
    public sealed class MV791FloorDepthTests
    {
        private const float MinSeparation = 0.008f;

        [Test]
        public void World2FloorLayers_ResolveDepthSeparated_WhileWorld1GroundStaysPut()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData w2map, out string reason), reason);

            var host = new GameObject("MV791 host").transform;
            try
            {
                MapBuild build = MapRuntime.Build(w2map, host);
                StormdrainDressing.Dress(host, w2map, build.Cover);

                // AC1: no two DISTINCT floor-layer renderers land within 0.008 m of each other's top
                // face at a shared XZ point. Scoped to the structural floor slabs the ticket's own table
                // names (Ground/Map Floor, Bay, Crack, Panel Joint) — a stain's own two stacked layers
                // (Halo/Core, Meniscus/Water) are one composite decal by design, deliberately close
                // together, and their own lift constants are what AC3 below actually gates.
                List<Renderer> layers = FloorLayerRenderers(host);
                Assert.Greater(layers.Count, 1, "expected multiple floor-layer renderers to compare");

                for (int i = 0; i < layers.Count; i++)
                {
                    for (int j = i + 1; j < layers.Count; j++)
                    {
                        Renderer a = layers[i], b = layers[j];
                        // Two instances of the SAME layer kind (two bays, two panel-joint segments, two
                        // cracks, ...) are tiled copies of one feature, deliberately coplanar with each
                        // other by design — the coincidence this ticket fixes is between DIFFERENT floor
                        // features sharing a plane by accident, not a feature sharing a plane with itself.
                        if (a.transform.name == b.transform.name) continue;
                        if (!XZOverlap(a.bounds, b.bounds)) continue;

                        float gap = Mathf.Abs(a.bounds.max.y - b.bounds.max.y);
                        Assert.GreaterOrEqual(gap, MinSeparation,
                            $"'{Path(a.transform)}' and '{Path(b.transform)}' top faces are only " +
                            $"{gap * 1000f:F1} mm apart at a shared XZ point");
                    }
                }

                // AC2: the arena Ground (the single "Map Floor" slab) is either disabled, or its top
                // face sits at least 0.20 m below the bays'.
                Transform mapFloor = FindDescendant(host, "Map Floor");
                Assert.IsNotNull(mapFloor, "expected a 'Map Floor' object in the built map");
                Renderer floorRenderer = mapFloor.GetComponent<Renderer>();

                Transform bay = FindDescendant(host, "Bay");
                Assert.IsNotNull(bay, "World 2's own shipped map cast no 'Bay' at all");
                float bayTop = bay.GetComponent<Renderer>().bounds.max.y;

                if (floorRenderer.enabled)
                    Assert.GreaterOrEqual(bayTop - floorRenderer.bounds.max.y, 0.20f,
                        $"Map Floor top ({floorRenderer.bounds.max.y:F3}) is not at least 0.20 m below " +
                        $"the bay top ({bayTop:F3})");

                // AC3: CrackLift/StainLift resolve to at least the ticket's own floor.
                Assert.GreaterOrEqual(StormdrainKit.CrackLift, 0.010f,
                    $"CrackLift resolved to {StormdrainKit.CrackLift}, below the 0.010 floor");
                Assert.GreaterOrEqual(StormdrainKit.StainLift, 0.018f,
                    $"StainLift resolved to {StormdrainKit.StainLift}, below the 0.018 floor");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }

            // AC4: World 1's ground plane is untouched — still enabled, still at its base-commit Y.
            WorldConfig w1cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(w1cfg, "World 1's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w1cfg, out MapData w1map, out string w1reason), w1reason);

            var w1host = new GameObject("MV791 world1 host").transform;
            try
            {
                MapRuntime.Build(w1map, w1host);

                Transform w1floor = FindDescendant(w1host, "Map Floor");
                Assert.IsNotNull(w1floor, "expected a 'Map Floor' object in World 1's built map");
                Renderer w1rend = w1floor.GetComponent<Renderer>();
                Assert.IsTrue(w1rend.enabled, "World 1's ground plane must stay enabled");
                Assert.AreEqual(-MapGeometry.FloorThickness * 0.5f, w1floor.localPosition.y, 0.0001f,
                    "World 1's ground plane must stay at its base-commit Y");
            }
            finally
            {
                Object.DestroyImmediate(w1host.gameObject);
            }
        }

        private static List<Renderer> FloorLayerRenderers(Transform host)
        {
            var names = new HashSet<string> { "Map Floor", "Bay", "Crack", "Panel Joint" };
            var result = new List<Renderer>();
            foreach (Transform t in host.GetComponentsInChildren<Transform>(true))
            {
                if (!names.Contains(t.name)) continue;
                Renderer r = t.GetComponent<Renderer>();
                if (r != null && r.enabled) result.Add(r);
            }
            return result;
        }

        private static bool XZOverlap(Bounds a, Bounds b) =>
            a.min.x < b.max.x && a.max.x > b.min.x && a.min.z < b.max.z && a.max.z > b.min.z;

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            for (Transform cur = t; cur != null; cur = cur.parent) parts.Insert(0, cur.name);
            return string.Join("/", parts);
        }
    }
}
