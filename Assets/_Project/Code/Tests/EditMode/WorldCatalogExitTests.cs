using System.Collections.Generic;
using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1143 (the one new test, per CC_AUTONOMY's testing policy): a world's exit, corridor and
    /// arrival now come off its own <see cref="WorldDefinition"/> row instead of two tables keyed by a
    /// literal world number (<c>WorldTransitions.Entries</c> / <c>FinaleOnlyDoors</c>, both deleted by
    /// this ticket). Asserts only RESOLVED values through the real entry points
    /// (<see cref="WorldTransitions.For"/>, <see cref="WorldTransitions.ApplyExitDoorway"/>,
    /// <see cref="WorldCatalog.Validate"/>) against the real world configs — never an authored row field
    /// re-read against itself (Testing Policy v2, Tier 1).
    ///
    /// Fails on the commit this ticket starts from: <c>WorldExitMode</c>, <c>ExitCorridorDressingKind</c>,
    /// <c>ArrivalDressingKind</c> and <see cref="WorldDefinition"/>'s own <c>ExitMode</c>/<c>Entry</c>/
    /// <c>ExitDressing</c>/<c>ArrivalDressing</c>/<c>ExitWall</c>/<c>ExitDoorPos</c> fields don't exist yet
    /// (CS0246/CS1061).
    /// </summary>
    public sealed class WorldCatalogExitTests
    {
        [TearDown]
        public void TearDown() => WorldCatalog.ResetForTests();

        private static WorldDefinition CloneRowWithId(WorldDefinition source, string id) => new WorldDefinition
        {
            Id = id,
            ConfigKey = source.ConfigKey,
            Palette = source.Palette,
            Look = source.Look,
            Kit = source.Kit,
            GateSkin = source.GateSkin,
            Style = source.Style,
            FiresWaterBeam = source.FiresWaterBeam,
            Music = source.Music,
            RingAlphaScale = source.RingAlphaScale,
            RigBoardResourcePath = source.RigBoardResourcePath,
            PrimaryWeapon = source.PrimaryWeapon,
            SecondaryWeapon = source.SecondaryWeapon,
            CarrySecondaryAcrossMorph = source.CarrySecondaryAcrossMorph,
            SecondaryMysteryLockedOnMorph = source.SecondaryMysteryLockedOnMorph,
            PrimarySplitSeeded = source.PrimarySplitSeeded,
            FusionsEnabled = source.FusionsEnabled,
            PrimarySecondaryCostMultiplier = source.PrimarySecondaryCostMultiplier,
            UpgradeCostScale = source.UpgradeCostScale,
            PartsMultiplier = source.PartsMultiplier,
            SupercellCadenceAreas = source.SupercellCadenceAreas,
            RackModuleDropsHere = source.RackModuleDropsHere,
            ExitMode = source.ExitMode,
            ExitWall = source.ExitWall,
            ExitDoorPos = source.ExitDoorPos,
            Entry = source.Entry,
            ExitDressing = source.ExitDressing,
            ArrivalDressing = source.ArrivalDressing,
            RunsLegacyExitGate = source.RunsLegacyExitGate,
        };

        [Test]
        public void ProbeOrder_InsertingACorridorRowShiftsTheRealLastWorldsDoorway()
        {
            WorldDefinition backyard = WorldCatalog.Get(0);
            WorldDefinition stormdrain = WorldCatalog.Get(1);
            WorldDefinition realReef = WorldCatalog.Get(2);

            WorldConfig world3Cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(world3Cfg, "world3_config failed to load — see the error log above.");

            // Not named worlds -- these two are deliberately raw structural positions: "one past
            // the shipped catalog's end" and "where the probe row below lands", before and after
            // this test shifts the real Reef one place further out (MV-1146: the guard's patterns
            // flag a bare literal here, but WorldCatalog.IndexOf(WorldIds...) has no id to resolve
            // for a row that hasn't been inserted yet, or for "probe").
            int pastShippedRows = WorldCatalog.Count;
            int probeIndex = WorldCatalog.IndexOf(WorldIds.Reef);

            // On the shipped 3-row catalog, index 3 (past the last row) gets no doorway at all.
            Assert.IsTrue(WorldMapLoader.TryLoad(world3Cfg, out MapData baselineMap, out string baselineReason), baselineReason);
            WorldTransitions.ApplyExitDoorway(baselineMap, world3Cfg, worldIndex: pastShippedRows);
            Assert.IsFalse(baselineMap.exitDoorway.HasValue,
                "before the probe is inserted, world index 3 (past the shipped rows) must get no doorway");

            // What the shipped rows produce for the Reef TODAY, at index 2 — the doorway index 3 must
            // still produce once the probe pushes the real Reef row one place further out.
            Assert.IsTrue(WorldMapLoader.TryLoad(world3Cfg, out MapData expectedMap, out string expectedReason), expectedReason);
            WorldTransitions.ApplyExitDoorway(expectedMap, world3Cfg, worldIndex: probeIndex);
            Assert.IsTrue(expectedMap.exitDoorway.HasValue, "the shipped Reef row must resolve a doorway at index 2");
            ExitDoorway expected = expectedMap.exitDoorway.Value;

            WorldDefinition probe = CloneRowWithId(realReef, "probe");
            probe.ExitMode = WorldExitMode.Corridor;
            probe.Entry = new WorldTransitionEntry(fromWorld: probeIndex, exitWall: realReef.ExitWall, exitDoorPos: realReef.ExitDoorPos,
                corridorLength: stormdrain.Entry.CorridorLength,
                segmentAEnd: stormdrain.Entry.SegmentAEnd, segmentBEnd: stormdrain.Entry.SegmentBEnd,
                arrivalWall: stormdrain.Entry.ArrivalWall, arrivalDoorPos: stormdrain.Entry.ArrivalDoorPos,
                arrivalShellLength: stormdrain.Entry.ArrivalShellLength);

            WorldCatalog.UseForTests(new[] { backyard, stormdrain, probe, realReef });
            int shiftedReefIndex = probeIndex + 1;

            Assert.IsNotNull(WorldTransitions.For(probeIndex), "the probe's own row (index 2) must resolve a Corridor entry");
            Assert.AreSame(probe.Entry, WorldTransitions.For(probeIndex), "index 2 must resolve the probe's own entry, not some other row's");
            Assert.IsNull(WorldTransitions.For(shiftedReefIndex), "the real Reef, now at index 3, must still resolve no corridor (DoorOnly)");

            Assert.IsTrue(WorldMapLoader.TryLoad(world3Cfg, out MapData actualMap, out string actualReason), actualReason);
            WorldTransitions.ApplyExitDoorway(actualMap, world3Cfg, worldIndex: shiftedReefIndex);
            Assert.IsTrue(actualMap.exitDoorway.HasValue, "the real Reef, now at index 3, must still resolve a doorway");
            ExitDoorway actual = actualMap.exitDoorway.Value;

            Assert.AreEqual(expected.Wall, actual.Wall, "the Reef's doorway wall must not change when it moves to index 3");
            Assert.AreEqual(expected.Coord, actual.Coord, 0.001f, "the Reef's doorway coordinate must not change when it moves to index 3");
            Assert.AreEqual(expected.Hole.Min, actual.Hole.Min, 0.001f, "the Reef's doorway span start must not change when it moves to index 3");
            Assert.AreEqual(expected.Hole.Max, actual.Hole.Max, 0.001f, "the Reef's doorway span end must not change when it moves to index 3");
        }

        [Test]
        public void ShippedRows_ForAndApplyExitDoorwayResolveEveryWorld()
        {
            int stormdrainIndex = WorldCatalog.IndexOf(WorldIds.Stormdrain);
            int reefIndex = WorldCatalog.IndexOf(WorldIds.Reef);

            Assert.AreSame(WorldCatalog.Get(0).Entry, WorldTransitions.For(0),
                "world 0's own entry must come straight off its row");
            Assert.AreSame(WorldCatalog.Get(stormdrainIndex).Entry, WorldTransitions.For(stormdrainIndex),
                "world 1's own entry must come straight off its row");
            Assert.IsNull(WorldTransitions.For(reefIndex), "world 2 (the Reef) is DoorOnly — it must resolve no corridor entry");

            for (int i = 0; i < WorldLibrary.Count; i++)
            {
                WorldConfig cfg = WorldLibrary.Load(WorldLibrary.KeyForIndex(i));
                Assert.IsNotNull(cfg, $"world {i} failed to load — see the error log above.");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
                WorldTransitions.ApplyExitDoorway(map, cfg, i);
                Assert.IsTrue(map.exitDoorway.HasValue, $"world {i} must resolve a doorway for either exit mode");
            }
        }

        [Test]
        public void Validate_ShippedRowsClean_ThreeRowListWithCorridorLastRowReportsAProblem()
        {
            List<string> shippedProblems = WorldCatalog.Validate();
            Assert.IsEmpty(shippedProblems, "the shipped rows must validate clean: " + string.Join("; ", shippedProblems));

            WorldDefinition backyard = WorldCatalog.Get(0);
            WorldDefinition stormdrain = WorldCatalog.Get(1);
            WorldDefinition badLastRow = CloneRowWithId(WorldCatalog.Get(2), "bad-reef");
            badLastRow.ExitMode = WorldExitMode.Corridor;
            badLastRow.Entry = stormdrain.Entry;

            WorldCatalog.UseForTests(new[] { backyard, stormdrain, badLastRow });
            List<string> problems = WorldCatalog.Validate();
            Assert.IsNotEmpty(problems, "a three-row list whose last row is Corridor must report a problem");
        }
    }
}
