using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Refuses a map that would not play. Every rule here is one a playtest would otherwise have to
    /// find: a boss you cannot walk to, a gate with no key, a prop sitting on the spawn ring, a room
    /// so pinched there is nowhere to run. Authoring gets faster only if the feedback is instant, and
    /// this is where "instant" comes from — a bad number fails a test, not a build-and-deploy.
    ///
    /// <paramref name="reason"/> names the FIRST breach in plain language, because the person reading
    /// it is mid-edit and wants to know what to drag, not which assertion tripped.
    /// </summary>
    public static class MapValidation
    {
        /// <summary>Narrowest doorway Max and a chasing swarm both fit through.</summary>
        public const float MinDoorway = 3f;

        /// <summary>Shortest a wall/fence may be and still read as a wall from the fixed 72°
        /// top-down camera. Not tied to Max's own 1.83 m height (<c>MaxRig.cs</c>) — a wall only needs
        /// enough vertical face to throw a clear silhouette against the floor, not to loom over him.
        /// Lowered from 2 m for the 0.6.1 lower-wall pass (MV-277); still comfortably above cover's own
        /// 1 m minimum (below), which only has to break a chase, not read as a boundary.</summary>
        public const float MinWallHeight = 1.5f;

        /// <summary>A room narrower than this is a corridor, not a room. Only enforced on rooms the
        /// author called a fight room (Open/Dense) — an entry patio is allowed to be tight, that is
        /// what makes the lawn beyond it read as a release.</summary>
        public const float MinFightRoomWidth = 18f;

        /// <summary>Clearance cover must leave outside a factory's spawn ring. Merely not touching the
        /// ring is not enough — a robot has a body, so a prop tangent to the ring still spawns robots
        /// halfway inside it.</summary>
        public const float SpawnClearance = 0.8f;

        /// <summary>How wide a factory's spawn ring is. Matches the EnemySpawner's radius.</summary>
        public const float SpawnRadius = 3.5f;

        /// <summary>Cover must leave the mouth of a doorway clear, or the way through stops reading as
        /// a way through.</summary>
        public const float DoorwayClearance = 2f;

        /// <summary>Narrowest gap the player must always have to run through, at any depth of a
        /// room.</summary>
        public const float MinFreeChannel = 6f;

        public static bool Validate(MapData map, out string reason)
        {
            if (map == null) { reason = "the map is null"; return false; }

            return Structure(map, out reason)
                && Links(map, out reason)
                && Actors(map, out reason)
                && Reachable(map, out reason)
                && Cover(map, out reason);
        }

        private static bool Structure(MapData map, out string reason)
        {
            if (map.zones == null || map.zones.Length == 0)
            { reason = "the map has no zones — there is nothing to stand in"; return false; }

            if (map.wallHeight < MinWallHeight)
            { reason = $"wallHeight {map.wallHeight} is too short to read as a wall (min {MinWallHeight})"; return false; }

            if (map.wallThickness <= 0f)
            { reason = "wallThickness must be positive"; return false; }

            var seen = new HashSet<string>();
            foreach (MapZone z in map.zones)
            {
                if (z == null) { reason = "a zone is null"; return false; }

                if (string.IsNullOrWhiteSpace(z.id))
                { reason = "a zone has no id — links refer to zones by id"; return false; }

                if (!seen.Add(z.id))
                { reason = $"two zones share the id '{z.id}'"; return false; }

                if (z.width <= 0f || z.depth <= 0f)
                { reason = $"zone '{z.id}' has no area ({z.width}×{z.depth})"; return false; }

                // A room you are meant to fight in has to be one you can circle in (the lesson of the
                // 9 m corridor that read as a path and played as a treadmill).
                bool isFightRoom = z.Kind == ZoneKind.Open || z.Kind == ZoneKind.Dense || z.Kind == ZoneKind.Boss;
                if (isFightRoom && Mathf.Min(z.width, z.depth) < MinFightRoomWidth)
                {
                    reason = $"zone '{z.id}' is a {z.type} room but only {Mathf.Min(z.width, z.depth):0.#} m " +
                             $"across — under {MinFightRoomWidth} m there is no room to circle-strafe";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        private static bool Links(MapData map, out string reason)
        {
            if (map.links != null)
            {
                foreach (MapLink link in map.links)
                {
                    if (link == null) { reason = "a link is null"; return false; }

                    if (map.Zone(link.from) == null)
                    { reason = $"link references zone '{link.from}', which does not exist"; return false; }

                    if (map.Zone(link.to) == null)
                    { reason = $"link references zone '{link.to}', which does not exist"; return false; }

                    if (!MapGeometry.Doorway(map, link, out _, out _, out Span hole))
                    {
                        reason = $"zones '{link.from}' and '{link.to}' are linked but do not share an edge — " +
                                 "move one so they touch, or the doorway cuts nothing";
                        return false;
                    }

                    if (hole.Length < MinDoorway)
                    {
                        reason = $"the doorway between '{link.from}' and '{link.to}' is {hole.Length:0.#} m — " +
                                 $"under {MinDoorway} m Max and the swarm cannot both get through";
                        return false;
                    }

                    if (!string.IsNullOrEmpty(link.gate) && map.Entity(link.gate) == null)
                    { reason = $"link '{link.from}'→'{link.to}' names gate '{link.gate}', which does not exist"; return false; }
                }
            }

            // A gate that fills no doorway is a slab standing in a field. An area gate (WV-222) is the
            // same claim about a different entity kind — it seals a doorway exactly like the
            // scene-adopted Gate does, it just opens on its own HP instead of a factory's death.
            foreach (MapEntity e in Kind(map, EntityKind.Gate))
                if (!FillsADoorway(map, e.id))
                { reason = $"gate '{e.id}' does not fill any doorway — no link names it"; return false; }

            foreach (MapEntity e in Kind(map, EntityKind.AreaGate))
                if (!FillsADoorway(map, e.id))
                { reason = $"area gate '{e.id}' does not fill any doorway — no link names it"; return false; }

            reason = null;
            return true;
        }

        private static bool FillsADoorway(MapData map, string entityId)
        {
            if (map.links != null)
                foreach (MapLink link in map.links)
                    if (link != null && link.gate == entityId) return true;
            return false;
        }

        private static bool Actors(MapData map, out string reason)
        {
            if (map.entities != null)
            {
                foreach (MapEntity e in map.entities)
                {
                    if (e == null) { reason = "an entity is null"; return false; }

                    if (string.IsNullOrWhiteSpace(e.id))
                    { reason = $"a {e.kind} entity has no id"; return false; }

                    if (e.Kind == EntityKind.Unknown)
                    { reason = $"entity '{e.id}' has unknown kind '{e.kind}'"; return false; }

                    // A gate stands ON a wall line, so it is legitimately outside every room — true of
                    // an area gate (WV-222) exactly as it is the scene-adopted one. Anything else
                    // authored outside a room is standing in the void.
                    if (e.Kind != EntityKind.Gate && e.Kind != EntityKind.AreaGate && map.ZoneAt(e.x, e.z) == null)
                    { reason = $"'{e.id}' is at ({e.x}, {e.z}), which is not inside any zone"; return false; }
                }
            }

            var spawns = Kind(map, EntityKind.PlayerSpawn);
            if (spawns.Count != 1)
            { reason = $"the map has {spawns.Count} player spawns — it needs exactly one"; return false; }

            foreach (MapEntity gate in Kind(map, EntityKind.Gate))
            {
                string[] keys = gate.Keys;
                if (keys.Length == 0)
                {
                    reason = $"gate '{gate.id}' has no opensOn — a locked door with no key is a dead end";
                    return false;
                }

                // Every key, not just the first: a gate that names two factories and gets one of the
                // names wrong is a gate that can never open, and it would play as a finished level
                // that simply refuses to end.
                var named = new HashSet<string>();
                foreach (string id in keys)
                {
                    MapEntity key = map.Entity(id);
                    if (key == null || key.Kind != EntityKind.Factory)
                    {
                        reason = $"gate '{gate.id}' opens on '{id}', which is not a factory in this map";
                        return false;
                    }

                    if (!named.Add(id))
                    { reason = $"gate '{gate.id}' names factory '{id}' twice"; return false; }
                }
            }

            foreach (MapEntity boss in Kind(map, EntityKind.Boss))
            {
                MapZone zone = map.ZoneAt(boss.x, boss.z);
                if (zone != null && zone.Kind != ZoneKind.Boss)
                { reason = $"boss '{boss.id}' stands in '{zone.id}', which is not a boss zone"; return false; }
            }

            reason = null;
            return true;
        }

        /// <summary>Can Max actually walk from where he spawns to the boss? Gates do not block this —
        /// they open. A map that fails here is one where the run cannot be finished, which is the one
        /// bug a layout must never ship with.</summary>
        private static bool Reachable(MapData map, out string reason)
        {
            MapEntity spawn = map.First(EntityKind.PlayerSpawn);
            MapZone from = spawn == null ? null : map.ZoneAt(spawn.x, spawn.z);
            if (from == null) { reason = "the player spawn is not inside a zone"; return false; }

            var reached = new HashSet<string> { from.id };
            var queue = new Queue<string>();
            queue.Enqueue(from.id);

            while (queue.Count > 0)
            {
                string here = queue.Dequeue();
                if (map.links == null) break;

                foreach (MapLink link in map.links)
                {
                    if (link == null) continue;
                    string next = link.from == here ? link.to
                                : link.to == here ? link.from
                                : null;

                    if (next != null && reached.Add(next)) queue.Enqueue(next);
                }
            }

            foreach (MapZone z in map.zones)
            {
                if (z != null && z.Kind == ZoneKind.Boss && !reached.Contains(z.id))
                {
                    reason = $"the boss zone '{z.id}' cannot be walked to from the player spawn — " +
                             "no chain of links reaches it";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        private static bool Cover(MapData map, out string reason)
        {
            List<MapEntity> cover = Kind(map, EntityKind.Cover);
            List<MapEntity> factories = Kind(map, EntityKind.Factory);

            for (int i = 0; i < cover.Count; i++)
            {
                MapEntity c = cover[i];
                ArenaCover body = c.ToCover();

                if (c.height < 1f)
                { reason = $"'{c.id}' is {c.height} m tall — too short to break a chase"; return false; }

                MapZone zone = map.ZoneAt(c.x, c.z);
                if (zone != null && zone.Kind == ZoneKind.Boss)
                {
                    reason = $"'{c.id}' is cover in the boss arena '{zone.id}' — the boss fight is " +
                             "readability-first and stays open";
                    return false;
                }

                foreach (MapEntity f in factories)
                {
                    if (body.DistanceTo(f.CenterXz) < SpawnRadius + SpawnClearance)
                    { reason = $"'{c.id}' crowds '{f.id}'s spawn ring — robots would spawn inside it"; return false; }
                }

                // A doorway you cannot see through is a doorway you cannot find.
                if (map.links != null)
                {
                    foreach (MapLink link in map.links)
                    {
                        if (link == null) continue;
                        if (!MapGeometry.Doorway(map, link, out bool alongX, out float coord, out Span hole)) continue;

                        var mouth = new Vector2(alongX ? hole.Mid : coord, alongX ? coord : hole.Mid);
                        if (body.DistanceTo(mouth) < DoorwayClearance)
                        { reason = $"'{c.id}' blocks the doorway between '{link.from}' and '{link.to}'"; return false; }
                    }
                }

                for (int j = i + 1; j < cover.Count; j++)
                {
                    if (body.Overlaps(cover[j].ToCover()))
                    { reason = $"'{c.id}' overlaps '{cover[j].id}'"; return false; }
                }
            }

            // Sweep every room: at no depth may cover pinch it shut. The player must always have
            // somewhere to run.
            foreach (MapZone z in map.zones)
            {
                if (z == null || z.width < MinFreeChannel) continue;

                for (float depth = z.ZMin; depth <= z.ZMax; depth += 0.5f)
                {
                    if (FreeChannelAt(z, cover, depth) < MinFreeChannel)
                    { reason = $"cover pinches '{z.id}' shut at z={depth:0.#}"; return false; }
                }
            }

            reason = null;
            return true;
        }

        /// <summary>Widest continuous gap a player can run through at depth <paramref name="z"/>,
        /// across a room. Note it is the WIDEST gap, not the sum of the gaps — three 2 m slots are not
        /// a 6 m channel.</summary>
        public static float FreeChannelAt(MapZone zone, IReadOnlyList<MapEntity> cover, float z)
        {
            float min = zone.XMin, max = zone.XMax;

            var blocked = new List<Span>();
            foreach (MapEntity c in cover)
            {
                Rect r = c.ToCover().Footprint;
                if (z < r.yMin || z > r.yMax) continue;
                if (r.xMax < min || r.xMin > max) continue;
                blocked.Add(new Span(Mathf.Max(r.xMin, min), Mathf.Min(r.xMax, max)));
            }
            blocked.Sort((a, b) => a.Min.CompareTo(b.Min));

            float widest = 0f, cursor = min;
            foreach (Span b in blocked)
            {
                if (b.Min > cursor) widest = Mathf.Max(widest, b.Min - cursor);
                cursor = Mathf.Max(cursor, b.Max);
            }
            return Mathf.Max(widest, max - cursor);
        }

        public static List<MapEntity> Kind(MapData map, EntityKind kind)
        {
            var found = new List<MapEntity>();
            if (map?.entities == null) return found;

            foreach (MapEntity e in map.entities)
                if (e != null && e.Kind == kind) found.Add(e);

            return found;
        }

        // ---------------------------------------------------------------------------------------
        // World-config validation (MV-267) — the 2D-area-placement + gates-on-any-wall-at-a-fraction
        // schema (Confluence MVW 34439170 §7). This runs BEFORE a WorldConfig is converted to a
        // MapData (WorldMapLoader): it is the only place that still knows which WALL and FRACTION a
        // gate was authored against, which is lost the moment it becomes an absolute doorway. Once
        // converted, the resulting MapData still passes through the ordinary Validate() above —
        // belt and suspenders, and it is what actually proves "renders with correctly aligned
        // openings" rather than merely "the numbers were self-consistent".
        // ---------------------------------------------------------------------------------------

        public static bool ValidateWorldConfig(WorldConfig cfg, out string reason)
        {
            if (cfg == null) { reason = "the world config is null"; return false; }

            return WorldAreas(cfg, out reason)
                && WorldGates(cfg, out reason)
                && WorldReachability(cfg, out reason)
                && WorldBossGate(cfg, out reason);
        }

        private static bool WorldAreas(WorldConfig cfg, out string reason)
        {
            if (cfg.areas == null || cfg.areas.Length == 0)
            { reason = "the world config has no areas"; return false; }

            var seen = new HashSet<string>();
            foreach (WorldArea a in cfg.areas)
            {
                if (a == null) { reason = "an area is null"; return false; }

                if (string.IsNullOrWhiteSpace(a.id))
                { reason = "an area has no id — gates refer to areas by id"; return false; }

                if (!seen.Add(a.id))
                { reason = $"two areas share the id '{a.id}'"; return false; }

                if (a.origin == null)
                { reason = $"area '{a.id}' has no origin"; return false; }

                if (a.size == null || a.size.w <= 0f || a.size.d <= 0f)
                { reason = $"area '{a.id}' has no area ({a.size?.w ?? 0f}×{a.size?.d ?? 0f})"; return false; }
            }

            for (int i = 0; i < cfg.areas.Length; i++)
            for (int j = i + 1; j < cfg.areas.Length; j++)
            {
                if (AreasOverlap(cfg.areas[i], cfg.areas[j]))
                {
                    reason = $"area '{cfg.areas[i].id}' overlaps area '{cfg.areas[j].id}'";
                    return false;
                }
            }

            int entryCount = 0;
            foreach (WorldArea a in cfg.areas) if (a.IsEntryRole) entryCount++;
            if (entryCount != 1)
            {
                reason = $"the world config has {entryCount} entry areas — it needs exactly one " +
                         "('An entry stub precedes Area 1', spec §7)";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>True only for a genuine overlap — areas that merely touch along a shared wall
        /// (the normal case for two gated neighbours) are not overlapping.</summary>
        private static bool AreasOverlap(WorldArea a, WorldArea b) =>
            a.XMin < b.XMax - Geo.Epsilon && a.XMax > b.XMin + Geo.Epsilon &&
            a.ZMin < b.ZMax - Geo.Epsilon && a.ZMax > b.ZMin + Geo.Epsilon;

        private static bool WorldGates(WorldConfig cfg, out string reason)
        {
            var seenIds = new HashSet<string>();
            foreach (WorldGate g in cfg.gates)
            {
                if (g == null) { reason = "a gate is null"; return false; }

                if (string.IsNullOrWhiteSpace(g.id))
                { reason = "a gate has no id"; return false; }

                if (!seenIds.Add(g.id))
                { reason = $"two gates share the id '{g.id}'"; return false; }

                if (!ResolveEndpoint(cfg, g.from, out WorldArea fromArea, out Wall fromWall, out reason)) return false;
                if (!ResolveEndpoint(cfg, g.to, out WorldArea toArea, out Wall toWall, out reason)) return false;

                if (toWall != WallEnums.Opposite(fromWall))
                {
                    reason = $"gate '{g.id}' joins '{fromArea.id}'s {fromWall} wall to '{toArea.id}'s {toWall} wall — " +
                             $"they must be opposite walls ({fromWall}↔{WallEnums.Opposite(fromWall)})";
                    return false;
                }

                if (!Geo.Same(fromArea.WallCoord(fromWall), toArea.WallCoord(toWall)))
                {
                    reason = $"gate '{g.id}': '{fromArea.id}'s {fromWall} wall and '{toArea.id}'s {toWall} wall " +
                             "do not sit on the same line — move one area so the walls coincide";
                    return false;
                }

                if (g.width < MinDoorway)
                {
                    reason = $"gate '{g.id}' is {g.width} m wide — under {MinDoorway} m Max and a swarm cannot both fit through";
                    return false;
                }

                Span fromSpan = fromArea.WallSpan(fromWall);
                Span toSpan = toArea.WallSpan(toWall);
                var overlap = new Span(Mathf.Max(fromSpan.Min, toSpan.Min), Mathf.Min(fromSpan.Max, toSpan.Max));

                if (overlap.IsEmpty)
                {
                    reason = $"gate '{g.id}': '{fromArea.id}' and '{toArea.id}' walls do not overlap at all — " +
                             "the opening has nowhere to sit";
                    return false;
                }

                if (overlap.Length < g.width - Geo.Epsilon)
                {
                    reason = $"gate '{g.id}' is {g.width} m wide but its two walls only share {overlap.Length:0.#} m " +
                             "— the opening does not fit";
                    return false;
                }

                float posFrom = fromSpan.Min + Mathf.Clamp01(g.from.pos) * fromSpan.Length;
                float posTo = toSpan.Min + Mathf.Clamp01(g.to.pos) * toSpan.Length;
                float along = (posFrom + posTo) * 0.5f;
                float half = g.width * 0.5f;

                if (along - half < overlap.Min - Geo.Epsilon || along + half > overlap.Max + Geo.Epsilon)
                {
                    reason = $"gate '{g.id}' at {along:0.#} spills past the shared wall [{overlap.Min:0.#}, {overlap.Max:0.#}] " +
                             $"— move its pos closer to the middle or narrow it below {overlap.Length:0.#} m";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        private static bool ResolveEndpoint(WorldConfig cfg, WorldGateEndpoint ep, out WorldArea area, out Wall wall, out string reason)
        {
            area = null; wall = default;

            if (ep == null) { reason = "a gate endpoint is missing"; return false; }

            area = cfg.Area(ep.area);
            if (area == null)
            { reason = $"a gate references area '{ep.area}', which does not exist"; return false; }

            if (!WallEnums.TryParse(ep.wall, out wall))
            { reason = $"gate endpoint on '{ep.area}' has an unknown wall '{ep.wall}' — expected N, E, S or W"; return false; }

            if (ep.pos < 0f || ep.pos > 1f)
            { reason = $"gate endpoint on '{ep.area}' has pos {ep.pos} — must be between 0 and 1"; return false; }

            reason = null;
            return true;
        }

        /// <summary>Every area reachable from the entry stub via the gate graph (spec rule (b)) —
        /// unlike <see cref="Reachable"/> above, which only checks the boss, this checks ALL of them,
        /// because a free-2D layout can strand a side area a straight corridor never could.</summary>
        private static bool WorldReachability(WorldConfig cfg, out string reason)
        {
            WorldArea entry = FindEntry(cfg);

            var reached = new HashSet<string> { entry.id };
            var queue = new Queue<string>();
            queue.Enqueue(entry.id);

            while (queue.Count > 0)
            {
                string here = queue.Dequeue();
                foreach (WorldGate g in cfg.gates)
                {
                    if (g?.from == null || g.to == null) continue;

                    string next = g.from.area == here ? g.to.area
                                : g.to.area == here ? g.from.area
                                : null;

                    if (next != null && reached.Add(next)) queue.Enqueue(next);
                }
            }

            foreach (WorldArea a in cfg.areas)
            {
                if (!reached.Contains(a.id))
                {
                    reason = $"area '{a.id}' is not reachable from the entry stub '{entry.id}' — no chain of gates reaches it";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        /// <summary>The boss area must sit behind its gate — every gate touching it has to require
        /// every shed down first (spec rule (c)). A boss you can walk in on before the sheds fall is
        /// the one bug this framework exists to make structurally impossible, not just balanced away.</summary>
        private static bool WorldBossGate(WorldConfig cfg, out string reason)
        {
            const string requiredCondition = "all-sheds-destroyed";

            foreach (WorldArea a in cfg.areas)
            {
                if (!a.IsBossRole) continue;

                bool hasGate = false;
                foreach (WorldGate g in cfg.gates)
                {
                    bool touches = g?.from != null && g.to != null &&
                                   (g.from.area == a.id || g.to.area == a.id);
                    if (!touches) continue;

                    hasGate = true;
                    if (!string.Equals(g.opensWith, requiredCondition, StringComparison.OrdinalIgnoreCase))
                    {
                        reason = $"boss area '{a.id}' is opened by gate '{g.id}' with opensWith='{g.opensWith}' " +
                                 $"— it must be '{requiredCondition}' so the boss sits behind every shed";
                        return false;
                    }
                }

                if (!hasGate)
                { reason = $"boss area '{a.id}' has no gate at all — it cannot sit behind anything"; return false; }
            }

            reason = null;
            return true;
        }

        private static WorldArea FindEntry(WorldConfig cfg)
        {
            foreach (WorldArea a in cfg.areas) if (a.IsEntryRole) return a;
            return null;
        }
    }
}
