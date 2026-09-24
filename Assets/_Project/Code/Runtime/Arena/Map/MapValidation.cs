using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Refuses a map that would not play. Every rule here is one a playtest would otherwise have to
    /// find: a boss you cannot walk to, a gate with no key, a prop sitting on the spawn ring, a room
    /// so pinched there is nowhere to run. Authoring gets faster only if the feedback is instant, and
    /// this is where "instant" comes from — a bad number fails a test, not a build-and-deploy.
    ///
    /// <paramref name="reason"/> names EVERY breach found in one run, joined "; " (MV-879) — not just
    /// the first. A round trip that fixes one rule only to discover the next was already sitting there
    /// is a round trip the sweep should have collected up front; every rule below appends to a shared
    /// violations list and the sweep moves on to the next candidate rather than stopping.
    /// </summary>
    public static class MapValidation
    {
        /// <summary>How many violations <see cref="Validate"/>/<see cref="ValidateWorldConfig"/> will
        /// name before truncating with an "and N more" tail — a badly broken config can otherwise
        /// produce a wall of text nobody reads.</summary>
        private const int MaxReportedViolations = 50;

        /// <summary>Joins a phase's collected violations into the single <c>reason</c> string every
        /// caller already expects, capped at <see cref="MaxReportedViolations"/>. Null (not empty)
        /// when there is nothing to report, matching the old single-reason convention.</summary>
        private static string Join(List<string> violations)
        {
            if (violations.Count == 0) return null;
            if (violations.Count <= MaxReportedViolations) return string.Join("; ", violations);

            return string.Join("; ", violations.GetRange(0, MaxReportedViolations)) +
                   $"; and {violations.Count - MaxReportedViolations} more";
        }
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

        /// <summary>Closest two sheds authored in the SAME area may sit, centre to centre (MV-475) —
        /// two <see cref="SpawnRadius"/> 3.5 m rings plus <see cref="SpawnClearance"/> 0.8 m each, plus
        /// a lane between them.</summary>
        public const float MinShedSeparation = 11f;

        /// <summary>Closest a shed may sit to its own area's walls (MV-475) — room for its spawn ring
        /// plus clearance without crowding the boundary.</summary>
        public const float MinShedWallMargin = 6f;

        /// <summary>Closest two bosses authored in the SAME area may sit, centre to centre (MV-561) —
        /// room enough for a fight with two Big Bermudas to not have them standing on top of each
        /// other.</summary>
        public const float MinBossSeparation = 10f;

        /// <summary>Closest a boss may sit to its own area's walls (MV-561).</summary>
        public const float MinBossWallMargin = 6f;

        /// <summary>Closest cover may sit to a boss inside a boss arena (MV-565), measured
        /// footprint-to-footprint between the cover's box and the boss's own authored size — not from
        /// the boss's centre point (MV-649). The old centre-point rule was written when a boss was
        /// 3x3 m; MV-621 doubled bosses to 6x6 m, which turned "12 m from centre" into a ~24 m ban that
        /// ate a boss arena's own body along with it. 2 m of clear ground round the boss's edge is
        /// enough that the boss and its telegraphs still read from any approach, while letting a
        /// designer wall the rest of the arena.</summary>
        public const float MinBossCoverClearance = 2f;

        /// <summary>Narrowest gap the player must always have to run through, at any depth of a
        /// room. A readability minimum — twice Max's 1.0 m body width — not a physical-fit check.</summary>
        public const float MinFreeChannel = 2f;

        /// <summary>Narrowest gap the under-route beneath a bridge must keep, at floor level, once its
        /// piers are placed (MV-711) — wider than the ordinary <see cref="MinFreeChannel"/> because the
        /// first pass under a bridge is deliberately a fuller room, not a corridor squeezed by columns.</summary>
        public const float MinBridgeUnderChannel = 6f;

        /// <summary>A bridge pier's own footprint for the under-route channel check (MV-711) — a single
        /// support column, not a wide obstacle.</summary>
        public const float BridgePierSize = 1f;

        public static bool Validate(MapData map, out string reason)
        {
            if (map == null) { reason = "the map is null"; return false; }

            var violations = new List<string>();
            Structure(map, violations);
            Links(map, violations);
            Actors(map, violations);
            Reachable(map, violations);
            Cover(map, violations);

            reason = Join(violations);
            return violations.Count == 0;
        }

        private static void Structure(MapData map, List<string> violations)
        {
            if (map.zones == null || map.zones.Length == 0)
            {
                violations.Add("the map has no zones — there is nothing to stand in");
                return; // nothing further to sweep without zones
            }

            if (map.wallHeight < MinWallHeight)
                violations.Add($"wallHeight {map.wallHeight} is too short to read as a wall (min {MinWallHeight})");

            if (map.wallThickness <= 0f)
                violations.Add("wallThickness must be positive");

            var seen = new HashSet<string>();
            foreach (MapZone z in map.zones)
            {
                if (z == null) { violations.Add("a zone is null"); continue; }

                if (string.IsNullOrWhiteSpace(z.id))
                { violations.Add("a zone has no id — links refer to zones by id"); continue; }

                if (!seen.Add(z.id))
                    violations.Add($"two zones share the id '{z.id}'");

                if (z.width <= 0f || z.depth <= 0f)
                { violations.Add($"zone '{z.id}' has no area ({z.width}×{z.depth})"); continue; }

                // A room you are meant to fight in has to be one you can circle in (the lesson of the
                // 9 m corridor that read as a path and played as a treadmill).
                bool isFightRoom = z.Kind == ZoneKind.Open || z.Kind == ZoneKind.Dense || z.Kind == ZoneKind.Boss;
                if (isFightRoom && Mathf.Min(z.width, z.depth) < MinFightRoomWidth)
                {
                    violations.Add($"zone '{z.id}' is a {z.type} room but only {Mathf.Min(z.width, z.depth):0.#} m " +
                             $"across — under {MinFightRoomWidth} m there is no room to circle-strafe");
                }
            }
        }

        private static void Links(MapData map, List<string> violations)
        {
            if (map.links != null)
            {
                foreach (MapLink link in map.links)
                {
                    if (link == null) { violations.Add("a link is null"); continue; }

                    bool fromMissing = map.Zone(link.from) == null;
                    bool toMissing = map.Zone(link.to) == null;
                    if (fromMissing)
                        violations.Add($"link references zone '{link.from}', which does not exist");
                    if (toMissing)
                        violations.Add($"link references zone '{link.to}', which does not exist");
                    if (fromMissing || toMissing) continue;

                    if (!MapGeometry.Doorway(map, link, out _, out _, out Span hole))
                    {
                        violations.Add($"zones '{link.from}' and '{link.to}' are linked but do not share an edge — " +
                                 "move one so they touch, or the doorway cuts nothing");
                        continue;
                    }

                    if (hole.Length < MinDoorway - Geo.Epsilon)
                    {
                        violations.Add($"the doorway between '{link.from}' and '{link.to}' is {hole.Length:0.#} m — " +
                                 $"under {MinDoorway} m Max and the swarm cannot both get through");
                    }

                    if (!string.IsNullOrEmpty(link.gate) && map.Entity(link.gate) == null)
                        violations.Add($"link '{link.from}'→'{link.to}' names gate '{link.gate}', which does not exist");
                }
            }

            // A gate that fills no doorway is a slab standing in a field. An area gate (WV-222) is the
            // same claim about a different entity kind — it seals a doorway exactly like the
            // scene-adopted Gate does, it just opens on its own HP instead of a factory's death.
            foreach (MapEntity e in Kind(map, EntityKind.Gate))
                if (!FillsADoorway(map, e.id))
                    violations.Add($"gate '{e.id}' does not fill any doorway — no link names it");

            foreach (MapEntity e in Kind(map, EntityKind.AreaGate))
                if (!FillsADoorway(map, e.id))
                    violations.Add($"area gate '{e.id}' does not fill any doorway — no link names it");
        }

        private static bool FillsADoorway(MapData map, string entityId)
        {
            if (map.links != null)
                foreach (MapLink link in map.links)
                    if (link != null && link.gate == entityId) return true;
            return false;
        }

        private static void Actors(MapData map, List<string> violations)
        {
            if (map.entities != null)
            {
                foreach (MapEntity e in map.entities)
                {
                    if (e == null) { violations.Add("an entity is null"); continue; }

                    if (string.IsNullOrWhiteSpace(e.id))
                    { violations.Add($"a {e.kind} entity has no id"); continue; }

                    if (e.Kind == EntityKind.Unknown)
                    { violations.Add($"entity '{e.id}' has unknown kind '{e.kind}'"); continue; }

                    // A gate stands ON a wall line, so it is legitimately outside every room — true of
                    // an area gate (WV-222) exactly as it is the scene-adopted one. Anything else
                    // authored outside a room is standing in the void.
                    if (e.Kind != EntityKind.Gate && e.Kind != EntityKind.AreaGate && map.ZoneAt(e.x, e.z) == null)
                        violations.Add($"'{e.id}' is at ({e.x}, {e.z}), which is not inside any zone");
                }
            }

            var spawns = Kind(map, EntityKind.PlayerSpawn);
            if (spawns.Count != 1)
                violations.Add($"the map has {spawns.Count} player spawns — it needs exactly one");

            foreach (MapEntity gate in Kind(map, EntityKind.Gate))
            {
                string[] keys = gate.Keys;
                if (keys.Length == 0)
                {
                    violations.Add($"gate '{gate.id}' has no opensOn — a locked door with no key is a dead end");
                    continue;
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
                        violations.Add($"gate '{gate.id}' opens on '{id}', which is not a factory in this map");
                        continue;
                    }

                    if (!named.Add(id))
                        violations.Add($"gate '{gate.id}' names factory '{id}' twice");
                }
            }

            foreach (MapEntity boss in Kind(map, EntityKind.Boss))
            {
                MapZone zone = map.ZoneAt(boss.x, boss.z);
                if (zone != null && zone.Kind != ZoneKind.Boss)
                    violations.Add($"boss '{boss.id}' stands in '{zone.id}', which is not a boss zone");
            }
        }

        /// <summary>Can Max actually walk from where he spawns to the boss? Gates do not block this —
        /// they open. A map that fails here is one where the run cannot be finished, which is the one
        /// bug a layout must never ship with.</summary>
        private static void Reachable(MapData map, List<string> violations)
        {
            MapEntity spawn = map.First(EntityKind.PlayerSpawn);
            MapZone from = spawn == null ? null : map.ZoneAt(spawn.x, spawn.z);
            if (from == null) { violations.Add("the player spawn is not inside a zone"); return; }

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
                    violations.Add($"the boss zone '{z.id}' cannot be walked to from the player spawn — " +
                             "no chain of links reaches it");
                }
            }
        }

        private static void Cover(MapData map, List<string> violations)
        {
            List<MapEntity> cover = Kind(map, EntityKind.Cover);
            List<MapEntity> factories = Kind(map, EntityKind.Factory);
            // MV-860: a Replicator no longer shares the shed's blanket all-round spawn ring — Lee's
            // World 2 v3 lanes pack pipe barriers close enough on the sides that the old 4.3 m circle
            // could never hold. It keeps its own directional IN-lane/OUT-pad check instead, below.
            List<MapEntity> replicators = Kind(map, EntityKind.Replicator);
            List<MapEntity> bosses = Kind(map, EntityKind.Boss);

            for (int i = 0; i < cover.Count; i++)
            {
                MapEntity c = cover[i];
                ArenaCover body = c.ToCover();

                if (c.height < 1f)
                    violations.Add($"'{c.id}' is {c.height} m tall — too short to break a chase (min 1 m)");

                MapZone zone = map.ZoneAt(c.x, c.z);
                if (zone != null && zone.Kind == ZoneKind.Boss)
                {
                    List<MapEntity> zoneBosses = bosses.FindAll(b => map.ZoneAt(b.x, b.z) == zone);
                    if (zoneBosses.Count == 0)
                    {
                        violations.Add($"'{c.id}' is cover in the boss arena '{zone.id}' — the boss fight is " +
                                 "readability-first and stays open");
                    }
                    else
                    {
                        foreach (MapEntity boss in zoneBosses)
                        {
                            float dist = body.DistanceTo(boss.ToCover());
                            if (dist < MinBossCoverClearance)
                            {
                                violations.Add($"'{c.id}' is {dist:0.#} m from boss '{boss.id}' — a boss arena needs " +
                                         $"{MinBossCoverClearance:0.#} m of clear ground round every boss");
                            }
                        }
                    }
                }

                foreach (MapEntity f in factories)
                {
                    if (body.DistanceTo(f.CenterXz) < SpawnRadius + SpawnClearance)
                        violations.Add($"'{c.id}' crowds '{f.id}'s spawn ring — robots would spawn inside it");
                }

                // MV-860: a Replicator's own two rects — the IN-face lane a lured robot actually walks,
                // and the OUT-face pad its twins emit onto — replace the blanket ring above for this
                // kind. Solid cover or a move-blocking pipe barrier (built as an ordinary Cover entity,
                // see WorldReplicator.facing's own doc comment) is not allowed in either.
                foreach (MapEntity r in replicators)
                {
                    if (body.Footprint.Overlaps(ReplicatorInLane(r)))
                    {
                        violations.Add($"'{c.id}' blocks '{r.id}'s IN lane — the queue that walks to its hatch " +
                                 $"needs a clear {ReplicatorLaneWidth:0.#} x {ReplicatorLaneDepth:0.#} m lane in front of it");
                    }
                    if (body.Footprint.Overlaps(ReplicatorOutPad(r)))
                    {
                        violations.Add($"'{c.id}' blocks '{r.id}'s OUT pad — its twins emit there and need a " +
                                 $"clear {ReplicatorPadWidth:0.#} x {ReplicatorPadDepth:0.#} m pad in front of it");
                    }
                }

                for (int j = i + 1; j < cover.Count; j++)
                {
                    if (body.Overlaps(cover[j].ToCover()))
                        violations.Add($"'{c.id}' overlaps '{cover[j].id}'");
                }
            }

            // Sweep every room: at no depth may cover pinch it shut. The player must always have
            // somewhere to run. One reported violation per zone (not one per 0.5 m step) — the same
            // pinch would otherwise repeat dozens of times and flood the report.
            foreach (MapZone z in map.zones)
            {
                if (z == null || z.width < MinFreeChannel) continue;

                for (float depth = z.ZMin; depth <= z.ZMax; depth += 0.5f)
                {
                    float widest = FreeChannelAt(z, cover, depth);
                    if (widest < MinFreeChannel - 1e-3f)
                    {
                        violations.Add($"cover pinches '{z.id}' shut at z={depth:0.#} " +
                                 $"(widest crossing {widest:0.#} m, floor {MinFreeChannel:0.#} m)");
                        break;
                    }
                }
            }
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

        /// <summary>MV-860: how deep (away from the box) and wide a Replicator's IN-face lane must stay
        /// clear — the queue a lured robot actually walks in on. Replaces the old blanket
        /// <see cref="SpawnRadius"/>+<see cref="SpawnClearance"/> ring for this one entity kind, since
        /// its lure/twin geometry is direction-specific (see <see cref="MaxWorlds.Factories.Replicator"/>).</summary>
        public const float ReplicatorLaneDepth = 3f;
        public const float ReplicatorLaneWidth = 2f;

        /// <summary>MV-860: same idea as the lane above, but for the OUT face — twins emit right there,
        /// not down a walked queue, so it only needs a short pad, not a lane.</summary>
        public const float ReplicatorPadDepth = 2f;
        public const float ReplicatorPadWidth = 2f;

        /// <summary>The world-XZ unit vector a Replicator's <see cref="MapEntity.facing"/> names — the
        /// same N=+Z/E=+X/S=-Z/W=-X compass <see cref="MapRuntime"/>'s own deck-wall code already uses.
        /// Unrecognised/empty falls back to "S", same default <see cref="WorldReplicator.facing"/> and
        /// <see cref="MaxWorlds.Factories.Replicator.SetFacing"/> use.</summary>
        private static Vector2 FacingDirection(string facing) => facing switch
        {
            "N" => new Vector2(0f, 1f),
            "E" => new Vector2(1f, 0f),
            "W" => new Vector2(-1f, 0f),
            _ => new Vector2(0f, -1f),
        };

        /// <summary>A rect starting at the box face <paramref name="boxHalfExtent"/> out from
        /// <paramref name="boxCenter"/> along <paramref name="dir"/>, running <paramref name="depth"/>
        /// further out, <paramref name="width"/> wide across the face — <paramref name="dir"/> is always
        /// axis-aligned (N/E/S/W only, no diagonal facings), so this is always an axis-aligned rect.</summary>
        private static Rect DirectionalRect(Vector2 boxCenter, float boxHalfExtent, Vector2 dir, float depth, float width)
        {
            Vector2 faceCenter = boxCenter + dir * boxHalfExtent;
            Vector2 farCenter = faceCenter + dir * depth;
            bool alongZ = Mathf.Abs(dir.y) > 0.5f; // N/S face: depth runs along Z, width across X
            return alongZ
                ? new Rect(boxCenter.x - width * 0.5f, Mathf.Min(faceCenter.y, farCenter.y), width, depth)
                : new Rect(Mathf.Min(faceCenter.x, farCenter.x), boxCenter.y - width * 0.5f, depth, width);
        }

        /// <summary>MV-860: the lane in front of Replicator <paramref name="r"/>'s own IN face.</summary>
        private static Rect ReplicatorInLane(MapEntity r) =>
            DirectionalRect(r.CenterXz, r.width * 0.5f, FacingDirection(r.facing), ReplicatorLaneDepth, ReplicatorLaneWidth);

        /// <summary>MV-860: the pad in front of Replicator <paramref name="r"/>'s own OUT face — the
        /// opposite side to the IN face.</summary>
        private static Rect ReplicatorOutPad(MapEntity r) =>
            DirectionalRect(r.CenterXz, r.width * 0.5f, -FacingDirection(r.facing), ReplicatorPadDepth, ReplicatorPadWidth);

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

        /// <summary>Closest an authored garrison entry (MV-559) may sit to a piece of cover — the
        /// entry's own collider radius plus a 0.1 m margin (MV-655): enough to refuse a robot authored
        /// inside a hedge, while letting one stand in a gap it physically fits through. A flat 1 m
        /// (pre-MV-655) was roughly double the widest body (the Brute's 0.6 m radius) and rejected ANY
        /// robot the designer drew against cover, because a cell centre on the 1 m design grid sits
        /// exactly 0.5 m from an adjacent hedge face.</summary>
        private static float MinGarrisonCoverGap(EnemyKind kind) => EnemyArchetype.Of(kind).ColliderRadius + 0.1f;

        /// <summary>Clearance a non-robot placement (<see cref="WorldMapLoader.IsClearForCache"/>'s
        /// parts-cache pickup) must keep from cover — unrelated to any specific robot's collider, so
        /// unlike <see cref="MinGarrisonCoverGap"/> it keeps the original flat 1 m rather than following
        /// MV-655's per-robot gap.</summary>
        public const float MinPickupCoverGap = 1f;

        public static bool ValidateWorldConfig(WorldConfig cfg, out string reason)
        {
            if (cfg == null) { reason = "the world config is null"; return false; }

            var violations = new List<string>();
            WorldAreas(cfg, violations);
            WorldSheds(cfg, violations);
            WorldBosses(cfg, violations);
            WorldGarrison(cfg, violations);
            WorldLurkerGrates(cfg, violations);
            WorldGates(cfg, violations);
            WorldReachability(cfg, violations);
            WorldVerticality(cfg, violations);
            WorldBridges(cfg, violations);

            reason = Join(violations);
            return violations.Count == 0;
        }

        /// <summary>World-level bridges (MV-711): every violation across every bridge is collected and
        /// reported together — the same collector shape every other rule in this file now uses
        /// (MV-879).</summary>
        private static void WorldBridges(WorldConfig cfg, List<string> violations)
        {
            var deckRects = new List<Rect>();
            foreach (WorldArea a in cfg.areas)
                foreach (WorldDeck d in a.decks ?? Array.Empty<WorldDeck>())
                    if (d != null) deckRects.Add(a.WorldRectOf(d.x, d.z, d.w, d.d));

            var bridgeRects = new List<(string id, Rect rect)>();
            var seenIds = new HashSet<string>();

            foreach (WorldBridge b in cfg.bridges ?? Array.Empty<WorldBridge>())
            {
                if (b == null) { violations.Add("a bridge is null"); continue; }
                if (string.IsNullOrWhiteSpace(b.id)) { violations.Add("a bridge has no id"); continue; }
                if (!seenIds.Add(b.id)) { violations.Add($"two bridges share the id '{b.id}'"); continue; }

                if (b.width < MinDoorway)
                    violations.Add($"bridge '{b.id}' is {b.width} m wide — under {MinDoorway} m Max and a swarm cannot both fit across");

                if (!b.TryResolveFootprint(cfg, out Rect rect, out string endReason))
                {
                    violations.Add(endReason);
                    continue; // no resolved footprint to check overlap/piers against
                }

                bridgeRects.Add((b.id, rect));

                foreach (Rect deckRect in deckRects)
                    if (rect.Overlaps(deckRect))
                        violations.Add($"bridge '{b.id}' overlaps a deck");

                foreach (WorldBridgePier pier in b.piers ?? Array.Empty<WorldBridgePier>())
                {
                    if (pier == null) { violations.Add($"bridge '{b.id}' has a null pier"); continue; }

                    WorldArea under = AreaAt(cfg, pier.x, pier.z);
                    if (under == null) continue; // nothing under this pier to pinch

                    var blockers = new List<(float x, float z, float size)> { (pier.x, pier.z, BridgePierSize) };
                    foreach (WorldCover c in under.cover ?? Array.Empty<WorldCover>())
                        if (c != null) blockers.Add((c.x, c.z, Mathf.Max(c.width, c.depth)));
                    foreach (WorldBridge other in cfg.bridges)
                    {
                        if (other == null || other == b) continue;
                        foreach (WorldBridgePier op in other.piers ?? Array.Empty<WorldBridgePier>())
                            if (op != null && AreaAt(cfg, op.x, op.z) == under)
                                blockers.Add((op.x, op.z, BridgePierSize));
                    }

                    float channel = UnderChannelAt(under, blockers, pier.z);
                    if (channel < MinBridgeUnderChannel)
                    {
                        violations.Add($"bridge '{b.id}' pier at ({pier.x:0.#}, {pier.z:0.#}) leaves only " +
                                       $"{channel:0.#} m clear under '{under.id}' — under {MinBridgeUnderChannel} m " +
                                       "the under-route pinches shut");
                    }
                }
            }

            for (int i = 0; i < bridgeRects.Count; i++)
            for (int j = i + 1; j < bridgeRects.Count; j++)
                if (bridgeRects[i].rect.Overlaps(bridgeRects[j].rect))
                    violations.Add($"bridge '{bridgeRects[i].id}' overlaps bridge '{bridgeRects[j].id}'");
        }

        private static WorldArea AreaAt(WorldConfig cfg, float x, float z)
        {
            foreach (WorldArea a in cfg.areas)
                if (a != null && a.Footprint.Contains(new Vector2(x, z))) return a;
            return null;
        }

        /// <summary>Widest continuous gap along X at depth <paramref name="z"/> inside <paramref name="area"/>,
        /// once <paramref name="blockers"/> (a bridge's own piers plus the area's authored cover) are
        /// placed — the pre-conversion, World-config-space counterpart of <see cref="FreeChannelAt"/>,
        /// needed here because bridge validation runs before a <see cref="MapData"/> exists to sweep.</summary>
        private static float UnderChannelAt(WorldArea area, List<(float x, float z, float size)> blockers, float z)
        {
            float min = area.XMin, max = area.XMax;

            var blocked = new List<Span>();
            foreach (var (bx, bz, size) in blockers)
            {
                float half = size * 0.5f;
                if (z < bz - half || z > bz + half) continue;
                blocked.Add(new Span(Mathf.Max(bx - half, min), Mathf.Min(bx + half, max)));
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

        /// <summary>Sludge/deck/ramp rects (MV-692): (a) every rect must lie inside its own area's
        /// floor; (b) a deck may not overlap floor-level cover/a shed/a boss that stands as tall as or
        /// taller than the deck itself — an overlap with something SHORTER than the deck is exactly the
        /// "deck OVER cover" case the design wants, so it is not an error; and a ramp must touch exactly
        /// one deck rect edge, since that adjacency is how <see cref="WorldMapLoader"/> orients it.
        /// A deck with no ramp reaching it is deliberately NOT checked here — until deck gates ship
        /// (MV-697) that is a validation WARNING (logged by <see cref="WorldMapLoader"/> at load time),
        /// not a reason to refuse the whole config.</summary>
        private static void WorldVerticality(WorldConfig cfg, List<string> violations)
        {
            foreach (WorldArea a in cfg.areas)
            {
                var deckRects = new List<Rect>();

                foreach (WorldDeck deck in a.decks ?? Array.Empty<WorldDeck>())
                {
                    if (deck == null) { violations.Add($"area '{a.id}' has a null deck"); continue; }

                    Rect rect = a.WorldRectOf(deck.x, deck.z, deck.w, deck.d);
                    if (!RectInsideArea(rect, a))
                    { violations.Add($"area '{a.id}': deck '{deck.id}' rect falls outside the area floor"); continue; }

                    deckRects.Add(rect);
                    float deckHeight = deck.height > 0f ? deck.height : (cfg.dials?.deckHeight ?? 2.5f);

                    foreach (WorldCover c in a.cover)
                    {
                        if (c == null) continue;
                        var coverRect = new Rect(c.x - c.width * 0.5f, c.z - c.depth * 0.5f, c.width, c.depth);
                        if (rect.Overlaps(coverRect) && c.height >= deckHeight)
                        {
                            violations.Add($"area '{a.id}': deck '{deck.id}' (height {deckHeight:0.#} m) overlaps " +
                                     $"cover '{c.id}', which stands {c.height:0.#} m tall — too tall to sit under it");
                        }
                    }

                    foreach (WorldShed s in a.Sheds())
                    {
                        var shedRect = new Rect(s.x - WorldMapLoader.ShedFootprint * 0.5f,
                            s.z - WorldMapLoader.ShedFootprint * 0.5f, WorldMapLoader.ShedFootprint, WorldMapLoader.ShedFootprint);
                        if (rect.Overlaps(shedRect) && WorldMapLoader.ShedHeight >= deckHeight)
                        {
                            violations.Add($"area '{a.id}': deck '{deck.id}' (height {deckHeight:0.#} m) overlaps a shed " +
                                     $"— too tall to sit under it");
                        }
                    }

                    foreach (WorldBoss b in a.Bosses())
                    {
                        float bw = b.size?.w ?? 3.5f, bd = b.size?.d ?? 3.5f;
                        var bossRect = new Rect(b.x - bw * 0.5f, b.z - bd * 0.5f, bw, bd);
                        if (rect.Overlaps(bossRect) && WorldMapLoader.BossHeight >= deckHeight)
                        {
                            violations.Add($"area '{a.id}': deck '{deck.id}' (height {deckHeight:0.#} m) overlaps boss " +
                                     $"'{b.id}' — too tall to sit under it");
                        }
                    }
                }

                foreach (WorldSludge s in a.sludge ?? Array.Empty<WorldSludge>())
                {
                    if (s == null) { violations.Add($"area '{a.id}' has a null sludge rect"); continue; }
                    Rect rect = a.WorldRectOf(s.x, s.z, s.w, s.d);
                    if (!RectInsideArea(rect, a))
                        violations.Add($"area '{a.id}': sludge '{s.id}' rect falls outside the area floor");
                }

                var rampRects = new List<(string id, Rect rect)>();
                foreach (WorldRamp r in a.ramps ?? Array.Empty<WorldRamp>())
                {
                    if (r == null) { violations.Add($"area '{a.id}' has a null ramp"); continue; }
                    Rect rect = a.WorldRectOf(r.x, r.z, r.w, r.d);
                    if (!RectInsideArea(rect, a))
                    { violations.Add($"area '{a.id}': ramp '{r.id}' rect falls outside the area floor"); continue; }

                    int touches = 0;
                    foreach (Rect deckRect in deckRects)
                        if (TouchesEdge(rect, deckRect)) touches++;

                    if (touches != 1)
                    {
                        violations.Add($"area '{a.id}': ramp '{r.id}' must touch exactly one deck — touches {touches}");
                        continue;
                    }

                    rampRects.Add((r.id, rect));
                }

                // MV-697: a hatch is a locked opening ON a deck cell, not free-floating geometry — it
                // must sit inside its own area's floor AND actually overlap one of that area's decks,
                // or there is no deck for WorldMapLoader to resolve its built height from.
                foreach (WorldHatch h in a.hatches ?? Array.Empty<WorldHatch>())
                {
                    if (h == null) { violations.Add($"area '{a.id}' has a null hatch"); continue; }

                    Rect rect = a.WorldRectOf(h.x, h.z, h.w, h.d);
                    if (!RectInsideArea(rect, a))
                    { violations.Add($"area '{a.id}': hatch '{h.id}' rect falls outside the area floor"); continue; }

                    bool onDeck = false;
                    foreach (Rect deckRect in deckRects)
                        if (deckRect.Overlaps(rect)) { onDeck = true; break; }

                    if (!onDeck)
                    { violations.Add($"area '{a.id}': hatch '{h.id}' does not sit on any of the area's deck cells"); continue; }

                    // MV-829: overlapping SOME deck cell is not enough — a3_hatch2 shipped on its
                    // deck's west edge while a3_ramp2 climbed to the east edge, so the flat panel sat
                    // over the wrong side and never blocked the actual approach. A hatch guards a given
                    // ramp only if it (a) actually reaches the same deck-boundary line the ramp touches
                    // — not merely overlapping the deck somewhere else on it, which is all a3_hatch2's
                    // bug ever did — and (b) its span along that boundary overlaps the ramp's own span,
                    // so it blocks the ramp's actual mouth rather than some unrelated stretch of the
                    // same edge. Deliberately NOT "hatch touches exactly one edge": a6/a11's hatches
                    // span their deck's full (3 m) width, so they legitimately touch BOTH side edges at
                    // once — only the edge-reach + span-overlap pair distinguishes a real guard from a
                    // hatch parked on the wrong side.
                    bool sitsAtRampEdge = false;
                    var nearbyRamps = new List<string>();
                    foreach (Rect deckRect in deckRects)
                    {
                        if (!deckRect.Overlaps(rect)) continue;

                        foreach (var (rampId, rampRect) in rampRects)
                        {
                            if (!TryOuterEdge(rampRect, deckRect, out EdgeSide rampSide)) continue;
                            nearbyRamps.Add(rampId);

                            bool guardsThisRamp = rampSide switch
                            {
                                EdgeSide.West => Geo.Same(rect.xMin, deckRect.xMin) &&
                                                  RangesOverlap(rect.yMin, rect.yMax, rampRect.yMin, rampRect.yMax),
                                EdgeSide.East => Geo.Same(rect.xMax, deckRect.xMax) &&
                                                  RangesOverlap(rect.yMin, rect.yMax, rampRect.yMin, rampRect.yMax),
                                EdgeSide.South => Geo.Same(rect.yMin, deckRect.yMin) &&
                                                   RangesOverlap(rect.xMin, rect.xMax, rampRect.xMin, rampRect.xMax),
                                EdgeSide.North => Geo.Same(rect.yMax, deckRect.yMax) &&
                                                   RangesOverlap(rect.xMin, rect.xMax, rampRect.xMin, rampRect.xMax),
                                _ => false,
                            };
                            if (guardsThisRamp) sitsAtRampEdge = true;
                        }
                    }

                    if (!sitsAtRampEdge)
                    {
                        string ramps = nearbyRamps.Count > 0 ? string.Join(", ", nearbyRamps) : "no ramp reaching its deck";
                        violations.Add($"area '{a.id}': hatch '{h.id}' sits on the wrong edge of its deck — " +
                                 $"it must sit where ramp {ramps} arrives, not the opposite side");
                        continue;
                    }

                    if (!ValidateOpensWith(cfg, "hatch", h.id, h.opensWith, out string opensReason))
                        violations.Add(opensReason);
                }
            }
        }

        private enum EdgeSide { North, South, East, West }

        /// <summary>Which edge of <paramref name="deck"/> does <paramref name="rect"/> touch from
        /// OUTSIDE it (a ramp arriving at a deck) — mirrors <see cref="TouchesEdge"/>'s own adjacency
        /// test but names WHICH of the four edges matched, since <see cref="WorldVerticality"/>'s new
        /// hatch rule (MV-829) needs to check a hatch reaches that SAME boundary line.</summary>
        private static bool TryOuterEdge(Rect rect, Rect deck, out EdgeSide side)
        {
            if (Geo.Same(rect.xMax, deck.xMin) && RangesOverlap(rect.yMin, rect.yMax, deck.yMin, deck.yMax))
            { side = EdgeSide.West; return true; }
            if (Geo.Same(rect.xMin, deck.xMax) && RangesOverlap(rect.yMin, rect.yMax, deck.yMin, deck.yMax))
            { side = EdgeSide.East; return true; }
            if (Geo.Same(rect.yMax, deck.yMin) && RangesOverlap(rect.xMin, rect.xMax, deck.xMin, deck.xMax))
            { side = EdgeSide.South; return true; }
            if (Geo.Same(rect.yMin, deck.yMax) && RangesOverlap(rect.xMin, rect.xMax, deck.xMin, deck.xMax))
            { side = EdgeSide.North; return true; }
            side = default;
            return false;
        }

        private static bool RectInsideArea(Rect rect, WorldArea a) =>
            rect.xMin >= a.XMin - Geo.Epsilon && rect.xMax <= a.XMax + Geo.Epsilon &&
            rect.yMin >= a.ZMin - Geo.Epsilon && rect.yMax <= a.ZMax + Geo.Epsilon;

        /// <summary>True if the two rects share a full run along exactly one edge — the adjacency
        /// <see cref="WorldMapLoader"/> needs to orient a ramp onto its deck. Mirrors
        /// <see cref="MapGeometry.Doorway"/>'s own "same coordinate, overlapping span" test.</summary>
        private static bool TouchesEdge(Rect a, Rect b)
        {
            bool northSouth = (Geo.Same(a.yMax, b.yMin) || Geo.Same(a.yMin, b.yMax)) &&
                               RangesOverlap(a.xMin, a.xMax, b.xMin, b.xMax);
            bool eastWest = (Geo.Same(a.xMax, b.xMin) || Geo.Same(a.xMin, b.xMax)) &&
                             RangesOverlap(a.yMin, a.yMax, b.yMin, b.yMax);
            return northSouth || eastWest;
        }

        private static bool RangesOverlap(float minA, float maxA, float minB, float maxB) =>
            minA < maxB - Geo.Epsilon && maxA > minB + Geo.Epsilon;

        /// <summary>Every boss an area carries (MV-561, <see cref="WorldArea.Bosses"/>) must sit clear of
        /// its own area's walls, and two bosses in the same area must sit clear of each other — same
        /// reasoning as <see cref="WorldSheds"/>, one area over.
        ///
        /// Only applies once an area actually carries MORE THAN ONE boss. A legacy single-boss area
        /// "behaves exactly as it does today" (AC3) — world1's compost clearing predates this rule and
        /// was never authored against it, so gating on count is what keeps it valid rather than
        /// retroactively breaking already-shipped content.</summary>
        private static void WorldBosses(WorldConfig cfg, List<string> violations)
        {
            foreach (WorldArea a in cfg.areas)
            {
                WorldBoss[] bosses = a.Bosses();
                if (bosses.Length <= 1) continue;

                foreach (WorldBoss b in bosses)
                {
                    if (b.x - a.XMin < MinBossWallMargin || a.XMax - b.x < MinBossWallMargin ||
                        b.z - a.ZMin < MinBossWallMargin || a.ZMax - b.z < MinBossWallMargin)
                    {
                        violations.Add($"a boss in area '{a.id}' at ({b.x:0.#}, {b.z:0.#}) is within " +
                                 $"{MinBossWallMargin} m of its area's walls");
                    }
                }

                for (int i = 0; i < bosses.Length; i++)
                for (int j = i + 1; j < bosses.Length; j++)
                {
                    float dist = Vector2.Distance(new Vector2(bosses[i].x, bosses[i].z), new Vector2(bosses[j].x, bosses[j].z));
                    if (dist < MinBossSeparation)
                    {
                        violations.Add($"area '{a.id}' has two bosses {dist:0.#} m apart — under the " +
                                 $"{MinBossSeparation} m minimum boss separation");
                    }
                }
            }
        }

        /// <summary>Every authored garrison entry (MV-559, <see cref="WorldArea.garrison"/>) must sit
        /// inside its own area, clear of cover and clear of every shed — the same "nowhere for a robot
        /// to spawn on top of something" guarantee <see cref="WorldSheds"/> gives a shed's own
        /// neighbours, extended to a designer's own placed robots — and an area must not author more of
        /// a kind than its solved composition actually has, or a garrison slot would have nothing to
        /// draw from.</summary>
        private static void WorldGarrison(WorldConfig cfg, List<string> violations)
        {
            foreach (WorldArea a in cfg.areas)
            {
                WorldGarrisonEntry[] garrison = a.garrison;
                if (garrison == null || garrison.Length == 0) continue;

                var countByKind = new Dictionary<EnemyKind, int>();

                foreach (WorldGarrisonEntry entry in garrison)
                {
                    if (entry == null) { violations.Add($"area '{a.id}' has a null garrison entry"); continue; }

                    var point = new Vector2(entry.x, entry.z);

                    if (!a.Footprint.Contains(point))
                    {
                        violations.Add($"area '{a.id}': garrison entry ({entry.x:0.#}, {entry.z:0.#}) is outside the area");
                        continue;
                    }

                    if (!EnemyKindNames.TryParse(entry.kind, out EnemyKind kind))
                    {
                        violations.Add($"area '{a.id}': garrison entry has an unrecognised kind '{entry.kind}'");
                        continue;
                    }

                    float requiredGap = MinGarrisonCoverGap(kind);

                    // MV-927: a level-1 entry stands on a deck, not the floor — its own resolved
                    // elevation (same lookup a seeded robot's spawn Y uses) is the height to clear
                    // cover from, not 0. A deck-standing robot cannot physically overlap a cover box
                    // that tops out below the deck surface, so XZ-only distance can't tell "same
                    // footprint, different height" from "actually touching"; skip the clearance check
                    // per-cover once the cover's own top height is below the entry's resolved elevation.
                    float entryElevation = entry.level > 0 ? Garrison.ResolveLevelHeight(a, cfg, point) : 0f;

                    foreach (WorldCover c in a.cover)
                    {
                        if (c == null) continue;
                        if (entryElevation > 0f && c.height < entryElevation) continue;

                        ArenaCover body = new MapEntity
                        {
                            x = c.x, z = c.z, width = c.width, height = c.height, depth = c.depth, shape = c.shape,
                        }.ToCover();

                        float gap = body.DistanceTo(point);
                        if (gap < requiredGap)
                        {
                            violations.Add($"area '{a.id}': garrison entry ({entry.x:0.#}, {entry.z:0.#}) is {gap:0.#} m " +
                                     $"from cover '{c.id}' — a {kind} needs {requiredGap:0.#} m clearance");
                        }
                    }

                    foreach (WorldShed s in a.Sheds())
                    {
                        float toShed = Vector2.Distance(point, new Vector2(s.x, s.z));
                        if (toShed < SpawnRadius + SpawnClearance)
                        {
                            violations.Add($"area '{a.id}': garrison entry ({entry.x:0.#}, {entry.z:0.#}) is within " +
                                     $"{SpawnRadius + SpawnClearance:0.#} m of a shed");
                        }
                    }

                    countByKind[kind] = countByKind.TryGetValue(kind, out int existing) ? existing + 1 : 1;
                }

                DifficultyEngine.Composition solved = cfg.SolveComposition(a.index);
                foreach (KeyValuePair<EnemyKind, int> kv in countByKind)
                {
                    int authoredCount = CompositionCount(solved, kv.Key);
                    if (kv.Value > authoredCount)
                        violations.Add($"area '{a.id}': garrison authors {kv.Value} {kv.Key}(s) but composition only has {authoredCount}");
                }
            }
        }

        /// <summary>Every garrisoned Grate Lurker (MV-688, AC2; containment rule MV-724) must sit
        /// somewhere on one of its own area's authored <see cref="WorldArea.grates"/> 1x1 tiles — the
        /// grate IS the Lurker's visible body while submerged, so a Lurker authored off one has no body
        /// to stand in for it at all. A grate is authored at its integer corner, but a robot standing on
        /// that tile actually occupies the whole 1x1 square, so anywhere within it (inclusive of the
        /// edges) counts, not just the corner itself. Names the area and the entry's index in the
        /// failure reason, per the ticket's own AC2 wording.</summary>
        private static void WorldLurkerGrates(WorldConfig cfg, List<string> violations)
        {
            foreach (WorldArea a in cfg.areas)
            {
                WorldGarrisonEntry[] garrison = a.garrison;
                if (garrison == null || garrison.Length == 0) continue;

                for (int i = 0; i < garrison.Length; i++)
                {
                    WorldGarrisonEntry entry = garrison[i];
                    if (entry == null) continue;
                    if (!EnemyKindNames.TryParse(entry.kind, out EnemyKind kind) || kind != EnemyKind.Lurker) continue;

                    bool onGrate = false;
                    foreach (WorldGrate grate in a.grates ?? Array.Empty<WorldGrate>())
                    {
                        if (grate == null) continue;
                        bool inX = entry.x >= grate.x - Geo.Epsilon && entry.x <= grate.x + 1f + Geo.Epsilon;
                        bool inZ = entry.z >= grate.z - Geo.Epsilon && entry.z <= grate.z + 1f + Geo.Epsilon;
                        if (inX && inZ) { onGrate = true; break; }
                    }

                    if (!onGrate)
                    {
                        violations.Add($"area '{a.id}': garrison entry {i} is a lurker at ({entry.x:0.#}, {entry.z:0.#}) " +
                                 "but no grate sits there — a Lurker must be authored exactly on a grate");
                    }
                }
            }
        }

        private static int CompositionCount(DifficultyEngine.Composition c, EnemyKind kind) => kind switch
        {
            EnemyKind.Rusher => c.Rusher,
            EnemyKind.Bruiser => c.Bruiser,
            EnemyKind.Heavy => c.Heavy,
            EnemyKind.Brute => c.Brute,
            EnemyKind.Gunner => c.Gunner,
            EnemyKind.Launcher => c.Launcher,
            EnemyKind.Blinker => c.Blinker,
            EnemyKind.Bolter => c.Bolter,
            EnemyKind.Lurker => c.Lurker,
            EnemyKind.Turret => c.Turret,
            EnemyKind.Sludger => c.Sludger,
            EnemyKind.Charger => c.Charger,
            _ => 0,
        };

        private static void WorldAreas(WorldConfig cfg, List<string> violations)
        {
            if (cfg.areas == null || cfg.areas.Length == 0)
            {
                violations.Add("the world config has no areas");
                return; // nothing further to sweep without areas
            }

            var seen = new HashSet<string>();
            foreach (WorldArea a in cfg.areas)
            {
                if (a == null) { violations.Add("an area is null"); continue; }

                if (string.IsNullOrWhiteSpace(a.id))
                { violations.Add("an area has no id — gates refer to areas by id"); continue; }

                if (!seen.Add(a.id))
                    violations.Add($"two areas share the id '{a.id}'");

                if (a.origin == null)
                { violations.Add($"area '{a.id}' has no origin"); continue; }

                if (a.size == null || a.size.w <= 0f || a.size.d <= 0f)
                { violations.Add($"area '{a.id}' has no area ({a.size?.w ?? 0f}×{a.size?.d ?? 0f})"); continue; }

                // MV-828: WorldRunner stamps every Replicator's AreaIndex from its own area's
                // area.index (the same number zones/robots key off) — an area authoring a replicator
                // outside 1..areaCount would resolve to a Replicator no robot could ever match.
                if (a.replicators != null && a.replicators.Length > 0)
                {
                    int areaCount = cfg.dials?.areaCount ?? 0;
                    if (a.index < 1 || a.index > areaCount)
                    {
                        violations.Add($"area '{a.id}' authors a replicator but its index {a.index} is not a " +
                                 $"combat area (1..{areaCount})");
                    }
                }
            }

            // MV-697: an overlays area must name a real target, and if it authors its own origin/size
            // they must agree with the target's — WorldMapLoader.TryLoad copies the target's onto it
            // right after validation passes, so a silent mismatch here would otherwise just vanish
            // instead of being caught.
            foreach (WorldArea a in cfg.areas)
            {
                if (a == null || string.IsNullOrEmpty(a.overlays)) continue;

                WorldArea target = cfg.Area(a.overlays);
                if (target == null)
                { violations.Add($"area '{a.id}' overlays unknown area '{a.overlays}'"); continue; }

                if (!Geo.Same(a.XMin, target.XMin) || !Geo.Same(a.XMax, target.XMax) ||
                    !Geo.Same(a.ZMin, target.ZMin) || !Geo.Same(a.ZMax, target.ZMax))
                {
                    violations.Add($"area '{a.id}' overlays '{target.id}' but its origin/size disagree");
                }
            }

            for (int i = 0; i < cfg.areas.Length; i++)
            for (int j = i + 1; j < cfg.areas.Length; j++)
            {
                if (cfg.areas[i] == null || cfg.areas[j] == null) continue;

                // MV-697: an overlay area shares its target's exact footprint on purpose — the same
                // gantry-over-a-floor-room revisit AreasOverlap otherwise exists to refuse.
                if (IsOverlayPair(cfg.areas[i], cfg.areas[j])) continue;

                if (AreasOverlap(cfg.areas[i], cfg.areas[j]))
                    violations.Add($"area '{cfg.areas[i].id}' overlaps area '{cfg.areas[j].id}'");
            }

            int entryCount = 0;
            foreach (WorldArea a in cfg.areas) if (a != null && a.IsEntryRole) entryCount++;
            if (entryCount != 1)
            {
                violations.Add($"the world config has {entryCount} entry areas — it needs exactly one " +
                         "('An entry stub precedes Area 1', spec §7)");
            }
        }

        /// <summary>True only for a genuine overlap — areas that merely touch along a shared wall
        /// (the normal case for two gated neighbours) are not overlapping.</summary>
        private static bool AreasOverlap(WorldArea a, WorldArea b) =>
            a.XMin < b.XMax - Geo.Epsilon && a.XMax > b.XMin + Geo.Epsilon &&
            a.ZMin < b.ZMax - Geo.Epsilon && a.ZMax > b.ZMin + Geo.Epsilon;

        /// <summary>True if either area overlays the other (MV-697) — a deliberate, same-footprint pair
        /// (a floor room and the gantry deck revisiting it), not the two-rooms-claiming-the-same-ground
        /// mistake <see cref="AreasOverlap"/> exists to catch.</summary>
        private static bool IsOverlayPair(WorldArea a, WorldArea b) =>
            (a.overlays != null && a.overlays == b.id) || (b.overlays != null && b.overlays == a.id);

        /// <summary>Every shed an area carries (MV-475, <see cref="WorldArea.Sheds"/>) must sit clear of
        /// its own area's walls, and two sheds in the same area must sit clear of each other — the same
        /// "nowhere for a robot to spawn on top of something" guarantee the ordinary Cover rules give a
        /// single shed, extended to a shed's own neighbours now that there can be more than one.
        ///
        /// Only applies once an area actually carries MORE THAN ONE shed. A legacy single-shed area
        /// "behaves exactly as it does today" (AC2) — several shipped areas (e.g. world1's a11, 4.8-5.4 m
        /// from its walls) predate this rule and were never authored against it, so gating on count is
        /// what keeps them valid rather than retroactively breaking already-shipped content.</summary>
        private static void WorldSheds(WorldConfig cfg, List<string> violations)
        {
            foreach (WorldArea a in cfg.areas)
            {
                WorldShed[] sheds = a.Sheds();
                if (sheds.Length <= 1) continue;

                foreach (WorldShed s in sheds)
                {
                    if (s.x - a.XMin < MinShedWallMargin || a.XMax - s.x < MinShedWallMargin ||
                        s.z - a.ZMin < MinShedWallMargin || a.ZMax - s.z < MinShedWallMargin)
                    {
                        violations.Add($"a shed in area '{a.id}' at ({s.x:0.#}, {s.z:0.#}) is within " +
                                 $"{MinShedWallMargin} m of its area's walls");
                    }
                }

                for (int i = 0; i < sheds.Length; i++)
                for (int j = i + 1; j < sheds.Length; j++)
                {
                    float dist = Vector2.Distance(new Vector2(sheds[i].x, sheds[i].z), new Vector2(sheds[j].x, sheds[j].z));
                    if (dist < MinShedSeparation)
                    {
                        violations.Add($"area '{a.id}' has two sheds {dist:0.#} m apart — under the " +
                                 $"{MinShedSeparation} m minimum shed separation");
                    }
                }
            }
        }

        private static void WorldGates(WorldConfig cfg, List<string> violations)
        {
            var seenIds = new HashSet<string>();
            foreach (WorldGate g in cfg.gates)
            {
                if (g == null) { violations.Add("a gate is null"); continue; }

                if (string.IsNullOrWhiteSpace(g.id))
                { violations.Add("a gate has no id"); continue; }

                if (!seenIds.Add(g.id))
                    violations.Add($"two gates share the id '{g.id}'");

                if (!ResolveEndpoint(cfg, g.from, out WorldArea fromArea, out Wall fromWall, out string fromReason))
                { violations.Add(fromReason); continue; }
                if (!ResolveEndpoint(cfg, g.to, out WorldArea toArea, out Wall toWall, out string toReason))
                { violations.Add(toReason); continue; }

                if (!ValidateOpensWith(cfg, "gate", g.id, g.opensWith, out string opensReason))
                    violations.Add(opensReason);

                if (toWall != WallEnums.Opposite(fromWall))
                {
                    violations.Add($"gate '{g.id}' joins '{fromArea.id}'s {fromWall} wall to '{toArea.id}'s {toWall} wall — " +
                             $"they must be opposite walls ({fromWall}↔{WallEnums.Opposite(fromWall)})");
                    continue;
                }

                if (!Geo.Same(fromArea.WallCoord(fromWall), toArea.WallCoord(toWall)))
                {
                    violations.Add($"gate '{g.id}': '{fromArea.id}'s {fromWall} wall and '{toArea.id}'s {toWall} wall " +
                             "do not sit on the same line — move one area so the walls coincide");
                    continue;
                }

                if (g.width < MinDoorway)
                    violations.Add($"gate '{g.id}' is {g.width} m wide — under {MinDoorway} m Max and a swarm cannot both fit through");

                Span fromSpan = fromArea.WallSpan(fromWall);
                Span toSpan = toArea.WallSpan(toWall);
                var overlap = new Span(Mathf.Max(fromSpan.Min, toSpan.Min), Mathf.Min(fromSpan.Max, toSpan.Max));

                if (overlap.IsEmpty)
                {
                    violations.Add($"gate '{g.id}': '{fromArea.id}' and '{toArea.id}' walls do not overlap at all — " +
                             "the opening has nowhere to sit");
                    continue;
                }

                if (overlap.Length < g.width - Geo.Epsilon)
                {
                    violations.Add($"gate '{g.id}' is {g.width} m wide but its two walls only share {overlap.Length:0.#} m " +
                             "— the opening does not fit");
                    continue;
                }

                float posFrom = fromSpan.Min + Mathf.Clamp01(g.from.pos) * fromSpan.Length;
                float posTo = toSpan.Min + Mathf.Clamp01(g.to.pos) * toSpan.Length;
                float along = (posFrom + posTo) * 0.5f;
                float half = g.width * 0.5f;

                if (along - half < overlap.Min - Geo.Epsilon || along + half > overlap.Max + Geo.Epsilon)
                {
                    violations.Add($"gate '{g.id}' at {along:0.#} spills past the shared wall [{overlap.Min:0.#}, {overlap.Max:0.#}] " +
                             $"— move its pos closer to the middle or narrow it below {overlap.Length:0.#} m");
                }
            }
        }

        /// <summary>Validates an authored <c>opensWith</c> string against the <see cref="GateCondition"/>
        /// grammar (MV-703), naming <paramref name="entityId"/> (prefixed with <paramref name="entityKind"/>
        /// — <c>"gate"</c> or <c>"hatch"</c>) in any failure reason. For a
        /// <c>replicators-destroyed:&lt;list&gt;</c> form, also checks every named area exists and
        /// actually authors at least one replicator — an empty list is fine
        /// (see <see cref="FactoryCensus.ReplicatorsDestroyedInAreas"/>'s own "nothing to wait on" rule),
        /// a typo'd or replicator-free area is not, since that can only be a content bug.</summary>
        private static bool ValidateOpensWith(WorldConfig cfg, string entityKind, string entityId, string opensWith, out string reason)
        {
            if (!GateCondition.TryParse(opensWith, out GateCondition condition, out string parseReason))
            {
                reason = $"{entityKind} '{entityId}': {parseReason}";
                return false;
            }

            if (condition.Kind == GateConditionKind.ReplicatorsDestroyed && !condition.ReplicatorsAll)
            {
                foreach (string areaId in condition.ReplicatorAreaIds)
                {
                    WorldArea area = cfg.Area(areaId);
                    if (area == null)
                    {
                        reason = $"{entityKind} '{entityId}' opens on replicators-destroyed:{areaId}, but area '{areaId}' does not exist";
                        return false;
                    }

                    if (area.replicators == null || area.replicators.Length == 0)
                    {
                        reason = $"{entityKind} '{entityId}' opens on replicators-destroyed:{areaId}, but area '{areaId}' authors no replicator";
                        return false;
                    }
                }
            }

            // MV-829: an area-entered:<id> hatch condition must name a real area, the same "typo'd
            // area is a content bug, not a runtime maybe" guard the replicators-destroyed list gets
            // above.
            if (condition.Kind == GateConditionKind.AreaEntered && cfg.Area(condition.AreaEnteredId) == null)
            {
                reason = $"{entityKind} '{entityId}' opens on area-entered:{condition.AreaEnteredId}, " +
                         $"but area '{condition.AreaEnteredId}' does not exist";
                return false;
            }

            // MV-833: "never" is a hatch-only condition — a wall gate authored "never" could never be
            // walked through at all, which is always a content bug (a level's own critical path running
            // through a door that can never open), whereas a hatch is a REVISIT-only shortcut back into
            // an already-cleared floor, where "never" is exactly the intended, permanent lock.
            if (condition.Kind == GateConditionKind.Never && entityKind != "hatch")
            {
                reason = $"{entityKind} '{entityId}' opens on 'never', which only a hatch may do";
                return false;
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
        private static void WorldReachability(WorldConfig cfg, List<string> violations)
        {
            WorldArea entry = FindEntry(cfg);
            if (entry == null) return; // WorldAreas already reports the missing/duplicate entry stub

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
                if (a != null && !reached.Contains(a.id))
                    violations.Add($"area '{a.id}' is not reachable from the entry stub '{entry.id}' — no chain of gates reaches it");
            }
        }

        private static WorldArea FindEntry(WorldConfig cfg)
        {
            foreach (WorldArea a in cfg.areas) if (a.IsEntryRole) return a;
            return null;
        }
    }
}
