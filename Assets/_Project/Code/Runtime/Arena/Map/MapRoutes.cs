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
    /// A note on gates: a shut gate is as impassable as a wall, and this does not model it — because it
    /// does not have to. A robot walks toward where it last saw Max, and Max cannot be behind a gate
    /// that is shut (it stops him too). The day a map puts a factory on the far side of a locked door,
    /// this is the assumption that breaks, and this comment is where you will find it.
    /// </summary>
    public static class MapRoutes
    {
        /// <summary>How far past a doorway's wall line to aim. A robot that aims AT the line stops in
        /// the gap with nothing left to walk toward and jitters in the frame; one that aims through it
        /// is already in the next room and asking the next question.</summary>
        public const float ThroughDoorway = 1.5f;

        /// <summary>
        /// The chain of rooms from one to another, inclusive of both — the fewest doorways between
        /// them. Empty if there is no way through at all, which validation has already refused, so a
        /// caller getting one back is looking at a map that never built.
        /// </summary>
        public static List<MapZone> Rooms(MapData map, MapZone from, MapZone to)
        {
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
                    if (link == null) continue;

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

        /// <summary>
        /// Where to walk NEXT to get from <paramref name="from"/> to <paramref name="goal"/>: the goal
        /// itself when they are in the same room (walk at it — that is what a beeline is FOR), and
        /// otherwise a point just through the doorway into the next room along the route.
        ///
        /// Falls back to the goal whenever it cannot do better — no map, no rooms, no way through, or
        /// either end standing outside every room. A robot that can't be routed still chases, exactly
        /// as it did before; it does not stop dead because the level could not answer a question.
        ///
        /// Every robot asks this every frame, so it does no work: which room leads to which is a
        /// property of the LEVEL, not of the asking, and it is solved once (<see cref="Hops"/>) and
        /// then looked up. A search per robot per frame would have been a fresh dictionary and a fresh
        /// queue sixteen times a frame — garbage, at 60 fps, on a phone, to answer a question whose
        /// answer never changes.
        /// </summary>
        public static Vector2 Waypoint(MapData map, Vector2 from, Vector2 goal)
        {
            if (map == null) return goal;

            MapZone here = map.ZoneAt(from.x, from.y);
            MapZone there = map.ZoneAt(goal.x, goal.y);

            if (here == null || there == null || here.id == there.id) return goal;

            Solve(map);

            return _hops.TryGetValue(HopKey(_index[here.id], _index[there.id]), out Vector2 hop)
                ? hop
                : goal;
        }

        /// <summary>
        /// The way out of every room toward every other, solved once for a level: room A → room B gives
        /// the doorway you leave A by. Eight rooms is sixty-four answers, and they do not change while
        /// the level stands.
        ///
        /// Rebuilt when it is handed a different map. It does NOT notice a map being edited underneath
        /// it — the map editor mutates a MapData in place — which is fine because nothing navigates a
        /// map mid-edit, and <see cref="Forget"/> exists for anything that ever needs to.
        /// </summary>
        private static void Solve(MapData map)
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

                List<MapZone> route = Rooms(map, from, to);
                if (route.Count < 2) continue;   // no way through: the caller falls back to the goal

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
