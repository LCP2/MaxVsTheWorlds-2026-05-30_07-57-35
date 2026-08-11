using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.UI
{
    /// <summary>What the minimap draws for one area (MV-264): hidden until the player has been
    /// there, current while they are standing in it, visited once they have moved on. A one-way
    /// progression — the gated arena has no way back past a broken gate — so this is read straight
    /// off <see cref="AreaAccumulationDirector.CurrentArea"/> rather than tracked independently.</summary>
    public enum AreaVisibility { Hidden, Visited, Current }

    /// <summary>
    /// Pure fog-of-war maths for the HUD minimap (MV-264, reintroducing YT-217's minimap now that the
    /// v0.5 recut replaced "a bounded single garden" with a 10-area gated arena). No MonoBehaviour, no
    /// state of its own — <see cref="HudController"/> calls this off the real
    /// <see cref="AreaAccumulationDirector.CurrentArea"/>, the same live index MV-242 already wired.
    /// </summary>
    public static class MinimapModel
    {
        /// <summary>How many "area&lt;N&gt;" zones the map defines — 1-based, so also the highest
        /// valid area index. Everything else the map authors (the boss clearing, an unrecognised id)
        /// is not part of the strip, exactly as <see cref="AreaAccumulationDirector.AreaIndexOf"/>
        /// already decides for the population director. Never hardcoded, so a map with a different
        /// area count draws its own strip rather than being truncated or overrun by a fixed ten.</summary>
        public static int CountAreas(MapData map)
        {
            if (map?.zones == null) return 0;

            int max = 0;
            foreach (MapZone zone in map.zones)
            {
                if (zone == null) continue;
                int index = AreaAccumulationDirector.AreaIndexOf(zone.id);
                if (index > max) max = index;
            }
            return max;
        }

        /// <summary>One state per area, 1..<paramref name="totalAreas"/> in order. Areas below
        /// <paramref name="currentArea"/> are Visited — the gates behind Max do not reopen, so once
        /// passed they stay passed — the one Max is standing in is Current, and everything ahead is
        /// Hidden: the fog-of-war the ticket asks for.</summary>
        public static AreaVisibility[] BuildStates(int totalAreas, int currentArea)
        {
            if (totalAreas < 0) totalAreas = 0;

            var states = new AreaVisibility[totalAreas];
            for (int i = 0; i < totalAreas; i++)
            {
                int areaIndex = i + 1;
                states[i] = areaIndex < currentArea ? AreaVisibility.Visited
                    : areaIndex == currentArea ? AreaVisibility.Current
                    : AreaVisibility.Hidden;
            }
            return states;
        }

        /// <summary>World-space (XZ) bounding rect of just the "area&lt;N&gt;" zones (MV-341) — the
        /// boss clearing and anything else <see cref="CountAreas"/> already excludes stays out here
        /// too, so a spatial minimap's scale is not stretched to fit a room the strip never draws.</summary>
        public static Rect AreaBounds(MapData map)
        {
            if (map?.zones == null) return new Rect(0f, 0f, 0f, 0f);

            float xMin = float.MaxValue, xMax = float.MinValue;
            float zMin = float.MaxValue, zMax = float.MinValue;
            bool any = false;
            foreach (MapZone zone in map.zones)
            {
                if (zone == null || AreaAccumulationDirector.AreaIndexOf(zone.id) <= 0) continue;
                any = true;
                xMin = Mathf.Min(xMin, zone.XMin); xMax = Mathf.Max(xMax, zone.XMax);
                zMin = Mathf.Min(zMin, zone.ZMin); zMax = Mathf.Max(zMax, zone.ZMax);
            }
            return any ? new Rect(xMin, zMin, xMax - xMin, zMax - zMin) : new Rect(0f, 0f, 0f, 0f);
        }

        /// <summary>A zone's footprint expressed as a 0..1 fraction of <paramref name="bounds"/> (MV-341)
        /// — how the HUD places each room inside the fixed-size minimap frame. A degenerate (zero-size)
        /// bounds returns the full 0..1 square rather than dividing by zero, so a one-zone or malformed
        /// map still draws something instead of NaN-ing the HUD.</summary>
        public static Rect NormalizedZoneRect(Rect bounds, MapZone zone)
        {
            if (zone == null) return new Rect(0f, 0f, 0f, 0f);
            if (bounds.width <= 0f || bounds.height <= 0f) return new Rect(0f, 0f, 1f, 1f);

            return new Rect(
                (zone.XMin - bounds.xMin) / bounds.width,
                (zone.ZMin - bounds.yMin) / bounds.height,
                zone.width / bounds.width,
                zone.depth / bounds.height);
        }

        /// <summary>A world XZ point as a 0..1 position within <paramref name="bounds"/> (MV-341),
        /// clamped so a player standing outside the area strip's own bounds (the boss clearing, mid-
        /// transition through a doorway) still draws a marker pinned to the map's edge instead of
        /// flying off the HUD panel.</summary>
        public static Vector2 NormalizedPosition(Rect bounds, float worldX, float worldZ)
        {
            if (bounds.width <= 0f || bounds.height <= 0f) return new Vector2(0.5f, 0.5f);

            float nx = Mathf.Clamp01((worldX - bounds.xMin) / bounds.width);
            float nz = Mathf.Clamp01((worldZ - bounds.yMin) / bounds.height);
            return new Vector2(nx, nz);
        }
    }
}
