using System.IO;
using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.Save;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-687: the world library gains a second world, and a save advances into it on Victory. The
    /// one new test this ticket adds — fails on base commit 81cc1a9, which has no
    /// <c>SaveSlotData.WorldIndex</c> and no <c>WorldLibrary.World2</c> to resolve against, and whose
    /// <c>Resources/Worlds/</c> carries no <c>world2_config.json</c> for the second half to load.
    /// </summary>
    public sealed class MV687WorldAdvanceTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv687-tests");
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
        public void RecordResult_AdvancesWorldIndexAndResetsCheckpoint_ClampedAtTheLastWorld_AndWorld2ConfigValidates()
        {
            // AC1: a fresh World 1 profile finishes Victory — WorldIndex must resolve to World 2, and
            // the mid-run checkpoint area must reset to 0 for the new world's own beginning.
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER", WorldIndex = 0, CheckpointAreaIndex = 7 });

            SaveSystem.RecordResult(0, deathsTaken: 3);

            SaveSlotData afterFirstVictory = SaveSystem.Load(0);
            Assert.AreEqual("world2_config", WorldLibrary.KeyForIndex(afterFirstVictory.WorldIndex),
                "a Victory on World 1 must resolve the next world to world2_config");
            Assert.AreEqual(0, afterFirstVictory.CheckpointAreaIndex,
                "a new world must not inherit the previous world's mid-run checkpoint area");

            // Already on the last world — a further Victory must clamp, not run off the end of
            // WorldLibrary.Keys.
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER", WorldIndex = WorldLibrary.Count - 1 });

            SaveSystem.RecordResult(0, deathsTaken: 1);

            SaveSlotData afterLastVictory = SaveSystem.Load(0);
            Assert.AreEqual(WorldLibrary.Count - 1, afterLastVictory.WorldIndex,
                "WorldIndex must clamp at the last world, not advance past WorldLibrary.Keys");

            // AC2: world2_config.json loads and validates with zero errors, through the same entry
            // point world1's own runtime tests use
            // (World1RuntimeTests.World1_ValidatesBothAsAWorldConfigAndAsTheBuiltMap).
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "the shipped world2_config.json failed to load — see the error log above");

            Assert.IsTrue(MapValidation.ValidateWorldConfig(cfg, out string configReason), configReason);

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);
            Assert.IsTrue(MapValidation.Validate(map, out string mapReason), mapReason);
        }
    }
}
