using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1080 (the one new test): MV-1078's finale CLEAN-UP morphs THE RIG onto the NEXT world's board
    /// the instant the Weapon Core is collected — before the world actually being PLAYED advances, since
    /// the exit stays shut until the final area's robots are all dead. A checkpoint captured in that
    /// window (<c>WorldRunner</c>'s own pause/focus handlers fire regardless of finale state) recorded
    /// only <c>SaveSlotData.CheckpointWorldIndex</c> — the world PLAYED — never the rig board's OWN world
    /// (<c>RigBoard.ActiveWorldIndex</c>), so a cold-boot RESUME re-applied the PLAYED world's loadout
    /// (<c>HomeScreen.ApplyResumeState</c>'s <c>WeaponSystemState.ApplyWorldLoadout(worldIndex)</c>),
    /// putting the OLD board back and firing the RCDA instead of the already-taken LPPE.
    ///
    /// Fails on base commit 745d8a2: <c>WeaponSystemState.ActivePrimary</c> comes back <c>Rcda</c>, not
    /// <c>Lppe</c> (quoted in the fix comment).
    /// </summary>
    public sealed class MV1080ResumeRestoresRigBoardWorldTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1080-tests");
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
        public void ResumeMidFinaleCleanup_RestoresTheTakenWorldsBoard_NotThePlayedWorlds()
        {
            const int slot = 0;

            // The finale CLEAN-UP window (MV-1078 WorldFinaleGate.BeginCleanup): the Core already morphed
            // THE RIG onto World 2's board (LPPE), while Max is still standing in World 1's final area —
            // the world actually PLAYED — with the exit not yet open.
            WeaponSystemState.ApplyWorldLoadout(1);
            RigState.RestoreSnapshot(new Dictionary<string, int> { ["p_dmg"] = 3 }, new[] { "PRIMARY" });

            SaveSystem.ActiveSlot = slot;
            SaveSystem.CaptureCheckpoint(slot, areaIndex: 30, worldIndex: 0);   // played world: World 1

            // Cold boot: every process static back to app-launch defaults.
            WeaponSystemState.Reset();

            // The resume seam, same as HomeScreen.ApplyResume's own call — the map still resumes into
            // the PLAYED world (World 1), never the rig board's own (already-taken) world.
            int resumeWorld = SaveSystem.ResolveResumePlan(slot, builtWorldIndex: 0).WorldIndex;
            Assert.AreEqual(0, resumeWorld, "test precondition: the map still resumes into the PLAYED world");
            HomeScreen.ApplyResumeState(slot, resumeWorld);

            Assert.AreEqual(WeaponCatalog.PrimaryKind.Lppe, WeaponSystemState.ActivePrimary,
                "MV-1080: the checkpoint's own rig board (World 2, already morphed by the finale's Core) " +
                "must survive a cold-boot RESUME, not be overwritten by the PLAYED world's own loadout");
            Assert.AreEqual(3, RigState.Level("p_dmg"),
                "MV-1080: a node's level on the TAKEN world's board must survive the resume");
        }
    }
}
