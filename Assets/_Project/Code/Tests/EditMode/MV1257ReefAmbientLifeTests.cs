using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1257 AC1: "build the Reef ambient system for a loaded Reef map, and assert that the resolved
    /// number of active ambient particle emitters is greater than 0 and does not exceed a named budget
    /// constant." <see cref="MaxWorlds.Arena.ReefAmbientLife"/> does not exist before this ticket, so
    /// this fails to COMPILE on the base commit — the same "doesn't exist there yet" fail-first shape
    /// <c>MV1254ReefBulkheadDressingTests</c> documents for its own base commit.
    ///
    /// Loads the REAL shipped config through the production path (<see cref="WorldLibrary.Load"/> -&gt;
    /// <see cref="WorldMapLoader.TryLoad"/> -&gt; <see cref="MapRuntime.Build"/>), then runs the same
    /// dressing sequence <c>BackyardPath.ApplyReefKit</c> itself runs (hull, cover, walls, windows,
    /// far-wall light) before calling <see cref="ReefAmbientLife.Build"/> — so the drums/manifolds/
    /// consoles/lamps/strips it reads back by name are the real ones a built Reef map actually has, not
    /// a hand-rolled fixture. Asserts a RESOLVED value: the count of <see cref="ReefAmbientEmitter"/>-
    /// tagged GameObjects the built hierarchy actually carries, never an authored constant (testing
    /// policy Tier 2).
    /// </summary>
    public sealed class MV1257ReefAmbientLifeTests
    {
        [Test]
        public void World3Map_ResolvesAmbientEmitterCountWithinBudget()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV1257 World3 Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                var wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
                wm.Apply(WorldCatalog.Get(2).Palette);

                ReefKit.DressHull(host.transform);
                ReefDressing.DressCover(host.transform, built.Cover);

                var walls = ReefKit.CollectStructuralWalls(host.transform);
                float wallHeight = map.wallHeight;
                float wallThickness = map.wallThickness;
                ReefAreaKit.BuildDecor(host.transform, cfg);
                ReefAreaKit.BuildWindows(host.transform, cfg, walls, wallHeight, wallThickness);
                ReefAreaKit.BuildWallDressing(host.transform, cfg, walls, wallHeight, wallThickness);
                ReefAreaKit.BuildFarWallLight(host.transform, cfg, walls, wallHeight);

                ReefAmbientLife.Build(host.transform);

                ReefAmbientEmitter[] emitters = host.GetComponentsInChildren<ReefAmbientEmitter>(true);

                Assert.Greater(emitters.Length, 0,
                    "a built Reef map must resolve at least one active ambient particle emitter");
                Assert.LessOrEqual(emitters.Length, ReefAmbientLife.MaxAmbientEmitters,
                    $"resolved {emitters.Length} ambient particle emitters, over the " +
                    $"{ReefAmbientLife.MaxAmbientEmitters}-emitter budget");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
