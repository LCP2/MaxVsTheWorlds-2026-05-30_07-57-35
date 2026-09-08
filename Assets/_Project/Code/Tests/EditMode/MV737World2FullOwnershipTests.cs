using System.IO;
using NUnit.Framework;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-737 (the one new test): Lee's requirement (2026-09-08) that the WORLD 2 dev entry point
    /// (<see cref="HomeScreen.StartSlotWorld2"/>, MV-726) must land THE RIG in World 1's own EXIT
    /// state, not a fresh, empty World 1 RIG — a player who reaches World 2 the intended way has
    /// spent a whole World 1 on the board first. Asserts the RESOLVED <see cref="RigState"/> after
    /// the World 2 start: every ENERGY/MOVE/SUPPORT node at its own board-authored
    /// <see cref="RigBoard.MaxLevel"/> (read live from <c>rig_board.world2.json</c> via
    /// <see cref="RigBoard"/>, never a number written into this test), PRIMARY's upgrade tracks
    /// unbought, SECONDARY entirely mystery-locked with nothing owned, every FORGE fusion unforged,
    /// and the wallet empty.
    ///
    /// PRIMARY's own root node (<c>p_dmg</c>) is asserted at level 1, not 0: that is the same
    /// run-start ownership floor <see cref="WeaponSystemState.ApplyWeaponCoreMorph"/> already grants
    /// on every Weapon Core morph (real or dev-shortcut) and that a fresh PLAY grants the RCDA's own
    /// <c>p_dmg</c> — "no upgrades" means the three upgrade tracks below it (<c>p_rng</c>/<c>p_rof</c>/
    /// <c>p_frk</c>) stay at 0, not that the owned weapon itself reverts to unowned. There is no
    /// ordinary <see cref="RigState"/> call that lowers an already-granted level, and this ticket
    /// forbids a debug back door, so touching <c>p_dmg</c> would mean changing the shared morph
    /// <see cref="WeaponSystemState.ApplyWeaponCoreMorph"/> also uses for a real in-run Weapon Core
    /// pickup — flagged in the fix comment rather than guessed at silently.
    ///
    /// Fails on base commit eb822cb (main HEAD before this ticket): <c>StartSlotWorld2</c> runs the
    /// morph but never unlocks or levels ENERGY/MOVE/SUPPORT, so they stay LOCKED and every node sits
    /// at level 0 — quoted in the fix comment.
    /// </summary>
    public sealed class MV737World2FullOwnershipTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv737-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            WeaponSystemState.Reset();   // also resets RigState and RigBoard back to World 1
            RigFusionState.Reset();
            PickupWallet.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            WeaponSystemState.Reset();
            RigBoard.ResetForTests();
            RigFusionState.Reset();
            PickupWallet.Reset();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void StartSlotWorld2_LandsTheRigInWorldOnesOwnExitState()
        {
            // A slot mid-way through a World 1 campaign on the RCDA.
            SaveSystem.Save(0, new SaveSlotData
            {
                HasData = true,
                DisplayName = "DEXTER",
                WorldIndex = 0,
                PrimaryKind = WeaponCatalog.PrimaryKind.Rcda,
            });

            HomeScreen.StartSlotWorld2(0);

            // ENERGY / MOVE / SUPPORT — fully owned: unlocked, every node at its own authored cap.
            foreach (string category in new[] { "ENERGY", "MOVE", "SUPPORT" })
            {
                Assert.IsTrue(RigState.IsCategoryUnlocked(category), $"{category} must be unlocked");
                foreach (string id in RigBoard.AllIds)
                {
                    if (RigBoard.Category(id) != category) continue;
                    Assert.AreEqual(RigBoard.MaxLevel(id), RigState.Level(id),
                        $"{id} ({category}) must sit at its own board-authored maxLevel");
                }
            }

            // PRIMARY — LPPE, no upgrades: owned baseline only, every upgrade track unbought.
            Assert.AreEqual(WeaponCatalog.PrimaryKind.Lppe, WeaponSystemState.ActivePrimary,
                "the live weapon system must be firing the LPPE");
            Assert.AreEqual(1, RigState.Level("p_dmg"), "p_dmg is the owned-but-unupgraded floor, same as any fresh weapon");
            Assert.AreEqual(0, RigState.Level("p_rng"), "p_rng (RANGE) must be unbought");
            Assert.AreEqual(0, RigState.Level("p_rof"), "p_rof (RATE) must be unbought");
            Assert.AreEqual(0, RigState.Level("p_frk"), "p_frk (FORK) must be unbought");

            // SECONDARY — nothing: mystery-locked, category not unlocked, no node owned.
            Assert.IsFalse(RigState.IsCategoryUnlocked("SECONDARY"), "SECONDARY must not be unlocked");
            Assert.IsTrue(RigState.SecondaryLocked, "SECONDARY must still read as mystery-locked");
            foreach (string id in RigBoard.AllIds)
            {
                if (RigBoard.Category(id) != "SECONDARY") continue;
                Assert.IsFalse(RigState.IsOwned(id), $"{id} (SECONDARY) must be unowned");
            }

            // FORGE — every fusion unforged.
            foreach (RigFusionDef fusion in RigBoard.Fusions)
                Assert.IsFalse(RigFusionState.IsForged(fusion.Id), $"{fusion.Id} must be unforged");

            // Wallet — empty: no Parts, no Power Cells.
            Assert.AreEqual(0, PickupWallet.PowerCells, "no Parts");
            Assert.AreEqual(0, PickupWallet.PowerCellsSecondary, "no Power Cells");
        }
    }
}
