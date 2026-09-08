using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>MV-724: a garrisoned Grate Lurker authored at its grate tile's CENTRE (where a robot
    /// standing on the grate actually is) must pass <c>MapValidation.WorldLurkerGrates</c> — the rule
    /// used to require exact equality with the grate's authored corner, which every one of World 2's 21
    /// garrisoned Lurkers fails since they're all authored +0.5/+0.5 off that corner, at the tile centre.
    /// Fails on base commit 38dc2bd: the centre case (6.5, 6.5) is rejected as "no grate sits there".</summary>
    public sealed class MV724LurkerGrateContainmentTests
    {
        // The two passing cases: tile centre (6.5, 6.5, where a robot standing on the grate actually
        // is) and the grate's own origin (6, 6, the old passing case) — both must validate clean.
        private static WorldArea PassingArea() => new WorldArea
        {
            id = "a1", index = 1, role = "normal",
            origin = new WorldAreaOrigin { x = 0f, z = 0f },
            size = new WorldAreaSize { w = 30f, d = 30f },
            composition = new WorldComposition { lurker = 2 },
            grates = new[] { new WorldGrate { id = "g1", x = 6f, z = 6f } },
            garrison = new[]
            {
                new WorldGarrisonEntry { kind = "lurker", x = 6.5f, z = 6.5f },   // tile centre
                new WorldGarrisonEntry { kind = "lurker", x = 6f, z = 6f },       // origin
            },
        };

        // Same two passing entries plus a third, entry index 2, authored two tiles away (8.5, 8.5) —
        // nowhere near the (6, 6) grate's tile — which must fail and name area 'a1' and index 2.
        private static WorldArea FailingArea() => new WorldArea
        {
            id = "a1", index = 1, role = "normal",
            origin = new WorldAreaOrigin { x = 0f, z = 0f },
            size = new WorldAreaSize { w = 30f, d = 30f },
            composition = new WorldComposition { lurker = 3 },
            grates = new[] { new WorldGrate { id = "g1", x = 6f, z = 6f } },
            garrison = new[]
            {
                new WorldGarrisonEntry { kind = "lurker", x = 6.5f, z = 6.5f },   // tile centre — passes
                new WorldGarrisonEntry { kind = "lurker", x = 6f, z = 6f },       // origin — passes
                new WorldGarrisonEntry { kind = "lurker", x = 8.5f, z = 8.5f },   // two tiles away — fails
            },
        };

        private static WorldConfig OneAreaWorld(WorldArea area) => new WorldConfig
        {
            world = "Test World",
            dials = new WorldDials { areaCount = 1, baseThreat = 1f, threatGrowth = 0f, pacingRhythm = new[] { 1f } },
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", role = "entry",
                    origin = new WorldAreaOrigin { x = 13f, z = -6f },
                    size = new WorldAreaSize { w = 4f, d = 6f },
                },
                area,
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
        public void GarrisonedLurker_AnywhereOnItsGrateTilePasses_ButTwoTilesAwayFails()
        {
            Assert.IsTrue(
                MapValidation.ValidateWorldConfig(OneAreaWorld(PassingArea()), out string passingReason),
                passingReason);

            Assert.IsFalse(
                MapValidation.ValidateWorldConfig(OneAreaWorld(FailingArea()), out string failingReason));
            Assert.IsTrue(failingReason.Contains("a1") && failingReason.Contains("2"),
                $"expected the failure to name area 'a1' and entry index 2, got: {failingReason}");
        }
    }
}
