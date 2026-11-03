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

        // ---- MV-1342, Change item 3: per-area light shafts ---------------------------------------

        // Well clear of every wall (0.3 of the area's own span, each side) — a gate always sits IN a
        // wall, so insetting this far keeps a shaft out of a gate's own footprint without needing to
        // cross-reference gate positions directly.
        private const float LightShaftEdgeInsetFraction = 0.3f;
        private const float LightShaftHeightFraction = 0.9f;   // most of the wall height, ceiling to floor
        private const float LightShaftTopRadius = 0.6f;
        private const float LightShaftBottomRadius = 0.18f;
        private const float LightShaftMaxTiltDegrees = 18f;

        /// <summary>Change item 3: three to four faint, soft, slanted light shafts per area, at
        /// deterministic seeded positions well inside the area's own footprint (see
        /// <see cref="LightShaftEdgeInsetFraction"/>'s own comment for why that alone keeps them off
        /// the gates).</summary>
        public static void BuildLightShafts(Transform host, WorldConfig cfg, float wallHeight)
        {
            if (host == null || cfg?.areas == null) return;

            foreach (WorldArea area in cfg.areas)
            {
                if (area == null) continue;

                float insetX = (area.XMax - area.XMin) * LightShaftEdgeInsetFraction;
                float insetZ = (area.ZMax - area.ZMin) * LightShaftEdgeInsetFraction;
                float xMin = area.XMin + insetX, xMax = area.XMax - insetX;
                float zMin = area.ZMin + insetZ, zMax = area.ZMax - insetZ;
                if (xMin >= xMax || zMin >= zMax) continue;

                uint areaSeed = StableHash($"lightshaft_{area.id}");
                int count = 3 + (int)(areaSeed % 2u);   // ticket: "three to four"

                for (int i = 0; i < count; i++)
                {
                    float x = Mathf.Lerp(xMin, xMax, Hash01(areaSeed, i * 4 + 0));
                    float z = Mathf.Lerp(zMin, zMax, Hash01(areaSeed, i * 4 + 1));
                    float tiltX = (Hash01(areaSeed, i * 4 + 2) * 2f - 1f) * LightShaftMaxTiltDegrees;
                    float tiltZ = (Hash01(areaSeed, i * 4 + 3) * 2f - 1f) * LightShaftMaxTiltDegrees;
                    float yaw = Hash01(areaSeed, i * 4 + 100) * 360f;

                    MaxWorlds.VFX.ReefLightShaft.Build(host, new Vector3(x, 0f, z), wallHeight * LightShaftHeightFraction,
                        LightShaftTopRadius, LightShaftBottomRadius, new Vector3(tiltX, yaw, tiltZ));
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
