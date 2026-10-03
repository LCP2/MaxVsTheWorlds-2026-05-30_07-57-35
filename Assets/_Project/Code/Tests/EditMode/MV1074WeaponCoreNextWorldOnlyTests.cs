using System.IO;
using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Save;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1074 (the one new test, per CC_AUTONOMY's testing policy): a World 1 Weapon Core must never
    /// morph the weapon in the world that dropped it -- only the NEXT world's run start may apply it.
    ///
    /// Root cause (see the ticket): <c>WeaponsScreen.Open()</c> calls
    /// <c>WeaponSystemState.OpenWeaponCoreMorphIfPending(CurrentWorldIndex())</c>, and
    /// <c>CurrentWorldIndex()</c> reads the profile's own <see cref="SaveSlotData.WorldIndex"/>, which has
    /// NOT yet advanced between collecting the Core and crossing the finale gate
    /// (<c>SaveSystem.RecordResult</c> only advances it once the run seals). So opening THE RIG in the
    /// finale world itself used to run the morph against the CURRENT world (0), wiping PRIMARY/SECONDARY
    /// back to their run-start shape and consuming the Core early -- exactly what Lee saw.
    ///
    /// Fails on base commit b04b39b: <c>WeaponSystemState.OpenWeaponCoreMorphIfPending</c> has no guard
    /// against <paramref name="worldIndex"/> not yet having advanced past <see cref="RigBoard.ActiveWorldIndex"/>,
    /// so the first assertion below (<c>p_dmg</c> still at 3 after the simulated Open() call) fails there --
    /// base reads 1 (the RCDA's run-start level, re-granted by the premature morph).
    /// </summary>
    public sealed class MV1074WeaponCoreNextWorldOnlyTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1074-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 0 });

            WeaponSystemState.Reset();   // also resets RigState and RigBoard back to World 1
            PendingMorphingModule.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            PendingMorphingModule.Reset();
            SaveSystem.ActiveSlot = -1;
            SaveSystem.DirectoryOverride = null;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void OpeningTheRigInTheFinaleWorldLeavesTheBoardUntouched_MorphAppliesOnlyAtNextWorldStart_MV1074()
        {
            // Arrange: real investment on World 1's board, a Weapon Core just collected, and the profile
            // still parked on World 1 (WorldIndex 0) -- the run hasn't sealed yet.
            RigState.RaiseLevel("p_dmg");
            RigState.RaiseLevel("p_dmg");   // p_dmg: run-start L1 -> L3
            RigState.UnlockCategory("SECONDARY");
            Assert.AreEqual(3, RigState.Level("p_dmg"));
            Assert.IsTrue(RigState.IsCategoryUnlocked("SECONDARY"));

            PendingMorphingModule.SetWeaponCore();
            Assert.IsTrue(PendingMorphingModule.WeaponCorePending);

            // Act 1: the exact call WeaponsScreen.Open() makes -- CurrentWorldIndex() reads the profile's
            // still-unadvanced WorldIndex (0), the same value the real Open() flow would resolve here.
            int worldIndexOpenWouldUse = SaveSystem.Load(SaveSystem.ActiveSlot).WorldIndex;
            Assert.AreEqual(0, worldIndexOpenWouldUse, "precondition: the run hasn't sealed, so WorldIndex hasn't advanced");

            bool morphed = WeaponSystemState.OpenWeaponCoreMorphIfPending(worldIndexOpenWouldUse);

            // Assert 1: opening THE RIG in the finale world changes nothing and leaves the Core pending.
            Assert.IsFalse(morphed, "opening THE RIG in the world that dropped the Core must not morph anything");
            Assert.AreEqual(3, RigState.Level("p_dmg"), "PRIMARY must stay exactly as invested -- on base this reads 1 (the premature morph's RCDA re-grant)");
            Assert.IsTrue(RigState.IsCategoryUnlocked("SECONDARY"), "SECONDARY must stay unlocked -- the finale-world open must not touch it");
            Assert.IsTrue(PendingMorphingModule.WeaponCorePending, "the Core must still be banked for the next world's run start");

            // Act 2: the run seals and the NEXT world's BackyardPath.Awake applies the banked morph.
            BackyardPath.ApplyPendingMorphAtRunStart(worldIndex: 1);

            // Assert 2: the next world starts on its own new weapon, and the Core is finally consumed.
            Assert.AreEqual(WeaponCatalog.PrimaryKind.Lppe, WeaponSystemState.ActivePrimary,
                "the next world's run start must apply the Weapon Core morph onto the LPPE");
            Assert.IsFalse(PendingMorphingModule.WeaponCorePending, "the next world's run start must consume the banked Core");
        }
    }
}
