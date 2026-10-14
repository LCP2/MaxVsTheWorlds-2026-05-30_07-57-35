using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1180 (the one new test): the DEV row's W3 button used to hand the tester a fully maxed
    /// rig for every row from index 2 up (<c>HomeScreen.BuildDevWorldShortcuts</c>'s own
    /// <c>capturedIndex &gt;= 2</c> rule), so once MV-1176 inserted the City at row 2 it arrived
    /// maxed too — but Lee tests the City the way a player who just finished World 2 actually
    /// arrives: World 1 -&gt; World 2's own exit-rig shape, one world later. Asserts
    /// <see cref="HomeScreen.StartDevWorld"/> (new in this ticket) routes each row by its own
    /// <see cref="WorldDefinition.DevStart"/> instead.
    ///
    /// Fails on main HEAD before this ticket (8b3300f, MV-1178's merge — the commit on top of
    /// MV-1176's own f5c04d1): <c>HomeScreen.StartDevWorld</c> does not exist there, so this does not
    /// compile against that commit — the compile error is quoted in the fix comment, same shape
    /// MV-736's own "does not compile against that commit" fail-first proof used for a brand-new
    /// static entry point.
    /// </summary>
    public sealed class CityDevStartTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1180-tests");
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
        public void CityDevStartIsTheWorldTwoExitRig()
        {
            int stormdrainIndex = WorldCatalog.IndexOf(WorldIds.Stormdrain);
            int cityIndex = WorldCatalog.IndexOf(WorldIds.City);
            int reefIndex = WorldCatalog.IndexOf(WorldIds.Reef);

            HomeScreen.StartDevWorld(0, cityIndex);

            Assert.AreEqual(WeaponCatalog.PrimaryKind.Undertow, WeaponSystemState.ActivePrimary,
                "the City's own Weapon Core morph grants Undertow");
            Assert.AreEqual(1, RigState.Level("p_dmg"), "p_dmg is the owned-but-unupgraded morph floor");
            Assert.AreEqual(1, RigState.Level("p_spr"), "City's PRIMARY is PrimarySplitSeeded — p_spr arrives owned at level 1");

            IReadOnlyDictionary<string, int> stormdrainMaxLevels = RigBoard.SnapshotMaxLevels(stormdrainIndex);
            foreach (var kv in stormdrainMaxLevels)
            {
                string category = RigBoard.Category(kv.Key);
                if (category != "ENERGY" && category != "MOVE" && category != "SECONDARY") continue;
                Assert.AreEqual(kv.Value, RigState.Level(kv.Key),
                    $"{kv.Key} ({category}) must cap at World 2's own authored maxLevel, carried in as the City's own exit rig");
            }
            Assert.Greater(RigState.Level("s_rkt"), 0, "s_rkt (SECONDARY) must be owned, carried in from World 2");

            Assert.IsFalse(RigState.IsCategoryUnlocked("SUPPORT"), "SUPPORT must stay LOCKED on a City dev arrival");
            Assert.AreEqual(cityIndex, SaveSystem.Load(0).WorldIndex, "StartDevWorld must seed WorldIndex to the City's own row");

            // World 2 must still arrive exactly as before (MV-856/MV-1095): capped at World 1's own
            // levels, SECONDARY still mystery-locked (World 2's row does not carry SECONDARY across
            // the morph).
            HomeScreen.StartDevWorld(0, stormdrainIndex);
            IReadOnlyDictionary<string, int> world1MaxLevels = RigBoard.SnapshotMaxLevels(WorldCatalog.IndexOf(WorldIds.Backyard));
            foreach (var kv in world1MaxLevels)
            {
                string category = RigBoard.Category(kv.Key);
                if (category != "ENERGY" && category != "MOVE") continue;
                Assert.AreEqual(kv.Value, RigState.Level(kv.Key),
                    $"{kv.Key} ({category}) must still cap at World 1's own authored maxLevel for a World 2 dev start");
            }
            Assert.IsTrue(RigState.SecondaryLocked, "World 2's own dev start must still leave SECONDARY mystery-locked");

            // Reef (MaxedRig) must still arrive fully maxed (MV-736), untouched by this ticket.
            HomeScreen.StartDevWorld(0, reefIndex);
            foreach (string id in RigBoard.AllIds)
                Assert.AreEqual(RigBoard.MaxLevel(id), RigState.Level(id), $"{id} must be maxed on a Reef dev start");
        }
    }
}
