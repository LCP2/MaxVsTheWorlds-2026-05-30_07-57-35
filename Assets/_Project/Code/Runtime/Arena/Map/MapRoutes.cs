using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// The way from here to there, through the level's own rooms and doorways (YT-93).
    ///
    /// The robots have beelined at Max since YT-36 — fine on an open plane, and the class that does it
    /// says so out loud: <em>"steering is direct rather than NavMesh… revisit if the levels ever get
    /// maze-like."</em> The levels got maze-like. A yard of eight rooms with doorways between them is a
    /// maze, and a beeline into a wall is a robot standing still with its face against a fence while
    /// the player walks away — which is exactly what the playtest found.
    ///
    /// This is the "or equivalent" the ticket allows, and it is not a navmesh on purpose. A navmesh
    /// would have to be baked from geometry that only exists at runtime, and it would answer a question
    /// we already have the answer to: the map IS a graph of rooms joined by doorways. So the route is a
    /// breadth-first walk over that graph — eight nodes — and the waypoint is the middle of the next
    /// doorway. It costs nothing, it cannot disagree with the level (it IS the level), and it is pure
    /// maths, so a test can walk a robot across the whole yard without building a single GameObject.
    ///
    /// What it deliberately does NOT do is avoid cover. Cover sits INSIDE a room, it is sparse, and a
    /// chaser already rounds it (<see cref="MaxWorlds.Enemies.ObstacleSteering"/>). Walls are the thing
    /// a beeline cannot solve, and walls are what this solves.
    ///
    /// A note on gates (MV-272): a shut gate now counts as a wall, not a doorway. The comment this
    /// replaces predicted the day this would matter — "a map puts a factory on the far side of a locked
    /// door" — and the World & Difficulty Framework's chain of area gates (MV-270/271) is exactly that
    /// day. A caller that cares passes <c>gateOpen</c>: a link whose gate reports closed is excluded
    /// from the search, so a robot on the wrong side of one is never routed AT it, let alone through it
    /// — which is what used to press a robot's collider into the gate's and read as a pile-up. Omit the
    /// callback (the default) and every link is open, exactly the pre-MV-272 behaviour every existing
    /// caller and test still gets.
    /// </summary>
    public static class MapRoutes
    {
        /// <summary>How far past a doorway's wall line to aim. A robot that aims AT the line stops in
        /// the gap with nothing left to walk toward and jitters in the frame; one that aims through it
        /// is already in the next room and asking the next question.</summary>
        public const float ThroughDoorway = 1.5f;

        /// <summary>
        /// How many times the room graph has actually been searched. The whole performance claim of
        /// this class is that the answer is "once per level, not once per robot per frame", and this is
        /// how a test says so — by counting the searches rather than by weighing the garbage, which is
        /// a proxy for the same thing that measures differently on every machine it runs on.
        /// </summary>
        public static int Searches { get; private set; }

        /// <summary>
        /// The chain of rooms from one to another, inclusive of both — the fewest doorways between
        /// them. Empty if there is no way through at all — which, with no <paramref name="gateOpen"/>
        /// given, validation has already refused, so a caller getting one back is looking at a map that
        /// never built; with one given, it also means "every way through is behind a shut gate right
        /// now" (MV-272), which a fully-validated map can absolutely be mid-run.
        /// </summary>
        /// <param name="gateOpen">Reports whether a named gate is open. Null (the default) treats
        /// every link as open, whatever gate it names — the routing this class shipped with, before a
        /// gate could ever be the reason a room is unreachable.</param>
        public static List<MapZone> Rooms(MapData map, MapZone from, MapZone to,
                                          Func<string, bool> gateOpen = null)
        {
            Searches++;

            var path = new List<MapZone>();
            if (map == null || from == null || to == null) return path;

            var cameFrom = new Dictionary<string, string> { { from.id, null } };
            var queue = new Queue<string>();
            queue.Enqueue(from.id);

            while (queue.Count > 0 && !cameFrom.ContainsKey(to.id))
            {
                string here = queue.Dequeue();
                if (map.links == null) break;

                foreach (MapLink link in map.links)
                {
                    if (link == null || !Passable(link, gateOpen)) continue;

                    string next = link.from == here ? link.to
                                : link.to == here ? link.from
                                : null;

                    if (next == null || cameFrom.ContainsKey(next)) continue;
                    cameFrom[next] = here;
                    queue.Enqueue(next);
                }
            }

            if (!cameFrom.ContainsKey(to.id)) return path;

            for (string id = to.id; id != null; id = cameFrom[id]) path.Add(map.Zone(id));
            path.Reverse();
            return path;
        }

        /// <summary>A link the search may cross: one with no gate at all, or one whose gate
        /// <paramref name="gateOpen"/> reports open. No <paramref name="gateOpen"/> given means every
        /// link is passable — the caller isn't asking the question, so the answer can't be "no".</summary>
        private static bool Passable(MapLink link, Func<string, bool> gateOpen) =>
            gateOpen == null || string.IsNullOrEmpty(link.gate) || gateOpen(link.gate);

        /// <summary>
        /// Where to walk NEXT to get from <paramref name="from"/> to <paramref name="goal"/>: the goal
        /// itself when they are in the same room (walk at it — that is what a beeline is FOR), and
        /// otherwise a point just through the doorway into the next room along the route.
        ///
        /// Falls back to the goal itself when there is nothing to route through at all — no map, or
        /// either end standing outside every room. A robot in that spot still chases, exactly as it did
        /// before; it does not stop dead because the level could not answer a question. A room graph
        /// that HAS an answer but no way through it right now is a different case — see below.
        ///
        /// Every robot asks this every frame, so it does no work: which room leads to which is a
        /// property of the LEVEL, not of the asking, and it is solved once (<see cref="Hops"/>) and
        /// then looked up. A search per robot per frame would have been a fresh dictionary and a fresh
        /// queue sixteen times a frame — garbage, at 60 fps, on a phone, to answer a question whose
        /// answer never changes.
        ///
        /// With no way through RIGHT NOW — every route crosses a shut gate (MV-272) — this hands back
        /// <paramref name="from"/>, not <paramref name="goal"/>: a robot that can't get there stands
        /// still and holds rather than beelining at (and grinding on) whatever gate or wall is actually
        /// in the way. That is the "hold/patrol" the pile-up bug asked for, for free — a robot that
        /// stops advancing starts stalling <see cref="MaxWorlds.Enemies.RobotEnemy"/>'s own progress
        /// clock, and it already knows what to do once that runs out.
        /// </summary>
        public static Vector2 Waypoint(MapData map, Vector2 from, Vector2 goal,
                                       Func<string, bool> gateOpen = null)
        {
            if (map == null) return goal;

            MapZone here = map.ZoneAt(from.x, from.y);
            MapZone there = map.ZoneAt(goal.x, goal.y);

            if (here == null || there == null || here.id == there.id) return goal;

            Solve(map, gateOpen);

            return _hops.TryGetValue(HopKey(_index[here.id], _index[there.id]), out Vector2 hop)
                ? hop
                : from;
        }

        /// <summary>
        /// The way out of every room toward every other, solved once for a level: room A → room B gives
        /// the doorway you leave A by. Eight rooms is sixty-four answers, and they do not change while
        /// the level stands.
        ///
        /// Rebuilt when it is handed a different map. It does NOT notice a map being edited underneath
        /// it — the map editor mutates a MapData in place — which is fine because nothing navigates a
        /// map mid-edit, and <see cref="Forget"/> exists for anything that ever needs to (a gate opening
        /// among them: <see cref="MaxWorlds.Enemies.EnemyNavigation.RegisterGate"/> calls it the instant
        /// one does, so a room that was unreachable a moment ago is re-solved into the graph the next
        /// time anything asks the way).
        /// </summary>
        private static void Solve(MapData map, Func<string, bool> gateOpen)
        {
            if (ReferenceEquals(_solvedFor, map) && _hops != null) return;

            _solvedFor = map;
            _hops = new Dictionary<int, Vector2>(64);
            _index = new Dictionary<string, int>(16);

            if (map.zones == null) return;

            for (int i = 0; i < map.zones.Length; i++)
                if (map.zones[i] != null) _index[map.zones[i].id] = i;

            for (int a = 0; a < map.zones.Length; a++)
            for (int b = 0; b < map.zones.Length; b++)
            {
                MapZone from = map.zones[a], to = map.zones[b];
                if (from == null || to == null || a == b) continue;

                List<MapZone> route = Rooms(map, from, to, gateOpen);
                if (route.Count < 2) continue;   // no way through right now: the caller holds instead

                _hops[HopKey(a, b)] = Mouth(map, route[0], route[1], to.CenterXz);
            }
        }

        /// <summary>Drop the solved routes. A level that is rebuilt in place has to be re-solved.</summary>
        public static void Forget()
        {
            _solvedFor = null;
            _hops = null;
            _index = null;
        }

        private static MapData _solvedFor;
        private static Dictionary<int, Vector2> _hops;
        private static Dictionary<string, int> _index;

        /// <summary>Two room indices in one int. No string built, nothing boxed — this is looked up
        /// sixteen times a frame and it has to cost nothing.</summary>
        private static int HopKey(int from, int to) => (from << 8) | to;

        /// <summary>The point to aim at to leave <paramref name="here"/> for <paramref name="next"/>:
        /// the middle of the doorway they share, pushed through the wall line into the room beyond.
        /// The goal itself if the two rooms turn out not to share a doorway at all.</summary>
        private static Vector2 Mouth(MapData map, MapZone here, MapZone next, Vector2 fallback)
        {
            if (map.links == null) return fallback;

            foreach (MapLink link in map.links)
            {
                if (link == null) continue;

                bool joins = (link.from == here.id && link.to == next.id)
                          || (link.from == next.id && link.to == here.id);

                if (!joins) continue;
                if (!MapGeometry.Doorway(map, link, out bool alongX, out float coord, out Span hole))
                    continue;

                // alongX: the wall runs along X at this Z, so the hole is a span of X — and crossing it
                // means moving in Z. The other way round when it doesn't.
                Vector2 mouth = alongX ? new Vector2(hole.Mid, coord) : new Vector2(coord, hole.Mid);
                Vector2 across = alongX ? new Vector2(0f, 1f) : new Vector2(1f, 0f);

                float toward = alongX ? next.z - here.z : next.x - here.x;
                float depth = map.wallThickness * 0.5f + ThroughDoorway;

                return mouth + across * Mathf.Sign(toward) * depth;
            }

            return fallback;
        }
    }
}
