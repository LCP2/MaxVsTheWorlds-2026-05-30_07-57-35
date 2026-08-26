using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Pure map-geometry maths (MV-264, spatial rework MV-341): projecting the "area&lt;N&gt;" zones a
    /// world defines into a 2D diagram. No MonoBehaviour, no state of its own — originally
    /// <see cref="HudController"/>'s always-on minimap read this off the real
    /// <see cref="AreaAccumulationDirector.CurrentArea"/>; MV-563 replaced that widget with a full-screen
    /// <see cref="MapScreen"/> (no fog of war — every area is visible from the start) that reads the same
    /// geometry here, plus the shed lookup this class adds for it.
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

        /// <summary>Is this an area's boss arena? Reads the same <c>MapZone.type</c> the design already
        /// authors ("boss") through <see cref="WorldMapLoader"/> — a boss-role area keeps its numbered
        /// "area&lt;N&gt;" id (so <see cref="CountAreas"/>/<see cref="AreaBounds"/> already include it),
        /// it just carries <see cref="ZoneKind.Boss"/> instead of Open/Entry. Null-safe so a caller
        /// iterating <c>map.zones</c> doesn't need its own guard.</summary>
        public static bool IsBossZone(MapZone zone) => zone != null && zone.Kind == ZoneKind.Boss;

        /// <summary>Does a shed stand inside this zone's own footprint (MV-563)? A shed is authored as
        /// its own <see cref="MapEntity"/> (kind <c>factory</c>, dressing <c>shed</c>,
        /// <see cref="WorldMapLoader"/>) at a position inside the area that owns it, not as a flag on the
        /// zone itself — so this is a live spatial lookup against <paramref name="map"/>'s entities
        /// rather than a stored bit, exactly the "map is drawn from live map data" the ticket asks for
        /// (adding a shed to the config makes this true with no further wiring).</summary>
        public static bool ZoneHasShed(MapData map, MapZone zone)
        {
            if (map?.entities == null || zone == null) return false;

            foreach (MapEntity entity in map.entities)
            {
                if (entity == null) continue;
                if (entity.Kind != EntityKind.Factory || entity.Dressing != CoverDressing.Shed) continue;
                if (zone.Contains(entity.x, entity.z)) return true;
            }
            return false;
        }

        /// <summary>Is this link's doorway a boss gate (MV-566 AC 5)? True if EITHER zone it joins is a
        /// boss arena. A gate INTO a boss area is exactly the one the World &amp; Difficulty Framework's
        /// "opensWith: sheds-destroyed-before" rule requires every such gate to carry
        /// (<see cref="MaxWorlds.Arena.MapValidation"/>'s <c>WorldBossGate</c> check) — reading the
        /// distinction off the zone graph here means the map screen can draw it from
        /// <see cref="MapData"/> alone, without needing the raw <c>WorldConfig</c> that authored it.
        /// Null-safe throughout so a caller iterating <c>map.links</c> doesn't need its own guard.</summary>
        public static bool IsBossGate(MapData map, MapLink link)
        {
            if (map == null || link == null) return false;
            return IsBossZone(map.Zone(link.from)) || IsBossZone(map.Zone(link.to));
        }

        /// <summary>The two world XZ endpoints of the bar a doorway draws as, from the same
        /// <c>(runsAlongX, coord, hole)</c> triple <see cref="MaxWorlds.Arena.MapGeometry.Doorway"/>
        /// returns — the one place that decides which axis the hole's <c>Min</c>/<c>Max</c> sit on, so a
        /// caller can never mix up <paramref name="runsAlongX"/> and land the bar on the wrong wall.</summary>
        public static void DoorwayEndpoints(bool runsAlongX, float coord, Span hole, out Vector2 worldA, out Vector2 worldB)
        {
            worldA = runsAlongX ? new Vector2(hole.Min, coord) : new Vector2(coord, hole.Min);
            worldB = runsAlongX ? new Vector2(hole.Max, coord) : new Vector2(coord, hole.Max);
        }
    }
}
