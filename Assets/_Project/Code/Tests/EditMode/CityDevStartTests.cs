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
    ///
    /// MV-1328 (Lee, 2026-10-31) supersedes this test's own SECONDARY assertions: a dev start no
    /// longer carries/maxes SECONDARY's level across (same rule the City's own corridor entry now
    /// follows) — PRIMARY/SECONDARY land on their own board's bottom floor everywhere, including the
    /// Reef's MaxedRig row. Updated in place rather than left to rot red, same precedent MV-1095's own
    /// note elsewhere in this suite sets.
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

            // MV-1195: the City's own Weapon Core morph now grants RICOCHET, not UNDERTOW -- and
            // RICOCHET's own RICOCHETS/RATE tracks need the same explicit non-zero-floor seed p_dmg
            // already gets (ApplyWeaponCoreMorph's own doc), so a DEV start lands exactly at the
            // ticket's own authored start levels: Ricochets 2 (MV-1330 restored, was 2 pre-MV-1325),
            // Power 1, Rate 1, Aiming 0.
            Assert.AreEqual(WeaponCatalog.PrimaryKind.Ricochet, WeaponSystemState.ActivePrimary,
                "the City's own Weapon Core morph grants Ricochet");
            Assert.AreEqual(1, RigState.Level("p_dmg"), "p_dmg (POWER) is the owned-but-unupgraded morph floor");
            Assert.AreEqual(2, RigState.Level("p_ric"), "p_ric (RICOCHETS) must arrive at its own board start level, 2");
            Assert.AreEqual(1, RigState.Level("p_rof"), "p_rof (RATE) must arrive at its own board start level, 1");
            Assert.AreEqual(0, RigState.Level("p_aim"), "p_aim (AIMING) must arrive unowned, at 0");

            IReadOnlyDictionary<string, int> stormdrainMaxLevels = RigBoard.SnapshotMaxLevels(stormdrainIndex);
            foreach (var kv in stormdrainMaxLevels)
            {
                string category = RigBoard.Category(kv.Key);
                if (category != "ENERGY" && category != "MOVE") continue;
                Assert.AreEqual(kv.Value, RigState.Level(kv.Key),
                    $"{kv.Key} ({category}) must cap at World 2's own authored maxLevel, carried in as the City's own exit rig");
            }
            // MV-1328: SECONDARY no longer carries/maxes to World 2's own level here -- it lands on the
            // City's own board floor instead (s_rkt's own authored startLevel, 0, bumped to 1 so the
            // rack the player already owns still fires), same floor a real corridor entry gets.
            Assert.AreEqual(1, RigState.Level("s_rkt"),
                "s_rkt (SECONDARY) must land on the City's own board floor, not carried/maxed from World 2");

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

            // Reef (MaxedRig, MV-736): every OTHER category still arrives fully maxed, but MV-1328
            // stops PRIMARY/SECONDARY being maxed here too -- they land on the same bottom-of-board
            // floor a plain corridor entry into the Reef gets (AC4), captured below by running that
            // same ApplyWeaponCoreMorph entry on an identical fresh history before MaxOutRig's own
            // extra maxing runs, rather than guessing at Undertow/Shoulder Rack's own authored ids.
            WeaponSystemState.Reset();
            // MV-1328/MV-1180: StartSlotWorld itself unlocks SECONDARY before the morph whenever
            // CarrySecondaryAcrossMorph keeps it open (simulating a dev start arriving having actually
            // played the previous world) -- mirror that here so the probe reads the same floor the
            // real dev-start path does, not a still-locked one.
            if (WorldCatalog.Get(reefIndex).CarrySecondaryAcrossMorph) RigState.UnlockCategory("SECONDARY");
            WeaponSystemState.ApplyWeaponCoreMorph(reefIndex);
            var expectedWeaponFloor = new Dictionary<string, int>();
            foreach (string id in RigBoard.AllIds)
            {
                string category = RigBoard.Category(id);
                if (category == "PRIMARY" || category == "SECONDARY") expectedWeaponFloor[id] = RigState.Level(id);
            }
            WeaponSystemState.Reset();

            HomeScreen.StartDevWorld(0, reefIndex);
            foreach (string id in RigBoard.AllIds)
            {
                string category = RigBoard.Category(id);
                if (category == "PRIMARY" || category == "SECONDARY")
                    Assert.AreEqual(expectedWeaponFloor[id], RigState.Level(id),
                        $"{id} ({category}) must land on the same bottom-of-board floor a plain corridor entry gets, not maxed (MV-1328)");
                else
                    Assert.AreEqual(RigBoard.MaxLevel(id), RigState.Level(id), $"{id} must be maxed on a Reef dev start");
            }
        }
    }
}
