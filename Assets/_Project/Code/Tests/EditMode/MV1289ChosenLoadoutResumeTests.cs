using System.IO;
using NUnit.Framework;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1289 (the one new test): <see cref="SaveSlotData.ChosenPrimaryForWorldIndex"/>/
    /// <see cref="SaveSlotData.ChosenPrimaryForWorld"/> used to be read by nothing on the resume path --
    /// <see cref="HomeScreen.ApplyResumeState"/> always applied the world's own native primary
    /// (<see cref="WeaponSystemState.ApplyWorldLoadout(int)"/>), so a slot that had picked a non-native
    /// primary for a world silently reverted to that world's native weapon on every resume.
    ///
    /// Fail-first (main @ 10afa8d, test-only against the pre-fix <see cref="WeaponSystemState.ApplyWorldLoadout(int)"/>):
    /// <c>Assert.AreEqual</c> quoted verbatim: "the slot's chosen primary for this world must win over
    /// the City's native RICOCHET Expected: Lppe But was: Ricochet".
    ///
    /// Guards MV-1289: the sole EditMode coverage of the chosen-loadout resume path.
    /// </summary>
    public sealed class MV1289ChosenLoadoutResumeTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1289-tests");
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
        public void ResumeAppliesTheSlotsChosenPrimaryForTheWorld_NotTheWorldsNativeOne()
        {
            const int slot = 0;
            const int world = 2;   // the City -- native primary RICOCHET (rig_board.city.json)

            SaveSystem.Save(slot, new SaveSlotData
            {
                HasData = true,
                DisplayName = "DEXTER",
                WorldIndex = world,
                HasRunInProgress = true,
                CheckpointWorldIndex = world,
                CheckpointAreaIndex = 2,
                ChosenPrimaryForWorldIndex = world,
                ChosenPrimaryForWorld = WeaponCatalog.PrimaryKind.Lppe,
            });

            WeaponSystemState.Reset();

            int resolvedWorld = SaveSystem.ResolveResumePlan(slot, builtWorldIndex: 0).WorldIndex;
            Assert.AreEqual(world, resolvedWorld, "test precondition: the checkpoint's own world must resolve back");
            HomeScreen.ApplyResumeState(slot, resolvedWorld);

            Assert.AreEqual(WeaponCatalog.PrimaryKind.Lppe, WeaponSystemState.ActivePrimary,
                "the slot's chosen primary for this world must win over the City's native RICOCHET");
            Assert.IsTrue(RigBoard.Exists("p_frk"),
                "the LPPE's own ARC node must be on the active board once LPPE is the equipped primary");
        }
    }
}
