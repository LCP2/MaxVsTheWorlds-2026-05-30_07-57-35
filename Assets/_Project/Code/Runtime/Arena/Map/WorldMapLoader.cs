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

            // Combat areas 1..dials.areaCount are renamed to the old engine's "area<N>" convention —
            // AreaAccumulationDirector (MV-223/242/245) still resolves a zone's area number by parsing
            // that literal prefix (MV-270), and this is the one place that can translate for it without
            // touching that director's public surface. The entry stub and boss room keep their authored
            // ids (they never match "area<N>" and are never meant to — the ambient-population system
            // already treats an id it can't parse as index 0, exactly what a non-combat room wants).
            int areaCount = cfg.dials?.areaCount ?? 0;
            var zoneId = new Dictionary<string, string>(cfg.areas.Length);
            foreach (WorldArea a in cfg.areas)
                zoneId[a.id] = (a.index >= 1 && a.index <= areaCount) ? $"area{a.index}" : a.id;

            var zones = new MapZone[cfg.areas.Length];
            for (int i = 0; i < cfg.areas.Length; i++)
            {
                WorldArea a = cfg.areas[i];
                Vector2 c = a.CenterXz;
                zones[i] = new MapZone
                {
                    id = zoneId[a.id],
                    name = string.IsNullOrEmpty(a.name) ? a.id : a.name,
                    type = ZoneType(a),
                    x = c.x,
                    z = c.y,
                    width = a.size.w,
                    depth = a.size.d,
                };
            }

            // Not authored (0) → the same default MapData itself falls back to, so an un-tuned world
            // still builds the wall height it always has.
            float wallHeight = cfg.wallHeight > 0f ? cfg.wallHeight : MapData.DefaultWallHeight;

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
                    // Matches the wall it is set into (MV-277) — a fixed height here would leave the
                    // gate towering over (or sunk into) a wall/fence line tuned to a different height.
                    height = wallHeight,
                    // MV-297: matches MapData.DefaultWallThickness, the same fallback this MapData
                    // implicitly builds with (wallThickness is never authored here) — a literal that
                    // drifted from that default would z-fight or gap against the wall it seals.
                    depth = MapData.DefaultWallThickness,
                });

                links[i] = new MapLink { from = zoneId[g.from.area], to = zoneId[g.to.area], doorway = g.width, gate = g.id };
            }

            // The schema authors areas and gates, not individual entities — synthesise the one entity
            // every map still needs: where Max stands at the start, in the middle of the entry stub.
            WorldArea entry = FindEntry(cfg);
            Vector2 entryCenter = entry.CenterXz;
            entities.Add(new MapEntity { id = "spawn", kind = "playerSpawn", x = entryCenter.x, z = entryCenter.y });

            // A shed area's factory (MV-270, World & Difficulty Framework §6): the same MowerHutch
            // recipe every map's factory already builds through (MapRuntime.BuildFactory) — this is
            // what makes "sheds produce reinforcements" real rather than authored-but-inert data.
            foreach (WorldArea a in cfg.areas)
            {
                if (!a.hasShed || a.shed == null) continue;
                entities.Add(new MapEntity
                {
                    id = $"{a.id}_shed",
                    kind = "factory",
                    x = a.shed.x,
                    z = a.shed.z,
                    width = 3f,
                    height = 2f,
                    depth = 3f,
                    dressing = "shed",
                });
            }

            // The boss, adopted into place the same way the old corridor engine's compost clearing
            // did (MV-270) — without this entity MapRuntime has nowhere to move BigBermudaBoss to.
            foreach (WorldArea a in cfg.areas)
            {
                if (!a.IsBossRole) continue;

                // WorldBoss is a nested [Serializable] class — JsonUtility default-constructs it even
                // when the JSON never authors a "boss" object (the same reason hasShed exists as an
                // explicit flag rather than a shed != null check), so an unauthored boss reads back as
                // (0,0), not null. Falling back to the area's own centre for that case is a sane
                // default, and keeps a boss-role area with no authored boss position out of whatever
                // zone happens to sit at the world origin.
                Vector2 center = a.CenterXz;
                bool authored = a.boss != null && (a.boss.x != 0f || a.boss.z != 0f);

                entities.Add(new MapEntity
                {
                    id = authored && !string.IsNullOrEmpty(a.boss.id) ? a.boss.id : "big_bermuda",
                    kind = "boss",
                    x = authored ? a.boss.x : center.x,
                    z = authored ? a.boss.z : center.y,
                    width = authored ? (a.boss.size?.w ?? 3.5f) : 3.5f,
                    height = 3f,
                    depth = authored ? (a.boss.size?.d ?? 3.5f) : 3.5f,
                });
            }

            map = new MapData
            {
                name = string.IsNullOrEmpty(cfg.world) ? "World" : cfg.world,
                wallHeight = wallHeight,
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
