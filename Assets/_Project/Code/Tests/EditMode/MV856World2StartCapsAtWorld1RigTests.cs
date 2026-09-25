using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-856 (the one new test): Lee's report (2026-09-20) that the WORLD 2 slot button hands out
    /// levels/nodes the player hasn't earned yet — <see cref="HomeScreen.StartSlotWorld"/>'s
    /// <c>maxRig: false</c> path used to raise every ENERGY/MOVE/SUPPORT node to whichever board
    /// <see cref="WeaponSystemState.ApplyWeaponCoreMorph"/> had just switched <see cref="RigBoard"/>
    /// onto (World 2's, MV-689), so a WORLD 2 start arrived with World 2-only levels (MV-840 raised
    /// e_ff 5-&gt;7 and u_dmg 5-&gt;10) and even a World-2-only node fully owned (MV-848's e_cmg).
    /// Asserts the RESOLVED <see cref="RigState"/> lands on World 1's own board-authored levels
    /// instead (read live via <see cref="RigBoard.SnapshotMaxLevels"/>, never a number written into
    /// this test) — the World 1 -&gt; World 2 carry-over this dev shortcut is meant to simulate.
    ///
    /// Fails on base commit f98494d (main HEAD before this ticket): e_ff lands at 7 not 5, u_dmg at
    /// 10 not 5, and e_cmg at 5 not 0 — quoted in the fix comment.
    /// </summary>
    public sealed class MV856World2StartCapsAtWorld1RigTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv856-tests");
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
        public void StartSlotWorld_World2_CapsEnergyMoveSupportAtWorldOnesOwnLevels()
        {
            HomeScreen.StartSlotWorld(0, worldIndex: 1, maxRig: false);

            IReadOnlyDictionary<string, int> world1MaxLevels = RigBoard.SnapshotMaxLevels(0);

            Assert.AreEqual(5, RigState.Level("e_ff"), "e_ff must cap at World 1's own maxLevel (5), not World 2's (7)");
            Assert.AreEqual(8, RigState.Level("u_dmg"), "u_dmg must cap at World 1's own maxLevel (8, MV-947), not World 2's (10)");
            Assert.AreEqual(0, RigState.Level("e_cmg"), "e_cmg does not exist on World 1's board and must stay unowned");
            Assert.AreEqual(0, RigState.Level("p_cap"), "p_cap (PRIMARY) must stay untouched by the World 2 additions");
            Assert.AreEqual(1, RigState.Level("p_dmg"), "p_dmg is the owned-but-unupgraded floor the morph itself grants");

            foreach (string category in new[] { "ENERGY", "MOVE", "SUPPORT" })
            foreach (string id in RigBoard.AllIds)
            {
                if (RigBoard.Category(id) != category) continue;
                int expected = world1MaxLevels.TryGetValue(id, out int cap) ? cap : 0;
                Assert.AreEqual(expected, RigState.Level(id),
                    $"{id} ({category}) must sit at World 1's own board-authored maxLevel (0 if absent from World 1's board)");
            }
        }
    }
}
