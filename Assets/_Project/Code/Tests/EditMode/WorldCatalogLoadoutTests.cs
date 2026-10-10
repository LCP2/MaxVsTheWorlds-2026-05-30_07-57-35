using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1142 (the one new test): every per-world weapon/board/economy decision this ticket moves
    /// onto <see cref="WorldCatalog"/> — PRIMARY/SECONDARY loadout, the RIG board path, FORGE
    /// availability, the PRIMARY/SECONDARY cell-cost multiplier, the Parts budget multiplier (and its
    /// live DevTuning knob), the Supercell cadence, and the Rack Module's world.
    ///
    /// AC1a installs a fourth "probe" row at index 3 whose every field disagrees with what a literal
    /// <c>worldIndex &gt;= 2</c> / <c>== 1</c> / <c>&lt; 1</c> comparison would have resolved for that
    /// index under the pre-ticket code: a site still comparing the raw index instead of reading
    /// <see cref="WorldCatalog.Get"/> would read the probe as Reef-shaped (UNDERTOW, 2.5x cost, parts
    /// x2, Supercell every 2 areas, no Rack Module) and fail every assertion below.
    ///
    /// AC1b re-checks the three shipped rows — captured live off <see cref="WorldCatalog"/> itself,
    /// never re-typed literals (the ticket forbids asserting a row's own field against the literal it
    /// was written with) — still resolve exactly as they did before this ticket.
    ///
    /// Fails on base commit 3165ac7 (before this ticket): <c>WorldDefinition</c> carries none of the
    /// fields this test sets on the probe row — <c>PrimaryWeapon</c>/<c>SecondaryWeapon</c>/
    /// <c>RigBoardResourcePath</c>/<c>PrimarySecondaryCostMultiplier</c>/<c>PartsMultiplier</c>/
    /// <c>SupercellCadenceAreas</c>/<c>RackModuleDropsHere</c> do not exist there, so this does not
    /// compile against that commit.
    /// </summary>
    public sealed class WorldCatalogLoadoutTests
    {
        private static readonly MethodInfo GrantsSupercellForAreaMethod =
            typeof(PickupDirector).GetMethod("GrantsSupercellForArea", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _directorGo;
        private GameObject _areaGo;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RigBoard.ResetForTests();
            RigFusionState.ResetForTests();
            DevTuning.Reset();
            WorldCatalog.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_areaGo != null) Object.DestroyImmediate(_areaGo);
            WeaponSystemState.Reset();
            RigBoard.ResetForTests();
            RigFusionState.ResetForTests();
            DevTuning.Reset();
            WorldCatalog.ResetForTests();
        }

        [Test]
        public void ProbeWorldAndShippedWorldsResolveWeaponsBoardAndEconomyThroughTheCatalog()
        {
            // ------------------------------------------------------------ fixture: the three shipped
            // rows, live off the catalog itself -- never re-typed literals.
            WorldDefinition backyard = WorldCatalog.Get(0);
            WorldDefinition stormdrain = WorldCatalog.Get(1);
            WorldDefinition reef = WorldCatalog.Get(2);

            var probe = new WorldDefinition
            {
                Id = "probe",
                ConfigKey = backyard.ConfigKey,
                Palette = backyard.Palette,
                Look = backyard.Look,
                Kit = backyard.Kit,
                GateSkin = backyard.GateSkin,
                Style = backyard.Style,
                FiresWaterBeam = false,
                Music = backyard.Music,
                RingAlphaScale = 1f,
                RigBoardResourcePath = RigBoardLibrary.World1ResourcePath,
                PrimaryWeapon = WeaponCatalog.PrimaryKind.Lppe,
                SecondaryWeapon = SecondaryKind.WaterBalloon,
                CarrySecondaryAcrossMorph = false,
                SecondaryMysteryLockedOnMorph = true,
                PrimarySplitSeeded = false,
                FusionsEnabled = true,
                PrimarySecondaryCostMultiplier = 3f,
                UpgradeCostScale = 1f,
                PartsMultiplier = () => 1.5f,
                SupercellCadenceAreas = 3,
                RackModuleDropsHere = false,
            };

            WorldCatalog.UseForTests(new[] { backyard, stormdrain, reef, probe });

            _directorGo = new GameObject("WorldCatalogLoadoutTests PickupDirector");
            PickupDirector director = _directorGo.AddComponent<PickupDirector>();
            _areaGo = new GameObject("WorldCatalogLoadoutTests AreaAccumulationDirector");
            AreaAccumulationDirector areaDirector = _areaGo.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(new WorldConfig { dials = new WorldDials { areaCount = 1 } }, 3);

            // ------------------------------------------------------------ AC1a: the probe world (index 3)
            WeaponSystemState.ApplyWorldLoadout(3);
            Assert.AreEqual(WeaponCatalog.PrimaryKind.Lppe, WeaponSystemState.ActivePrimary,
                "probe: ApplyWorldLoadout must resolve the probe row's own PrimaryWeapon, not a worldIndex-based guess");
            Assert.AreEqual(SecondaryKind.WaterBalloon, WeaponSystemState.SecondaryKind,
                "probe: ApplyWorldLoadout must resolve the probe row's own SecondaryWeapon");
            Assert.AreEqual(3, RigBoard.ActiveWorldIndex, "probe: RigBoard.UseWorld must have run for index 3");
            Assert.AreEqual("PRIMARY", RigBoard.Category("p_rng"),
                "probe: the active board must be the one loaded from UI/rig_board (World 1's own file -- p_rng only exists there)");

            Assert.IsTrue(RigFusionState.EnabledInWorld(3), "probe: FusionsEnabled must read true off the probe row");

            Assert.AreEqual(30, CellSpend.UpgradeCostFor("p_dmg", 1),
                "probe: a PRIMARY node's upgrade cost must scale by the probe row's own 3x multiplier (base 10 * 3)");

            Assert.AreEqual(1.5f, CellEconomyTuning.WorldPartsMultiplier(3), 0.0001f,
                "probe: the Parts multiplier must resolve the probe row's own 1.5x");

            foreach (int area in new[] { 1, 4, 7 })
                Assert.IsTrue((bool)GrantsSupercellForAreaMethod.Invoke(director, new object[] { area }),
                    $"probe: a Supercell must be granted for area {area} on the probe row's own every-3-areas cadence");
            foreach (int area in new[] { 2, 3, 5, 6 })
                Assert.IsFalse((bool)GrantsSupercellForAreaMethod.Invoke(director, new object[] { area }),
                    $"probe: a Supercell must not be granted for area {area} on the probe row's own every-3-areas cadence");

            // ------------------------------------------------------------ AC1b: the three shipped rows
            // still resolve exactly what they resolved before this ticket.
            WeaponSystemState.ApplyWorldLoadout(0);
            Assert.AreEqual(WeaponCatalog.PrimaryKind.Rcda, WeaponSystemState.ActivePrimary, "World 1 must still fire the RCDA");
            Assert.AreEqual(SecondaryKind.WaterBalloon, WeaponSystemState.SecondaryKind, "World 1 must still throw the Water Balloon");
            Assert.IsTrue(RigFusionState.EnabledInWorld(0), "World 1 must still have FORGE enabled");
            Assert.AreEqual(1f, CellEconomyTuning.WorldPartsMultiplier(0), 0.0001f, "World 1's Parts multiplier must still be 1x");

            WeaponSystemState.ApplyWorldLoadout(1);
            Assert.AreEqual(WeaponCatalog.PrimaryKind.Lppe, WeaponSystemState.ActivePrimary, "World 2 must still fire the LPPE");
            Assert.AreEqual(SecondaryKind.ShoulderRack, WeaponSystemState.SecondaryKind, "World 2 must still fire the Shoulder Rack");
            Assert.IsFalse(RigFusionState.EnabledInWorld(1), "World 2 must still have FORGE disabled");
            Assert.AreEqual(1f, CellEconomyTuning.WorldPartsMultiplier(1), 0.0001f, "World 2's Parts multiplier must still be 1x");

            WeaponSystemState.ApplyWorldLoadout(2);
            Assert.AreEqual(WeaponCatalog.PrimaryKind.Undertow, WeaponSystemState.ActivePrimary, "World 3 must still fire UNDERTOW");
            Assert.AreEqual(SecondaryKind.ShoulderRack, WeaponSystemState.SecondaryKind, "World 3 must still fire the Shoulder Rack");
            Assert.IsFalse(RigFusionState.EnabledInWorld(2), "World 3 must still have FORGE disabled");
            Assert.AreEqual(2f, CellEconomyTuning.WorldPartsMultiplier(2), 0.0001f,
                "World 3's Parts multiplier must still default to 2x with the dev knob unset");

            // ------------------------------------------------------------ AC1c: Validate() still reports
            // no problems for the shipped rows.
            WorldCatalog.ResetForTests();
            CollectionAssert.IsEmpty(WorldCatalog.Validate(), "the shipped rows must still validate clean");
        }
    }
}
