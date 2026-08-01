using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

using MaxWorlds.Arena;


namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The way through the yard (YT-93).
    ///
    /// The playtest verdict was that the robots pile up behind walls and never reach Max. The fix is
    /// that they route through the level's own rooms and doorways instead of walking at him — and the
    /// point of doing it as maths over the map, rather than as a baked navmesh, is THIS: a test can
    /// walk a robot the whole length of the yard, from every room in it, without building a single
    /// GameObject. The acceptance criterion is "no permanent pile-ups", and that is a claim a
    /// simulation can settle before anyone plays it.
    /// </summary>
    public sealed class MapRoutesTests
    {
        private static MapData Shipped() => MapLibrary.Load(MapLibrary.BackyardSlice);

        /// <summary>Is this point inside a wall? Pure geometry — the same question the robot's body
        /// asks the physics engine, asked of the data instead.</summary>
        private static bool Walled(MapData map, Vector2 p)
        {
            foreach (WallSegment w in MapGeometry.Walls(map))
            {
                if (Mathf.Abs(p.x - w.Center.x) <= w.Size.x * 0.5f &&
                    Mathf.Abs(p.y - w.Center.z) <= w.Size.z * 0.5f) return true;
            }
            return false;
        }

        /// <summary>
        /// Walk a robot from <paramref name="from"/> to <paramref name="goal"/>, one step at a time,
        /// asking the level the way at every step — exactly as <c>RobotEnemy.TickChase</c> does.
        ///
        /// Walls stop it dead, and that is deliberate: a robot that walks into a wall in the real game
        /// does not pass through it, it grinds against it, and that grinding IS the pile-up. So a step
        /// into a wall is a failed walk, and the test says where it stopped.
        ///
        /// Cover is not modelled, and that is honest rather than convenient: cover sits inside a room,
        /// it is sparse, and the steering layer rounds it (ObstacleSteering). Walls are what a beeline
        /// cannot solve and walls are what routing solves — so walls are what this asserts.
        /// </summary>
        private static bool Walks(MapData map, Vector2 from, Vector2 goal, out Vector2 stoppedAt,
                                  out int steps)
        {
            const float Step = 0.3f;        // ~a frame of a rusher at 3.6 m/s, twice over
            const float Arrived = 1.2f;     // RobotEnemy.arriveRadius
            const int Limit = 3000;         // 900 m of walking: far longer than the yard is round

            Vector2 at = from;

            for (steps = 0; steps < Limit; steps++)
            {
                if ((at - goal).magnitude <= Arrived) { stoppedAt = at; return true; }

                Vector2 waypoint = MapRoutes.Waypoint(map, at, goal);
                Vector2 dir = waypoint - at;
                if (dir.magnitude < 1e-4f) { stoppedAt = at; return false; }   // nowhere to go

                Vector2 next = at + dir.normalized * Step;
                if (Walled(map, next)) { stoppedAt = at; return false; }        // face against a fence

                at = next;
            }

            stoppedAt = at;
            return false;   // still walking after 900 m: it is going round in circles
        }

        // ---------------------------------------------------------------- the acceptance criterion

        /// <summary>
        /// THE ticket, asserted: a robot standing anywhere in the yard can walk to Max.
        ///
        /// From the middle of every room — including both sheds, which are the rooms the factories
        /// stand in and therefore the rooms every robot in the game is born in. Before this, a robot in
        /// the shed walked into the side of the shed and stayed there.
        /// </summary>
        [Test]
        public void FromEveryRoomInTheYard_ARobotCanWalkToMax()
        {
            MapData map = Shipped();
            Vector2 max = map.First(EntityKind.PlayerSpawn).CenterXz;

            foreach (MapZone room in map.zones)
            {
                // The boss arena is behind a shut gate at the time robots exist; nothing spawns there
                // and nothing has to leave it. Every other room is a room a robot has to get out of.
                if (room.Kind == ZoneKind.Boss) continue;

                bool arrived = Walks(map, room.CenterXz, max, out Vector2 stoppedAt, out int steps);

                Assert.IsTrue(arrived,
                    $"a robot in '{room.id}' cannot reach Max — it stopped at {stoppedAt} after {steps} " +
                    "steps. That is a pile-up: it stands there while the player walks away.");
            }
        }

        /// <summary>The two rooms that actually matter: robots are BORN in the sheds, one on each side
        /// of the yard, and both have to get out through a doorway to reach a player who is nowhere
        /// near them.</summary>
        [Test]
        public void FromBothFactories_ARobotWalksOutOfItsShedAndAcrossTheYard()
        {
            MapData map = Shipped();
            Vector2 max = map.First(EntityKind.PlayerSpawn).CenterXz;

            var factories = MapValidation.Kind(map, EntityKind.Factory);
            Assert.AreEqual(3, factories.Count, "the yard has lost a factory");

            foreach (MapEntity factory in factories)
            {
                // Where the mouth puts it: a few metres out in front, still inside its room.
                Vector2 born = factory.CenterXz + new Vector2(0f, -MapValidation.SpawnRadius);

                Assert.IsTrue(Walks(map, born, max, out Vector2 stoppedAt, out int steps),
                    $"a robot from '{factory.id}' never got out — it stopped at {stoppedAt}. The " +
                    "factory is producing robots that stand in a shed.");

                // And it is a real walk across a real yard, not a stroll: it should take a while.
                Assert.Greater(steps, 60, $"'{factory.id}' is suspiciously close to Max");
            }
        }

        /// <summary>
        /// The bug, pinned — and the proof that the test above has teeth.
        ///
        /// A test that walks a robot across the yard and passes is worth nothing unless the walk it
        /// replaced would have failed. So: the OLD behaviour, run through the same simulation. A robot
        /// born in the shed, walking straight at Max the way it did since YT-36, gets as far as the
        /// inside of the shed wall and stops. That is not a hypothesis about the playtest report — it
        /// is the playtest report, reproduced in a unit test.
        ///
        /// If this ever starts passing, the yard has been flattened back into a room and the routing is
        /// no longer earning its keep.
        /// </summary>
        [Test]
        public void ABeelineFromTheShed_WalksIntoTheWallAndStaysThere()
        {
            MapData map = Shipped();
            Vector2 max = map.First(EntityKind.PlayerSpawn).CenterXz;
            Vector2 born = map.Entity("mower_hutch").CenterXz + new Vector2(0f, -MapValidation.SpawnRadius);

            Assert.IsFalse(Beelines(map, born, max, out Vector2 stoppedAt),
                "a straight line out of the shed reaches Max, so the yard has no walls in the way and " +
                "the routing this ticket added is not doing anything");

            MapZone stuckIn = map.ZoneAt(stoppedAt.x, stoppedAt.y);
            Assert.AreEqual("shed", stuckIn?.id,
                "it got stuck somewhere other than the shed it was born in");
        }

        /// <summary>The chase as it was: straight at him, every step, whatever is in between.</summary>
        private static bool Beelines(MapData map, Vector2 from, Vector2 goal, out Vector2 stoppedAt)
        {
            const float Step = 0.3f;
            const float Arrived = 1.2f;

            Vector2 at = from;

            for (int i = 0; i < 3000; i++)
            {
                if ((at - goal).magnitude <= Arrived) { stoppedAt = at; return true; }

                Vector2 next = at + (goal - at).normalized * Step;
                if (Walled(map, next)) { stoppedAt = at; return false; }

                at = next;
            }

            stoppedAt = at;
            return false;
        }

        // ---------------------------------------------------------------- the routing itself

        [Test]
        public void InTheSameRoom_TheWayToMaxIsStraightAtHim()
        {
            MapData map = Shipped();
            MapZone lawn = map.Zone("lawn");

            var from = new Vector2(lawn.XMin + 2f, lawn.z);
            var goal = new Vector2(lawn.XMax - 2f, lawn.z);

            Assert.AreEqual(goal, MapRoutes.Waypoint(map, from, goal),
                "it took a detour across a room with nothing in it — a beeline is what a chase IS");
        }

        [Test]
        public void FromAnotherRoom_TheWayOutIsThroughTheDoorway_NotThroughTheWall()
        {
            MapData map = Shipped();
            MapZone shed = map.Zone("shed");
            MapZone lawn = map.Zone("lawn");

            Vector2 waypoint = MapRoutes.Waypoint(map, shed.CenterXz, lawn.CenterXz);

            // The shed opens onto the lawn through a doorway in the wall they share (x = shed.XMin).
            // The way out has to be THAT gap — beyond the wall line, and within the hole.
            Assert.Less(waypoint.x, shed.XMin,
                "it is heading for a point still inside the shed — it will walk into the wall");

            MapLink link = Link(map, "lawn", "shed");
            Assert.IsTrue(MapGeometry.Doorway(map, link, out _, out _, out Span hole));
            Assert.GreaterOrEqual(waypoint.y, hole.Min, "the way out is not in the doorway");
            Assert.LessOrEqual(waypoint.y, hole.Max, "the way out is not in the doorway");
        }

        /// <summary>A robot cannot be sent somewhere the level does not go. With no map to read, it
        /// chases exactly as it always did — the routing can make a chase smarter, never stop one.</summary>
        [Test]
        public void WithNoMap_TheWayIsSimplyTheGoal()
        {
            var goal = new Vector2(4f, 9f);
            Assert.AreEqual(goal, MapRoutes.Waypoint(null, Vector2.zero, goal));
        }

        [Test]
        public void StandingOutsideEveryRoom_ItStillChases()
        {
            MapData map = Shipped();
            var goal = new Vector2(0f, 0f);

            // Out past the fence, where no room is. Nothing to route through; head for him anyway.
            Assert.AreEqual(goal, MapRoutes.Waypoint(map, new Vector2(500f, 500f), goal));
        }

        /// <summary>
        /// Asking the way costs nothing: the yard is solved ONCE, not once per robot per frame.
        ///
        /// Sixteen robots ask this sixty times a second. The first version searched the room graph on
        /// every one of those calls — a fresh dictionary and a fresh queue, a thousand times a second,
        /// to answer a question whose answer is a property of the level and never changes. Garbage at
        /// 60 fps on a phone is a dropped frame, and 60 fps is an acceptance criterion.
        ///
        /// Counted rather than weighed. The obvious way to write this is "assert it allocates no GC
        /// memory", and I did, and it passed here and failed on CI — because a GC probe measures the
        /// runtime it happens to be standing on (JIT, the closure, the collector) as much as it
        /// measures the code. Counting the searches asks the question directly, and it gets the same
        /// answer on every machine.
        /// </summary>
        [Test]
        public void AskingTheWay_SolvesTheYardOnce_NotOncePerQuestion()
        {
            MapData map = Shipped();
            Vector2 from = map.Zone("greenhouse").CenterXz;
            Vector2 goal = map.First(EntityKind.PlayerSpawn).CenterXz;

            MapRoutes.Waypoint(map, from, goal);   // the level is solved on the first question asked

            int searchesAfterSolving = MapRoutes.Searches;

            for (int i = 0; i < 64; i++) MapRoutes.Waypoint(map, from, goal);

            Assert.AreEqual(searchesAfterSolving, MapRoutes.Searches,
                "the room graph is being searched again on every question — that is a fresh dictionary " +
                "and a fresh queue, sixteen times a frame, to answer something the level already knows");
        }

        [Test]
        public void TheRouteThroughTheYard_IsTheFewestDoorways()
        {
            MapData map = Shipped();

            List<MapZone> route = MapRoutes.Rooms(map, map.Zone("greenhouse"), map.Zone("patio"));

            Assert.AreEqual(new[] { "greenhouse", "orchard", "lawn", "patio" },
                            route.ConvertAll(z => z.id).ToArray(),
                            "that is not the shortest way out of the greenhouse");
        }

        private static MapLink Link(MapData map, string a, string b)
        {
            foreach (MapLink link in map.links)
                if ((link.from == a && link.to == b) || (link.from == b && link.to == a)) return link;

            Assert.Fail($"no link between '{a}' and '{b}'");
            return null;
        }
    }
}
