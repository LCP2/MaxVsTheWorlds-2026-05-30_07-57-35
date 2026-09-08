using System.IO;
using NUnit.Framework;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-736 (the one new test): the WORLD 3 dev entry point on the Home screen's slot card.
    /// Reaching World 3 legitimately means clearing all of World 2 as well as World 1, which makes
    /// World 3 untestable in practice — this proves <see cref="HomeScreen.StartSlotWorld"/>'s new
    /// <c>maxRig: true</c> path (the plain, EditMode-testable method behind the WORLD 3 button) drops
    /// the tester in with a completely maxed rig on World 2's board (the board World 3 also loads,
    /// <see cref="RigBoardLibrary.ForWorld"/>'s own rule): every node at its own board-authored
    /// <see cref="RigBoard.MaxLevel"/>, every category unlocked, SECONDARY's mystery flag cleared by
    /// <c>s_rkt</c> being owned rather than a special case, and every FORGE fusion forged.
    ///
    /// Fails on base commit 16005b0: <c>HomeScreen.StartSlotWorld</c> does not exist there (only the
    /// single-world <c>StartSlotWorld2</c>) — this does not compile against that commit (quoted in the
    /// fix comment).
    /// </summary>
    public sealed class MV736World3MaxRigTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv736-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            WeaponSystemState.Reset();   // also resets RigState and RigBoard back to World 1
            RigFusionState.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            WeaponSystemState.Reset();
            RigBoard.ResetForTests();
            RigFusionState.Reset();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void StartSlotWorld_MaxRigTrue_FullyMaxesTheBoardAndForgesEveryFusion()
        {
            // A slot mid-way through a World 1 campaign on the RCDA.
            SaveSystem.Save(0, new SaveSlotData
            {
                HasData = true,
                DisplayName = "DEXTER",
                WorldIndex = 0,
                PrimaryKind = WeaponCatalog.PrimaryKind.Rcda,
            });

            HomeScreen.StartSlotWorld(0, worldIndex: 2, maxRig: true);

            SaveSlotData after = SaveSystem.Load(0);
            Assert.AreEqual(2, after.WorldIndex, "WORLD 3 must seed WorldIndex = 2");

            // AC2: every node on the board (21 ids) sits at its own authored maxLevel — assert the
            // sum (77, per the ticket's own table) plus one named node from each of the five
            // categories, read live from RigBoard rather than hard-coded per-node.
            int sum = 0;
            foreach (var kv in RigState.SnapshotLevels()) sum += kv.Value;
            Assert.AreEqual(77, sum, "every node on the board must be maxed, summing to 77");

            Assert.AreEqual(RigBoard.MaxLevel("p_dmg"), RigState.Level("p_dmg"), "p_dmg (PRIMARY) must be maxed");
            Assert.AreEqual(RigBoard.MaxLevel("s_rkt"), RigState.Level("s_rkt"), "s_rkt (SECONDARY) must be maxed");
            Assert.AreEqual(RigBoard.MaxLevel("e_ff"), RigState.Level("e_ff"), "e_ff (ENERGY) must be maxed");
            Assert.AreEqual(RigBoard.MaxLevel("m_spd"), RigState.Level("m_spd"), "m_spd (MOVE) must be maxed");
            Assert.AreEqual(RigBoard.MaxLevel("u_sen"), RigState.Level("u_sen"), "u_sen (SUPPORT) must be maxed");

            // AC3: every category unlocked, and the SECONDARY mystery flag cleared by s_rkt's own
            // ownership rather than a special case.
            foreach (string category in new[] { "PRIMARY", "SECONDARY", "ENERGY", "MOVE", "SUPPORT" })
                Assert.IsTrue(RigState.IsCategoryUnlocked(category), $"{category} must be unlocked");
            Assert.IsFalse(RigState.SecondaryLocked, "SECONDARY must no longer read as mystery-locked");

            // AC4: all four fusions forged.
            foreach (string fusionId in new[] { "f_del", "f_bgd", "f_ovc", "f_skr" })
                Assert.IsTrue(RigFusionState.IsForged(fusionId), $"{fusionId} must be forged");
        }
    }
}
