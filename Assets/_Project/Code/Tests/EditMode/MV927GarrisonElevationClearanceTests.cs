using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-927 — <see cref="MapValidation"/>'s garrison-vs-cover clearance rule measured XZ-plane
    /// distance only, so a level-1 entry standing on a deck (MV-697) was flagged against a floor-level
    /// cover box directly beneath it even though a deck-standing robot cannot physically overlap
    /// something well below its feet (World 2 V8's a11/a16 garrison entries, all four 0.0-0.5 m XZ from
    /// covers under their deck). The fix resolves the entry's own elevation and skips a cover once its
    /// top height sits below that elevation — the SAME 0.0 m XZ placement now passes when the entry is
    /// level 1 and the cover tops out under the deck, but a level-0 entry at the identical XZ placement
    /// still fails, proving the gate is elevation-aware rather than just disabled.
    /// </summary>
    public sealed class MV927GarrisonElevationClearanceTests
    {
        // A deck at 2.5 m (World 2's own deck height) sitting directly over a 1 m-tall cover box — same
        // shape as the ticket's measured a11 case: entry and cover share the exact same XZ point (0.0 m
        // gap), which is only safe because the entry stands 2.5 m above the cover's 1 m top.
        private static WorldConfig DeckOverCoverWorld(int entryLevel) => new WorldConfig
        {
            world = "Test World",
            dials = new WorldDials { areaCount = 1, baseThreat = 1f, threatGrowth = 0f, pacingRhythm = new[] { 1f }, deckHeight = 2.5f },
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", role = "entry",
                    origin = new WorldAreaOrigin { x = 13f, z = -6f },
                    size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    id = "a1", index = 1, role = "normal",
                    origin = new WorldAreaOrigin { x = 0f, z = 0f },
                    size = new WorldAreaSize { w = 30f, d = 30f },
                    composition = new WorldComposition { rusher = 1 },
                    decks = new[] { new WorldDeck { id = "deck1", x = 5f, z = 5f, w = 10f, d = 10f, height = 2.5f } },
                    garrison = new[] { new WorldGarrisonEntry { kind = "rusher", level = entryLevel, x = 10f, z = 10f } },
                    cover = new[] { new WorldCover { id = "cover1", x = 10f, z = 10f, width = 1f, height = 1f, depth = 1f } },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
            },
        };

        [Test]
        public void DeckLevelEntry_ClearsCoverBelowDeckHeight_ButSameXZAtFloorLevelStillFails()
        {
            Assert.IsTrue(
                MapValidation.ValidateWorldConfig(DeckOverCoverWorld(entryLevel: 1), out string deckReason),
                deckReason);

            Assert.IsFalse(
                MapValidation.ValidateWorldConfig(DeckOverCoverWorld(entryLevel: 0), out string floorReason));
            Assert.IsTrue(floorReason.Contains("cover1"),
                $"expected the floor-level entry to still be flagged against cover1, got: {floorReason}");
        }
    }
}
