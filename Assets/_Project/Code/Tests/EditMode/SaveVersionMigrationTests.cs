using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Save;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>MV-1144: a save carries a schema version and migrates forward on <see cref="SaveSystem.Load"/>,
    /// so inserting a new world later can renumber every existing save's world-number fields instead of
    /// resuming players into the wrong world. Drives the real <see cref="SaveSystem.Load"/>/<see cref="SaveSystem.Save"/>
    /// on a redirected folder, plus <see cref="SaveMigrations"/>' test seam to exercise a migration step
    /// the shipped list doesn't need yet.</summary>
    public sealed class SaveVersionMigrationTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-save-version-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            Directory.CreateDirectory(_dir);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            SaveMigrations.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            SaveMigrations.ResetForTests();
            SaveSystem.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private void WriteSlotJson(int slot, string json) =>
            File.WriteAllText(Path.Combine(_dir, $"save_slot_{slot}.json"), json);

        private string ReadSlotJson(int slot) =>
            File.ReadAllText(Path.Combine(_dir, $"save_slot_{slot}.json"));

        [Test]
        public void OldSave_MigratesToCurrentVersion_UnchangedOtherwise_AndSaveStampsIt()
        {
            // MV-1176: WorldIndex 0 (below the City's insertion point, CityInsertedAtSaveV2 = 2) stays
            // unshifted by the real shipped 1->2 step, so this generic "migration doesn't otherwise touch
            // data" test stays decoupled from the specific world-shifting behaviour its own sibling test
            // (AStepThatMovesWorlds...) already covers.
            WriteSlotJson(0,
                "{\"HasData\":true,\"WorldIndex\":0,\"FurthestWorldIndex\":0,\"HasRunInProgress\":true," +
                "\"CheckpointAreaIndex\":7,\"CheckpointWorldIndex\":0,\"CheckpointRigBoardWorldIndex\":0}");

            SaveSlotData data = SaveSystem.Load(0);

            Assert.That(data.SaveVersion, Is.EqualTo(SaveSystem.CurrentSaveVersion),
                "a save with no SaveVersion key must migrate all the way to the current version");
            Assert.That(data.WorldIndex, Is.EqualTo(0));
            Assert.That(data.FurthestWorldIndex, Is.EqualTo(0));
            Assert.That(data.HasRunInProgress, Is.True);
            Assert.That(data.CheckpointAreaIndex, Is.EqualTo(7));
            Assert.That(data.CheckpointWorldIndex, Is.EqualTo(0));
            Assert.That(data.CheckpointRigBoardWorldIndex, Is.EqualTo(0));

            SaveSystem.Save(0, data);

            Assert.That(ReadSlotJson(0), Does.Contain($"\"SaveVersion\":{SaveSystem.CurrentSaveVersion}"));
        }

        [Test]
        public void AStepThatMovesWorlds_ShiftsOnlyTheFourWorldFields_LeavingUnknownAlone()
        {
            SaveMigrations.UseForTests(new List<SaveMigrations.Step>
            {
                new SaveMigrations.Step(0, data => { }),
                new SaveMigrations.Step(1, data => SaveMigrations.ShiftWorldIndices(data, 2)),
            }, currentVersion: 2);

            WriteSlotJson(0,
                "{\"HasData\":true,\"WorldIndex\":2,\"FurthestWorldIndex\":2,\"HasRunInProgress\":true," +
                "\"CheckpointAreaIndex\":7,\"CheckpointWorldIndex\":2,\"CheckpointRigBoardWorldIndex\":2}");
            SaveSlotData first = SaveSystem.Load(0);

            Assert.That(first.WorldIndex, Is.EqualTo(3));
            Assert.That(first.FurthestWorldIndex, Is.EqualTo(3));
            Assert.That(first.CheckpointWorldIndex, Is.EqualTo(3));
            Assert.That(first.CheckpointRigBoardWorldIndex, Is.EqualTo(3));
            Assert.That(first.CheckpointAreaIndex, Is.EqualTo(7), "the shift only touches world-number fields");
            Assert.That(first.SaveVersion, Is.EqualTo(2));

            WriteSlotJson(1,
                "{\"HasData\":true,\"WorldIndex\":1,\"FurthestWorldIndex\":1," +
                "\"CheckpointWorldIndex\":-1,\"CheckpointRigBoardWorldIndex\":-1}");
            SaveSlotData second = SaveSystem.Load(1);

            Assert.That(second.WorldIndex, Is.EqualTo(1));
            Assert.That(second.FurthestWorldIndex, Is.EqualTo(1));
            Assert.That(second.CheckpointWorldIndex, Is.EqualTo(-1), "an unknown checkpoint world must never be shifted");
            Assert.That(second.CheckpointRigBoardWorldIndex, Is.EqualTo(-1));
        }

        [Test]
        public void ASavedMigratedSlot_NeverMigratesTwiceOnTheNextLoad()
        {
            SaveMigrations.UseForTests(new List<SaveMigrations.Step>
            {
                new SaveMigrations.Step(0, data => { }),
                new SaveMigrations.Step(1, data => SaveMigrations.ShiftWorldIndices(data, 2)),
            }, currentVersion: 2);

            WriteSlotJson(0,
                "{\"HasData\":true,\"WorldIndex\":2,\"FurthestWorldIndex\":2,\"HasRunInProgress\":true," +
                "\"CheckpointAreaIndex\":7,\"CheckpointWorldIndex\":2,\"CheckpointRigBoardWorldIndex\":2}");

            SaveSlotData loaded = SaveSystem.Load(0);
            SaveSystem.Save(0, loaded);

            SaveSlotData reloaded = SaveSystem.Load(0);

            Assert.That(reloaded.WorldIndex, Is.EqualTo(3), "a migration already applied and saved must never re-apply");
            Assert.That(reloaded.CheckpointWorldIndex, Is.EqualTo(3));
            Assert.That(reloaded.CheckpointRigBoardWorldIndex, Is.EqualTo(3));
        }

        [Test]
        public void ANewerSave_LoadsUnchangedWithOneWarning_AndSavingNeverLowersIt()
        {
            WriteSlotJson(0, "{\"SaveVersion\":99,\"HasData\":true,\"WorldIndex\":5,\"DisplayName\":\"NEWER\"}");

            var warnings = new List<string>();
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning) warnings.Add(condition);
            }

            SaveSlotData data;
            Application.logMessageReceived += OnLog;
            try
            {
                data = SaveSystem.Load(0);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            Assert.That(data.SaveVersion, Is.EqualTo(99));
            Assert.That(data.WorldIndex, Is.EqualTo(5));
            Assert.That(data.DisplayName, Is.EqualTo("NEWER"));
            Assert.That(warnings.Count, Is.EqualTo(1), "a newer-than-this-build save must log exactly one warning");

            SaveSystem.Save(0, data);

            Assert.That(SaveSystem.Load(0).SaveVersion, Is.EqualTo(99), "saving a newer save must never lower its version");
        }

        [Test]
        public void AnEmptySlot_LoadsAsEmptyToday()
        {
            SaveSlotData data = SaveSystem.Load(2);
            Assert.That(data.HasData, Is.False);
        }
    }
}
