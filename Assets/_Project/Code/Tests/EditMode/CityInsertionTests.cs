using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Save;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1176 (the one new test, per CC_AUTONOMY's testing policy): the City is inserted as World 3
    /// and the Reef becomes World 4 — the catalog row, the real config it loads, its finale and an old
    /// save's world-number fields must all move together, or a player either never reaches the City or
    /// resumes in the wrong world.
    ///
    /// Fails on the MV-1175 merge commit (de04569): <c>WorldIds.City</c> does not exist there, so this
    /// fails to COMPILE (CS0117), the same "new API is its own base-commit failure" bar this project's
    /// other catalog-insertion tests already use.
    ///
    /// Tier 2 (resolved values) throughout: every assertion reads a value the engine computed —
    /// <see cref="WorldCatalog.IndexOf"/>, a resolved door mouth, a loaded <see cref="WorldConfig"/>, a
    /// built <see cref="MapData"/> zone, a parsed garrison kind, or a migrated <see cref="SaveSlotData"/>
    /// — never an authored JSON number restated as itself.
    /// </summary>
    public sealed class CityInsertionTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-save-tests-mv1176");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            Directory.CreateDirectory(_dir);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private void WriteSlotJson(int slot, string json) =>
            File.WriteAllText(Path.Combine(_dir, $"save_slot_{slot}.json"), json);

        [Test]
        public void CityIsWorldThreeReefIsWorldFourAndOldSavesFollow()
        {
            int stormdrainIndex = WorldCatalog.IndexOf(WorldIds.Stormdrain);
            int cityIndex = WorldCatalog.IndexOf(WorldIds.City);
            int reefIndex = WorldCatalog.IndexOf(WorldIds.Reef);

            // --- catalog order and internal soundness ---
            Assert.AreEqual(stormdrainIndex + 1, cityIndex, "the City must sit directly after the Stormdrain");
            Assert.AreEqual(cityIndex + 1, reefIndex, "the Reef must sit directly after the City");
            Assert.AreEqual(reefIndex + 1, WorldCatalog.Count, "the Reef must still be the last row");
            Assert.IsEmpty(WorldCatalog.Validate(), "the shipped rows (City included) must carry no problems");

            // --- resolved transition geometry ---
            WorldConfig cityCfg = WorldLibrary.Load(WorldCatalog.Get(cityIndex).ConfigKey);
            WorldConfig reefCfg = WorldLibrary.Load(WorldCatalog.Get(reefIndex).ConfigKey);
            Assert.IsNotNull(cityCfg, "the City's own ConfigKey must load");
            Assert.IsNotNull(reefCfg, "the Reef's own ConfigKey must load");

            Vector2 stormdrainIntoCity = WorldTransitions.For(stormdrainIndex).ArrivalDoorMouth(cityCfg);
            Assert.That(stormdrainIntoCity.x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(stormdrainIntoCity.y, Is.EqualTo(3.5f).Within(0.01f));

            Vector2 cityExit = WorldTransitions.For(cityIndex).ExitDoorMouth(cityCfg);
            Assert.That(cityExit.x, Is.EqualTo(50f).Within(0.01f));
            Assert.That(cityExit.y, Is.EqualTo(20.5f).Within(0.01f));

            Vector2 cityIntoReef = WorldTransitions.For(cityIndex).ArrivalDoorMouth(reefCfg);
            Assert.That(cityIntoReef.x, Is.EqualTo(-6f).Within(0.01f));
            Assert.That(cityIntoReef.y, Is.EqualTo(45f).Within(0.01f));

            // --- the City's own map actually builds and validates ---
            Assert.IsTrue(WorldMapLoader.TryLoad(cityCfg, out MapData cityMap, out string loadReason), loadReason);
            Assert.IsTrue(MapValidation.Validate(cityMap, out string validateReason), validateReason);

            // WorldMapLoader renames combat areas 1..dials.areaCount to "area<N>" (AreaAccumulationDirector
            // parses that literal prefix) — a1's built zone id is "area1", not the authored "a1".
            MapZone a1Zone = cityMap.Zone("area1");
            Assert.IsNotNull(a1Zone, "the built map must have a zone 'area1' (Market Street)");
            Assert.That(a1Zone.width, Is.EqualTo(44f).Within(0.01f));
            Assert.That(a1Zone.depth, Is.EqualTo(24f).Within(0.01f));

            WorldArea a1 = cityCfg.Area("a1");
            var counts = new Dictionary<EnemyKind, int>();
            foreach (WorldGarrisonEntry entry in a1.garrison)
            {
                Assert.IsTrue(EnemyKindNames.TryParse(entry.kind, out EnemyKind kind),
                    $"unrecognised garrison kind '{entry.kind}'");
                counts.TryGetValue(kind, out int existing);
                counts[kind] = existing + 1;
            }
            Assert.AreEqual(18, a1.garrison.Length, "Market Street's own garrison count");
            Assert.AreEqual(8, counts.ContainsKey(EnemyKind.Rusher) ? counts[EnemyKind.Rusher] : 0);
            Assert.AreEqual(4, counts.ContainsKey(EnemyKind.Bruiser) ? counts[EnemyKind.Bruiser] : 0);
            Assert.AreEqual(4, counts.ContainsKey(EnemyKind.Gunner) ? counts[EnemyKind.Gunner] : 0);
            Assert.AreEqual(2, counts.ContainsKey(EnemyKind.Turret) ? counts[EnemyKind.Turret] : 0);

            WorldArea finale = WorldRunner.ResolveFinaleArea(cityCfg);
            Assert.IsNotNull(finale, "the City must resolve a finale area");
            Assert.AreEqual("a1", finale.id, "Market Street is the City's bossless finale");

            // --- an old save's world-number fields follow the Reef forward ---
            WriteSlotJson(0,
                $"{{\"HasData\":true,\"WorldIndex\":{reefIndex - 1},\"FurthestWorldIndex\":{reefIndex - 1}," +
                $"\"HasRunInProgress\":true,\"CheckpointAreaIndex\":7," +
                $"\"CheckpointWorldIndex\":{reefIndex - 1},\"CheckpointRigBoardWorldIndex\":{reefIndex - 1}}}");
            SaveSlotData migrated = SaveSystem.Load(0);

            Assert.AreEqual(2, migrated.SaveVersion, "a pre-City save must migrate all the way to version 2");
            Assert.AreEqual(reefIndex, migrated.WorldIndex, "a save that was in the (old) World 3 must now read World 4 (the Reef)");
            Assert.AreEqual(reefIndex, migrated.FurthestWorldIndex);
            Assert.AreEqual(reefIndex, migrated.CheckpointWorldIndex);
            Assert.AreEqual(reefIndex, migrated.CheckpointRigBoardWorldIndex);

            // --- a save already at/before the Stormdrain, and an unknown -1 checkpoint, are untouched ---
            WriteSlotJson(1,
                $"{{\"HasData\":true,\"WorldIndex\":{stormdrainIndex},\"FurthestWorldIndex\":{stormdrainIndex}," +
                "\"CheckpointWorldIndex\":-1,\"CheckpointRigBoardWorldIndex\":-1}");
            SaveSlotData unaffected = SaveSystem.Load(1);

            Assert.AreEqual(stormdrainIndex, unaffected.WorldIndex, "a save already at the Stormdrain must not shift");
            Assert.AreEqual(stormdrainIndex, unaffected.FurthestWorldIndex);
            Assert.AreEqual(-1, unaffected.CheckpointWorldIndex, "an unknown checkpoint world must never be shifted");
            Assert.AreEqual(-1, unaffected.CheckpointRigBoardWorldIndex);
        }
    }
}
