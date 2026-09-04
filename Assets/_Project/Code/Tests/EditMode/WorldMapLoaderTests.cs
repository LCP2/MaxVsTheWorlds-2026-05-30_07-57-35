using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The 2D-area-placement + gates-on-any-wall-at-a-fraction map engine (MV-267, Confluence MVW
    /// 34439170 §7) — the schema, the validation rules it adds on top of the old engine's, and the
    /// converter into the <see cref="MapData"/> the rest of the game already runs on.
    /// </summary>
    public sealed class WorldMapLoaderTests
    {
        /// <summary>A small, valid three-area world: entry stub → a fight room → a boss room gated
        /// behind "all-sheds-destroyed". Every wall pair here sits on the same line on purpose, the way
        /// a real world config has to.</summary>
        private static WorldConfig SmallValidWorld()
        {
            return new WorldConfig
            {
                world = "Test World",
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "stub", role = "entry",
                        origin = new WorldAreaOrigin { x = -2f, z = -6f },
                        size = new WorldAreaSize { w = 4f, d = 6f },
                    },
                    new WorldArea
                    {
                        id = "a1", role = "normal",
                        origin = new WorldAreaOrigin { x = -10f, z = 0f },
                        size = new WorldAreaSize { w = 20f, d = 20f },
                    },
                    new WorldArea
                    {
                        id = "boss", role = "boss+exit",
                        origin = new WorldAreaOrigin { x = -10f, z = 20f },
                        size = new WorldAreaSize { w = 20f, d = 20f },
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
                    new WorldGate
                    {
                        id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                        from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                    },
                },
            };
        }

        [Test]
        public void AValidWorldConfig_LoadsIntoAPlayableMap()
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(SmallValidWorld(), out MapData map, out string reason), reason);

            Assert.AreEqual(3, map.zones.Length);
            Assert.AreEqual(2, map.links.Length);
            Assert.IsTrue(MapValidation.Validate(map, out string why), why);

            var gates = MapValidation.Kind(map, EntityKind.AreaGate);
            Assert.AreEqual(2, gates.Count, "each WorldGate should become one AreaGate entity");

            var spawns = MapValidation.Kind(map, EntityKind.PlayerSpawn);
            Assert.AreEqual(1, spawns.Count, "the loader should synthesise exactly one spawn in the entry area");
        }

        // ---------------------------------------------------------------- shrubbery cover (MV-318)

        [Test]
        public void AnAuthoredCoverPiece_BecomesACoverEntity()
        {
            WorldConfig cfg = SmallValidWorld();
            cfg.Area("a1").cover = new[]
            {
                new WorldCover
                {
                    id = "a1_shrub", x = -6f, z = 6f,
                    width = 4.5f, height = 1.8f, depth = 1.3f,
                    shape = "box", dressing = "hedge",
                },
            };

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapEntity shrub = map.Entity("a1_shrub");
            Assert.IsNotNull(shrub, "the authored cover piece did not round-trip into the map");
            Assert.AreEqual(EntityKind.Cover, shrub.Kind);
            Assert.AreEqual(CoverDressing.Hedge, shrub.Dressing);

            // Still an ordinary Cover entity as far as the rest of the engine is concerned — same
            // invariants apply (never seals a path, never crowds a spawn ring), no special case.
            Assert.IsTrue(MapValidation.Validate(map, out string why), why);
        }

        [Test]
        public void Validation_RejectsOverlappingAreas()
        {
            WorldConfig cfg = SmallValidWorld();
            cfg.Area("boss").origin = new WorldAreaOrigin { x = -5f, z = 10f }; // now sits on top of a1

            Assert.IsFalse(MapValidation.ValidateWorldConfig(cfg, out string reason));
            StringAssert.Contains("overlaps", reason);
        }

        [Test]
        public void Validation_RejectsAnAreaTheGateGraphCannotReach()
        {
            WorldConfig cfg = SmallValidWorld();

            var withOrphan = new System.Collections.Generic.List<WorldArea>(cfg.areas)
            {
                new WorldArea
                {
                    id = "orphan", role = "normal",
                    origin = new WorldAreaOrigin { x = 100f, z = 100f },
                    size = new WorldAreaSize { w = 20f, d = 20f },
                },
            };
            cfg.areas = withOrphan.ToArray();

            Assert.IsFalse(MapValidation.ValidateWorldConfig(cfg, out string reason));
            StringAssert.Contains("not reachable", reason);
        }

        [Test]
        public void Validation_RejectsABossAreaWithNoGateAtAll()
        {
            WorldConfig cfg = SmallValidWorld();
            cfg.gates = new[] { cfg.gates[0] }; // drop the gate into the boss room

            // Cutting the only way in also strands the room, so this proves whichever rule trips
            // first still names the boss room as the problem.
            Assert.IsFalse(MapValidation.ValidateWorldConfig(cfg, out string reason));
            StringAssert.Contains("boss", reason);
        }

        [Test]
        public void Validation_RejectsAGateJoiningNonOppositeWalls()
        {
            WorldConfig cfg = SmallValidWorld();
            cfg.gates[0].to.wall = "E"; // stub's N wall can't meet a1's E wall

            Assert.IsFalse(MapValidation.ValidateWorldConfig(cfg, out string reason));
            StringAssert.Contains("opposite walls", reason);
        }

        [Test]
        public void Validation_RejectsWallsThatDoNotSitOnTheSameLine()
        {
            WorldConfig cfg = SmallValidWorld();
            // Shrink the stub instead of moving a1 — moving a1 would also overlap it with 'boss' and
            // trip that check first, which is not the rule this test is isolating.
            cfg.Area("stub").size.d = 5f; // stub's N wall is now at z=-1, not z=0

            Assert.IsFalse(MapValidation.ValidateWorldConfig(cfg, out string reason));
            StringAssert.Contains("same line", reason);
        }

        [Test]
        public void Validation_RejectsADoorwayWiderThanTheWallsShare()
        {
            WorldConfig cfg = SmallValidWorld();
            cfg.gates[0].width = 50f; // the stub's whole wall is only 4 m

            Assert.IsFalse(MapValidation.ValidateWorldConfig(cfg, out string reason));
            StringAssert.Contains("does not fit", reason);
        }

        [Test]
        public void Validation_RejectsMoreOrFewerThanOneEntryArea()
        {
            WorldConfig cfg = SmallValidWorld();
            cfg.Area("stub").role = "normal";

            Assert.IsFalse(MapValidation.ValidateWorldConfig(cfg, out string reason));
            StringAssert.Contains("entry area", reason);
        }

        // ---------------------------------------------------------------- world1_config.json (MV-270)

        /// <summary>The LOCKED v1 (2026-08-05) World 1 config, embedded verbatim from MV-270 — proof
        /// this ticket's engine (not just a hand-built toy world) loads the real thing without a
        /// geometry error, per this ticket's AC3. Ticket 4 (MV-270) owns wiring it up as a playable
        /// map; this only proves the engine underneath it is sound.</summary>
        private const string World1ConfigJson = @"
{
  ""$schema"": ""MaxVsTheWorlds/world-config@0.6-draft"",
  ""world"": ""World 1 — Backyard"",
  ""revision"": ""LOCKED v1 (2026-08-05)"",
  ""dials"": {
    ""areaCount"": 8,
    ""baseThreat"": 14.0,
    ""threatGrowth"": 0.1,
    ""band"": { ""up"": 0.4, ""down"": -0.15 },
    ""pacingRhythm"": [1.0, 1.1, 1.15, 0.9, 1.1, 1.2, 0.9, 1.25],
    ""toughnessCurve"": { ""heavyFromArea"": 5, ""bruteFromArea"": 8, ""toughSubstitutionPct"": 0.25, ""tankShareEnd"": 0.7 },
    ""powerupCadence"": 2
  },
  ""enemyTypes"": {
    ""small"": { ""thv"": 1.0 },
    ""large"": { ""thv"": 2.5 },
    ""heavy"": { ""thv"": 4.5 },
    ""brute"": { ""thv"": 7.0 }
  },
  ""gates"": [
    { ""id"": ""g0"", ""from"": { ""area"": ""stub"", ""wall"": ""N"", ""pos"": 0.5 }, ""to"": { ""area"": ""a1"", ""wall"": ""S"", ""pos"": 0.5 }, ""width"": 3, ""opensWith"": ""start"" },
    { ""id"": ""g1"", ""from"": { ""area"": ""a1"", ""wall"": ""E"", ""pos"": 0.67 }, ""to"": { ""area"": ""a2"", ""wall"": ""W"", ""pos"": 0.3 }, ""width"": 3, ""opensWith"": ""primary"" },
    { ""id"": ""g2"", ""from"": { ""area"": ""a2"", ""wall"": ""N"", ""pos"": 0.5 }, ""to"": { ""area"": ""a3"", ""wall"": ""S"", ""pos"": 0.42 }, ""width"": 3, ""opensWith"": ""primary"" },
    { ""id"": ""g3"", ""from"": { ""area"": ""a3"", ""wall"": ""W"", ""pos"": 0.56 }, ""to"": { ""area"": ""a4"", ""wall"": ""E"", ""pos"": 0.3 }, ""width"": 3, ""opensWith"": ""primary"" },
    { ""id"": ""g4"", ""from"": { ""area"": ""a4"", ""wall"": ""N"", ""pos"": 0.5 }, ""to"": { ""area"": ""a5"", ""wall"": ""S"", ""pos"": 0.5 }, ""width"": 3, ""opensWith"": ""primary"" },
    { ""id"": ""g5"", ""from"": { ""area"": ""a5"", ""wall"": ""E"", ""pos"": 0.45 }, ""to"": { ""area"": ""a6"", ""wall"": ""W"", ""pos"": 0.36 }, ""width"": 3, ""opensWith"": ""primary"" },
    { ""id"": ""g6"", ""from"": { ""area"": ""a6"", ""wall"": ""N"", ""pos"": 0.5 }, ""to"": { ""area"": ""a7"", ""wall"": ""S"", ""pos"": 0.47 }, ""width"": 3, ""opensWith"": ""primary"" },
    { ""id"": ""g7"", ""from"": { ""area"": ""a7"", ""wall"": ""E"", ""pos"": 0.5 }, ""to"": { ""area"": ""a8"", ""wall"": ""W"", ""pos"": 0.5 }, ""width"": 3, ""opensWith"": ""primary"" },
    { ""id"": ""bg"", ""from"": { ""area"": ""a8"", ""wall"": ""N"", ""pos"": 0.5 }, ""to"": { ""area"": ""boss"", ""wall"": ""S"", ""pos"": 0.5 }, ""width"": 11, ""opensWith"": ""all-sheds-destroyed"" }
  ],
  ""areas"": [
    { ""id"": ""stub"", ""index"": 0, ""name"": ""Entry path"", ""role"": ""entry"", ""origin"": { ""x"": -3, ""z"": -8 }, ""size"": { ""w"": 6, ""d"": 8 }, ""hasShed"": false, ""garrisonDensity"": ""none"" },
    { ""id"": ""a1"", ""index"": 1, ""name"": ""Area 1"", ""role"": ""normal"", ""origin"": { ""x"": -11, ""z"": 0 }, ""size"": { ""w"": 22, ""d"": 18 }, ""hasShed"": false, ""garrisonDensity"": ""light"" },
    { ""id"": ""a2"", ""index"": 2, ""name"": ""Area 2"", ""role"": ""normal"", ""origin"": { ""x"": 11, ""z"": 6 }, ""size"": { ""w"": 20, ""d"": 20 }, ""hasShed"": false, ""garrisonDensity"": ""normal"" },
    { ""id"": ""a3"", ""index"": 3, ""name"": ""Area 3 — The Shed"", ""role"": ""shed"", ""origin"": { ""x"": 11, ""z"": 26 }, ""size"": { ""w"": 24, ""d"": 18 }, ""hasShed"": true, ""garrisonDensity"": ""normal"" },
    { ""id"": ""a4"", ""index"": 4, ""name"": ""Area 4"", ""role"": ""normal"", ""origin"": { ""x"": -15, ""z"": 30 }, ""size"": { ""w"": 26, ""d"": 20 }, ""hasShed"": false, ""garrisonDensity"": ""light"" },
    { ""id"": ""a5"", ""index"": 5, ""name"": ""Area 5"", ""role"": ""normal"", ""origin"": { ""x"": -15, ""z"": 50 }, ""size"": { ""w"": 26, ""d"": 22 }, ""hasShed"": false, ""garrisonDensity"": ""normal"" },
    { ""id"": ""a6"", ""index"": 6, ""name"": ""Area 6 — The Greenhouse"", ""role"": ""shed"", ""origin"": { ""x"": 11, ""z"": 52 }, ""size"": { ""w"": 28, ""d"": 22 }, ""hasShed"": true, ""garrisonDensity"": ""heavy"" },
    { ""id"": ""a7"", ""index"": 7, ""name"": ""Area 7"", ""role"": ""normal"", ""origin"": { ""x"": 11, ""z"": 74 }, ""size"": { ""w"": 30, ""d"": 22 }, ""hasShed"": false, ""garrisonDensity"": ""light"" },
    { ""id"": ""a8"", ""index"": 8, ""name"": ""Area 8 — The Toolshed"", ""role"": ""shed"", ""origin"": { ""x"": 41, ""z"": 73 }, ""size"": { ""w"": 32, ""d"": 24 }, ""hasShed"": true, ""garrisonDensity"": ""heavy"" },
    { ""id"": ""boss"", ""index"": 9, ""name"": ""Compost Clearing"", ""role"": ""boss+exit"", ""origin"": { ""x"": 40, ""z"": 97 }, ""size"": { ""w"": 34, ""d"": 26 }, ""hasShed"": false, ""garrisonDensity"": ""none"" }
  ]
}";

        [Test]
        public void World1Config_LoadsWithoutGeometryErrors()
        {
            Assert.IsTrue(WorldMapLoader.TryLoadJson(World1ConfigJson, out MapData map, out string reason), reason);

            Assert.AreEqual(10, map.zones.Length, "8 combat areas + entry stub + compost clearing");
            Assert.AreEqual(9, map.links.Length, "g0..g7 plus the boss gate bg");

            Assert.IsTrue(MapValidation.Validate(map, out string why), why);

            MapZone boss = map.Zone("boss");
            Assert.IsNotNull(boss);
            Assert.AreEqual(ZoneKind.Boss, boss.Kind);

            MapEntity bossGate = map.Entity("bg");
            Assert.IsNotNull(bossGate, "the boss gate entity did not round-trip");
        }

        [Test]
        public void World1Config_EveryAreaIsReachableFromTheEntryStub()
        {
            WorldConfig cfg = JsonUtility.FromJson<WorldConfig>(World1ConfigJson);
            Assert.IsTrue(MapValidation.ValidateWorldConfig(cfg, out string reason), reason);
        }
    }
}
