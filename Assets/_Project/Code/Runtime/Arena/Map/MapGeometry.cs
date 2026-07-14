using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>A span on a line — the 1-D primitive the wall solver works in.</summary>
    public readonly struct Span
    {
        public readonly float Min;
        public readonly float Max;

        public Span(float min, float max) { Min = min; Max = max; }

        public float Length => Max - Min;
        public float Mid => (Min + Max) * 0.5f;
        public bool IsEmpty => Max - Min <= Geo.Epsilon;
        public bool Contains(float v) => v >= Min - Geo.Epsilon && v <= Max + Geo.Epsilon;

        public override string ToString() => $"[{Min:0.##}, {Max:0.##}]";
    }

    /// <summary>A generated wall: a solid slab, ready to become a primitive.</summary>
    public readonly struct WallSegment
    {
        public readonly string Name;
        public readonly Vector3 Center;   // world; Y is already half the wall height
        public readonly Vector3 Size;

        public WallSegment(string name, Vector3 center, Vector3 size)
        {
            Name = name; Center = center; Size = size;
        }
    }

    /// <summary>The floor slab. One piece, cut to the map's bounds — not one per room, so there are no
    /// coplanar seams to z-fight along.</summary>
    public readonly struct FloorSlab
    {
        public readonly Vector3 Center;
        public readonly Vector3 Size;

        public FloorSlab(Vector3 center, Vector3 size) { Center = center; Size = size; }
    }

    internal static class Geo
    {
        public const float Epsilon = 0.01f;
        public static bool Same(float a, float b) => Mathf.Abs(a - b) <= Epsilon;
    }

    /// <summary>
    /// Turns a <see cref="MapData"/> into walls and floor. Pure maths — no GameObjects — so a map's
    /// shape is unit-testable without a scene, which is what lets a bad layout fail a test instead of
    /// a playtest.
    ///
    /// THE IDEA: walls are not authored, they are DERIVED. A room lays down a solid edge; a link cuts
    /// a doorway out of it. Move a room and it re-walls itself; widen a doorway and the wall either
    /// side of it gets out of the way. That is the whole reason authoring a map is now fast.
    ///
    /// Two things make it come out right rather than merely close:
    ///
    /// 1. It solves per LINE, not per room. Where the lawn ends and the arena begins, both rooms claim
    ///    the same line. Solve it per room and you get two overlapping slabs (z-fighting) and a
    ///    doorway that one room punches open and the other bricks up. Solve it per line and the
    ///    doorway belongs to the LINK, is subtracted exactly once, and every stretch of wall is
    ///    emitted exactly once.
    ///
    /// 2. A wall knows which side its room is on. An EXTERIOR wall sits fully OUTSIDE its room, so a
    ///    room is as wide to walk across as it was authored — a 24 m lawn gives you 24 m. A PARTY wall,
    ///    with a room on both sides, straddles the line and the two rooms share it, exactly as a wall
    ///    between two rooms does in a real building. Get this wrong and every room is quietly a wall's
    ///    thickness smaller than the number the author typed.
    /// </summary>
    public static class MapGeometry
    {
        public const float FloorThickness = 0.1f;

        /// <summary>Floor reaches this far past the outermost wall, so the world never visibly ends at
        /// a wall's outside face from the fixed camera angle.</summary>
        public const float FloorMargin = 2f;

        /// <summary>One line of wall: an orientation, a coordinate, and what sits on either side of
        /// it.</summary>
        private sealed class WallLine
        {
            public float Coord;
            public readonly List<Span> Lower = new List<Span>();  // rooms on the -X / -Z side
            public readonly List<Span> Upper = new List<Span>();  // rooms on the +X / +Z side
            public readonly List<Span> Holes = new List<Span>();  // doorways cut by links
        }

        /// <summary>The single floor slab under the whole map.</summary>
        public static FloorSlab Floor(MapData map)
        {
            Rect b = map.Bounds();
            float m = FloorMargin + map.wallThickness;
            return new FloorSlab(
                new Vector3(b.center.x, -FloorThickness * 0.5f, b.center.y),
                new Vector3(b.width + m * 2f, FloorThickness, b.height + m * 2f));
        }

        /// <summary>Every wall the map needs, doorways already cut.</summary>
        public static List<WallSegment> Walls(MapData map)
        {
            var walls = new List<WallSegment>(16);
            if (map?.zones == null || map.zones.Length == 0) return walls;

            float h = map.wallHeight;
            float t = map.wallThickness;

            // Lines running along X (walls you meet head-on, at a constant Z) and along Z (the side
            // walls, at a constant X).
            var alongX = new List<WallLine>();
            var alongZ = new List<WallLine>();

            foreach (MapZone zone in map.zones)
            {
                if (zone == null) continue;
                var acrossX = new Span(zone.XMin, zone.XMax);
                var acrossZ = new Span(zone.ZMin, zone.ZMax);

                // The room is ABOVE its own south edge and BELOW its own north edge.
                Line(alongX, zone.ZMin).Upper.Add(acrossX);
                Line(alongX, zone.ZMax).Lower.Add(acrossX);
                Line(alongZ, zone.XMin).Upper.Add(acrossZ);
                Line(alongZ, zone.XMax).Lower.Add(acrossZ);
            }

            if (map.links != null)
            {
                foreach (MapLink link in map.links)
                {
                    if (link == null) continue;
                    if (!Doorway(map, link, out bool runsAlongX, out float coord, out Span hole)) continue;
                    Line(runsAlongX ? alongX : alongZ, coord).Holes.Add(hole);
                }
            }

            foreach (WallLine line in alongX)
                foreach (var (span, offset) in Solids(line, t))
                    walls.Add(new WallSegment(
                        $"Wall Z{line.Coord:0.#} [{span.Min:0.#},{span.Max:0.#}]",
                        new Vector3(span.Mid, h * 0.5f, line.Coord + offset),
                        new Vector3(span.Length, h, t)));

            foreach (WallLine line in alongZ)
                foreach (var (span, offset) in Solids(line, t))
                    walls.Add(new WallSegment(
                        $"Wall X{line.Coord:0.#} [{span.Min:0.#},{span.Max:0.#}]",
                        new Vector3(line.Coord + offset, h * 0.5f, span.Mid),
                        new Vector3(t, h, span.Length)));

            return walls;
        }

        /// <summary>Where a link cuts its doorway. False if the two rooms do not actually share an
        /// edge — a link between rooms that do not touch cuts nothing, and validation refuses it.</summary>
        public static bool Doorway(MapData map, MapLink link, out bool runsAlongX, out float coord, out Span hole)
        {
            runsAlongX = false; coord = 0f; hole = default;

            MapZone a = map.Zone(link.from);
            MapZone b = map.Zone(link.to);
            if (a == null || b == null) return false;

            Span overlap;
            if (Geo.Same(a.ZMax, b.ZMin) || Geo.Same(a.ZMin, b.ZMax))
            {
                runsAlongX = true;
                coord = Geo.Same(a.ZMax, b.ZMin) ? a.ZMax : a.ZMin;
                overlap = new Span(Mathf.Max(a.XMin, b.XMin), Mathf.Min(a.XMax, b.XMax));
            }
            else if (Geo.Same(a.XMax, b.XMin) || Geo.Same(a.XMin, b.XMax))
            {
                runsAlongX = false;
                coord = Geo.Same(a.XMax, b.XMin) ? a.XMax : a.XMin;
                overlap = new Span(Mathf.Max(a.ZMin, b.ZMin), Mathf.Min(a.ZMax, b.ZMax));
            }
            else
            {
                return false;   // the rooms do not touch
            }

            if (overlap.IsEmpty) return false;   // they meet at a corner only

            if (link.doorway <= 0f || link.doorway >= overlap.Length)
            {
                hole = overlap;                  // the whole shared edge is open
                return true;
            }

            // A doorway narrower than the shared edge. Centre it on the gate that fills it, if there
            // is one, so dragging the gate slides the hole with it and the two can never disagree.
            float centre = overlap.Mid;
            MapEntity gate = map.Entity(link.gate);
            if (gate != null) centre = runsAlongX ? gate.x : gate.z;

            float half = link.doorway * 0.5f;
            centre = Mathf.Clamp(centre, overlap.Min + half, overlap.Max - half);
            hole = new Span(centre - half, centre + half);
            return true;
        }

        /// <summary>
        /// The solid stretches of one line, each with the offset that puts it on the right side of
        /// the line.
        ///
        /// The line is chopped at every point where anything about it changes — a room edge starting
        /// or ending, a doorway edge — and each resulting stretch is classified: rooms on both sides
        /// (a party wall, straddling), a room on one side (an exterior wall, pushed clear of it), or
        /// no room at all / a doorway (no wall). Neighbouring stretches that agree are merged back
        /// together, so a long wall is one slab and not twenty.
        /// </summary>
        private static List<(Span Span, float Offset)> Solids(WallLine line, float t)
        {
            var result = new List<(Span, float)>();

            var cuts = new List<float>();
            foreach (Span s in line.Lower) { cuts.Add(s.Min); cuts.Add(s.Max); }
            foreach (Span s in line.Upper) { cuts.Add(s.Min); cuts.Add(s.Max); }
            foreach (Span s in line.Holes) { cuts.Add(s.Min); cuts.Add(s.Max); }
            if (cuts.Count == 0) return result;

            cuts.Sort();

            float half = t * 0.5f;
            bool open = false;         // is a run of wall currently being accumulated?
            float runMin = 0f, runMax = 0f, runOffset = 0f;

            for (int i = 0; i < cuts.Count - 1; i++)
            {
                float a = cuts[i], b = cuts[i + 1];
                if (b - a <= Geo.Epsilon) continue;

                float mid = (a + b) * 0.5f;
                bool lower = Covers(line.Lower, mid);
                bool upper = Covers(line.Upper, mid);
                bool doorway = Covers(line.Holes, mid);

                bool solid = (lower || upper) && !doorway;
                // Rooms both sides → the wall straddles the line and they share it. One side only →
                // it sits wholly outside that room, so the room keeps every metre it was authored.
                float offset = !solid ? 0f
                             : lower && upper ? 0f
                             : lower ? half     // room below → wall goes above the line
                             : -half;           // room above → wall goes below the line

                if (solid && open && Geo.Same(offset, runOffset) && Geo.Same(a, runMax))
                {
                    runMax = b;   // same wall, keep growing it
                    continue;
                }

                if (open) { result.Add((new Span(runMin, runMax), runOffset)); open = false; }

                if (solid)
                {
                    open = true; runMin = a; runMax = b; runOffset = offset;
                }
            }
            if (open) result.Add((new Span(runMin, runMax), runOffset));

            return Cap(result, line.Holes, t);
        }

        /// <summary>Grow each wall's ends out to close the corners. Where two perpendicular walls meet
        /// they leave a square hole the thickness of a wall; extending by a full thickness fills it in
        /// every configuration (overshooting into another wall is invisible — they are both solid and
        /// wear the same material). Ends that are a DOORWAY edge are left exactly where they are:
        /// grow one of those and you narrow the door.</summary>
        private static List<(Span Span, float Offset)> Cap(List<(Span Span, float Offset)> runs,
                                                           List<Span> holes, float t)
        {
            var capped = new List<(Span, float)>(runs.Count);
            foreach ((Span s, float offset) in runs)
            {
                float min = AtHoleEdge(s.Min, holes) ? s.Min : s.Min - t;
                float max = AtHoleEdge(s.Max, holes) ? s.Max : s.Max + t;
                capped.Add((new Span(min, max), offset));
            }
            return capped;
        }

        private static bool AtHoleEdge(float v, List<Span> holes)
        {
            foreach (Span h in holes)
                if (Geo.Same(h.Min, v) || Geo.Same(h.Max, v)) return true;
            return false;
        }

        private static bool Covers(List<Span> spans, float v)
        {
            foreach (Span s in spans)
                if (s.Contains(v)) return true;
            return false;
        }

        private static WallLine Line(List<WallLine> lines, float coord)
        {
            foreach (WallLine l in lines)
                if (Geo.Same(l.Coord, coord)) return l;

            var made = new WallLine { Coord = coord };
            lines.Add(made);
            return made;
        }
    }
}
