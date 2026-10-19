using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1256 AC1: the Reef deck's circuit inlay used to bake exactly ONE fixed pattern into a single
    /// repeating 2 m plate image (MV-1053's own <see cref="StylizedTextures.ReefDeckEmission"/>), so
    /// the whole floor read as a uniform tile grid. <see cref="ReefKit.ApplyDeckTiling"/> now bakes a
    /// <see cref="WorldMaterials.ReefDeckGridCells"/>-square "supertile" of individually varied plates
    /// instead, and attaches a <see cref="ReefDeckPlateField"/> to the floor recording which of
    /// <see cref="WorldMaterials.ReefDeckInlayPatternCount"/> inlay patterns
    /// <see cref="StylizedTextures.ReefDeckPatternIndexAt"/>'s own seeded hash picked for each cell
    /// (ticket, Change item 2: "at least 4 inlay patterns, picked per plate from a seeded hash").
    ///
    /// Fails before this fix: <c>ReefDeckPlateField</c> does not exist on the base commit — the same
    /// "doesn't exist there yet" fail-first shape <c>MV1254ReefBulkheadDressingTests</c> documents for
    /// its own base commit.
    ///
    /// Loads the REAL shipped config through the production path (<see cref="WorldLibrary.Load"/> -&gt;
    /// <see cref="WorldMapLoader.TryLoad"/> -&gt; <see cref="MapRuntime.Build"/> -&gt;
    /// <see cref="ReefKit.DressHull"/>, same as <c>MV1053ReefDeckFloorTests</c>), then asserts a
    /// RESOLVED value: the floor's own attached <see cref="ReefDeckPlateField.PatternIndices"/>, never
    /// a hardcoded array literal sitting in this test.
    /// </summary>
    public sealed class MV1256ReefDeckDetailTests
    {
        [Test]
        public void World3Floor_ResolvesAtLeastFourDistinctInlayPatternIndices()
        {
            WorldConfig cfg3 = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg3, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg3, out MapData map3, out string reason3), reason3);

            var root3 = new GameObject("MV1256 World3 Probe Root");
            try
            {
                MapRuntime.Build(map3, root3.transform);
                var wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
                wm.Apply(WorldCatalog.Get(2).Palette);
                ReefKit.DressHull(root3.transform);

                Renderer floor = FindNamed(root3.transform, "Map Floor");
                Assert.IsNotNull(floor, "expected World 3's built map to include its floor slab");

                var field = floor.GetComponent<ReefDeckPlateField>();
                Assert.IsNotNull(field, "the dressed floor must carry a ReefDeckPlateField recording its resolved plate pattern layout");
                Assert.IsNotNull(field.PatternIndices, "PatternIndices must be populated, not left null");

                int distinct = field.PatternIndices.Distinct().Count();
                Assert.GreaterOrEqual(distinct, 4,
                    $"expected at least 4 distinct inlay pattern indices across the built floor's resolved " +
                    $"plate field, found {distinct} ({string.Join(",", field.PatternIndices)})");
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
