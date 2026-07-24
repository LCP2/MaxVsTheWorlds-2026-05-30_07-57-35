using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The map engine (YT-89): the format, the wall solver, the validator, and the shipped Backyard
    /// map itself.
    ///
    /// The shipped map is no longer the straight corridor the slice first shipped as — it has a nook
    /// off the lawn, a shed housing the factory, a gatehouse and a boss clearing, and it TURNS. That
    /// was the point of reshaping it. So what is pinned here is the new shape, room by room: the
    /// proof that the engine derives a level with rooms hanging off it just as happily as it derived
    /// a hallway, and that the run through it can still be finished.
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

        /// <summary>The shape the slice is now: nine rooms, not three, and not in one straight line.
        /// This is the thing the engine was built to make cheap, so it is the thing that gets
        /// pinned. The ninth is the toolshed off the orchard's right (YT-148) — a third factory that
        /// mirrors the greenhouse on the left, so the orchard fight has pressure from both flanks.</summary>
        [Test]
        public void TheShippedMap_IsNineRooms_WithPocketsOffBothFightRooms()
        {
            MapData map = Shipped();

            Assert.AreEqual(9, map.zones.Length,
                "the slice is nine rooms — patio, lawn, nook, shed, orchard, greenhouse, gatehouse, " +
                "compost, toolshed");

            foreach (string id in new[] { "patio", "lawn", "nook", "shed", "orchard", "greenhouse",
                                          "gatehouse", "compost", "toolshed" })
                Assert.IsNotNull(map.Zone(id), $"the map has no '{id}'");

            // The turn is the point. A nook off the lawn's left and a shed off its right mean the
            // route is no longer "walk up Z", which is what every hard-coded thing in the yard assumed.
            MapZone lawn = map.Zone("lawn");
            Assert.Less(map.Zone("nook").x, lawn.XMin, "the nook is not off the lawn's left");
            Assert.Greater(map.Zone("shed").x, lawn.XMax, "the shed is not off the lawn's right");

            // And the orchard is a SECOND fight room past the lawn, flanked by TWO sheds — the
            // greenhouse off its left and the toolshed off its right (YT-148) — so clearing the run
            // is a sequence of factories you fight your way through, not one beat (YT-92).
            MapZone orchard = map.Zone("orchard");
            Assert.Greater(orchard.ZMin, lawn.ZMin, "the orchard is not up-field of the lawn");
            Assert.Less(map.Zone("greenhouse").x, orchard.XMin, "the greenhouse is not off the orchard's left");
            Assert.Greater(map.Zone("toolshed").x, orchard.XMax, "the toolshed is not off the orchard's right");
        }

        /// <summary>The run is noticeably bigger than the one-factory slice it grew out of (YT-92).
        /// Pinned as an area, because "bigger" is a claim about how much ground there is to fight
        /// across, and a map could grow one long thin corridor and satisfy anything less.</summary>
        [Test]
        public void TheShippedMap_IsMuchBiggerThanTheOldOneFactorySlice()
        {
            Rect bounds = Shipped().Bounds();
            float area = bounds.width * bounds.height;

            // The shipped slice was ~47 x 64 m = ~3,000 m². The playtest verdict was that you reach
            // the boss before the fight has any build-up, and the arena being small is half of why.
            Assert.Greater(area, 5000f,
                $"the arena is {bounds.width:0} x {bounds.height:0} m — barely bigger than the one you " +
                "could cross before the fight started");
        }

        /// <summary>Three factories, each standing in a room of its own, spread down the run — so
        /// clearing them is a sequence you fight your way through, not one beat (YT-92, YT-148). The
        /// third is the toolshed off the orchard's right, flanking the greenhouse on the left. A
        /// fourth factory stands centrally in the lawn itself (YT-185) — it is not part of this
        /// pocket sequence, so it is looked up by id rather than assumed to be absent.</summary>
        [Test]
        public void TheShippedMap_HasThreeFactories_OneOffEachFightPocket()
        {
            MapData map = Shipped();
            var factories = MapValidation.Kind(map, EntityKind.Factory);

            Assert.AreEqual(4, factories.Count, "the run does not have four sources of pressure");

            MapEntity shed = map.Entity("mower_hutch");
            MapEntity greenhouse = map.Entity("greenhouse_hutch");
            MapEntity toolshed = map.Entity("toolshed_hutch");

            Assert.AreEqual("shed", map.ZoneAt(shed.x, shed.z)?.id,
                "the first factory is not in the shed — the objective is standing in the open again");
            Assert.AreEqual("greenhouse", map.ZoneAt(greenhouse.x, greenhouse.z)?.id,
                "the second factory is not in the greenhouse");
            Assert.AreEqual("toolshed", map.ZoneAt(toolshed.x, toolshed.z)?.id,
                "the third factory is not in the toolshed off the orchard's right (YT-148)");

            // The later ones are genuinely FURTHER IN. Factories side by side in the same room
            // would be more pressure and none of the build-up.
            Assert.Greater(greenhouse.z, shed.z + 10f,
                "the second factory is not deep enough into the run to be a second objective");
            // The toolshed flanks the greenhouse — same depth, opposite side — so the orchard fight
            // has a source on each hand rather than a single objective to the west.
            Assert.Greater(toolshed.x, greenhouse.x,
                "the toolshed is not on the orchard's opposite flank from the greenhouse");

            // The central factory (YT-185) stands in the open lawn, not tucked in a fight pocket —
            // that's what makes it the one Max sees the instant he walks in from the patio.
            MapEntity central = map.Entity("central_hutch");
            Assert.IsNotNull(central, "the central garden factory is missing (YT-185)");
            Assert.AreEqual("lawn", map.ZoneAt(central.x, central.z)?.id,
                "the central factory is not standing in the lawn");
        }

        [Test]
        public void TheShippedMap_LetsYouWalkToTheShedAndOnToTheBoss()
        {
            MapData map = Shipped();

            Assert.IsTrue(Linked(map, "lawn", "shed"), "the shed cannot be entered from the lawn");
            Assert.IsTrue(Linked(map, "lawn", "nook"), "the nook is walled off");
            Assert.IsTrue(Linked(map, "gatehouse", "compost"), "the boss arena cannot be reached");

            // And the engine agrees: validation refuses a boss you cannot walk to.
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
        /// through. Four factories now that the central garden shed is a real one too (YT-185) — the
        /// gate has to name it or the door opens while it is still standing.</summary>
        [Test]
        public void TheShippedMap_OpensTheBossGateOnlyWhenEveryFactoryIsDown()
        {
            MapData map = Shipped();
            MapEntity gate = map.First(EntityKind.Gate);

            Assert.IsNotNull(gate);
            Assert.AreEqual(4, gate.Keys.Length, "the boss gate is not waiting on all four factories");

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

            // Straight up the middle from the patio, through the lawn and the orchard, to the boss
            // gate: never walled. The doorways between the rooms are on this line, which is what makes
            // it the line.
            for (float z = -13f; z <= 48f; z += 1f)
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

            // The lawn spans x −16..16, so its left wall stands just outside that. z = 20 is above the
            // nook's doorway, so this stretch of the edge is solid wall.
            Assert.IsTrue(WalledAt(map, -16.5f, 20f), "the lawn's left wall is not where it started");

            map.Zone("lawn").x += 5f;   // slide the fight room right

            Assert.IsFalse(WalledAt(map, -16.5f, 20f), "the old wall is still standing where the lawn used to be");
            Assert.IsTrue(WalledAt(map, -11.5f, 20f), "no wall was built along the lawn's new edge");
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
                new MapEntity { id = "Cover Compost", kind = "cover", x = 8f, z = 66f, width = 2f, height = 2f, depth = 2f },
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
            map.Zone("lawn").width = 9f;   // back to the corridor YT-68 tore out

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
            map.Zone("lawn").id = "patio";

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
            Assert.AreEqual(before.Zone("lawn").width, after.Zone("lawn").width, 1e-3);
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
    }
}
