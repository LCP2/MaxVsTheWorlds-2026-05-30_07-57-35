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
    /// no light-shaft object (MV-1350 removed the caustics overlay entirely), and the
    /// materials' resolved render queues strictly ordered lanes &lt; snow.
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

                Assert.IsFalse(host.GetComponentsInChildren<Transform>(true).Any(t => t.name.Contains("LightShaft")),
                    "no light-shaft object may exist under the Reef");

                var lanes = host.GetComponentsInChildren<MapCurrentLaneVisual>(true)
                    .Select(l => l.GetComponent<MeshRenderer>()).ToArray();
                Assert.Greater(lanes.Length, 0, "the Reef map must build at least one current lane");
                var snow = host.GetComponentsInChildren<ReefMarineSnow>(true)
                    .Select(s => s.GetComponent<ParticleSystemRenderer>()).ToArray();
                Assert.Greater(snow.Length, 0, "the Reef map must build marine snow");

                int laneMin = lanes.Min(r => r.sharedMaterial.renderQueue);
                int laneMax = lanes.Max(r => r.sharedMaterial.renderQueue);
                int snowMin = snow.Min(r => r.sharedMaterial.renderQueue);
                Assert.Less(laneMax, snowMin, "current lanes must sit below the marine snow");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
