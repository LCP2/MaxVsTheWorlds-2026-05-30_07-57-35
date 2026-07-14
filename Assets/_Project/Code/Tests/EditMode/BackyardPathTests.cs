using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>Unit tests for the Backyard greybox layout (YT-38, reshaped by YT-68): a coherent
    /// path whose middle room is a fight arena you can circle in, not a corridor. The layout struct
    /// is a VIEW of the map now (<see cref="MapLayoutBridge"/>), so what it says about a path still
    /// has to hold; the cover section below asks the map itself, because that is where cover
    /// lives.</summary>
    public sealed class BackyardPathTests
    {
        private const float ShedZ = 15f;          // BackyardPath.shedZ
        private const float SpawnRadius = 3.5f;   // BackyardPath.shedSpawnRadius

        [Test]
        public void Default_IsAValidTraversablePath()
        {
            var l = BackyardPathLayout.Default;
            Assert.IsTrue(l.IsValid());
            Assert.Greater(l.PatioLength, 0f);
            Assert.Greater(l.LawnLength, 0f);
            Assert.Greater(l.ArenaLength, 0f);
        }

        [Test]
        public void Default_RoomsRunInOrderAlongZ()
        {
            var l = BackyardPathLayout.Default;
            Assert.Less(l.StartZ, l.LawnStartZ);   // patio
            Assert.Less(l.LawnStartZ, l.GateZ);    // lawn
            Assert.Less(l.GateZ, l.ArenaEndZ);     // boss arena
        }

        // --- The point of YT-68 ---------------------------------------------------------------

        [Test]
        public void Default_LawnIsAFightRoomNotACorridor()
        {
            var l = BackyardPathLayout.Default;
            Assert.GreaterOrEqual(l.LawnWidth, BackyardPathLayout.MinFightRoomWidth,
                "the lawn is the main fight room — it has to be wide enough to circle-strafe in");
            Assert.Greater(l.LawnHalfWidth, l.PatioHalfWidth,
                "the lawn must open OUT from the patio, so arriving in it reads as space");
        }

        [Test]
        public void Default_LawnIsWiderThanTheOldCorridor()
        {
            // The shape this ticket replaced: a 9 m lane all the way to the gate.
            Assert.Greater(BackyardPathLayout.Default.LawnWidth, 9f * 2f,
                "the reshape has to be a step change, not a nudge");
        }

        [Test]
        public void Default_LawnHasRoomForACirclingLoop()
        {
            // Max moves at 6 m/s; a loop worth running is several metres of radius, not a pirouette.
            Assert.GreaterOrEqual(BackyardPathLayout.Default.LawnCircleRadius, 8f);
        }

        [Test]
        public void Default_BossArenaDoesNotShrinkTheFight()
        {
            var l = BackyardPathLayout.Default;
            Assert.GreaterOrEqual(l.ArenaHalfWidth, l.LawnHalfWidth);
            Assert.Greater(l.ArenaEndZ, l.GateZ);
        }

        // --- The gate -------------------------------------------------------------------------

        [Test]
        public void Default_GateIsADoorwayNotAnOpenEnd()
        {
            var l = BackyardPathLayout.Default;
            Assert.Greater(l.GateHalfWidth, 1.5f);              // wide enough to walk through
            Assert.Less(l.GateHalfWidth, l.LawnHalfWidth);      // but the wall still closes the room
        }

        [Test]
        public void GateSealWidth_CoversTheDoorwayPlusWallThickness()
        {
            var l = BackyardPathLayout.Default;
            Assert.AreEqual(l.GateHalfWidth * 2f + l.WallThickness * 2f, l.GateSealWidth, 1e-4);
            Assert.Greater(l.GateSealWidth, l.GateHalfWidth * 2f); // no gap to slip past
        }

        [Test]
        public void Centres_SitInTheMiddleOfTheirRooms()
        {
            var l = BackyardPathLayout.Default;
            Assert.AreEqual((l.LawnStartZ + l.GateZ) * 0.5f, l.LawnCenter.z, 1e-4);
            Assert.AreEqual((l.GateZ + l.ArenaEndZ) * 0.5f, l.ArenaCenter.z, 1e-4);
        }

        [Test]
        public void IsValid_RejectsBrokenLayouts()
        {
            var corridor = BackyardPathLayout.Default; corridor.LawnHalfWidth = 4.5f; // back to a lane
            Assert.IsFalse(corridor.IsValid(), "a lawn narrower than a fight room is the bug YT-68 fixed");

            var pinched = BackyardPathLayout.Default; pinched.PatioHalfWidth = 13f; // wider than the lawn
            Assert.IsFalse(pinched.IsValid());

            var gatePastArena = BackyardPathLayout.Default; gatePastArena.GateZ = 100f;
            Assert.IsFalse(gatePastArena.IsValid());

            var noDoor = BackyardPathLayout.Default; noDoor.GateHalfWidth = 0.5f; // can't get through
            Assert.IsFalse(noDoor.IsValid());

            var openEnd = BackyardPathLayout.Default; openEnd.GateHalfWidth = 12f; // no wall left
            Assert.IsFalse(openEnd.IsValid());

            var shrinkingArena = BackyardPathLayout.Default; shrinkingArena.ArenaHalfWidth = 5f;
            Assert.IsFalse(shrinkingArena.IsValid());
        }

        // --- Cover ----------------------------------------------------------------------------
        //
        // Cover is authored in the MAP now, and the map is no longer a corridor: pieces stand in the
        // shed as well as the lawn, and the shed is off to one side. So these ask the same questions
        // they always asked — is there enough of it, does it rest on the floor, does it leave the
        // player somewhere to run, does it keep off the ring the factory spawns robots on — of the
        // shipped map, rather than of a straight lane along Z.

        private static MapData Shipped() => MapLibrary.Load(MapLibrary.BackyardSlice);

        [Test]
        public void Cover_IsAValidSet()
        {
            Assert.IsTrue(MapValidation.Validate(Shipped(), out string why), why);
        }

        [Test]
        public void Cover_HasEnoughPiecesToCreateAngles()
        {
            MapData map = Shipped();
            var cover = MapValidation.Kind(map, EntityKind.Cover);

            // Enough to break the beeline, not so much it is a maze — and the fight room itself has
            // to hold several of them, or the lawn is an empty box again.
            Assert.GreaterOrEqual(cover.Count, 3);
            Assert.LessOrEqual(cover.Count, 10);

            int inTheLawn = 0;
            foreach (MapEntity c in cover)
                if (map.ZoneAt(c.x, c.z)?.id == "lawn") inTheLawn++;

            Assert.GreaterOrEqual(inTheLawn, 2, "the lawn is the fight room and it has nothing to fight around");
        }

        [Test]
        public void Cover_SitsOnTheFloor()
        {
            foreach (MapEntity c in MapValidation.Kind(Shipped(), EntityKind.Cover))
                Assert.AreEqual(c.height * 0.5f, c.GroundedCenter.y, 1e-4,
                    $"{c.id} is not resting on the ground");
        }

        [Test]
        public void Cover_LeavesTheCentreLineClear()
        {
            MapData map = Shipped();
            MapEntity spawn = map.First(EntityKind.PlayerSpawn);
            MapEntity gate = map.First(EntityKind.Gate);

            // Straight up the middle, from where Max starts to the gate he has to open. Cover breaks
            // the beeline by standing BESIDE it; a piece standing ON it would hide the way through,
            // which at a fixed 72° camera is the one thing the level must never do.
            for (float z = spawn.z; z <= gate.z; z += 0.5f)
            {
                foreach (MapEntity c in MapValidation.Kind(map, EntityKind.Cover))
                    Assert.Greater(c.ToCover().DistanceTo(new Vector2(spawn.x, z)), 0f,
                        $"{c.id} sits on the centre line at z={z}");
            }
        }

        [Test]
        public void Cover_NeverPinchesAnyRoomShut()
        {
            MapData map = Shipped();
            var cover = MapValidation.Kind(map, EntityKind.Cover);

            foreach (MapZone zone in map.zones)
            {
                if (zone.width < MapValidation.MinFreeChannel) continue;

                for (float z = zone.ZMin; z <= zone.ZMax; z += 0.5f)
                    Assert.GreaterOrEqual(MapValidation.FreeChannelAt(zone, cover, z),
                        MapValidation.MinFreeChannel,
                        $"nowhere to run in '{zone.id}' at z={z}");
            }
        }

        [Test]
        public void Cover_StaysOffTheShedsSpawnRing()
        {
            // Robots appear on a ring around the factory. A prop in that ring spawns them inside it —
            // and a robot has a body, so tangent isn't good enough. The factory is in the shed now, so
            // the ring is where the shed is, not on the centre line.
            MapData map = Shipped();
            Vector2 ring = map.First(EntityKind.Factory).CenterXz;

            foreach (MapEntity c in MapValidation.Kind(map, EntityKind.Cover))
                Assert.GreaterOrEqual(c.ToCover().DistanceTo(ring),
                    MapValidation.SpawnRadius + MapValidation.SpawnClearance,
                    $"{c.id} crowds the spawn ring");
        }

        [Test]
        public void Validate_RejectsCoverThatWallsTheRoomOff()
        {
            var l = BackyardPathLayout.Default;
            var wall = new[]
            {
                // A prop spanning the full lawn — the "corridor" failure mode, from the other side.
                new ArenaCover("Bad Wall", new Vector2(0f, 5f), new Vector3(l.LawnWidth, 2f, 1f), CoverShape.Box),
            };
            Assert.IsFalse(BackyardCover.Validate(l, wall, ShedZ, SpawnRadius, out _));
        }

        [Test]
        public void Validate_RejectsCoverOnTheSpawnRing()
        {
            var l = BackyardPathLayout.Default;
            var onRing = new[]
            {
                new ArenaCover("Bad Planter", new Vector2(5f, ShedZ), new Vector3(3f, 2f, 3f), CoverShape.Box),
            };
            Assert.IsFalse(BackyardCover.Validate(l, onRing, ShedZ, SpawnRadius, out _));
        }

        [Test]
        public void Validate_RejectsCoverMerelyTangentToTheSpawnRing()
        {
            // Footprint edge lands exactly on the ring: the robot spawns half inside the planter.
            var l = BackyardPathLayout.Default;
            var tangent = new[]
            {
                new ArenaCover("Tangent Planter", new Vector2(SpawnRadius + 1.5f, ShedZ),
                    new Vector3(3f, 2f, 3f), CoverShape.Box),
            };
            Assert.IsFalse(BackyardCover.Validate(l, tangent, ShedZ, SpawnRadius, out _));
        }

        [Test]
        public void FreeChannelAt_IsTheWidestGap_NotTheSumOfGaps()
        {
            var l = BackyardPathLayout.Default;  // lawn x ∈ [-12, 12]
            var one = new[]
            {
                // Sits at x ∈ [-2, 2] → gaps of 10 either side. The answer is 10, not 20.
                new ArenaCover("Mid", new Vector2(0f, 5f), new Vector3(4f, 2f, 4f), CoverShape.Box),
            };
            Assert.AreEqual(10f, BackyardCover.FreeChannelAt(l, one, 5f), 1e-3);
            // Above/below the prop, the room is wide open again.
            Assert.AreEqual(l.LawnWidth, BackyardCover.FreeChannelAt(l, one, 12f), 1e-3);
        }
    }
}
