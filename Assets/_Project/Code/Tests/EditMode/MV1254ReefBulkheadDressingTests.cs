using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1254: World 3's (the Reef's) 281 <c>aN_hM</c> hull-fill cover pieces were authored
    /// <c>"dressing": "none", "height": 1.8</c> — the plain grey slabs Lee called out as the biggest
    /// bland surfaces in the world. The V9 layout (<c>world3_config.json</c>, this ticket's own byte-
    /// for-byte copy) raises every one of them to <c>"dressing": "reef_bulkhead", "height": 3.0</c>.
    ///
    /// <see cref="CoverDressing.ReefBulkhead"/> does not exist before this ticket, so this fails to
    /// COMPILE on the base commit — the same "doesn't exist there yet" fail-first shape
    /// MV1215ReefStructureKitTests documents for its own base commit.
    ///
    /// Loads the REAL shipped config through the production path (<see cref="WorldLibrary.Load"/> ->
    /// <see cref="WorldMapLoader.TryLoad"/> -> <see cref="MapRuntime.Build"/>, not a hand-rolled
    /// fixture map), then asserts RESOLVED values only (testing policy Rule 2, Tier 2): piece
    /// <c>a1_h1</c>'s own <see cref="ArenaCover.Dressing"/> and resolved height
    /// (<see cref="ArenaCover.Size"/>.y — the value <see cref="Rendering.ReefKit.BuildBulkhead"/> and
    /// every collision/sight check actually reads), not the authored JSON string.
    /// </summary>
    public sealed class MV1254ReefBulkheadDressingTests
    {
        [Test]
        public void A1H1HullFillPiece_ResolvesToReefBulkheadAtWallHeight()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV1254 World3 Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                CoverPiece a1H1 = built.Cover.FirstOrDefault(p => p.Cover.Name == "a1_h1");
                Assert.IsNotNull(a1H1.Body, "World 3 must still author hull-fill piece 'a1_h1' for this test to mean anything");

                Assert.AreEqual(CoverDressing.ReefBulkhead, a1H1.Cover.Dressing,
                    "a1_h1 must resolve to CoverDressing.ReefBulkhead, not fall back to None");
                Assert.AreEqual(3f, a1H1.Cover.Size.y, 0.001f,
                    "a1_h1 must resolve to wall height 3 m, not the old 1.8 m slab height");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
