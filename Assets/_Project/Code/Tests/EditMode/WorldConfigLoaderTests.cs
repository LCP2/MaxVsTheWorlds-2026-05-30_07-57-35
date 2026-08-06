using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The world-config schema loader — 8 dials + enemyTypes THV table (Confluence MVW 34439170 §4/§7-8,
    /// MV-269), pinned against this ticket's own AC1: <c>world1_config.json</c> loads with every dial +
    /// area + gate + enemyType populated, and a malformed config is rejected with a clear error.
    /// </summary>
    public sealed class WorldConfigLoaderTests
    {
        /// <summary>The LOCKED v1 (2026-08-05) World 1 config — the same fixture embedded verbatim in
        /// <c>WorldMapLoaderTests</c> (MV-267) for the geometry half; this proves the dial/enemyTypes
        /// half of the same real file.</summary>
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

        // --- AC1: world1_config.json loads through the schema, all 8 dials + areas + gates + enemyTypes ---

        [Test]
        public void World1Config_LoadsWithAllEightDialsPopulated()
        {
            Assert.IsTrue(WorldConfigLoader.TryLoad(World1ConfigJson, out WorldConfig cfg, out string reason), reason);

            Assert.IsNotNull(cfg.dials);
            Assert.AreEqual(8, cfg.dials.areaCount);
            Assert.AreEqual(14.0f, cfg.dials.baseThreat, 1e-4f);
            Assert.AreEqual(0.1f, cfg.dials.threatGrowth, 1e-4f);
            Assert.IsNotNull(cfg.dials.band);
            Assert.AreEqual(0.4f, cfg.dials.band.up, 1e-4f);
            Assert.AreEqual(-0.15f, cfg.dials.band.down, 1e-4f);
            Assert.AreEqual(8, cfg.dials.pacingRhythm.Length);
            Assert.IsNotNull(cfg.dials.toughnessCurve);
            Assert.AreEqual(5, cfg.dials.toughnessCurve.heavyFromArea);
            Assert.AreEqual(8, cfg.dials.toughnessCurve.bruteFromArea);
            Assert.AreEqual(0.25f, cfg.dials.toughnessCurve.toughSubstitutionPct, 1e-4f);
            Assert.AreEqual(0.7f, cfg.dials.toughnessCurve.tankShareEnd, 1e-4f);
            Assert.AreEqual(2, cfg.dials.powerupCadence);
        }

        [Test]
        public void World1Config_LoadsWithEnemyTypesThvTablePopulated()
        {
            Assert.IsTrue(WorldConfigLoader.TryLoad(World1ConfigJson, out WorldConfig cfg, out string reason), reason);

            Assert.IsNotNull(cfg.enemyTypes);
            Assert.AreEqual(1.0f, cfg.enemyTypes.small.thv, 1e-4f);
            Assert.AreEqual(2.5f, cfg.enemyTypes.large.thv, 1e-4f);
            Assert.AreEqual(4.5f, cfg.enemyTypes.heavy.thv, 1e-4f);
            Assert.AreEqual(7.0f, cfg.enemyTypes.brute.thv, 1e-4f);
        }

        [Test]
        public void World1Config_LoadsWithAreasAndGatesPopulated()
        {
            Assert.IsTrue(WorldConfigLoader.TryLoad(World1ConfigJson, out WorldConfig cfg, out string reason), reason);

            Assert.AreEqual(10, cfg.areas.Length, "8 combat areas + entry stub + boss/exit clearing");
            Assert.AreEqual(9, cfg.gates.Length, "g0..g7 plus the boss gate bg");
        }

        // --- AC1: a malformed config is rejected with a clear error --------------------------------

        [Test]
        public void MissingDials_IsRejectedWithAClearError()
        {
            // Renaming the key (rather than deleting the block) sidesteps having to balance braces on
            // a hand-edited fixture — JsonUtility simply finds no "dials" property and leaves it null.
            string json = World1ConfigJson.Replace("\"dials\": {", "\"dialsRenamed\": {");

            Assert.IsFalse(WorldConfigLoader.TryLoad(json, out _, out string reason));
            StringAssert.Contains("dials", reason);
        }

        [Test]
        public void NegativeThreatGrowth_IsRejectedWithAClearError()
        {
            string json = World1ConfigJson.Replace("\"threatGrowth\": 0.1,", "\"threatGrowth\": -0.1,");

            Assert.IsFalse(WorldConfigLoader.TryLoad(json, out _, out string reason));
            StringAssert.Contains("threatGrowth", reason);
        }

        [Test]
        public void EmptyPacingRhythm_IsRejectedWithAClearError()
        {
            string json = World1ConfigJson.Replace(
                "\"pacingRhythm\": [1.0, 1.1, 1.15, 0.9, 1.1, 1.2, 0.9, 1.25],", "\"pacingRhythm\": [],");

            Assert.IsFalse(WorldConfigLoader.TryLoad(json, out _, out string reason));
            StringAssert.Contains("pacingRhythm", reason);
        }

        [Test]
        public void MissingEnemyType_IsRejectedWithAClearError()
        {
            string json = World1ConfigJson.Replace("\"brute\": { \"thv\": 7.0 }", "\"bruteRenamed\": { \"thv\": 7.0 }");

            Assert.IsFalse(WorldConfigLoader.TryLoad(json, out _, out string reason));
            StringAssert.Contains("enemyTypes", reason);
        }

        [Test]
        public void NonPositiveThv_IsRejectedWithAClearError()
        {
            string json = World1ConfigJson.Replace("\"small\": { \"thv\": 1.0 }", "\"small\": { \"thv\": 0.0 }");

            Assert.IsFalse(WorldConfigLoader.TryLoad(json, out _, out string reason));
            StringAssert.Contains("thv", reason);
        }

        [Test]
        public void BadGeometry_IsStillCaughtThroughThisLoader()
        {
            // Zero areas — the geometry rule MapValidation.ValidateWorldConfig already enforces
            // (MV-267) — proves WorldConfigLoader does not skip it in favour of only checking dials.
            int areasStart = World1ConfigJson.IndexOf("\"areas\": [", System.StringComparison.Ordinal);
            string json = World1ConfigJson.Substring(0, areasStart) + "\"areas\": []\n}";

            Assert.IsFalse(WorldConfigLoader.TryLoad(json, out _, out string reason));
            StringAssert.Contains("no areas", reason);
        }
    }
}
