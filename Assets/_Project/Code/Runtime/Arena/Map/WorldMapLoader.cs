using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Turns a validated <see cref="WorldConfig"/> — 2D areas, gates on any wall at a fraction — into
    /// the <see cref="MapData"/> the rest of the engine already knows how to wall, dress and route
    /// (MV-267, Confluence MVW 34439170 §7).
    ///
    /// This is deliberately a CONVERTER, not a parallel engine: <see cref="MapGeometry"/> already
    /// solves arbitrary rectangles-with-shared-edges per line, so an area becomes a centre-authored
    /// <see cref="MapZone"/> and a gate becomes a <see cref="MapLink"/> plus the
    /// <see cref="EntityKind.AreaGate"/> entity that centres its doorway — the exact same doorway
    /// machinery <c>backyard_slice.json</c> already runs through. The new work is entirely upstream of
    /// that: resolving what a "wall at a fraction" even means as an absolute point
    /// (<see cref="MapValidation.ValidateWorldConfig"/> proves the two endpoints agree closely enough
    /// to have one), and averaging the two authored fractions into that one point.
    /// </summary>
    public static class WorldMapLoader
    {
        /// <summary>Parse a world-config JSON string and load it. The single entry point ticket 4
        /// (MV-270) is expected to call once <c>world1_config.json</c> is wired up as a real map.</summary>
        public static bool TryLoadJson(string json, out MapData map, out string reason)
        {
            map = null;

            if (string.IsNullOrWhiteSpace(json)) { reason = "the world config JSON is empty"; return false; }

            WorldConfig cfg;
            try
            {
                cfg = JsonUtility.FromJson<WorldConfig>(json);
            }
            catch (Exception e)
            {
                reason = $"world config JSON is malformed: {e.Message}";
                return false;
            }

            if (cfg == null) { reason = "world config JSON did not parse"; return false; }

            cfg.areas ??= Array.Empty<WorldArea>();
            cfg.gates ??= Array.Empty<WorldGate>();

            return TryLoad(cfg, out map, out reason);
        }

        /// <summary>Validate then convert. Refuses to hand back a <see cref="MapData"/> for a config
        /// that would not play — the whole point of validating twice (once in world-config terms, once
        /// after conversion in the old engine's terms) is that a bad number fails here, not in a
        /// playtest.</summary>
        public static bool TryLoad(WorldConfig cfg, out MapData map, out string reason)
        {
            map = null;

            if (!MapValidation.ValidateWorldConfig(cfg, out reason)) return false;

            var zones = new MapZone[cfg.areas.Length];
            for (int i = 0; i < cfg.areas.Length; i++)
            {
                WorldArea a = cfg.areas[i];
                Vector2 c = a.CenterXz;
                zones[i] = new MapZone
                {
                    id = a.id,
                    name = string.IsNullOrEmpty(a.name) ? a.id : a.name,
                    type = ZoneType(a),
                    x = c.x,
                    z = c.y,
                    width = a.size.w,
                    depth = a.size.d,
                };
            }

            var links = new MapLink[cfg.gates.Length];
            var entities = new List<MapEntity>(cfg.gates.Length + 1);

            for (int i = 0; i < cfg.gates.Length; i++)
            {
                WorldGate g = cfg.gates[i];
                WorldArea fromArea = cfg.Area(g.from.area);
                WorldArea toArea = cfg.Area(g.to.area);
                WallEnums.TryParse(g.from.wall, out Wall fromWall);
                WallEnums.TryParse(g.to.wall, out Wall toWall);

                ResolveDoorPosition(fromArea, fromWall, g.from.pos, toArea, toWall, g.to.pos,
                                     out float gx, out float gz);

                entities.Add(new MapEntity
                {
                    id = g.id,
                    kind = "areagate",
                    x = gx,
                    z = gz,
                    height = 3f,
                    depth = 0.6f,
                });

                links[i] = new MapLink { from = g.from.area, to = g.to.area, doorway = g.width, gate = g.id };
            }

            // The schema authors areas and gates, not individual entities — synthesise the one entity
            // every map still needs: where Max stands at the start, in the middle of the entry stub.
            WorldArea entry = FindEntry(cfg);
            Vector2 entryCenter = entry.CenterXz;
            entities.Add(new MapEntity { id = "spawn", kind = "playerSpawn", x = entryCenter.x, z = entryCenter.y });

            map = new MapData
            {
                name = string.IsNullOrEmpty(cfg.world) ? "World" : cfg.world,
                zones = zones,
                links = links,
                entities = entities.ToArray(),
            };

            return MapValidation.Validate(map, out reason);
        }

        /// <summary>Where a gate's two authored positions (each a fraction along ITS OWN area's wall)
        /// resolve to a single physical opening: the midpoint of the two, which
        /// <see cref="MapValidation.ValidateWorldConfig"/> has already proven sits inside the walls'
        /// shared span with room for the gate's full width.</summary>
        private static void ResolveDoorPosition(WorldArea from, Wall fromWall, float fromPos,
                                                 WorldArea to, Wall toWall, float toPos,
                                                 out float x, out float z)
        {
            Span fromSpan = from.WallSpan(fromWall);
            Span toSpan = to.WallSpan(toWall);

            float posFrom = fromSpan.Min + Mathf.Clamp01(fromPos) * fromSpan.Length;
            float posTo = toSpan.Min + Mathf.Clamp01(toPos) * toSpan.Length;
            float along = (posFrom + posTo) * 0.5f;

            float fixedCoord = from.WallCoord(fromWall);

            if (from.WallRunsAlongX(fromWall)) { x = along; z = fixedCoord; }
            else { x = fixedCoord; z = along; }
        }

        private static string ZoneType(WorldArea a)
        {
            if (a.IsEntryRole) return "entry";
            if (a.IsBossRole) return "boss";
            return "open";
        }

        private static WorldArea FindEntry(WorldConfig cfg)
        {
            foreach (WorldArea a in cfg.areas) if (a.IsEntryRole) return a;
            return null;
        }
    }
}
