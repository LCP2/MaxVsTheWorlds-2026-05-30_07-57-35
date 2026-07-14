using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Describes a map in the terms the rest of the game already speaks
    /// (<see cref="BackyardPathLayout"/>): an entry, a fight room, a gate, an arena. The minimap, the
    /// full map panel, the dressing pass and the backdrop all read a level through that struct, and
    /// none of them should have to be rewritten to make maps data-driven.
    ///
    /// The direction of travel matters: the MAP is the source of truth and the layout is a VIEW
    /// derived from it — never the other way round. It is exact for a slice-shaped map (entry → fight
    /// room → boss arena, which is what the Backyard slice is). For a map with more rooms than that
    /// it degrades honestly: the biggest non-boss room stands in as the fight room. When maps grow
    /// past what these four numbers can say, the fix is to teach those consumers to read
    /// <see cref="MapData.zones"/> directly — not to widen this struct.
    /// </summary>
    public static class MapLayoutBridge
    {
        public static BackyardPathLayout ToLayout(MapData map)
        {
            if (map == null || map.zones == null || map.zones.Length == 0)
                return BackyardPathLayout.Default;

            MapZone entry = Find(map, ZoneKind.Entry);
            MapZone boss = Find(map, ZoneKind.Boss);
            MapZone fight = BiggestFightRoom(map);

            if (entry == null || boss == null || fight == null)
                return BackyardPathLayout.Default;

            var layout = new BackyardPathLayout
            {
                StartZ = entry.ZMin,
                LawnStartZ = fight.ZMin,
                GateZ = fight.ZMax,
                ArenaEndZ = boss.ZMax,
                PatioHalfWidth = entry.width * 0.5f,
                LawnHalfWidth = fight.width * 0.5f,
                GateHalfWidth = GateHalfWidth(map, fight, boss),
                ArenaHalfWidth = boss.width * 0.5f,
                WallHeight = map.wallHeight,
                WallThickness = map.wallThickness,
            };

            // A map that validated can still describe a shape these four rooms cannot express. Say so
            // rather than hand the minimap a nonsense arena.
            if (!layout.IsValid())
            {
                Debug.LogWarning($"[MapLayoutBridge] map '{map.name}' does not fit the " +
                                 "entry→fight→gate→arena shape the minimap reads; falling back.");
                return BackyardPathLayout.Default;
            }

            return layout;
        }

        /// <summary>Where the factory stands — the "shed". The dressing pass builds a shed around it
        /// and keeps its spawn ring clear.</summary>
        public static float ShedZ(MapData map)
        {
            MapEntity factory = map?.First(EntityKind.Factory);
            return factory?.z ?? 15f;
        }

        /// <summary>Half-width of the doorway into the boss arena.</summary>
        private static float GateHalfWidth(MapData map, MapZone fight, MapZone boss)
        {
            if (map.links != null)
            {
                foreach (MapLink link in map.links)
                {
                    if (link == null) continue;
                    bool joinsThem = (link.from == fight.id && link.to == boss.id)
                                  || (link.from == boss.id && link.to == fight.id);

                    if (joinsThem && MapGeometry.Doorway(map, link, out _, out _, out Span hole))
                        return hole.Length * 0.5f;
                }
            }
            return BackyardPathLayout.Default.GateHalfWidth;
        }

        private static MapZone Find(MapData map, ZoneKind kind)
        {
            foreach (MapZone z in map.zones)
                if (z != null && z.Kind == kind) return z;
            return null;
        }

        /// <summary>The room the fight happens in: the largest one that is not the entry and not the
        /// boss arena.</summary>
        private static MapZone BiggestFightRoom(MapData map)
        {
            MapZone best = null;
            float bestArea = 0f;

            foreach (MapZone z in map.zones)
            {
                if (z == null || z.Kind == ZoneKind.Entry || z.Kind == ZoneKind.Boss) continue;

                float area = z.width * z.depth;
                if (area > bestArea) { best = z; bestArea = area; }
            }
            return best;
        }
    }
}
