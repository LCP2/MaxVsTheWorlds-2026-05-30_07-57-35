using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1258 AC1: "Load the Reef and assert that each of the six room types resolves at least one
    /// set-piece root in at least one of its areas, and that a3's set-piece root is a reactor core."
    /// <see cref="MaxWorlds.Arena.ReefSetPieces"/> does not exist before this ticket, so this fails to
    /// COMPILE on the base commit — the same "doesn't exist there yet" fail-first shape
    /// <c>MV1257ReefAmbientLifeTests</c> documents for its own base commit.
    ///
    /// Loads the REAL shipped config through the production path (<see cref="WorldLibrary.Load"/> ->
    /// <see cref="WorldMapLoader.TryLoad"/> -> <see cref="MapRuntime.Build"/>), then runs the same
    /// dressing sequence <c>BackyardPath.ApplyReefKit</c> itself runs (hull, cover, walls, windows,
    /// wall dressing, far-wall light) before calling <see cref="ReefSetPieces.Build"/> — so the areas,
    /// cover pieces and gates it reads back are the real ones a built Reef map actually has, not a
    /// hand-rolled fixture. Asserts RESOLVED values: the count of distinct room types a
    /// <see cref="ReefSetPieceRoot"/>-tagged GameObject's own <see cref="ReefSetPieceRoot.RoomType"/>
    /// covers, and a3's own tagged root's <see cref="ReefSetPieceRoot.Kind"/> (testing policy Tier 2) —
    /// never an authored constant, never a rendered pixel.
    /// </summary>
    public sealed class MV1258ReefSetPiecesTests
    {
        [Test]
        public void World3Map_ResolvesOneSetPieceRootPerRoomType_AndA3IsReactorCore()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV1258 World3 Probe Root");
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

                ReefSetPieces.Build(host.transform, cfg, built.Cover, walls, wallHeight, wallThickness);

                ReefSetPieceRoot[] roots = host.GetComponentsInChildren<ReefSetPieceRoot>(true);
                Assert.Greater(roots.Length, 0, "a built Reef map must resolve at least one hero set-piece root");

                var roomTypesSeen = new HashSet<string>();
                foreach (ReefSetPieceRoot r in roots)
                    if (!string.IsNullOrEmpty(r.RoomType)) roomTypesSeen.Add(r.RoomType);

                string[] expectedRoomTypes =
                {
                    "cargo_hold", "engine_room", "reactor_bay", "gallery", "pressure_lock", "crew_hydroponics",
                };
                foreach (string expected in expectedRoomTypes)
                    Assert.IsTrue(roomTypesSeen.Contains(expected),
                        $"room type '{expected}' resolved no hero set-piece root in any area");

                ReefSetPieceRoot a3Root = null;
                foreach (ReefSetPieceRoot r in roots)
                    if (r.AreaId == "a3") { a3Root = r; break; }

                Assert.IsNotNull(a3Root, "a3 (Reactor Junction) must resolve its own hero set-piece root");
                Assert.AreEqual(ReefSetPieceKind.ReactorCore, a3Root.Kind,
                    "a3 is a reactor-bay area; its set-piece root must be a reactor core");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
