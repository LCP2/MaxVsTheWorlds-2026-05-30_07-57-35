using System.IO;
using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The player-profile system underneath the Home screen (YT-218; supersedes YT-151's mid-run
    /// resume slots): three profiles on disk, each an identity plus a personal best, with no run
    /// state to round-trip. <see cref="SaveSystem.DirectoryOverride"/> points every test at a
    /// scratch folder so none of this ever touches a real device's save data.
    /// </summary>
    public sealed class SaveSystemTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-save-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            PickupWallet.Reset();   // also resets RigState
            DeathRunState.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            PickupWallet.Reset();   // also resets RigState
            DeathRunState.Reset();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void AnUntouchedSlotReadsEmpty()
        {
            SaveSlotData data = SaveSystem.Load(0);
            Assert.That(data.HasData, Is.False, "a slot that was never played must read as empty");
        }

        [Test]
        public void SaveThenLoad_RoundTripsNameAndPersonalBest()
        {
            var written = new SaveSlotData
            {
                HasData = true,
                DisplayName = "DEXTER",
                BestDeathsToVictory = 3,
            };
            SaveSystem.Save(1, written);

            SaveSlotData read = SaveSystem.Load(1);

            Assert.That(read.HasData, Is.True);
            Assert.That(read.DisplayName, Is.EqualTo("DEXTER"));
            Assert.That(read.BestDeathsToVictory, Is.EqualTo(3));
        }

        [Test]
        public void ACorruptFileReadsAsEmptyRatherThanThrowing()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "save_slot_2.json"), "{ not json");

            SaveSlotData data = SaveSystem.Load(2);

            Assert.That(data.HasData, Is.False);
        }

        [Test]
        public void DeleteRemovesTheSlotFile()
        {
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "MAX" });
            Assert.That(SaveSystem.Load(0).HasData, Is.True);

            SaveSystem.Delete(0);

            Assert.That(SaveSystem.Load(0).HasData, Is.False);
        }

        [Test]
        public void EnsureProfile_SeedsAnUntouchedSlotWithADefaultNameAndNoBest()
        {
            SaveSlotData data = SaveSystem.EnsureProfile(2);

            Assert.That(data.HasData, Is.True);
            Assert.That(data.DisplayName, Is.EqualTo(SaveSystem.DefaultDisplayName(2)));
            Assert.That(data.BestDeathsToVictory, Is.EqualTo(-1), "an untouched slot has never finished a run");
            Assert.That(SaveSystem.Load(2).HasData, Is.True, "the profile must be persisted, not just returned");
        }

        [Test]
        public void EnsureProfile_LeavesAnExistingProfileUntouched()
        {
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER", BestDeathsToVictory = 5 });

            SaveSlotData data = SaveSystem.EnsureProfile(0);

            Assert.That(data.DisplayName, Is.EqualTo("DEXTER"), "picking an existing profile must never rename it");
            Assert.That(data.BestDeathsToVictory, Is.EqualTo(5), "picking an existing profile must never reset its best");
        }

        [Test]
        public void RecordResult_LowersTheBestWhenThisRunBeatIt()
        {
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER", BestDeathsToVictory = 4 });

            SaveSystem.RecordResult(0, 2);

            Assert.That(SaveSystem.Load(0).BestDeathsToVictory, Is.EqualTo(2), "fewer deaths taken is a better run");
        }

        [Test]
        public void RecordResult_SetsTheBestOnAProfilesFirstEverFinish()
        {
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER", BestDeathsToVictory = -1 });

            SaveSystem.RecordResult(0, 7);

            Assert.That(SaveSystem.Load(0).BestDeathsToVictory, Is.EqualTo(7));
        }

        [Test]
        public void RecordResult_LeavesTheBestAloneWhenThisRunFellShort()
        {
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER", BestDeathsToVictory = 1 });

            SaveSystem.RecordResult(0, 6);

            Assert.That(SaveSystem.Load(0).BestDeathsToVictory, Is.EqualTo(1),
                "a run with more deaths must never overwrite a better (lower) personal best");
        }

        [Test]
        public void RecordResult_IgnoresNoActiveProfile()
        {
            SaveSystem.RecordResult(-1, 0);
            // No slot -1 file should ever be written; this is just asserting no exception is thrown.
        }

        /// <summary>MV-557 (part 1 of MV-524): a mid-run checkpoint round-trips through capture/restore,
        /// and a save file written before the checkpoint fields existed still loads cleanly as "no run
        /// in progress" rather than throwing or leaving the new array fields null.</summary>
        [Test]
        public void CaptureCheckpoint_RoundTripsEveryField_AndALegacySaveWithoutCheckpointFieldsLoadsCleanly()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "save_slot_1.json"),
                "{\"HasData\":true,\"DisplayName\":\"DEXTER\",\"BestDeathsToVictory\":3}");

            SaveSlotData legacy = SaveSystem.Load(1);

            Assert.That(legacy.HasData, Is.True);
            Assert.That(legacy.DisplayName, Is.EqualTo("DEXTER"));
            Assert.That(legacy.BestDeathsToVictory, Is.EqualTo(3));
            Assert.That(legacy.HasRunInProgress, Is.False,
                "a save predating checkpoints must read as no run in progress");
            Assert.That(legacy.CheckpointRigNodeIds, Is.Not.Null.And.Empty);
            Assert.That(legacy.CheckpointRigNodeLevels, Is.Not.Null.And.Empty);
            Assert.That(legacy.CheckpointUnlockedCategories, Is.Not.Null.And.Empty);

            RigState.RaiseLevel("p_dmg");   // owned at start level 1 (run-start ability) -> level 2
            PickupWallet.SetPowerCells(7);
            PickupWallet.SetPowerCellSecondary(4);   // MV-672: the new secondary currency
            DeathRunState.RecordDeath();
            DeathRunState.RecordDeath();    // DeathsTaken == 2

            SaveSystem.CaptureCheckpoint(0, areaIndex: 3);

            // Disturb live state so a passing restore is provably doing the work, not reading stale statics.
            PickupWallet.Reset();
            DeathRunState.Reset();

            bool restored = SaveSystem.RestoreCheckpoint(0);

            Assert.That(restored, Is.True);
            Assert.That(RigState.Level("p_dmg"), Is.EqualTo(2));
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(7));
            Assert.That(PickupWallet.PowerCellsSecondary, Is.EqualTo(4),
                "MV-672: Power Cells must round-trip through a checkpoint capture/restore too");
            Assert.That(DeathRunState.DeathsTaken, Is.EqualTo(2));
            SaveSlotData saved = SaveSystem.Load(0);
            Assert.That(saved.HasRunInProgress, Is.True);
            Assert.That(saved.CheckpointAreaIndex, Is.EqualTo(3));
        }
    }
}
