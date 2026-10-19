using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1262: every area's authored <see cref="WorldArea.windows"/> must resolve to a built
    /// "ObservationWindow" GameObject (<see cref="ReefKit.BuildObservationWindow"/>) — area a2's
    /// authored S+W window run silently resolved to zero, because <see cref="ReefAreaKit.FindAreaWall"/>
    /// matched a candidate wall's length/orientation off <see cref="Transform.lossyScale"/>, which
    /// <c>MapRuntime.Build</c>'s own <c>ApplyBevelledBoxMesh</c> (MV-778) resets to
    /// <see cref="Vector3.one"/> on every real wall once it bakes the true size into the mesh — the
    /// same root cause <c>ShipAreaKit</c>'s own header doc already names for its sibling kit. Asserts a
    /// RESOLVED value, the built window count against the authored window-entry count summed across
    /// every area (testing policy Tier 2) — never an authored constant, never a rendered pixel — so a
    /// future silent drop like this fails loudly instead of nobody noticing.
    /// </summary>
    public sealed class MV1262ReefAreaWindowsResolveTests
    {
        [Test]
        public void World3Map_BuiltObservationWindowCount_MatchesAuthoredWindowCount()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            int authoredCount = 0;
            foreach (WorldArea area in cfg.areas)
                authoredCount += area?.windows?.Length ?? 0;
            Assert.Greater(authoredCount, 0,
                "World 3's own shipped config must author at least one window for this test to mean anything");

            var host = new GameObject("MV1262 World3 Probe Root");
            try
            {
                MapRuntime.Build(map, host.transform);

                var wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
                wm.Apply(WorldCatalog.Get(2).Palette);

                ReefKit.DressHull(host.transform);

                var walls = ReefKit.CollectStructuralWalls(host.transform);
                float wallHeight = map.wallHeight;
                float wallThickness = map.wallThickness;
                ReefAreaKit.BuildWindows(host.transform, cfg, walls, wallHeight, wallThickness);

                int builtCount = 0;
                foreach (Transform t in host.GetComponentsInChildren<Transform>(true))
                    if (t.name == "ObservationWindow") builtCount++;

                Assert.AreEqual(authoredCount, builtCount,
                    $"World 3 authors {authoredCount} window(s) across its areas but only {builtCount} " +
                    "built ObservationWindow object(s) resolved -- a silent drop (FindAreaWall failing " +
                    "to match a real wall) must fail loudly, not disappear.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
