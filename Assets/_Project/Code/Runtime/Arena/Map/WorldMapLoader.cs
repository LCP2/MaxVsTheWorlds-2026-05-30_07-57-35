using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Enemies;

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
                    depth = 0.6f,
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
            // MV-475: one entity per authored shed, not per area — an area can carry several.
            foreach (WorldArea a in cfg.areas)
            {
                WorldShed[] sheds = a.Sheds();
                for (int i = 0; i < sheds.Length; i++)
                {
                    WorldShed s = sheds[i];
                    entities.Add(new MapEntity
                    {
                        id = a.ShedId(i, sheds.Length),
                        kind = "factory",
                        x = s.x,
                        z = s.z,
                        width = 2.25f,  // MV-541: 25% smaller (0.75x the pre-541 3 m body)
                        height = 1.5f,  // MV-541: 25% smaller (0.75x the pre-541 2 m body)
                        depth = 2.25f,  // MV-541: 25% smaller (0.75x the pre-541 3 m body)
                        dressing = "shed",
                        mobile = s.mobile,  // MV-548
                    });
                }
            }

            // MV-644: PowerupCadence.EnsureCoverage (Confluence MVW 34439170 §5/§8.7) made real — an
            // area with no shed of its own, sitting at the cadence limit, gets a reachable parts-cache
            // pickup so the "never more than dials.powerupCadence unfed areas in a row" guarantee is
            // something the game actually places, not just a property the authored sheds happen to have.
            if (cfg.dials != null)
            {
                var hasShed = new bool[areaCount];
                for (int i = 0; i < areaCount; i++)
                    hasShed[i] = cfg.AreaByIndex(i + 1)?.hasShed ?? false;

                bool[] coverage = PowerupCadence.EnsureCoverage(areaCount, hasShed, cfg.dials.powerupCadence);
                for (int i = 0; i < areaCount; i++)
                {
                    if (hasShed[i] || !coverage[i]) continue; // already fed, or the cadence never forced one here

                    WorldArea a = cfg.AreaByIndex(i + 1);
                    if (a == null) continue;

                    Vector2 pos = ResolveCachePosition(a);
                    entities.Add(new MapEntity
                    {
                        id = $"{a.id}_partscache",
                        kind = "pickup",
                        x = pos.x,
                        z = pos.y,
                        width = PartsCacheBodySize,
                        height = PartsCacheBodySize,
                        depth = PartsCacheBodySize,
                    });
                }
            }

            // Shrubbery authored per area (MV-318) — handed straight to the same Cover entity kind
            // backyard_slice.json's hand-placed hedges already build, validate and dress through, so an
            // area's shrub rows are obstacles the moment they're authored, not a parallel mechanic.
            foreach (WorldArea a in cfg.areas)
            {
                if (a.cover == null) continue;
                foreach (WorldCover c in a.cover)
                {
                    if (c == null) continue;
                    entities.Add(new MapEntity
                    {
                        id = c.id,
                        kind = "cover",
                        x = c.x,
                        z = c.z,
                        width = c.width,
                        height = c.height,
                        depth = c.depth,
                        shape = c.shape,
                        dressing = c.dressing,
                    });
                }
            }

            // The boss(es), built the same way MV-542 anticipated a 2+ boss fight would need
            // (BigBermudaBoss.FitColliderToRenderedBody's own comment). MV-561: one entity per
            // resolved boss (WorldArea.Bosses()), not one per area — an area can carry several.
            foreach (WorldArea a in cfg.areas)
            {
                if (!a.IsBossRole) continue;

                WorldBoss[] bosses = a.Bosses();
                if (bosses.Length == 0)
                {
                    // A boss-role area with no authored boss position at all still needs one entity so
                    // MapRuntime has something to build — falling back to the area's own centre keeps it
                    // out of whatever zone happens to sit at the world origin.
                    Vector2 center = a.CenterXz;
                    entities.Add(new MapEntity
                    {
                        id = a.BossId(null, 0, 1),
                        kind = "boss",
                        x = center.x,
                        z = center.y,
                        width = 3.5f,
                        height = 3f,
                        depth = 3.5f,
                    });
                    continue;
                }

                for (int i = 0; i < bosses.Length; i++)
                {
                    WorldBoss b = bosses[i];
                    entities.Add(new MapEntity
                    {
                        id = a.BossId(b, i, bosses.Length),
                        kind = "boss",
                        x = b.x,
                        z = b.z,
                        width = b.size?.w ?? 3.5f,
                        height = 3f,
                        depth = b.size?.d ?? 3.5f,
                    });
                }
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

        /// <summary>A guaranteed parts cache (MV-644) is treated as a 2 m x 2 m body for clearance
        /// purposes — the AC's own reachability spec.</summary>
        private const float PartsCacheBodySize = 2f;

        /// <summary>How far apart, in radians, each dodge attempt tries next on the ring — small
        /// enough that a few steps clears ordinary cover/garrison spacing without walking most of the
        /// way round the room.</summary>
        private const float CacheDodgeStep = 10f * Mathf.Deg2Rad;

        /// <summary>Deterministic placement for a guaranteed parts cache (MV-644): the area's own
        /// centre, unless that sits too close to authored cover or a garrison position, in which case
        /// it walks out along the same evenly-spaced-ring idiom <see cref="MaxWorlds.Enemies.Garrison"/>'s
        /// own cover-dodge uses — so the same config always produces the same layout. Falls back to the
        /// centre if nothing on the ring clears, same as that ring's own "stand there anyway" fallback.</summary>
        private static Vector2 ResolveCachePosition(WorldArea a)
        {
            Vector2 center = a.CenterXz;
            if (IsClearForCache(center, a)) return center;

            float radius = Mathf.Min(a.size.w, a.size.d) * 0.3f;
            for (float angle = 0f; angle < Mathf.PI * 2f; angle += CacheDodgeStep)
            {
                Vector2 candidate = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (IsClearForCache(candidate, a)) return candidate;
            }

            return center;
        }

        /// <summary>Reachable by a 2 m x 2 m body (margin off the area's own walls), and clear of
        /// authored cover and of any garrison position by the same margins <see cref="MapValidation"/>
        /// already enforces for a garrison entry (<see cref="MapValidation.MinPickupCoverGap"/> against
        /// cover, <see cref="MapValidation.SpawnRadius"/>+<see cref="MapValidation.SpawnClearance"/>
        /// against another placed body — the personal-space radius every spawn-adjacent placement in
        /// this engine already uses).</summary>
        private static bool IsClearForCache(Vector2 point, WorldArea a)
        {
            float half = PartsCacheBodySize * 0.5f;
            if (point.x - a.XMin < half || a.XMax - point.x < half ||
                point.y - a.ZMin < half || a.ZMax - point.y < half)
                return false;

            foreach (WorldCover c in a.cover)
            {
                if (c == null) continue;

                ArenaCover body = new MapEntity
                {
                    x = c.x, z = c.z, width = c.width, height = c.height, depth = c.depth, shape = c.shape,
                }.ToCover();

                if (body.DistanceTo(point) < MapValidation.MinPickupCoverGap) return false;
            }

            foreach (WorldGarrisonEntry g in a.garrison)
            {
                if (g == null) continue;
                if (Vector2.Distance(point, new Vector2(g.x, g.z)) < MapValidation.SpawnRadius + MapValidation.SpawnClearance)
                    return false;
            }

            return true;
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
