using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-942 — a16 ("Gantry Run") was the only World 2 area authoring a top-level <c>level: 1</c>
    /// (<see cref="WorldArea.level"/>'s own doc comment: "An area authored with overlays set is always
    /// the deck side of the pair and carries 1") with no <c>overlays</c> partner at all — unlike a15
    /// (overlays a13) and a17 (overlays a3), the only other two World 2 areas that author level 1. That
    /// false "I'm someone else's deck overlay" flag broke two things at once for the floor space under
    /// a16's own gantry platform: <see cref="MapGeometry.Walls"/> skips walling any zone with
    /// <c>level &gt; 0</c>, assuming its floor was already walled by the zone it overlays — true for
    /// a15/a17, false for a16, since nothing else claims its footprint — leaving a16's floor completely
    /// open; and <see cref="MapData.ZoneAt(float, float, float)"/>'s floor branch only ever matches a
    /// zone with <c>level == 0</c>, so a floor-height position under a16's deck resolved to no zone at
    /// all — the "outside every room"/void reading <see cref="MapData.ZoneAt(float, float)"/>'s own doc
    /// comment says this lookup exists to catch. Between an unwalled, unclaimed floor and
    /// AreaAccumulationDirector/RobotEnemy resolving area membership off this same lookup, a robot at
    /// floor level under a16 read as belonging nowhere and nothing physically stopped it standing there
    /// — TestFlight 0.9.9's "robots visible below the platform through the gaps".
    ///
    /// The fix removes a16's erroneous <c>level: 1</c> (world2_config.json) — the same "most areas never
    /// author this" default every other in-place-deck area (a10-a14, a18, a21) already uses. This test
    /// proves the resolved-value half of the fix (Tier 2, testing policy MV-465): a floor-level point
    /// under a16's own gantry, in a real gap between its 9 deck rects (world2_config.json a16.decks; this
    /// one clear of deck1's z&gt;=116, deck2's x 94-98 and deck3's x 98-146/z 106-110), resolves into
    /// a16's own zone instead of the void.
    ///
    /// Fails on 2ac29c2 (pre-fix, a16 still authors level: 1 with no overlays): <c>a16.level</c> reads 1
    /// and <c>ZoneAt</c> returns null at this point — see the fix comment for the quoted failure.
    /// </summary>
    public sealed class MV942A16FloorContainmentTests
    {
        [Test]
        public void FloorLevelPointUnderA16Deck_ResolvesIntoA16_NotTheVoid()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapZone a16 = map.Zone("area16");
            Assert.IsNotNull(a16, "setup failure: World 2 must build a zone for a16 (Gantry Run)");
            Assert.AreEqual(0, a16.level,
                "MV-942: a16 authors no 'overlays' partner, so its own zone must carry level 0 like " +
                "every other in-place-deck area (a10-a14, a18, a21) — level 1 is reserved for a true " +
                "overlay's deck side (a15/a17)");

            // A real gap between a16's 9 deck rects — 2 m in from a16's own west/south walls.
            float worldX = a16.XMin + 2f;
            float worldZ = a16.ZMin + 2f;

            MapZone resolved = map.ZoneAt(worldX, 0f, worldZ);
            Assert.IsNotNull(resolved,
                $"MV-942: ({worldX:0.#}, {worldZ:0.#}) at floor height under a16's deck resolved to no " +
                "zone at all (the void) — nothing walls it and nothing claims it, exactly what let a " +
                "robot exist there unaccounted for");
            Assert.AreEqual("area16", resolved.id,
                $"({worldX:0.#}, {worldZ:0.#}) is inside a16's own footprint and must resolve to a16, " +
                $"not '{resolved.id}'");
        }
    }
}
