using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Save;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1291 (the one new test): finishing the game once must unlock every weapon in every slot, not
    /// just the slot that finished it -- <see cref="WorldCatalog.OwnedPrimariesThroughWorld"/>/
    /// <see cref="WorldCatalog.OwnedSecondariesThroughWorld"/> used to read only the slot's own
    /// <see cref="SaveSlotData.FurthestWorldIndex"/>, so a freshly-RESET slot on a device that had
    /// already seen the ending still reported just World 1's own native pair owned.
    ///
    /// Guards MV-1291: the sole EditMode coverage of the device-wide unlock flag.
    /// </summary>
    public sealed class MV1291AllWeaponsUnlockedTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1291-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void EndingSeenOnOneSlot_UnlocksEveryWeaponForAFreshlyResetOtherSlotFromWorld1()
        {
            const int slotThatFinished = 0;
            const int freshSlot = 1;

            SaveSystem.EnsureProfile(slotThatFinished);
            SaveSystem.MarkEndingSeen(slotThatFinished);

            SaveSystem.Delete(freshSlot);   // RESET: never played on this device
            SaveSlotData fresh = SaveSystem.Load(freshSlot);
            Assert.AreEqual(0, fresh.FurthestWorldIndex, "test precondition: a freshly-reset slot sits at World 1 (index 0)");

            var ownedPrimaries = new HashSet<WeaponCatalog.PrimaryKind>(WorldCatalog.OwnedPrimariesThroughWorld(fresh.FurthestWorldIndex));
            var ownedSecondaries = new HashSet<SecondaryKind>(WorldCatalog.OwnedSecondariesThroughWorld(fresh.FurthestWorldIndex));

            Assert.AreEqual(5, ownedPrimaries.Count, "a fresh slot must own every PRIMARY once any slot has finished the game");
            Assert.AreEqual(3, ownedSecondaries.Count, "a fresh slot must own every SECONDARY once any slot has finished the game");

            // The flag must be a disk fact, not just live static state -- reload it through a fresh read.
            ProfileData reloaded = SaveSystem.LoadProfile();
            Assert.IsTrue(reloaded.AllWeaponsUnlocked, "the device-wide unlock flag must survive a save/load round-trip");
        }
    }
}
