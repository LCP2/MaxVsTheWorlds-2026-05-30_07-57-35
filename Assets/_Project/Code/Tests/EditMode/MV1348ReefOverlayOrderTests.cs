using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1348: Reef floor transparents flickered as Max moved because a map-wide caustics quad, the
    /// current-lane bands and the follow-the-player marine snow all sat in the same render queue and
    /// were re-sorted by camera distance every frame; MV-1342's light shafts (inverted cones with
    /// hexagon lids) were removed outright. Asserts RESOLVED values on the built Reef map (Tier 2):
    /// no light-shaft object, every caustics renderer's bounds inside ONE zone's footprint, and the
    /// materials' resolved render queues strictly ordered caustics &lt; lanes &lt; snow.
    /// Fails on base: the single map-wide caustics quad spans every zone, and all three layers resolve
    /// to the same Transparent queue.
    /// </summary>
    public sealed class MV1348ReefOverlayOrderTests
    {
        [Test]
        public void ReefMap_HasNoLightShafts_CausticsPerArea_AndFixedOrderedQueues()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV1348 Reef Probe Root");
            try
            {
                MapRuntime.Build(map, host.transform);
                ReefAmbientLife.Build(host.transform);
                ReefAmbientLife.BuildCausticsOverlay(host.transform, map);

                Assert.IsFalse(host.GetComponentsInChildren<Transform>(true).Any(t => t.name.Contains("LightShaft")),
                    "no light-shaft object may exist under the Reef");

                var caustics = host.GetComponentsInChildren<ReefCausticsOverlay>(true)
                    .Select(c => c.GetComponent<MeshRenderer>()).ToArray();
                Assert.Greater(caustics.Length, 1, "caustics must be one quad per area, not one for the map");
                foreach (MeshRenderer r in caustics)
                {
                    Bounds b = r.bounds;
                    bool inside = map.zones.Any(z => z != null &&
                        b.min.x >= z.XMin - 0.01f && b.max.x <= z.XMax + 0.01f &&
                        b.min.z >= z.ZMin - 0.01f && b.max.z <= z.ZMax + 0.01f);
                    Assert.IsTrue(inside, $"caustics bounds {b} must lie inside a single area footprint");
                }

                var lanes = host.GetComponentsInChildren<MapCurrentLaneVisual>(true)
                    .Select(l => l.GetComponent<MeshRenderer>()).ToArray();
                Assert.Greater(lanes.Length, 0, "the Reef map must build at least one current lane");
                var snow = host.GetComponentsInChildren<ReefMarineSnow>(true)
                    .Select(s => s.GetComponent<ParticleSystemRenderer>()).ToArray();
                Assert.Greater(snow.Length, 0, "the Reef map must build marine snow");

                int causticsMax = caustics.Max(r => r.sharedMaterial.renderQueue);
                int laneMin = lanes.Min(r => r.sharedMaterial.renderQueue);
                int laneMax = lanes.Max(r => r.sharedMaterial.renderQueue);
                int snowMin = snow.Min(r => r.sharedMaterial.renderQueue);
                Assert.Less(causticsMax, laneMin, "caustics queue must sit below the current lanes");
                Assert.Less(laneMax, snowMin, "current lanes must sit below the marine snow");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
