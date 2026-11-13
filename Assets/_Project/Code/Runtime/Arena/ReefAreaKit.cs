using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// World 3's area-authored Reef dressing (MV-1215, Change items 2/3/6) — the glow hatch, the
    /// window wall and the wall-mounted dressing, each an OPTIONAL per-area field on
    /// <see cref="WorldArea"/> that no shipped config authors yet (the V8 layout ticket is the one
    /// that will — this ticket adds the vocabulary only). Same WHERE/WHAT split
    /// <see cref="ReefDressing"/>/<see cref="ReefHydroponics"/> already keep with <see cref="ReefKit"/>:
    /// this class reads <see cref="WorldConfig"/>/<see cref="WorldArea"/> (which
    /// <c>MaxWorlds.Rendering</c> has no reference to) and hands <see cref="ReefKit"/> plain
    /// positions/sizes to build.
    /// </summary>
    public static class ReefAreaKit
    {
        private const float WindowHeightFraction = 0.5f;    // how tall a window run stands, of wallHeight
        private const float WindowSillFraction = 0.2f;       // how far off the floor it starts, of wallHeight
        private const float WallDressingDepthFraction = 0.8f; // how far into the wall's own thickness it sits
        private const float WallDressingHeightFraction = 0.5f;
        private const float EdgeMatchTolerance = 0.75f;      // matches a built wall's centre line to an area's authored WallCoord

        private static readonly Wall[] AllWalls = { Wall.N, Wall.E, Wall.S, Wall.W };

        /// <summary>Change item 2: an optional per-area glow hatch — walkable, collider-free.</summary>
        public static void BuildDecor(Transform host, WorldConfig cfg)
        {
            if (host == null || cfg?.areas == null) return;

            foreach (WorldArea area in cfg.areas)
            {
                if (area?.decor == null) continue;
                foreach (WorldAreaDecor d in area.decor)
                {
                    if (d == null || d.kind != "hatch") continue;
                    Vector3 center = new Vector3(area.XMin + d.x, 0f, area.ZMin + d.z);
                    ReefKit.BuildGlowHatch(host, center, d.w, d.d);
                }
            }
        }

        /// <summary>Change item 3: an optional per-area observation-window run along one of its own edge
        /// walls, built with <see cref="ReefKit.BuildObservationWindow"/> and scaled to
        /// <paramref name="wallHeight"/> — decoration only; the wall's own collider/sight-blocking is
        /// untouched (<see cref="ReefKit.BuildObservationWindow"/> is collider-free by construction).
        /// </summary>
        public static void BuildWindows(Transform host, WorldConfig cfg, List<StructuralWall> walls, float wallHeight,
            float wallThickness)
        {
            if (host == null || cfg?.areas == null) return;

            foreach (WorldArea area in cfg.areas)
            {
                if (area?.windows == null) continue;
                foreach (WorldAreaWindow win in area.windows)
                {
                    if (win == null || !WallEnums.TryParse(win.wall, out Wall wall)) continue;
                    StructuralWall match = FindEdgeWall(walls, area, wall);
                    if (match == null) continue;

                    Span span = area.WallSpan(wall);
                    float fromAlong = span.Min + Mathf.Clamp01(win.from) * span.Length;
                    float toAlong = span.Min + Mathf.Clamp01(win.to) * span.Length;
                    float runLength = Mathf.Abs(toAlong - fromAlong);
                    if (runLength <= 0f) continue;
                    float along = (fromAlong + toAlong) * 0.5f;

                    float coord = area.WallCoord(wall);
                    float sillY = wallHeight * WindowSillFraction + wallHeight * WindowHeightFraction * 0.5f;
                    bool runsAlongX = area.WallRunsAlongX(wall);

                    Vector3 position = runsAlongX
                        ? new Vector3(along, sillY, coord)
                        : new Vector3(coord, sillY, along);
                    // MV-1262: the matched wall's own thickness, not its transform — MapRuntime.Build's
                    // ApplyBevelledBoxMesh (MV-778) bakes the wall's true size into its mesh and resets
                    // localScale to Vector3.one, so transform.lossyScale no longer carries it. Every real
                    // wall shares one map-wide thickness (MapGeometry.Walls' own single `t`), so the
                    // caller's wallThickness is this wall's thickness too — no per-wall lookup needed.
                    Vector3 size = runsAlongX
                        ? new Vector3(runLength, wallHeight * WindowHeightFraction, wallThickness)
                        : new Vector3(wallThickness, wallHeight * WindowHeightFraction, runLength);

                    GameObject window = ReefKit.BuildObservationWindow(host, position, size);

                    // MV-1257, Change item 3: "beyond the window walls ... animated fish shoals
                    // crossing the deep, and roughly every 60-90 s one large whale silhouette gliding
                    // past one visible window". MV-1171's own anchor idiom (see
                    // WorldJoinDressing.DressObservationWindows) — a plain, unscaled anchor at the
                    // window's own world position, never parented straight onto the window itself: the
                    // backdrop's four layers are authored at a flat local scale (6x3) against an
                    // unscaled parent, and the window above carries its own non-uniform
                    // (runLength, height, thickness) local scale that would otherwise balloon them.
                    var backdropAnchor = new GameObject("Ocean Backdrop Anchor");
                    backdropAnchor.transform.SetParent(host, false);
                    backdropAnchor.transform.position = window.transform.position;
                    ReefKit.BuildOceanBackdrop(backdropAnchor.transform);
                }
            }
        }

        /// <summary>Change item 6 (decided 17 Oct 2026): an area's authored <see cref="WorldArea.wallDressing"/>
        /// mounts <c>racks</c>/<c>planters</c>/<c>pipes</c> on the inner face of every one of its OWN edge
        /// walls — never a window run (<see cref="WorldArea.windows"/> on the same wall), never a gate
        /// opening (<see cref="WorldConfig.gates"/> on the same wall) — by skipping the whole wall rather
        /// than carving an exact sub-run around either, which this tight a slice has no authored data to
        /// need yet (nothing ships it: the V8 layout ticket is what will). Mounted within the wall's own
        /// thickness: decoration only, no collider, no change to the wall's own sight/movement blocking.
        /// </summary>
        public static void BuildWallDressing(Transform host, WorldConfig cfg, List<StructuralWall> walls,
            float wallHeight, float wallThickness)
        {
            if (host == null || cfg?.areas == null) return;

            foreach (WorldArea area in cfg.areas)
            {
                if (area == null || string.IsNullOrEmpty(area.wallDressing)) continue;

                foreach (Wall wall in AllWalls)
                {
                    if (HasWindowOn(area, wall) || HasGateOn(cfg, area, wall)) continue;

                    StructuralWall match = FindEdgeWall(walls, area, wall);
                    if (match == null) continue;

                    Span span = area.WallSpan(wall);
                    float coord = area.WallCoord(wall);
                    float depth = wallThickness * WallDressingDepthFraction;
                    bool runsAlongX = area.WallRunsAlongX(wall);

                    Vector3 position = runsAlongX
                        ? new Vector3(span.Mid, wallHeight * 0.4f, coord)
                        : new Vector3(coord, wallHeight * 0.4f, span.Mid);
                    Vector3 size = runsAlongX
                        ? new Vector3(span.Length, wallHeight * WallDressingHeightFraction, depth)
                        : new Vector3(depth, wallHeight * WallDressingHeightFraction, span.Length);

                    switch (area.wallDressing)
                    {
                        case "racks": ReefKit.BuildWallRacks(host, position, size); break;
                        case "planters": ReefKit.BuildWallPlanters(host, position, size); break;
                        case "pipes": ReefKit.BuildWallPipes(host, position, size); break;
                    }
                }
            }
        }

        private static bool HasWindowOn(WorldArea area, Wall wall)
        {
            if (area.windows == null) return false;
            foreach (WorldAreaWindow w in area.windows)
                if (w != null && WallEnums.TryParse(w.wall, out Wall parsed) && parsed == wall) return true;
            return false;
        }

        private static bool HasGateOn(WorldConfig cfg, WorldArea area, Wall wall)
        {
            if (cfg?.gates == null) return false;
            foreach (WorldGate g in cfg.gates)
            {
                if (g?.from != null && g.from.area == area.id &&
                    WallEnums.TryParse(g.from.wall, out Wall fw) && fw == wall) return true;
                if (g?.to != null && g.to.area == area.id &&
                    WallEnums.TryParse(g.to.wall, out Wall tw) && tw == wall) return true;
            }
            return false;
        }

        /// <summary>The built <see cref="StructuralWall"/> that IS this area's own <paramref name="wall"/>
        /// side — matched by position (nothing tags a built wall with which area/side authored it), and
        /// required <see cref="StructuralWall.IsOuterEdge"/> because both callers above only ever dress a
        /// genuine hull wall (the ticket's own "edge wall" wording), never an interior partition between
        /// two rooms.</summary>
        private static StructuralWall FindEdgeWall(List<StructuralWall> walls, WorldArea area, Wall wall)
            => FindAreaWall(walls, area, wall, requireOuterEdge: true);

        /// <summary>MV-1255: every area HAS a north wall, hull-adjacent or not — an interior room's own
        /// "far wall" (<see cref="BuildFarWallLight"/>) is frequently an interior partition shared with
        /// the room to its north, which <see cref="FindEdgeWall"/>'s own <c>IsOuterEdge</c> requirement
        /// would never match. Same position-matching body as <see cref="FindEdgeWall"/>, factored out
        /// so the two never drift apart, with that one filter made optional. Internal (MV-1258): also
        /// called directly by <see cref="ReefSetPieces"/> to anchor a gallery area's observation dome
        /// on the same north wall <see cref="BuildFarWallLight"/> already uses.</summary>
        internal static StructuralWall FindAreaWall(List<StructuralWall> walls, WorldArea area, Wall wall,
            bool requireOuterEdge)
        {
            if (walls == null) return null;

            bool wantsAlongX = area.WallRunsAlongX(wall);
            float coord = area.WallCoord(wall);
            Span span = area.WallSpan(wall);

            StructuralWall best = null;
            float bestOverlap = 0f;

            foreach (StructuralWall w in walls)
            {
                if (w == null) continue;
                if (requireOuterEdge && !w.IsOuterEdge) continue;
                if (!ResolveWallExtent(w.transform, out bool alongX, out float length)) continue;
                if (alongX != wantsAlongX) continue;

                Transform wt = w.transform;
                float wallCoord = alongX ? wt.position.z : wt.position.x;
                if (Mathf.Abs(wallCoord - coord) > EdgeMatchTolerance) continue;

                float along = alongX ? wt.position.x : wt.position.z;
                float overlap = Mathf.Min(span.Max, along + length * 0.5f) - Mathf.Max(span.Min, along - length * 0.5f);
                if (overlap > bestOverlap) { bestOverlap = overlap; best = w; }
            }

            return bestOverlap > 0f ? best : null;
        }

        /// <summary>MV-1262: a built wall's real world-space length/orientation, read off its
        /// <see cref="BoxCollider"/> (<see cref="Transform.lossyScale"/> alone no longer carries it —
        /// <c>MapRuntime.Build</c>'s own <c>ApplyBevelledBoxMesh</c>, MV-778, bakes the wall's true size
        /// into its mesh and resets <c>localScale</c> to <see cref="Vector3.one"/> once it does, which is
        /// exactly why area2's own authored S+W window silently matched nothing: every merged hull run
        /// spanning more than one area collapsed to a 1 m "length" centred nowhere near area2's own span).
        /// Same body <c>ShipAreaKit.ResolveWallExtent</c> already uses for the Mothership's sibling kit —
        /// that class's own header names this as a bug it deliberately routes around rather than shares,
        /// so this is duplicated rather than factored out, matching that precedent. Falls back to plain
        /// <c>lossyScale</c> for a hand-built fixture that carries no collider.</summary>
        private static bool ResolveWallExtent(Transform wt, out bool alongX, out float length)
        {
            Vector3 size = wt.lossyScale;
            var collider = wt.GetComponent<BoxCollider>();
            if (collider != null) size = Vector3.Scale(collider.size, wt.lossyScale);

            alongX = size.x >= size.z;
            length = alongX ? size.x : size.z;
            return length > 0.01f;
        }

        /// <summary>MV-1255, "Strip lights": a magenta top strip on every area's own NORTH wall (the
        /// far wall the fixed top-down camera faces most) — interior partition or outer hull alike
        /// (<see cref="FindAreaWall"/>'s own <c>requireOuterEdge:false</c>), one per area rather than
        /// one per built wall, since a wall shared by two areas is "north" for only one of them.</summary>
        public static void BuildFarWallLight(Transform host, WorldConfig cfg, List<StructuralWall> walls, float wallHeight)
        {
            if (host == null || cfg?.areas == null) return;

            foreach (WorldArea area in cfg.areas)
            {
                if (area == null) continue;
                StructuralWall match = FindAreaWall(walls, area, Wall.N, requireOuterEdge: false);
                if (match == null) continue;

                ReefKit.BuildFarWallStrip(host, match.transform, wallHeight);
            }
        }


        // ---------------------------------------------------------------- MV-1349: wall work lamps

        /// <summary>MV-1349: spacing of caged amber work lamps along a room-facing wall, metres.</summary>
        public const float WallLampSpacing = 6f;
        public const float WallLampMountHeight = 2.4f;
        /// <summary>A wall no longer than this gets no lamp; every wall longer than it gets at least one.</summary>
        public const float WallLampMinWallLength = 4f;
        public const float WallLampPoolDiameter = 6f;
        public const float WallLampPoolStrength = 0.5f;
        /// <summary>At most this many realtime point lights per area (the lamps nearest its centre).</summary>
        public const int MaxRealtimeLampsPerArea = 2;
        private const float WallLampEndMargin = 0.8f;
        private const float WallLampGateClearance = 0.9f;

        public struct WallLampSite
        {
            public string areaId;
            public Wall wall;
            /// <summary>World position on the wall's inner face line (y = 0).</summary>
            public Vector3 ground;
            public Vector3 inward;
            public Vector3 along;
            public bool realtime;
        }

        /// <summary>Pure placement plan for one area's wall lamps - no scene needed. One lamp per
        /// <see cref="WallLampSpacing"/> of wall, centred in its slot, nudged clear of any gate or
        /// window on that wall; at most <see cref="MaxRealtimeLampsPerArea"/> sites (nearest the area
        /// centre) are flagged <see cref="WallLampSite.realtime"/>.</summary>
        public static List<WallLampSite> PlanWallLamps(WorldArea area, WorldConfig cfg, float wallThickness)
        {
            var sites = new List<WallLampSite>();
            if (area == null) return sites;

            foreach (Wall wall in AllWalls)
            {
                Span span = area.WallSpan(wall);
                float length = span.Max - span.Min;
                if (length <= WallLampMinWallLength) continue;

                var blocked = new List<Vector2>();   // (min, max) world-along ranges
                if (cfg?.gates != null)
                    foreach (WorldGate g in cfg.gates)
                    {
                        if (g == null) continue;
                        AddGateBlock(blocked, g.from, g.width, area, wall, span);
                        AddGateBlock(blocked, g.to, g.width, area, wall, span);
                    }
                if (area.windows != null)
                    foreach (WorldAreaWindow w in area.windows)
                        if (w != null && WallEnums.TryParse(w.wall, out Wall parsed) && parsed == wall)
                            blocked.Add(new Vector2(span.Min + length * Mathf.Min(w.from, w.to),
                                                    span.Min + length * Mathf.Max(w.from, w.to)));

                int count = Mathf.Max(1, Mathf.RoundToInt(length / WallLampSpacing));
                bool alongX = area.WallRunsAlongX(wall);
                float coord = area.WallCoord(wall);
                Vector3 inward;
                switch (wall)
                {
                    case Wall.N: inward = new Vector3(0f, 0f, -1f); break;
                    case Wall.S: inward = new Vector3(0f, 0f, 1f); break;
                    case Wall.E: inward = new Vector3(-1f, 0f, 0f); break;
                    default: inward = new Vector3(1f, 0f, 0f); break;
                }
                Vector3 alongV = alongX ? Vector3.right : Vector3.forward;
                float faceCoord = coord + (inward.x + inward.z) * wallThickness * 0.5f;

                for (int i = 0; i < count; i++)
                {
                    float slot = span.Min + length * (i + 0.5f) / count;
                    if (!TryClear(slot, blocked, span, out float at)) continue;
                    Vector3 ground = alongX ? new Vector3(at, 0f, faceCoord) : new Vector3(faceCoord, 0f, at);
                    sites.Add(new WallLampSite { areaId = area.id, wall = wall, ground = ground, inward = inward, along = alongV });
                }
            }

            // The lamps nearest the area centre carry the only realtime lights.
            var centre = new Vector3((area.XMin + area.XMax) * 0.5f, 0f, (area.ZMin + area.ZMax) * 0.5f);
            var order = new List<int>();
            for (int i = 0; i < sites.Count; i++) order.Add(i);
            order.Sort((a, b) => (sites[a].ground - centre).sqrMagnitude.CompareTo((sites[b].ground - centre).sqrMagnitude));
            for (int k = 0; k < order.Count && k < MaxRealtimeLampsPerArea; k++)
            {
                WallLampSite s = sites[order[k]];
                s.realtime = true;
                sites[order[k]] = s;
            }
            return sites;
        }

        private static void AddGateBlock(List<Vector2> blocked, WorldGateEndpoint e, float width, WorldArea area, Wall wall, Span span)
        {
            if (e == null || e.area != area.id) return;
            if (!WallEnums.TryParse(e.wall, out Wall parsed) || parsed != wall) return;
            float centre = span.Min + (span.Max - span.Min) * e.pos;
            float half = width * 0.5f + WallLampGateClearance;
            blocked.Add(new Vector2(centre - half, centre + half));
        }

        private static bool IsBlocked(float at, List<Vector2> blocked)
        {
            foreach (Vector2 r in blocked) if (at >= r.x && at <= r.y) return true;
            return false;
        }

        /// <summary>The slot, or the nearest clear position within 3 m of it, kept off the wall's corners.</summary>
        private static bool TryClear(float slot, List<Vector2> blocked, Span span, out float at)
        {
            float lo = span.Min + WallLampEndMargin, hi = span.Max - WallLampEndMargin;
            for (float d = 0f; d <= 3f; d += 0.5f)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float c = slot + sign * d;
                    if (c < lo || c > hi || IsBlocked(c, blocked)) continue;
                    at = c;
                    return true;
                }
            at = slot;
            return false;
        }

        /// <summary>MV-1349 change 3: caged amber lamps on every room-facing wall - fitting plus a graduated
        /// additive floor pool (shared per-tone material, no collider), and a realtime point light only
        /// on each area's <see cref="MaxRealtimeLampsPerArea"/> centre-nearest lamps.</summary>
        public static void BuildWallLamps(Transform host, WorldConfig cfg, float wallHeight, float wallThickness)
        {
            if (host == null || cfg?.areas == null) return;

            var root = new GameObject("Reef Wall Lamps").transform;
            root.SetParent(host, false);
            Color amber = WorldMaterials.ReefWorkLampAmber;
            float mount = Mathf.Min(WallLampMountHeight, wallHeight * 0.85f);

            foreach (WorldArea area in cfg.areas)
            {
                foreach (WallLampSite site in PlanWallLamps(area, cfg, wallThickness))
                {
                    string name = $"Wall Lamp {site.areaId} {site.wall} {site.ground.x:F0},{site.ground.z:F0}";
                    GameObject lamp = StormdrainLightKit.BuildBulkheadLamp(root, name, site.ground, site.inward,
                        site.along, amber, pulsing: false, mountHeight: mount, withPool: false);
                    ShipKit.BuildGraduatedPool(lamp.transform,
                        site.inward * (WallLampPoolDiameter * 0.5f * 0.55f),
                        WallLampPoolDiameter * 0.5f, amber * WallLampPoolStrength);

                    if (!site.realtime) continue;
                    var lightGo = new GameObject("Lamp Light");
                    lightGo.transform.SetParent(lamp.transform, false);
                    lightGo.transform.localPosition = site.inward * (StormdrainLightKit.BulkheadStandoff + 0.4f) + Vector3.up * mount;
                    var light = lightGo.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = amber;
                    light.range = 7f;
                    light.intensity = 1.6f;
                    light.shadows = LightShadows.None;
                }
            }
        }

        /// <summary>Stable, cross-platform string hash (FNV-1a) — same algorithm/reasoning as
        /// <c>ReefFloorDetail.StableHash</c>, its own small copy rather than a cross-class dependency
        /// for one pure function.</summary>
        private static uint StableHash(string s)
        {
            uint hash = 2166136261u;
            if (s != null)
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
            return hash;
        }

        /// <summary>Same integer-hash idiom <c>MaxWorlds.VFX.ReefAmbientHash.Hash01</c> uses for its own
        /// per-piece phase offsets — this file's own small copy since that one is VFX-assembly internal.</summary>
        private static float Hash01(uint seed, int i)
        {
            unchecked
            {
                uint h = seed * 374761393u + (uint)i * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFu) / 65535f;
            }
        }
    }
}
