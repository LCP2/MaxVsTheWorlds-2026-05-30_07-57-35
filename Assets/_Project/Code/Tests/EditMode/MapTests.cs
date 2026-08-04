using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The map engine (YT-89): the format, the wall solver, the validator, and the shipped Backyard
    /// map itself.
    ///
    /// The shipped map is the v0.5 recut's gated arena (spec §1-3, MV-242): a linear chain of 10
    /// areas — area1 (the entry patio) through area10 — each sealed from the next by its own
    /// <see cref="AreaGate"/>, with the three factories relocated into rooms along the chain
    /// (Areas 3/6/9) and Big Bermuda's compost clearing unchanged at the far end, behind the
    /// unchanged <c>boss_gate</c>. What is pinned here is that shape: the proof that the engine
    /// derives a long gated chain just as happily as it derived a branching yard, and that the run
    /// through it can still be finished.
    /// </summary>
    public sealed class MapTests
    {
        private static MapData Shipped() => MapLibrary.Load(MapLibrary.BackyardSlice);

        /// <summary>Is a point standing inside a wall? Pure geometry — no scene, no colliders.</summary>
        private static bool WalledAt(MapData map, float x, float z)
        {
            foreach (WallSegment w in MapGeometry.Walls(map))
            {
                if (Mathf.Abs(x - w.Center.x) <= w.Size.x * 0.5f &&
                    Mathf.Abs(z - w.Center.z) <= w.Size.z * 0.5f) return true;
            }
            return false;
        }

        /// <summary>Two rooms in a line with a doorway between them — the smallest map that exercises
        /// an exterior wall, a party wall and a hole all at once.</summary>
        private static MapData TwoRooms(float doorway = 4f)
        {
            return new MapData
            {
                name = "Two Rooms",
                wallHeight = 3f,
                wallThickness = 1f,
                zones = new[]
                {
                    new MapZone { id = "a", type = "entry", x = 0f, z = -10f, width = 20f, depth = 20f },
                    new MapZone { id = "b", type = "boss",  x = 0f, z =  10f, width = 20f, depth = 20f },
                },
                links = new[] { new MapLink { from = "a", to = "b", doorway = doorway, gate = "g" } },
                entities = new[]
                {
                    new MapEntity { id = "start", kind = "playerSpawn", x = 0f, z = -10f },
                    new MapEntity { id = "f", kind = "factory", x = 0f, z = -14f },
                    new MapEntity { id = "g", kind = "gate", x = 0f, z = 0f, height = 3f, depth = 0.6f, opensOn = "f" },
                },
            };
        }

        // ---------------------------------------------------------------- the shipped map

        [Test]
        public void TheShippedMap_LoadsAndIsPlayable()
        {
            MapData map = Shipped();
            Assert.IsNotNull(map, "the Backyard map did not load from Resources/Maps");
            Assert.IsTrue(MapValidation.Validate(map, out string why), why);
        }

        /// <summary>The shape the slice is now (v0.5 recut, MV-242): a linear chain of 10 areas plus
        /// the unchanged compost clearing — 11 zones, not 9, and not a branching yard.</summary>
        [Test]
        public void TheShippedMap_IsTenAreasPlusTheCompostClearing()
        {
            MapData map = Shipped();

            Assert.AreEqual(11, map.zones.Length,
                "the slice is 10 gated areas plus the compost clearing");

            for (int i = 1; i <= 10; i++)
                Assert.IsNotNull(map.Zone($"area{i}"), $"the map has no 'area{i}'");
            Assert.IsNotNull(map.Zone("compost"), "the map has no 'compost' clearing");

            // The chain is LINEAR, not branching (spec §1's "option 1" — no orphaned pockets).
            for (int i = 1; i < 10; i++)
            {
                MapZone here = map.Zone($"area{i}");
                MapZone next = map.Zone($"area{i + 1}");
                Assert.AreEqual(here.ZMax, next.ZMin, 1e-3,
                    $"area{i} and area{i + 1} do not sit edge-to-edge along the chain");
            }
        }

        /// <summary>The run is noticeably bigger than the one-factory slice it grew out of (YT-92),
        /// and much bigger again after the v0.5 recut stretched it to 10 areas (MV-242). Pinned as an
        /// area, because "bigger" is a claim about how much ground there is to fight across.</summary>
        [Test]
        public void TheShippedMap_IsMuchBiggerThanTheOldOneFactorySlice()
        {
            Rect bounds = Shipped().Bounds();
            float area = bounds.width * bounds.height;

            Assert.Greater(area, 5000f,
                $"the arena is {bounds.width:0} x {bounds.height:0} m — barely bigger than the one you " +
                "could cross before the fight started");
        }

        /// <summary>Three factories, each standing in a room of its own, spread down the 10-area chain
        /// (MV-242's default: Areas 3/6/9) — so clearing them is a sequence you fight your way
        /// through, not one beat (YT-92, YT-148).</summary>
        [Test]
        public void TheShippedMap_HasThreeFactories_InAreasThreeSixAndNine()
        {
            MapData map = Shipped();
            var factories = MapValidation.Kind(map, EntityKind.Factory);

            Assert.AreEqual(3, factories.Count, "the run does not have three sources of pressure");

            MapEntity mower = map.Entity("mower_hutch");
            MapEntity greenhouse = map.Entity("greenhouse_hutch");
            MapEntity toolshed = map.Entity("toolshed_hutch");

            Assert.AreEqual("area3", map.ZoneAt(mower.x, mower.z)?.id,
                "the first factory is not in area3");
            Assert.AreEqual("area6", map.ZoneAt(greenhouse.x, greenhouse.z)?.id,
                "the second factory is not in area6");
            Assert.AreEqual("area9", map.ZoneAt(toolshed.x, toolshed.z)?.id,
                "the third factory is not in area9");

            // The later ones are genuinely FURTHER IN — each factory sits deeper down the chain than
            // the last, so clearing the run is a sequence, not three objectives side by side.
            Assert.Greater(greenhouse.z, mower.z + 10f,
                "the second factory is not deep enough into the run to be a second objective");
            Assert.Greater(toolshed.z, greenhouse.z + 10f,
                "the third factory is not deep enough into the run to be a third objective");

            Assert.IsNull(map.Entity("central_hutch"),
                "the central garden factory should be gone (WV-229) — sheds now grant abilities " +
                "instead of standing as a fixed fourth source of pressure");
        }

        /// <summary>The whole 10-area chain, walkable link by link, ending at the compost clearing —
        /// the literal shape MV-242's AC asks for.</summary>
        [Test]
        public void TheShippedMap_ChainsAllTenAreasSequentially_WithNineAreaGates()
        {
            MapData map = Shipped();

            for (int i = 1; i < 10; i++)
                Assert.IsTrue(Linked(map, $"area{i}", $"area{i + 1}"),
                    $"area{i} is not linked to area{i + 1} — the chain is broken");
            Assert.IsTrue(Linked(map, "area10", "compost"), "the boss arena cannot be reached from area10");

            var gates = MapValidation.Kind(map, EntityKind.AreaGate);
            Assert.AreEqual(9, gates.Count, "10 areas in a chain need exactly 9 area gates between them");

            // And the engine agrees: validation refuses a boss you cannot walk to, and refuses any
            // area gate that fills no doorway.
            Assert.IsTrue(MapValidation.Validate(map, out string why), why);
        }

        private static bool Linked(MapData map, string a, string b)
        {
            foreach (MapLink link in map.links)
                if ((link.from == a && link.to == b) || (link.from == b && link.to == a)) return true;
            return false;
        }

        /// <summary>The whole mission in one assertion: the gate into the boss arena is opened by
        /// destroying ALL the factories, and that is stated in the map rather than coded anywhere
        /// (YT-92, YT-148). One key would put the boss behind a door the player opens halfway
        /// through. Three factories since the central garden shed was removed (WV-229) — the gate
        /// only names the ones still standing. Unchanged by the v0.5 recut (MV-242): only the
        /// factories' rooms moved, not this wiring.</summary>
        [Test]
        public void TheShippedMap_OpensTheBossGateOnlyWhenEveryFactoryIsDown()
        {
            MapData map = Shipped();
            MapEntity gate = map.First(EntityKind.Gate);

            Assert.IsNotNull(gate);
            Assert.AreEqual(3, gate.Keys.Length, "the boss gate is not waiting on all three factories");

            foreach (string key in gate.Keys)
            {
                MapEntity factory = map.Entity(key);
                Assert.IsNotNull(factory, $"the gate opens on '{key}', which is not in the map");
                Assert.AreEqual(EntityKind.Factory, factory.Kind, $"'{key}' is not a factory");
            }
        }

        [Test]
        public void TheShippedMap_LeavesTheMissionLineWalkable()
        {
            MapData map = Shipped();

            // Straight up the middle from the patio (area1), through every area, to the boss gate:
            // never walled. The doorways between the areas are on this line, which is what makes it
            // the line — and a doorway is cut into the wall regardless of whether its area gate has
            // been broken yet (MapGeometry works from the link, not the gate's HP).
            for (float z = -4f; z <= 220f; z += 1f)
                Assert.IsFalse(WalledAt(map, 0f, z), $"the mission line is walled at z={z}");
        }

        // ---------------------------------------------------------------- the wall solver

        /// <summary>The rule that keeps a room the size the author typed. An outside wall belongs
        /// OUTSIDE the room — put it on the room's edge and every room is quietly a wall thinner than
        /// it says it is.</summary>
        [Test]
        public void AnExteriorWall_SitsOutsideItsRoom_SoTheRoomIsAsWideAsAuthored()
        {
            MapData map = TwoRooms();

            // Room 'a' spans x −10..10. Every centimetre of that is floor, right up to the wall.
            for (float x = -9.9f; x <= 9.9f; x += 0.5f)
                Assert.IsFalse(WalledAt(map, x, -10f), $"room 'a' is walled at x={x} — it is narrower than authored");

            Assert.IsTrue(WalledAt(map, -10.5f, -10f), "no left wall on room 'a'");
            Assert.IsTrue(WalledAt(map, 10.5f, -10f), "no right wall on room 'a'");
        }

        /// <summary>Where two rooms meet, there is ONE wall and they share it — not two overlapping
        /// slabs fighting over the same plane.</summary>
        [Test]
        public void APartyWall_IsSharedBetweenTheTwoRooms_NotBuiltTwice()
        {
            MapData map = TwoRooms();

            List<WallSegment> onTheJoin = MapGeometry.Walls(map)
                .FindAll(w => Mathf.Abs(w.Center.z) < 1f && w.Size.x > w.Size.z);

            // Two shoulders, one either side of the doorway. Not four.
            Assert.AreEqual(2, onTheJoin.Count,
                "the shared boundary produced overlapping walls — it was solved per room, not per line");

            foreach (WallSegment w in onTheJoin)
                Assert.AreEqual(0f, w.Center.z, 1e-3, "a party wall should straddle the line the rooms share");
        }

        [Test]
        public void ADoorway_IsAHoleInTheWall_AndTheWallResumesEitherSideOfIt()
        {
            MapData map = TwoRooms(doorway: 4f);

            Assert.IsFalse(WalledAt(map, 0f, 0f), "the doorway is bricked up");
            Assert.IsFalse(WalledAt(map, 1.9f, 0f), "the doorway is narrower than authored");
            Assert.IsTrue(WalledAt(map, 3f, 0f), "the wall does not resume to the right of the doorway");
            Assert.IsTrue(WalledAt(map, -3f, 0f), "the wall does not resume to the left of the doorway");
        }

        /// <summary>The engine's whole promise: move a room and it re-walls itself. No scene edit, no
        /// wall to drag, nothing to keep in sync.</summary>
        [Test]
        public void MovingARoom_RewallsIt()
        {
            MapData map = Shipped();

            // area2 spans x −13..13, so its left wall stands just outside that. z = 17 is area2's own
            // centre, well clear of either doorway at its edges.
            Assert.IsTrue(WalledAt(map, -13.5f, 17f), "area2's left wall is not where it started");

            map.Zone("area2").x += 5f;   // slide the fight room right

            Assert.IsFalse(WalledAt(map, -13.5f, 17f), "the old wall is still standing where area2 used to be");
            Assert.IsTrue(WalledAt(map, -8.5f, 17f), "no wall was built along area2's new edge");
        }

        [Test]
        public void AGate_IsAsWideAsTheDoorwayItFills_PlusTheWallEitherSide()
        {
            MapData map = Shipped();
            MapEntity gate = map.First(EntityKind.Gate);

            // The doorway, plus a wall's thickness on each side. No sliver to squeeze through.
            // Derived rather than hard-coded: the wall's thickness is level data and gets tuned
            // (YT-112 took it from 1 m to 0.6 m), and a literal here fails that as if it were a bug.
            float doorway = 0f;
            foreach (MapLink link in map.links)
                if (link.gate == gate.id) doorway = link.doorway;

            Assert.Greater(doorway, 0f, "the shipped gate fills no doorway");
            Assert.AreEqual(doorway + map.wallThickness * 2f, MapRuntime.SealWidth(map, gate), 1e-3);
        }

        [Test]
        public void WideningADoorway_WidensTheGateThatFillsIt()
        {
            MapData map = Shipped();
            MapEntity gate = map.First(EntityKind.Gate);

            foreach (MapLink link in map.links)
                if (link.gate == gate.id) link.doorway = 11f;

            Assert.AreEqual(11f + map.wallThickness * 2f, MapRuntime.SealWidth(map, gate), 1e-3,
                "the gate did not follow its doorway — it would leave a gap beside itself");
        }

        // ---------------------------------------------------------------- validation

        [Test]
        public void Validation_RejectsABossYouCannotWalkTo()
        {
            MapData map = TwoRooms();

            // Cut the only way through — and take the gate with it, so the failure we get is the one
            // we are testing for and not "a gate that fills no doorway".
            map.links = new MapLink[0];
            map.entities = new[]
            {
                new MapEntity { id = "start", kind = "playerSpawn", x = 0f, z = -10f },
            };

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("cannot be walked to", why);
        }

        [Test]
        public void Validation_RejectsAGateWithNoKey()
        {
            MapData map = TwoRooms();
            map.Entity("g").opensOn = "";

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("no key", why);
        }

        [Test]
        public void Validation_RejectsADoorwayTooNarrowToFightThrough()
        {
            MapData map = TwoRooms(doorway: 1f);

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("doorway", why);
        }

        [Test]
        public void Validation_RejectsALinkBetweenRoomsThatDoNotTouch()
        {
            MapData map = TwoRooms();
            map.Zone("b").z += 30f;   // shove the boss arena away from the entry

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("do not share an edge", why);
        }

        /// <summary>The invariant that survived from the hand-built cover set: a prop tangent to the
        /// spawn ring still spawns robots halfway inside itself.</summary>
        [Test]
        public void Validation_RejectsCoverCrowdingTheFactorysSpawnRing()
        {
            MapData map = Shipped();
            MapEntity factory = map.First(EntityKind.Factory);

            var onTheRing = new List<MapEntity>(map.entities)
            {
                new MapEntity
                {
                    id = "Cover Crowder", kind = "cover",
                    x = factory.x, z = factory.z + MapValidation.SpawnRadius,
                    width = 2f, height = 2f, depth = 2f,
                },
            };
            map.entities = onTheRing.ToArray();

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("spawn ring", why);
        }

        /// <summary>Readability first: the boss fight is an open room. Cover in it is a rule the
        /// design board states and the engine now enforces.</summary>
        [Test]
        public void Validation_RejectsCoverInTheBossArena()
        {
            MapData map = Shipped();

            var withCover = new List<MapEntity>(map.entities)
            {
                new MapEntity { id = "Cover Compost", kind = "cover", x = 8f, z = 234f, width = 2f, height = 2f, depth = 2f },
            };
            map.entities = withCover.ToArray();

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("boss", why);
        }

        /// <summary>A gate can be keyed to several factories (YT-92) — and every one of the names has
        /// to be real. A gate that names two factories and misspells one is a gate that can never open,
        /// which plays as a finished level that simply refuses to end.</summary>
        [Test]
        public void Validation_RejectsAGateKeyedToSomethingThatIsNotAFactory()
        {
            MapData map = TwoRooms();
            map.Entity("g").opensOn = "f, start";   // 'start' is the player spawn

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("not a factory", why);
        }

        [Test]
        public void Validation_AcceptsAGateKeyedToEveryFactoryInTheMap()
        {
            MapData map = TwoRooms();

            var withTwo = new List<MapEntity>(map.entities)
            {
                new MapEntity { id = "f2", kind = "factory", x = 6f, z = -14f },
            };
            map.entities = withTwo.ToArray();
            map.Entity("g").opensOn = "f, f2";

            Assert.IsTrue(MapValidation.Validate(map, out string why), why);
            Assert.AreEqual(new[] { "f", "f2" }, map.Entity("g").Keys,
                "a hand-written key list has to read the way it looks");
        }

        [Test]
        public void Validation_RejectsAFightRoomTooTightToCircleIn()
        {
            MapData map = Shipped();
            map.Zone("area2").width = 9f;   // back to the corridor YT-68 tore out

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("circle-strafe", why);
        }

        [Test]
        public void Validation_RejectsAnEntityStandingInTheVoid()
        {
            MapData map = Shipped();
            map.First(EntityKind.Factory).x = 500f;

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("not inside any zone", why);
        }

        [Test]
        public void Validation_RejectsTwoZonesWithTheSameId()
        {
            MapData map = Shipped();
            map.Zone("area2").id = "area1";

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("share the id", why);
        }

        // ---------------------------------------------------------------- the format

        [Test]
        public void TheFormat_SurvivesARoundTripThroughJson()
        {
            MapData before = Shipped();
            MapData after = MapLibrary.Parse(MapLibrary.ToJson(before));

            Assert.IsNotNull(after);
            Assert.AreEqual(before.zones.Length, after.zones.Length);
            Assert.AreEqual(before.entities.Length, after.entities.Length);
            Assert.AreEqual(before.Zone("area2").width, after.Zone("area2").width, 1e-3);
            Assert.AreEqual(before.First(EntityKind.Gate).opensOn, after.First(EntityKind.Gate).opensOn);
        }

        /// <summary>A map file is written by hand, so the words in it are forgiving.</summary>
        [Test]
        public void TheFormat_ReadsItsKindsCaseAndSeparatorInsensitively()
        {
            Assert.AreEqual(EntityKind.PlayerSpawn, MapEnums.Entity("playerSpawn"));
            Assert.AreEqual(EntityKind.PlayerSpawn, MapEnums.Entity("player_spawn"));
            Assert.AreEqual(EntityKind.PlayerSpawn, MapEnums.Entity("PLAYER-SPAWN"));
            Assert.AreEqual(EntityKind.Unknown, MapEnums.Entity("teleporter"));
        }

        // ---------------------------------------------------------------- robot accumulation (MV-242)

        /// <summary>The runtime area index MV-223/MV-224 built with no live hook now has one — proved
        /// against the ACTUAL shipped tuning, across every area the shipped map has
        /// (<see cref="ArenaTuning.DefaultAreaCount"/>): population grows every area, and both tough
        /// tiers appear exactly where the spec table says they do.</summary>
        [Test]
        public void AreaPopulation_EscalatesAcrossEveryShippedArea_HeavyFromFiveBruteFromEight()
        {
            int areaCount = (int)ArenaTuning.DefaultAreaCount;
            Assert.AreEqual(10, areaCount, "the shipped chain is no longer 10 areas — update this test's range");

            int previousTotal = -1;
            for (int area = 1; area <= areaCount; area++)
            {
                var (large, small) = AreaPopulation.ComposeForArea(area,
                    RobotCompositionTuning.DefaultStartLargeCount, RobotCompositionTuning.DefaultStartSmallCount,
                    RobotCompositionTuning.DefaultAreaGrowthPct, RobotCompositionTuning.DefaultLargeToSmallRatio,
                    RobotCompositionTuning.DefaultLargeShareDriftPerArea);

                int total = large + small;
                Assert.GreaterOrEqual(total, previousTotal, $"area {area}'s population did not grow");
                previousTotal = total;

                var (bruiser, heavy, brute) = AreaPopulation.ToughSplitForArea(area, large,
                    RobotCompositionTuning.DefaultHeavyIntroArea, RobotCompositionTuning.DefaultBruteIntroArea,
                    RobotCompositionTuning.DefaultToughSubstitutionPct);

                Assert.AreEqual(area >= RobotCompositionTuning.DefaultHeavyIntroArea, heavy > 0,
                    $"area {area}: Heavy should only appear from Area {RobotCompositionTuning.DefaultHeavyIntroArea}");
                Assert.AreEqual(area >= RobotCompositionTuning.DefaultBruteIntroArea, brute > 0,
                    $"area {area}: Brute should only appear from Area {RobotCompositionTuning.DefaultBruteIntroArea}");
                Assert.AreEqual(large, bruiser + heavy + brute,
                    $"area {area}: the tough split invented or lost large-slot robots");
            }
        }
    }
}
