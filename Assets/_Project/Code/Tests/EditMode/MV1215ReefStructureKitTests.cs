using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1215: the Reef's structure kit — pillar, manifold, drum, console, planter, floor pipe, crate
    /// — adds seven <see cref="CoverDressing"/> values (Lee's 17 Oct 2026 concept-frame approval). Fails
    /// to compile on the base commit (de7fe68): none of <see cref="CoverDressing.ReefPillar"/> through
    /// <see cref="CoverDressing.ReefCrate"/> exist yet, so every reference below is a CS0117 "does not
    /// contain a definition for ..." error — the same fail-first shape CityKitTests/MarketStreetBuildTests
    /// already use for a brand-new vocabulary.
    ///
    /// Builds a fixture Reef map with one cover piece of each new dressing through the production
    /// <see cref="MapRuntime.Build"/> cover-build path (<c>MapRuntime.BuildCover</c>, not a hand-rolled
    /// substitute), then asserts RESOLVED values only (testing policy Rule 2, Tier 2): each piece's own
    /// <see cref="ArenaCover.Dressing"/> (proves the authored string actually resolves to the real
    /// member, not a silent fallback to <see cref="CoverDressing.None"/>), <see cref="ArenaCover.Kind"/>
    /// (the ticket's own table: Planter/FloorPipe see-through, everything else solid) and, for the drum,
    /// <see cref="ArenaCover.Shape"/> (<see cref="CoverShape.Cylinder"/>).
    /// </summary>
    public sealed class MV1215ReefStructureKitTests
    {
        // Distinctive, far-off origin — same reasoning MV745/MV863/MV917's own fixtures give: EditMode
        // tests share one physics scene for the whole cc-verify run, with no per-test reset.
        private static readonly Vector3 RigOrigin = new Vector3(-71400f, 0f, 58900f);

        private static MapData Probe()
        {
            return new MapData
            {
                name = "Reef Structure Kit Probe",
                zones = new[]
                {
                    new MapZone
                    {
                        id = "room", type = "open",
                        x = RigOrigin.x, z = RigOrigin.z, width = 100f, depth = 20f,
                    },
                },
                entities = new[]
                {
                    Piece("pillar", 0, "reef_pillar", "box", "solid", 2f, 3f, 2f),
                    Piece("manifold", 6, "reef_manifold", "box", "solid", 4f, 1.8f, 2f),
                    Piece("drum", 14, "reef_drum", "cylinder", "solid", 3f, 2.4f, 3f),
                    Piece("console", 22, "reef_console", "box", "solid", 2f, 1f, 1f),
                    Piece("planter", 28, "reef_planter", "box", "see_through", 1f, 0.9f, 3f),
                    Piece("floorpipe", 34, "reef_floor_pipe", "box", "see_through", 1f, 0.5f, 4f),
                    Piece("crate", 40, "reef_crate", "box", "solid", 1.5f, 1.5f, 1.5f),
                },
            };
        }

        private static MapEntity Piece(string id, float xOffset, string dressing, string shape,
            string coverKind, float width, float height, float depth)
        {
            return new MapEntity
            {
                id = id, kind = "cover",
                x = RigOrigin.x + xOffset, z = RigOrigin.z,
                width = width, height = height, depth = depth,
                shape = shape, dressing = dressing, coverKind = coverKind,
            };
        }

        [Test]
        public void EveryNewReefDressing_ResolvesItsAuthoredDressingKindAndShape()
        {
            MapData map = Probe();
            var root = new GameObject("MV-1215 Reef Structure Kit Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);

                CoverPiece pillar = built.Cover.Find(p => p.Cover.Name == "pillar");
                CoverPiece manifold = built.Cover.Find(p => p.Cover.Name == "manifold");
                CoverPiece drum = built.Cover.Find(p => p.Cover.Name == "drum");
                CoverPiece console = built.Cover.Find(p => p.Cover.Name == "console");
                CoverPiece planter = built.Cover.Find(p => p.Cover.Name == "planter");
                CoverPiece floorPipe = built.Cover.Find(p => p.Cover.Name == "floorpipe");
                CoverPiece crate = built.Cover.Find(p => p.Cover.Name == "crate");

                Assert.IsNotNull(pillar.Body, "the probe's pillar cover piece was never built");
                Assert.IsNotNull(manifold.Body, "the probe's manifold cover piece was never built");
                Assert.IsNotNull(drum.Body, "the probe's drum cover piece was never built");
                Assert.IsNotNull(console.Body, "the probe's console cover piece was never built");
                Assert.IsNotNull(planter.Body, "the probe's planter cover piece was never built");
                Assert.IsNotNull(floorPipe.Body, "the probe's floor pipe cover piece was never built");
                Assert.IsNotNull(crate.Body, "the probe's crate cover piece was never built");

                // Dressing: the authored string actually resolves to the real member, not a silent
                // fallback to CoverDressing.None.
                Assert.AreEqual(CoverDressing.ReefPillar, pillar.Cover.Dressing);
                Assert.AreEqual(CoverDressing.ReefManifold, manifold.Cover.Dressing);
                Assert.AreEqual(CoverDressing.ReefDrum, drum.Cover.Dressing);
                Assert.AreEqual(CoverDressing.ReefConsole, console.Cover.Dressing);
                Assert.AreEqual(CoverDressing.ReefPlanter, planter.Cover.Dressing);
                Assert.AreEqual(CoverDressing.ReefFloorPipe, floorPipe.Cover.Dressing);
                Assert.AreEqual(CoverDressing.ReefCrate, crate.Cover.Dressing);

                // Kind: Planter/FloorPipe see-through, every other new dressing solid (ticket table).
                Assert.AreEqual(CoverKind.Solid, pillar.Cover.Kind);
                Assert.AreEqual(CoverKind.Solid, manifold.Cover.Kind);
                Assert.AreEqual(CoverKind.Solid, drum.Cover.Kind);
                Assert.AreEqual(CoverKind.Solid, console.Cover.Kind);
                Assert.AreEqual(CoverKind.SeeThrough, planter.Cover.Kind);
                Assert.AreEqual(CoverKind.SeeThrough, floorPipe.Cover.Kind);
                Assert.AreEqual(CoverKind.Solid, crate.Cover.Kind);

                // Shape: the drum alone is a cylinder.
                Assert.AreEqual(CoverShape.Cylinder, drum.Cover.Shape);
                Assert.AreEqual(CoverShape.Box, pillar.Cover.Shape);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
