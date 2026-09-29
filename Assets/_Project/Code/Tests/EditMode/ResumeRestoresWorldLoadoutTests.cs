using System.IO;
using NUnit.Framework;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1023 (the one new test): a cold RESUME into World 2/3 used to keep World 1's RCDA/Water
    /// Balloon/board firing regardless of which world the checkpoint actually belonged to.
    /// <see cref="RigBoard"/>/<see cref="WeaponSystemState.ActivePrimary"/>/
    /// <see cref="WeaponSystemState.SecondaryKind"/> are process statics nothing on the resume path used
    /// to set — they default to World 1's own values, and <see cref="SaveSystem.RestoreCheckpoint"/>'s
    /// own <see cref="RigState.RestoreSnapshot"/> silently drops any node id absent from whichever board
    /// happened to still be active (<see cref="RigBoard.Exists"/>), so a World 2/3 checkpoint's own node
    /// ids were discarded on top of the wrong weapon firing.
    ///
    /// Guards MV-1023: this is the sole EditMode coverage of <see cref="HomeScreen.ApplyResumeState"/>,
    /// the resume seam MV-1023 extracted so this is testable without a scene.
    ///
    /// Fail-first (main @ 967d1be, reproduced by deleting the <c>WeaponSystemState.ApplyWorldLoadout</c>
    /// call from <see cref="HomeScreen.ApplyResumeState"/> entirely, so the resume path touches nothing
    /// but <see cref="SaveSystem.RestoreCheckpoint"/> — the exact pre-ticket shape): the world-index-1
    /// ("world 2" in the assertion's own 1-based message) iteration fails first —
    /// <c>Assert.AreEqual</c> quoted verbatim: "world 2: RigBoard must be on the checkpoint's own board
    /// Expected: 1 But was: 0" — RigBoard never left World 1's board, so
    /// <see cref="RigState.RestoreSnapshot"/>'s own <see cref="RigBoard.Exists"/> check (run against the
    /// wrong board) also silently drops the checkpoint's <c>s_rkt</c> level, though NUnit's classic
    /// <c>Assert</c> stops at the first failure so that second symptom isn't separately quoted here. The
    /// world-index-0 iteration passes even pre-fix — a resume into World 1 happens to match
    /// <c>WeaponSystemState.Reset()</c>'s own baseline regardless of ordering, exactly as the ticket's
    /// own Observation section describes ("a same-process RESUME only looks right when the statics
    /// happen to still hold the last world played").
    /// </summary>
    public sealed class ResumeRestoresWorldLoadoutTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1023-tests");
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
        public void ResumingASlotAppliesTheCheckpointsOwnWorldLoadout_NotWhicheverStaleWorldWasActive()
        {
            AssertWorldLoadoutRestored(world: 0, nodeId: "p_rng", nodeLevel: 1,
                WeaponCatalog.PrimaryKind.Rcda, SecondaryKind.WaterBalloon);
            AssertWorldLoadoutRestored(world: 1, nodeId: "s_rkt", nodeLevel: 2,
                WeaponCatalog.PrimaryKind.Lppe, SecondaryKind.ShoulderRack);
            AssertWorldLoadoutRestored(world: 2, nodeId: "p_spr", nodeLevel: 1,
                WeaponCatalog.PrimaryKind.Undertow, SecondaryKind.ShoulderRack);
        }

        private static void AssertWorldLoadoutRestored(int world, string nodeId, int nodeLevel,
            WeaponCatalog.PrimaryKind expectedPrimary, SecondaryKind expectedSecondary)
        {
            const int slot = 0;

            // A checkpoint belonging to `world`, carrying one node that exists only on that world's own
            // board — same shape SaveSystem.CaptureCheckpoint writes.
            SaveSystem.Save(slot, new SaveSlotData
            {
                HasData = true,
                DisplayName = "DEXTER",
                WorldIndex = world,
                HasRunInProgress = true,
                CheckpointWorldIndex = world,
                CheckpointAreaIndex = 2,
                CheckpointRigNodeIds = new[] { nodeId },
                CheckpointRigNodeLevels = new[] { nodeLevel },
            });

            // Every static back to app-launch defaults (World 1's RCDA/Water Balloon/board 0) — a fresh
            // process, or a stale one that last played a different world/slot.
            WeaponSystemState.Reset();

            // The resume seam: the same target-world resolution HomeScreen.ApplyResume makes
            // (SaveSystem.ResolveResumePlan's own WorldIndex), then the restore itself.
            int resolvedWorld = SaveSystem.ResolveResumePlan(slot, builtWorldIndex: 0).WorldIndex;
            Assert.AreEqual(world, resolvedWorld, "test precondition: the checkpoint's own world must resolve back");
            HomeScreen.ApplyResumeState(slot, resolvedWorld);

            Assert.AreEqual(world, RigBoard.ActiveWorldIndex, $"world {world + 1}: RigBoard must be on the checkpoint's own board");
            Assert.AreEqual(expectedPrimary, WeaponSystemState.ActivePrimary, $"world {world + 1}: wrong primary equipped");
            Assert.AreEqual(expectedSecondary, WeaponSystemState.SecondaryKind, $"world {world + 1}: wrong secondary equipped");
            Assert.AreEqual(nodeLevel, RigState.Level(nodeId),
                $"world {world + 1}: the checkpoint's own node ({nodeId}) must survive the restore, not be dropped for sitting on the wrong board");
        }
    }
}
