using System.IO;
using NUnit.Framework;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-726 (the one new test): the WORLD 2 dev entry point on the Home screen's slot card. Reaching
    /// World 2 legitimately means clearing all 30 areas of World 1 and collecting the Weapon Core,
    /// which makes World 2 untestable in practice — this proves <see cref="HomeScreen.StartSlotWorld2"/>
    /// (the plain, EditMode-testable method behind the button) seeds a fresh World 2 run correctly:
    /// resolved WorldIndex/PrimaryKind/WeaponCorePending/HasRunInProgress read back through
    /// <see cref="SaveSystem.Load"/>, and a SECONDARY column left mystery-locked with nothing owned,
    /// exactly as a real Weapon Core morph leaves it.
    ///
    /// Fails on base commit 6f3328a: <c>HomeScreen.StartSlotWorld2</c> does not exist there — this does
    /// not compile against that commit (quoted in the fix comment).
    /// </summary>
    public sealed class MV726World2StartTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv726-tests");
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
        public void StartSlotWorld2_SeedsWorldTwo_WithLppePrimaryAndAnUnownedMysteryLockedSecondary()
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

            SaveSlotData after = SaveSystem.Load(0);
            Assert.AreEqual(1, after.WorldIndex, "WORLD 2 must seed WorldIndex = 1 (Stormdrain)");
            Assert.AreEqual(WeaponCatalog.PrimaryKind.Lppe, after.PrimaryKind, "WORLD 2 must equip the LPPE, not the RCDA");
            Assert.IsFalse(after.WeaponCorePending, "no Weapon Core morph is pending on a dev-shortcut start");
            Assert.IsFalse(after.HasRunInProgress, "a fresh WORLD 2 start must not carry a mid-run checkpoint");

            Assert.AreEqual(WeaponCatalog.PrimaryKind.Lppe, WeaponSystemState.ActivePrimary,
                "the live weapon system must also be firing the LPPE, not just the save file");
            Assert.AreEqual(0, WeaponSystemState.ShoulderRackTrackLevel(ShoulderRackTrackKind.RocketDamage),
                "SECONDARY (the Shoulder Rack) must start unpurchased - no owned rocket");
            Assert.IsTrue(RigState.SecondaryLocked,
                "SECONDARY must render as the same mystery-locked state a real Weapon Core morph leaves");
        }
    }
}
