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
    /// The load-bearing test here is <see cref="TheShippedMap_IsTheArenaTheSliceAlreadyShipped"/> —
    /// it is the proof that moving the level from hand-written geometry into data changed the source
    /// of truth and nothing else. The arena Lee has been QAing does not move a centimetre.
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

        /// <summary>The migration proof (YT-89 ← YT-38). The map must describe the SAME arena the
        /// hand-built path shipped — same rooms, same gate, same walls. If this drifts, the level
        /// silently changed shape on the way into data, and Lee's QA of the old one means nothing.</summary>
        [Test]
        public void TheShippedMap_IsTheArenaTheSliceAlreadyShipped()
        {
            BackyardPathLayout was = BackyardPathLayout.Default;
            BackyardPathLayout now = MapLayoutBridge.ToLayout(Shipped());

            Assert.AreEqual(was.StartZ, now.StartZ, 1e-3, "patio back wall moved");
            Assert.AreEqual(was.LawnStartZ, now.LawnStartZ, 1e-3, "the lawn starts somewhere else");
            Assert.AreEqual(was.GateZ, now.GateZ, 1e-3, "the gate moved");
            Assert.AreEqual(was.ArenaEndZ, now.ArenaEndZ, 1e-3, "the arena's back wall moved");
            Assert.AreEqual(was.PatioHalfWidth, now.PatioHalfWidth, 1e-3, "the patio changed width");
            Assert.AreEqual(was.LawnHalfWidth, now.LawnHalfWidth, 1e-3, "the fight room changed width");
            Assert.AreEqual(was.GateHalfWidth, now.GateHalfWidth, 1e-3, "the doorway changed width");
            Assert.AreEqual(was.ArenaHalfWidth, now.ArenaHalfWidth, 1e-3, "the boss arena changed width");
            Assert.AreEqual(was.WallHeight, now.WallHeight, 1e-3);
            Assert.AreEqual(was.WallThickness, now.WallThickness, 1e-3);
        }

        [Test]
        public void TheShippedMap_KeepsTheFactoryAtTheFarEndOfTheLawn()
        {
            MapData map = Shipped();
            MapEntity factory = map.First(EntityKind.Factory);

            Assert.IsNotNull(factory, "the map has no factory — there is nothing to destroy");
            Assert.AreEqual(15f, factory.z, 1e-3, "the shed moved off the far end of the lawn");
            Assert.AreEqual("lawn", map.ZoneAt(factory.x, factory.z)?.id);
        }

        /// <summary>The whole mission in one assertion: the gate into the boss arena is opened by
        /// destroying the factory, and that link is stated in the map.</summary>
        [Test]
        public void TheShippedMap_OpensTheBossGateByKillingTheFactory()
        {
            MapData map = Shipped();
            MapEntity gate = map.First(EntityKind.Gate);

            Assert.IsNotNull(gate);
            Assert.AreEqual("mower_hutch", gate.opensOn);
            Assert.AreEqual(EntityKind.Factory, map.Entity(gate.opensOn).Kind);
        }

        [Test]
        public void TheShippedMap_LeavesTheMissionLineWalkable()
        {
            MapData map = Shipped();

            // Straight up the middle from the patio to the gate: never walled.
            for (float z = -13f; z <= 21f; z += 1f)
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

            Assert.IsTrue(WalledAt(map, -12.5f, 8.5f), "the lawn's left wall is not where it started");

            map.Zone("lawn").x += 5f;   // slide the fight room right

            Assert.IsFalse(WalledAt(map, -12.5f, 8.5f), "the old wall is still standing where the lawn used to be");
            Assert.IsTrue(WalledAt(map, -7.5f, 8.5f), "no wall was built along the lawn's new edge");
        }

        [Test]
        public void AGate_IsAsWideAsTheDoorwayItFills_PlusTheWallEitherSide()
        {
            MapData map = Shipped();
            MapEntity gate = map.First(EntityKind.Gate);

            // 9 m doorway + 1 m wall on each side = 11 m of gate. No sliver to squeeze through.
            Assert.AreEqual(11f, MapRuntime.SealWidth(map, gate), 1e-3);
        }

        [Test]
        public void WideningADoorway_WidensTheGateThatFillsIt()
        {
            MapData map = Shipped();
            MapEntity gate = map.First(EntityKind.Gate);

            foreach (MapLink link in map.links)
                if (link.gate == gate.id) link.doorway = 13f;

            Assert.AreEqual(15f, MapRuntime.SealWidth(map, gate), 1e-3,
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
                new MapEntity { id = "Cover Compost", kind = "cover", x = 8f, z = 33f, width = 2f, height = 2f, depth = 2f },
            };
            map.entities = withCover.ToArray();

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("boss", why);
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
