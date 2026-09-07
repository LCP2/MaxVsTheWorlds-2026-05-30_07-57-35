using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-697: an area gains <c>level</c>/<c>overlays</c> (World 2 revisits the same footprint twice —
    /// once on the floor, once on the gantry deck above it), a locked <c>hatches[]</c> opening on a deck
    /// cell, and a garrison entry's own elevation tier. The ONE new test this ticket adds (testing
    /// policy v2, MV-465): a synthetic two-record config (a floor area with a deck + hatch, and a second
    /// area that overlays it at level 1) resolves the RESOLVED area-by-position/level, the built hatch's
    /// position, and a level-1 garrison seed's resolved spawn height — never an authored constant. Fails
    /// to compile on cfb0698 (the MV-692 merge commit) — <c>WorldArea.level</c>/<c>overlays</c>/
    /// <c>hatches</c>, <c>WorldHatch</c> and <c>WorldGarrisonEntry.level</c> don't exist there.
    /// </summary>
    public sealed class MV697AreaLevelOverlayTests
    {
        /// <summary>Entry stub → aX (a floor room carrying one deck, one ramp reaching it, and one
        /// hatch on that deck) and aY (aX's own gantry revisit: <c>overlays: "aX"</c>, level 1, sharing
        /// aX's exact footprint) → a boss room, reachable from both aX (the ordinary floor route) and
        /// aY (a second doorway straight off the gantry) so <c>MapValidation.WorldReachability</c> is
        /// satisfied for every area including the overlay.</summary>
        private static WorldConfig OverlayWorld()
        {
            var origin = new WorldAreaOrigin { x = 0f, z = 0f };
            var size = new WorldAreaSize { w = 25f, d = 20f };

            return new WorldConfig
            {
                world = "Test World",
                dials = new WorldDials { deckHeight = 2.5f },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "stub", role = "entry",
                        origin = new WorldAreaOrigin { x = 0f, z = -6f },
                        size = new WorldAreaSize { w = 4f, d = 6f },
                    },
                    new WorldArea
                    {
                        id = "aX", role = "normal", level = 0,
                        origin = origin, size = size,
                        decks = new[] { new WorldDeck { id = "deck1", x = 8f, z = 5f, w = 3f, d = 12f, height = 2.5f } },
                        ramps = new[] { new WorldRamp { id = "ramp1", x = 8f, z = 2f, w = 3f, d = 3f } },
                        hatches = new[] { new WorldHatch { id = "hatch1", x = 8f, z = 5f, w = 3f, d = 3f } },
                    },
                    new WorldArea
                    {
                        id = "aY", role = "normal", overlays = "aX", level = 1,
                        origin = new WorldAreaOrigin { x = origin.x, z = origin.z },
                        size = new WorldAreaSize { w = size.w, d = size.d },
                    },
                    new WorldArea
                    {
                        id = "boss", role = "boss+exit",
                        origin = new WorldAreaOrigin { x = 0f, z = 20f },
                        size = new WorldAreaSize { w = 20f, d = 20f },
                    },
                },
                gates = new[]
                {
                    new WorldGate
                    {
                        id = "g0", width = 3f, opensWith = "start",
                        from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "aX", wall = "S", pos = 0.08f },
                    },
                    new WorldGate
                    {
                        id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                        from = new WorldGateEndpoint { area = "aX", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                    },
                    new WorldGate
                    {
                        id = "g1", width = 3f, opensWith = "start",
                        from = new WorldGateEndpoint { area = "aY", wall = "N", pos = 0.2f },
                        to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.2f },
                    },
                },
            };
        }

        [Test]
        public void OverlayArea_ResolvesByPositionAndLevel_BuildsItsHatchOnTheDeck_AndSeedsGarrisonAtDeckHeight()
        {
            WorldConfig cfg = OverlayWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            // aX and aY share the exact same footprint — only a probe's height should tell them apart.
            float cx = cfg.Area("aX").CenterXz.x, cz = cfg.Area("aX").CenterXz.y;
            Assert.AreEqual("aX", map.ZoneAt(cx, 0f, cz)?.id, "a floor-height probe should resolve to the floor area");
            Assert.AreEqual("aY", map.ZoneAt(cx, 2.5f, cz)?.id, "a deck-height probe should resolve to the overlay area");

            var root = new GameObject("MV697 Hatch Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Assert.IsTrue(built.Actors.TryGetValue("hatch1", out GameObject hatch), "the map built no hatch1 actor at all");
                Assert.IsNotNull(hatch.GetComponent<AreaGate>(), "a hatch must be an AreaGate instance");
                Assert.AreEqual(2.5f, hatch.transform.position.y, 0.01f, "the hatch should sit at its deck's resolved height");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            Garrison.Seed[] slots = LevelOneGarrisonSlots();
            Assert.AreEqual(2, slots.Length);
            Assert.GreaterOrEqual(slots[0].Position.y, 2.4f, "the first bolter should resolve onto the deck, not the floor");
            Assert.GreaterOrEqual(slots[1].Position.y, 2.4f, "the second bolter should resolve onto the deck, not the floor");
        }

        /// <summary>A standalone floor+overlay pair — deliberately not run through <see cref="WorldMapLoader"/>
        /// (which would also validate the garrison entries' kinds against a solved composition this
        /// fixture has no reason to author) — purely to resolve <see cref="Garrison.SeedSlots(WorldArea, int, WorldConfig)"/>'s
        /// level-1 spawn height for two authored bolters standing on aX's own deck.</summary>
        private static Garrison.Seed[] LevelOneGarrisonSlots()
        {
            var origin = new WorldAreaOrigin { x = 0f, z = 0f };
            var size = new WorldAreaSize { w = 25f, d = 20f };

            var floor = new WorldArea
            {
                id = "aX", origin = origin, size = size,
                decks = new[] { new WorldDeck { id = "deck1", x = 8f, z = 5f, w = 3f, d = 12f, height = 2.5f } },
            };
            var deck = new WorldArea
            {
                id = "aY", overlays = "aX", level = 1, origin = origin, size = size,
                garrison = new[]
                {
                    new WorldGarrisonEntry { kind = "bolter", level = 1, x = 9f,  z = 10f },
                    new WorldGarrisonEntry { kind = "bolter", level = 1, x = 10f, z = 12f },
                },
            };
            var cfg = new WorldConfig { dials = new WorldDials { deckHeight = 2.5f }, areas = new[] { floor, deck } };

            return Garrison.SeedSlots(deck, 2, cfg);
        }
    }
}
