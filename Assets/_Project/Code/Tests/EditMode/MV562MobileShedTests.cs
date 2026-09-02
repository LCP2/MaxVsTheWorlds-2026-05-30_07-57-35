using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Walking sheds are coming (MV-562): the schema needs a <see cref="WorldShed.mobile"/> flag
    /// before the movement behaviour lands. This pins the flag round-tripping through JSON and
    /// validating like any other shed. MV-655 deleted the separate mobile-shed clearance rule this
    /// class used to also cover (a mobile shed is now validated exactly like a static one), so the
    /// wall/cover-violation assertions that pinned that rule were removed with it.
    /// </summary>
    public sealed class MV562MobileShedTests
    {
        private static WorldArea ShedArea(WorldShed shed, WorldCover[] cover = null) => new WorldArea
        {
            id = "a1", index = 1, role = "shed", hasShed = true,
            origin = new WorldAreaOrigin { x = 0f, z = 0f },
            size = new WorldAreaSize { w = 30f, d = 30f },
            sheds = new[] { shed },
            cover = cover ?? System.Array.Empty<WorldCover>(),
        };

        private static WorldConfig OneAreaWorld(WorldArea area) => new WorldConfig
        {
            world = "Test World",
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
        public void MobileFlag_RoundTripsThroughJson_AndValidationGatesItsClearance()
        {
            // --- AC2: a config authoring "mobile": true loads, validates, and the flag survives the
            // round trip through WorldConfigLoader (JsonUtility under the hood).
            const string json = @"
            {
              ""world"": ""Test World"",
              ""dials"": {
                ""areaCount"": 1, ""baseThreat"": 14.0, ""threatGrowth"": 0.1,
                ""band"": { ""up"": 0.4, ""down"": -0.15 },
                ""pacingRhythm"": [1.0],
                ""toughnessCurve"": { ""heavyFromArea"": 5, ""bruteFromArea"": 8, ""toughSubstitutionPct"": 0.25, ""tankShareEnd"": 0.7 },
                ""powerupCadence"": 2
              },
              ""enemyTypes"": {
                ""small"": { ""thv"": 1.0 }, ""large"": { ""thv"": 2.5 }, ""heavy"": { ""thv"": 4.5 }, ""brute"": { ""thv"": 7.0 }
              },
              ""areas"": [
                { ""id"": ""stub"", ""index"": 0, ""role"": ""entry"", ""origin"": { ""x"": 13, ""z"": -6 }, ""size"": { ""w"": 4, ""d"": 6 } },
                { ""id"": ""a1"", ""index"": 1, ""role"": ""shed"", ""hasShed"": true,
                  ""origin"": { ""x"": 0, ""z"": 0 }, ""size"": { ""w"": 30, ""d"": 30 },
                  ""sheds"": [ { ""id"": ""s1"", ""x"": 15, ""z"": 15, ""mobile"": true } ] }
              ],
              ""gates"": [
                { ""id"": ""g0"", ""width"": 3, ""opensWith"": ""start"",
                  ""from"": { ""area"": ""stub"", ""wall"": ""N"", ""pos"": 0.5 },
                  ""to"": { ""area"": ""a1"", ""wall"": ""S"", ""pos"": 0.5 } }
              ]
            }";

            Assert.IsTrue(WorldConfigLoader.TryLoad(json, out WorldConfig cfg, out string loadReason), loadReason);
            WorldShed loaded = cfg.Area("a1").Sheds()[0];
            Assert.IsTrue(loaded.mobile, "the authored mobile:true flag did not round-trip");
            Assert.IsTrue(MapValidation.ValidateWorldConfig(cfg, out string validReason), validReason);

            // --- AC4: a config with no `mobile` field behaves exactly as today — defaults to false,
            // and a shed only 6 m from a wall (the only rule a single-shed area has ever been subject
            // to, and only once it carries more than one shed) still validates.
            var staticArea = ShedArea(new WorldShed { id = "s1", x = 6f, z = 15f });
            Assert.IsFalse(staticArea.Sheds()[0].mobile);
            Assert.IsTrue(MapValidation.ValidateWorldConfig(OneAreaWorld(staticArea), out string staticReason), staticReason);

            // --- MV-655: a mobile shed validates exactly like a static one now — same position, same
            // single-shed area, `mobile: true` changes nothing about whether it passes.
            var mobileArea = ShedArea(new WorldShed { id = "s1", x = 6f, z = 15f, mobile = true });
            Assert.IsTrue(MapValidation.ValidateWorldConfig(OneAreaWorld(mobileArea), out string mobileReason), mobileReason);
        }
    }
}
