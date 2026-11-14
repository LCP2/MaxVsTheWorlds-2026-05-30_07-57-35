using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1361: Reef floor layers z-fought (flicker as Max moved) because the legacy grates sat at exactly the
    /// deck's Y and the wear layer / current lanes sat 5 mm from it. Asserts RESOLVED world-space bounds
    /// on the built Reef (Tier 2): no two flat floor layers whose XZ bounds overlap have top surfaces
    /// within 0.01 m of each other. Fails on base: wear (0.02) vs deck (0.015), and lanes (0.02) vs wear.
    /// </summary>
    public sealed class MV1361ReefFloorSeparationTests
    {
        private const float MinSeparation = 0.01f;

        [Test]
        public void ReefFloorLayers_OverlappingBounds_AreAtLeastOneCentimetreApartInY()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV1361 Reef Probe Root");
            try
            {
                MapRuntime.Build(map, host.transform);
                ReefFloorDetail.PlaceDeck(host.transform, cfg);

                var layers = new List<Renderer>();
                foreach (Renderer r in host.GetComponentsInChildren<Renderer>(true))
                {
                    string n = r.name;
                    if (n == "Map Floor" || n.StartsWith("Reef Deck ") && !n.StartsWith("Reef Deck Props") || n.StartsWith("Reef Wear ")
                        || r.GetComponent<MapCurrentLaneVisual>() != null || r.GetComponent<SludgeFlow>() != null)
                        layers.Add(r);
                }
                Assert.Greater(layers.Count(r => r.name.StartsWith("Reef Wear ")), 0, "probe must find the wear layers");

                for (int i = 0; i < layers.Count; i++)
                for (int j = i + 1; j < layers.Count; j++)
                {
                    Bounds a = layers[i].bounds, b = layers[j].bounds;
                    bool overlapXz = a.min.x < b.max.x && b.min.x < a.max.x && a.min.z < b.max.z && b.min.z < a.max.z;
                    if (!overlapXz) continue;
                    float sep = Mathf.Abs(a.max.y - b.max.y);
                    Assert.GreaterOrEqual(sep, MinSeparation,
                        $"{layers[i].name} (top y {a.max.y:F4}) and {layers[j].name} (top y {b.max.y:F4}) overlap in XZ but are only {sep:F4} m apart");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
