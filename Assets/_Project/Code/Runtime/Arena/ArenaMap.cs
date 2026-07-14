using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Arena
{
    public enum MapMarkerKind { Player, Factory, Gate, Boss }

    /// <summary>One room of the path, as a footprint on the ground (XZ).</summary>
    public readonly struct MapRoom
    {
        public readonly string Name;
        public readonly Rect Xz;   // x = world X, y = world Z

        public MapRoom(string name, Rect xz) { Name = name; Xz = xz; }
    }

    /// <summary>
    /// The arena reduced to a floor plan (YT-72 / YT-73). Derived from the same
    /// <see cref="BackyardPathLayout"/> that builds the actual geometry, so the map cannot drift
    /// from the level — reshape the arena and the map reshapes with it, with nothing to re-author.
    ///
    /// Pure maths: no GameObjects, no Canvas. The minimap and the full-screen map are two renderers
    /// of THIS, at two scales, which is what "build the map data model once" means.
    ///
    /// Convention: +Z (up-field, toward the boss) maps to UP on the map, which is how the player
    /// already thinks about the path. So map-Y is world-Z, not world-Y.
    /// </summary>
    public static class ArenaMap
    {
        /// <summary>
        /// The rooms of a MAP — one per zone, named the way the author named it.
        ///
        /// This is what the minimap draws now. The layout overloads below say a level in four
        /// numbers, which was enough while a level was a corridor; a room hanging off the side of the
        /// lawn cannot be said in them at all, and a map that quietly leaves out the shed and the nook
        /// is a map that lies about where you can go. So: a room in the level is a room on the map,
        /// with nothing to re-author.
        /// </summary>
        public static MapRoom[] Rooms(MapData map)
        {
            if (map?.zones == null) return new MapRoom[0];

            var rooms = new List<MapRoom>(map.zones.Length);
            foreach (MapZone zone in map.zones)
            {
                if (zone == null) continue;

                string name = string.IsNullOrWhiteSpace(zone.name) ? zone.id : zone.name;
                rooms.Add(new MapRoom(name, zone.Footprint));
            }

            return rooms.ToArray();
        }

        /// <summary>Everything a map has to contain, walls included.</summary>
        public static Rect Bounds(MapData map)
        {
            if (map == null) return new Rect(0f, 0f, 0f, 0f);

            Rect b = map.Bounds();
            float t = map.wallThickness;
            return new Rect(b.xMin - t, b.yMin - t, b.width + t * 2f, b.height + t * 2f);
        }

        /// <summary>The rooms, in the order the player walks them.</summary>
        public static MapRoom[] Rooms(in BackyardPathLayout l)
        {
            return new[]
            {
                new MapRoom("Patio", RectFrom(-l.PatioHalfWidth, l.StartZ, l.PatioHalfWidth, l.LawnStartZ)),
                new MapRoom("Lawn", RectFrom(-l.LawnHalfWidth, l.LawnStartZ, l.LawnHalfWidth, l.GateZ)),
                new MapRoom("Boss Arena", RectFrom(-l.ArenaHalfWidth, l.GateZ, l.ArenaHalfWidth, l.ArenaEndZ)),
            };
        }

        /// <summary>Everything the map has to contain, walls included.</summary>
        public static Rect Bounds(in BackyardPathLayout l)
        {
            float halfWidth = Mathf.Max(l.ArenaHalfWidth, Mathf.Max(l.LawnHalfWidth, l.PatioHalfWidth))
                              + l.WallThickness;
            return RectFrom(-halfWidth, l.StartZ - l.WallThickness,
                             halfWidth, l.ArenaEndZ + l.WallThickness);
        }

        /// <summary>Width ÷ height of the arena. A map drawn at any other aspect is a lie about the
        /// shape of the space, which is the one thing a map exists to tell you.</summary>
        public static float AspectRatio(in Rect bounds) =>
            bounds.height > 0f ? bounds.width / bounds.height : 1f;

        /// <summary>A world XZ point as 0..1 across the map, +Z up. Points outside the arena fall
        /// outside 0..1 rather than clamping — a caller that needs clamping should say so.</summary>
        public static Vector2 Normalize(Vector2 worldXz, in Rect bounds)
        {
            return new Vector2(
                bounds.width > 0f ? (worldXz.x - bounds.xMin) / bounds.width : 0.5f,
                bounds.height > 0f ? (worldXz.y - bounds.yMin) / bounds.height : 0.5f);
        }

        /// <summary>A room's footprint as a 0..1 rect on the map.</summary>
        public static Rect NormalizeRect(in Rect roomXz, in Rect bounds)
        {
            Vector2 min = Normalize(new Vector2(roomXz.xMin, roomXz.yMin), bounds);
            Vector2 max = Normalize(new Vector2(roomXz.xMax, roomXz.yMax), bounds);
            return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        }

        /// <summary>
        /// The largest size fitting inside <paramref name="panel"/> that still has the arena's
        /// aspect. Used by both renderers so neither squashes the arena to fill its box — a map
        /// whose proportions are wrong makes distances read wrong, which defeats the point.
        /// </summary>
        public static Vector2 FitPreservingAspect(Vector2 panel, float aspect)
        {
            if (aspect <= 0f || panel.x <= 0f || panel.y <= 0f) return panel;
            float byWidth = panel.x / aspect;                     // height if we use the full width
            return byWidth <= panel.y
                ? new Vector2(panel.x, byWidth)
                : new Vector2(panel.y * aspect, panel.y);
        }

        /// <summary>The room containing an XZ point, or -1. Lets the map say WHERE you are, not
        /// just plot a dot.</summary>
        public static int RoomAt(Vector2 worldXz, IReadOnlyList<MapRoom> rooms)
        {
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i].Xz.Contains(worldXz)) return i;
            return -1;
        }

        private static Rect RectFrom(float xMin, float zMin, float xMax, float zMax)
            => new Rect(xMin, zMin, xMax - xMin, zMax - zMin);
    }
}
