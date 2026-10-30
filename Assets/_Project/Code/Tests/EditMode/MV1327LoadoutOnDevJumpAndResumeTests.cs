using System.IO;
using NUnit.Framework;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1327 (the one new test): <see cref="LoadoutScreen.ShouldShow"/> read the owned set through
    /// <see cref="SaveSlotData.FurthestWorldIndex"/> alone once a slot was active, so a dev jump straight
    /// to a later world (<see cref="HomeScreen.StartDevWorld"/> -> <see cref="HomeScreen.StartSlotWorld"/>,
    /// neither of which raises <c>FurthestWorldIndex</c> — AC5, unchanged by this ticket) counted only
    /// whatever the profile's furthest reach already was, not the world actually being jumped to.
    ///
    /// Fail-first (main @ 12a1e42, test-only): the AC1 assertion below fails first —
    /// <c>Assert.IsTrue</c> quoted verbatim: "AC1: a fresh-profile dev jump to World 3 (index 2) must
    /// show LOADOUT -- more than one PRIMARY/SECONDARY is owned through that world Expected: True
    /// But was: False".
    ///
    /// Guards MV-1327: the sole EditMode coverage of <see cref="LoadoutScreen.ShouldShow"/>'s
    /// furthest-world resolution against a dev jump and a RESUME with no recorded choice.
    /// </summary>
    public sealed class MV1327LoadoutOnDevJumpAndResumeTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1327-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            WeaponSystemState.Reset();   // also resets RigState and RigBoard back to World 1
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            WeaponSystemState.Reset();
            RigBoard.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void ShouldShow_ResolvesThroughTheJumpedOrResumedWorld_NotJustFurthestWorldIndex()
        {
            const int devSlot = 0;
            const int worldThree = 2;   // the City, 1-based "World 3"
            const int worldOne = 0;     // the Backyard

            // AC1: a fresh profile (FurthestWorldIndex still 0) jumping straight to World 3 must show
            // LOADOUT — it owns the Backyard/Stormdrain/City primaries & secondaries through that world.
            HomeScreen.StartDevWorld(devSlot, worldThree);
            Assert.IsTrue(LoadoutScreen.ShouldShow(worldThree),
                "AC1: a fresh-profile dev jump to World 3 (index 2) must show LOADOUT -- more than one PRIMARY/SECONDARY is owned through that world");

            // AC5: the dev jump itself must never raise FurthestWorldIndex -- that would unlock real
            // progress a player hasn't actually earned.
            Assert.AreEqual(0, SaveSystem.Load(devSlot).FurthestWorldIndex,
                "AC5: a dev jump must not raise FurthestWorldIndex");

            // AC2: a fresh profile jumping to World 1 owns only that world's own single PRIMARY/
            // SECONDARY, so LOADOUT must not show.
            const int freshSlot = 1;
            HomeScreen.StartDevWorld(freshSlot, worldOne);
            Assert.IsFalse(LoadoutScreen.ShouldShow(worldOne),
                "AC2: a dev jump to World 1 owns only one PRIMARY/SECONDARY -- LOADOUT must not show");

            // AC3: RESUME (HomeScreen.ApplyResumeState) into a world the slot has already reached, with
            // no loadout choice recorded for it yet, must show LOADOUT; recording a choice for that
            // world must then suppress it on the next RESUME.
            const int resumeSlot = 2;
            SaveSystem.Save(resumeSlot, new SaveSlotData
            {
                HasData = true,
                DisplayName = "DEXTER",
                WorldIndex = worldThree,
                FurthestWorldIndex = worldThree,
                HasRunInProgress = true,
                CheckpointWorldIndex = worldThree,
                CheckpointAreaIndex = 2,
            });
            HomeScreen.ApplyResumeState(resumeSlot, worldThree);
            Assert.IsTrue(LoadoutScreen.ShouldShow(worldThree),
                "AC3: RESUME into a world already reached, with no recorded choice, must show LOADOUT");

            SaveSlotData resumed = SaveSystem.Load(resumeSlot);
            resumed.ChosenPrimaryForWorldIndex = worldThree;
            resumed.ChosenSecondaryForWorldIndex = worldThree;
            SaveSystem.Save(resumeSlot, resumed);
            Assert.IsFalse(LoadoutScreen.ShouldShow(worldThree),
                "AC3: a recorded choice for this world must suppress LOADOUT on the next RESUME");
        }
    }
}
