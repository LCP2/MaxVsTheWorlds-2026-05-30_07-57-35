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
        /// <summary>A shed's built footprint, square, in metres (MV-541 chose the number; MV-692 gave
        /// it a shared name, matching the literal <see cref="TryLoad"/> stamps on a shed's own
        /// <see cref="MapEntity"/> below, so <see cref="MapValidation"/>'s deck-overlap check can never
        /// disagree with what actually gets built).</summary>
        public const float ShedFootprint = 2.25f;

        /// <summary>A shed's built height, in metres (MV-692 shared const — see <see cref="ShedFootprint"/>).</summary>
        public const float ShedHeight = 1.5f;

        /// <summary>A boss's built height, in metres, regardless of its authored footprint (MV-692
        /// shared const — see <see cref="ShedFootprint"/>).</summary>
        public const float BossHeight = 3f;

        /// <summary>A Replicator's built footprint, in metres (MV-706: "a 2x2x1.5 m armoured box").</summary>
        public const float ReplicatorFootprint = 2f;

        /// <summary>A Replicator's built height, in metres (MV-706).</summary>
        public const float ReplicatorHeight = 1.5f;

        /// <summary>The suffix on a <see cref="WorldGate.opensWith"/> that marks it a deck-level gate
        /// (MV-697) — parsed and stripped here, never carried onto the built <see cref="MapEntity"/>.</summary>
        public const string DeckGateSuffix = "[DECK]";

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

            // MV-697: an overlays area shares its target's origin/size BY DEFINITION — copy them now
            // that validation has already proven any authored value here agreed, so every downstream
            // reader (zones, decks/ramps/hatches, garrison) resolves off one single authoritative rect
            // rather than whatever the overlay's own JSON happened to repeat.
            foreach (WorldArea a in cfg.areas)
            {
                if (string.IsNullOrEmpty(a.overlays)) continue;
                WorldArea target = cfg.Area(a.overlays);
                if (target == null) continue; // MapValidation already refused this
                a.origin = target.origin;
                a.size = target.size;
            }

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
                    level = a.level,
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

                // MV-697: an opensWith carrying the "[DECK]" suffix is built at deck height in the wall
                // instead of the floor — MapRuntime.BuildAreaGate reads this level back off the entity
                // to shift its base up by MapData.deckHeight. The suffix itself is stripped here; the
                // remainder is exactly the same inert opensWith data every other gate already carries
                // (see WorldGate's own doc comment — nothing yet resolves it into locked/unlocked).
                int gateLevel = !string.IsNullOrEmpty(g.opensWith) &&
                                g.opensWith.EndsWith(DeckGateSuffix, StringComparison.Ordinal) ? 1 : 0;

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
                    level = gateLevel,
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
                        width = ShedFootprint,  // MV-541: 25% smaller (0.75x the pre-541 3 m body)
                        height = ShedHeight,    // MV-541: 25% smaller (0.75x the pre-541 2 m body)
                        depth = ShedFootprint,  // MV-541: 25% smaller (0.75x the pre-541 3 m body)
                        dressing = "shed",
                        mobile = s.mobile,  // MV-548
                    });
                }
            }

            // A Replicator area's box (MV-706, World & Difficulty Framework §6): World 2's factory in
            // place of a shed — the same MapRuntime.BuildReplicator recipe every map's replicator
            // builds through. One entity per authored replicator, same "per-entity not per-area" shape
            // as WorldShed above (MV-475).
            foreach (WorldArea a in cfg.areas)
            {
                WorldReplicator[] reps = a.replicators ?? Array.Empty<WorldReplicator>();
                for (int i = 0; i < reps.Length; i++)
                {
                    WorldReplicator r = reps[i];
                    if (r == null) continue;
                    entities.Add(new MapEntity
                    {
                        id = string.IsNullOrEmpty(r.id) ? $"{a.id}_replicator{i + 1}" : r.id,
                        kind = "replicator",
                        x = r.x,
                        z = r.z,
                        width = ReplicatorFootprint,
                        height = ReplicatorHeight,
                        depth = ReplicatorFootprint,
                        capacity = r.capacity,
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
                // MV-697: an overlays area shares its target's floor/fence/sludge/cover — it never
                // builds its own second pass of any of them, only its own decks/ramps/hatches/garrison.
                if (a.cover == null || !string.IsNullOrEmpty(a.overlays)) continue;
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

            // Sludge/decks/ramps (MV-692) — World 2's Stormdrain floor geometry. Rects are authored
            // area-local (WorldArea.WorldRectOf resolves them, same MIN-corner convention as an area's
            // own origin/size); MapValidation.ValidateWorldConfig has already proven every rect sits
            // inside its own area and every ramp touches exactly one deck, so building them here is
            // just resolving world coordinates, never re-checking correctness.
            float sludgeSpeedMultiplier = cfg.dials?.sludgeSpeedMultiplier ?? 0.6f;
            float defaultDeckHeight = cfg.dials?.deckHeight ?? 2.5f;

            foreach (WorldArea a in cfg.areas)
            {
                // MV-697: an overlays area shares its target's sludge — see the cover loop above for
                // the same reasoning.
                bool isOverlay = !string.IsNullOrEmpty(a.overlays);

                if (!isOverlay)
                foreach (WorldSludge s in a.sludge ?? Array.Empty<WorldSludge>())
                {
                    if (s == null) continue;
                    Rect rect = a.WorldRectOf(s.x, s.z, s.w, s.d);
                    entities.Add(new MapEntity
                    {
                        id = s.id,
                        kind = "sludge",
                        x = rect.center.x,
                        z = rect.center.y,
                        width = rect.width,
                        depth = rect.height,
                        // SludgeSpeedMultiplier reads this back off the otherwise-unused height field.
                        height = sludgeSpeedMultiplier,
                    });
                }

                var deckRects = new List<(WorldDeck deck, Rect rect, float height)>();
                foreach (WorldDeck d in a.decks ?? Array.Empty<WorldDeck>())
                {
                    if (d == null) continue;
                    Rect rect = a.WorldRectOf(d.x, d.z, d.w, d.d);
                    float height = d.height > 0f ? d.height : defaultDeckHeight;
                    deckRects.Add((d, rect, height));

                    entities.Add(new MapEntity
                    {
                        id = d.id,
                        kind = "deck",
                        x = rect.center.x,
                        z = rect.center.y,
                        width = rect.width,
                        depth = rect.height,
                        height = height,
                    });
                }

                foreach (WorldRamp r in a.ramps ?? Array.Empty<WorldRamp>())
                {
                    if (r == null) continue;
                    Rect rampRect = a.WorldRectOf(r.x, r.z, r.w, r.d);

                    Wall? facing = null;
                    float climbHeight = defaultDeckHeight;
                    foreach (var (_, deckRect, deckHeight) in deckRects)
                    {
                        Wall? touch = RampFacing(rampRect, deckRect);
                        if (touch == null) continue;
                        facing = touch;
                        climbHeight = deckHeight;
                        break;
                    }

                    entities.Add(new MapEntity
                    {
                        id = r.id,
                        kind = "ramp",
                        x = rampRect.center.x,
                        z = rampRect.center.y,
                        width = rampRect.width,
                        depth = rampRect.height,
                        height = climbHeight,
                        facing = facing?.ToString() ?? "",
                    });
                }

                // MV-697: hatches — a locked opening lying flat on whichever deck cell it overlaps.
                // MapValidation has already proven every hatch overlaps a deck in this same area, so
                // resolving its built height is just reading that deck's own resolved height back off it.
                foreach (WorldHatch h in a.hatches ?? Array.Empty<WorldHatch>())
                {
                    if (h == null) continue;
                    Rect hatchRect = a.WorldRectOf(h.x, h.z, h.w, h.d);

                    float hatchHeight = defaultDeckHeight;
                    foreach (var (_, deckRect, deckHeightVal) in deckRects)
                    {
                        if (!deckRect.Overlaps(hatchRect)) continue;
                        hatchHeight = deckHeightVal;
                        break;
                    }

                    entities.Add(new MapEntity
                    {
                        id = h.id,
                        kind = "hatch",
                        x = hatchRect.center.x,
                        z = hatchRect.center.y,
                        width = hatchRect.width,
                        depth = hatchRect.height,
                        // MapRuntime.BuildHatch reads this back as the flat panel's own world Y — a
                        // hatch has no vertical door extent the way a wall gate does, so, unlike a gate,
                        // this field carries the resolved position, not a size.
                        height = hatchHeight,
                        level = 1,
                    });
                }

                // MV-692 rule 4(c): a deck with no ramp reaching it is a validation WARNING, not an
                // error, until deck gates ship (MV-697) — logged here rather than failing TryLoad.
                foreach (var (deck, deckRect, _) in deckRects)
                {
                    bool reached = false;
                    foreach (WorldRamp r in a.ramps ?? Array.Empty<WorldRamp>())
                    {
                        if (r == null) continue;
                        if (RampFacing(a.WorldRectOf(r.x, r.z, r.w, r.d), deckRect) != null) { reached = true; break; }
                    }
                    if (!reached)
                        Debug.LogWarning($"[WorldMapLoader] area '{a.id}': deck '{deck.id}' has no ramp reaching " +
                                          "it — until deck gates ship (MV-697) it may be unreachable.");
                }
            }

            // World-level bridges (MV-711) — MapValidation.ValidateWorldConfig has already proven every
            // bridge resolves and clears its checks, so building one is just resolving world coordinates.
            // Built as an ordinary "deck" entity (MapRuntime.BuildDeck) — the SAME renderer/DeckVisibility
            // path an area's own deck already builds through, per the ticket's own "do not author a
            // second deck renderer" — with its short ends (the walls it meets each area on) left open via
            // the facing field, so kerb rails only appear on its two long edges.
            foreach (WorldBridge b in cfg.bridges ?? Array.Empty<WorldBridge>())
            {
                if (b == null) continue;
                if (!b.TryResolveFootprint(cfg, out Rect bridgeRect, out _)) continue; // already refused by validation

                WallEnums.TryParse(b.from.wall, out Wall bridgeFromWall);
                WallEnums.TryParse(b.to.wall, out Wall bridgeToWall);
                float bridgeHeight = b.height > 0f ? b.height : defaultDeckHeight;

                entities.Add(new MapEntity
                {
                    id = b.id,
                    kind = "deck",
                    x = bridgeRect.center.x,
                    z = bridgeRect.center.y,
                    width = bridgeRect.width,
                    depth = bridgeRect.height,
                    height = bridgeHeight,
                    facing = $"{bridgeFromWall},{bridgeToWall}",
                });

                WorldBridgePier[] piers = b.piers ?? Array.Empty<WorldBridgePier>();
                for (int i = 0; i < piers.Length; i++)
                {
                    WorldBridgePier pier = piers[i];
                    if (pier == null) continue;

                    entities.Add(new MapEntity
                    {
                        id = $"{b.id}_pier{i + 1}",
                        kind = "cover",
                        x = pier.x,
                        z = pier.z,
                        width = MapValidation.BridgePierSize,
                        height = bridgeHeight,
                        depth = MapValidation.BridgePierSize,
                        shape = "cylinder",
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
                        height = BossHeight,
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
                        height = BossHeight,
                        depth = b.size?.d ?? 3.5f,
                    });
                }
            }

            map = new MapData
            {
                name = string.IsNullOrEmpty(cfg.world) ? "World" : cfg.world,
                wallHeight = wallHeight,
                deckHeight = defaultDeckHeight,
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
            if (a.IsBridgeRole) return "bridge";
            return "open";
        }

        /// <summary>Which wall of <paramref name="rampRect"/> abuts <paramref name="deckRect"/>, or null
        /// if they don't touch — the same adjacency <see cref="MapValidation"/> already required to be
        /// unique before this ever runs, resolved here into the direction <see cref="MapGeometry.Ramps"/>
        /// climbs (MV-692).</summary>
        private static Wall? RampFacing(Rect rampRect, Rect deckRect)
        {
            if (Geo.Same(rampRect.yMax, deckRect.yMin) && RangesTouch(rampRect.xMin, rampRect.xMax, deckRect.xMin, deckRect.xMax))
                return Wall.N;
            if (Geo.Same(rampRect.yMin, deckRect.yMax) && RangesTouch(rampRect.xMin, rampRect.xMax, deckRect.xMin, deckRect.xMax))
                return Wall.S;
            if (Geo.Same(rampRect.xMax, deckRect.xMin) && RangesTouch(rampRect.yMin, rampRect.yMax, deckRect.yMin, deckRect.yMax))
                return Wall.E;
            if (Geo.Same(rampRect.xMin, deckRect.xMax) && RangesTouch(rampRect.yMin, rampRect.yMax, deckRect.yMin, deckRect.yMax))
                return Wall.W;
            return null;
        }

        private static bool RangesTouch(float minA, float maxA, float minB, float maxB) =>
            minA < maxB - Geo.Epsilon && maxA > minB + Geo.Epsilon;

        private static WorldArea FindEntry(WorldConfig cfg)
        {
            foreach (WorldArea a in cfg.areas) if (a.IsEntryRole) return a;
            return null;
        }
    }
}
