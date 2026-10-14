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
            BossVolleyIntervalSeconds = source.BossVolleyIntervalSeconds,
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
            WorldDefinition realReef = WorldCatalog.Get(WorldCatalog.IndexOf(WorldIds.Reef));

            WorldConfig world3Cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(world3Cfg, "world3_config failed to load — see the error log above.");

            // Not named worlds -- these two are deliberately raw structural positions: "one past
            // the shipped catalog's end" and "where the probe row below lands" in the synthetic
            // 4-row test catalog this test builds further down ({backyard, stormdrain, probe,
            // realReef} — independent of how many real rows sit between Stormdrain and the Reef
            // today). probeIndex is pinned to "right after Stormdrain", not to the Reef's own
            // (now-shifted, MV-1176) real index -- those two coincided before the City existed, but
            // no longer do (MV-1146: the guard's patterns flag a bare literal here, but
            // WorldCatalog.IndexOf(WorldIds...) has no id to resolve for a row that hasn't been
            // inserted yet, or for "probe").
            int pastShippedRows = WorldCatalog.Count;
            int probeIndex = WorldCatalog.IndexOf(WorldIds.Stormdrain) + 1;
            int todayRealReefIndex = WorldCatalog.IndexOf(WorldIds.Reef);

            // Past the shipped catalog's real end, no doorway resolves at all.
            Assert.IsTrue(WorldMapLoader.TryLoad(world3Cfg, out MapData baselineMap, out string baselineReason), baselineReason);
            WorldTransitions.ApplyExitDoorway(baselineMap, world3Cfg, worldIndex: pastShippedRows);
            Assert.IsFalse(baselineMap.exitDoorway.HasValue,
                $"before the probe is inserted, world index {pastShippedRows} (past the shipped rows) must get no doorway");

            // What the shipped rows produce for the Reef TODAY, at its own real index — the doorway
            // must still produce once the probe pushes the real Reef row one place further out.
            Assert.IsTrue(WorldMapLoader.TryLoad(world3Cfg, out MapData expectedMap, out string expectedReason), expectedReason);
            WorldTransitions.ApplyExitDoorway(expectedMap, world3Cfg, worldIndex: todayRealReefIndex);
            Assert.IsTrue(expectedMap.exitDoorway.HasValue, "the shipped Reef row must resolve a doorway at its real index");
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

            Assert.IsNotNull(WorldTransitions.For(probeIndex), $"the probe's own row (index {probeIndex}) must resolve a Corridor entry");
            Assert.AreSame(probe.Entry, WorldTransitions.For(probeIndex), $"index {probeIndex} must resolve the probe's own entry, not some other row's");
            Assert.IsNull(WorldTransitions.For(shiftedReefIndex), $"the real Reef, now at index {shiftedReefIndex}, must still resolve no corridor (DoorOnly)");

            Assert.IsTrue(WorldMapLoader.TryLoad(world3Cfg, out MapData actualMap, out string actualReason), actualReason);
            WorldTransitions.ApplyExitDoorway(actualMap, world3Cfg, worldIndex: shiftedReefIndex);
            Assert.IsTrue(actualMap.exitDoorway.HasValue, $"the real Reef, now at index {shiftedReefIndex}, must still resolve a doorway");
            ExitDoorway actual = actualMap.exitDoorway.Value;

            Assert.AreEqual(expected.Wall, actual.Wall, "the Reef's doorway wall must not change when it moves further out");
            Assert.AreEqual(expected.Coord, actual.Coord, 0.001f, "the Reef's doorway coordinate must not change when it moves further out");
            Assert.AreEqual(expected.Hole.Min, actual.Hole.Min, 0.001f, "the Reef's doorway span start must not change when it moves further out");
            Assert.AreEqual(expected.Hole.Max, actual.Hole.Max, 0.001f, "the Reef's doorway span end must not change when it moves further out");
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
            Assert.IsNull(WorldTransitions.For(reefIndex), "the Reef is DoorOnly — it must resolve no corridor entry");

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

        /// <summary>
        /// MV-1168: <see cref="WorldCatalog.Validate"/> caught an unset <c>RingAlphaScale</c> or
        /// <c>SupercellCadenceAreas</c> but had no check for <c>UpgradeCostScale</c> or
        /// <c>PrimarySecondaryCostMultiplier</c> — a row that omits either validates clean and then
        /// prices every RIG node in that world at 1 cell (<c>CellSpend.cs:74</c>/<c>:96</c>:
        /// <c>Mathf.Max(1, Mathf.RoundToInt(base * 0))</c>).
        ///
        /// Fails on the commit this ticket starts from: <see cref="WorldCatalog.Validate"/> reports no
        /// problem for either row below, so both <c>Assert.IsTrue(...Exists...)</c> calls fail.
        /// </summary>
        [Test]
        public void Validate_UpgradeCostScaleOrPrimarySecondaryCostMultiplierAtDefault_ReportsAProblemNamingTheRowAndField()
        {
            WorldDefinition backyard = WorldCatalog.Get(0);
            WorldDefinition stormdrain = WorldCatalog.Get(1);
            // MV-1176: must be the real (DoorOnly) Reef, not whatever row now sits at a bare index 2 —
            // cloning a Corridor-exit row (the City) as this probe's last row would trip the unrelated
            // "last row must be DoorOnly" check these assertions aren't testing.
            WorldDefinition reef = WorldCatalog.Get(WorldCatalog.IndexOf(WorldIds.Reef));

            WorldDefinition zeroCostScale = CloneRowWithId(reef, "probe-cost-scale");
            zeroCostScale.UpgradeCostScale = 0f;
            WorldCatalog.UseForTests(new[] { backyard, stormdrain, zeroCostScale });
            List<string> costScaleProblems = WorldCatalog.Validate();
            Assert.IsTrue(costScaleProblems.Exists(p => p.Contains("probe-cost-scale") && p.Contains("UpgradeCostScale")),
                "a row with UpgradeCostScale left at its default (0f) must report a problem naming the row and field: "
                + string.Join("; ", costScaleProblems));

            WorldDefinition fixedCostScale = CloneRowWithId(zeroCostScale, "probe-cost-scale");
            fixedCostScale.UpgradeCostScale = 1f;
            WorldCatalog.UseForTests(new[] { backyard, stormdrain, fixedCostScale });
            Assert.IsEmpty(WorldCatalog.Validate(), "setting UpgradeCostScale back to 1f must validate clean");

            WorldDefinition zeroMultiplier = CloneRowWithId(reef, "probe-psc-multiplier");
            zeroMultiplier.PrimarySecondaryCostMultiplier = 0f;
            WorldCatalog.UseForTests(new[] { backyard, stormdrain, zeroMultiplier });
            List<string> multiplierProblems = WorldCatalog.Validate();
            Assert.IsTrue(multiplierProblems.Exists(p => p.Contains("probe-psc-multiplier") && p.Contains("PrimarySecondaryCostMultiplier")),
                "a row with PrimarySecondaryCostMultiplier left at its default (0f) must report a problem naming the row and field: "
                + string.Join("; ", multiplierProblems));

            WorldDefinition fixedMultiplier = CloneRowWithId(zeroMultiplier, "probe-psc-multiplier");
            fixedMultiplier.PrimarySecondaryCostMultiplier = 1f;
            WorldCatalog.UseForTests(new[] { backyard, stormdrain, fixedMultiplier });
            Assert.IsEmpty(WorldCatalog.Validate(), "setting PrimarySecondaryCostMultiplier back to 1f must validate clean");
        }
    }
}
