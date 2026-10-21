using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// The Mothership's own area-authored wall dressing (MV-1260) — the Ship counterpart of
    /// <see cref="ReefAreaKit"/>: reads <see cref="WorldConfig"/>/<see cref="WorldArea"/> data
    /// <c>MaxWorlds.Rendering</c> cannot see and hands <see cref="ShipKit"/> plain positions/sizes to
    /// build. Unlike the Reef's own wall dressing (opt-in per area via <see cref="WorldArea.wallDressing"/>),
    /// the ticket's own "every room-facing wall face in all 30 areas" is unconditional — every area gets
    /// all four of its own sides dressed, whichever of them has a matching built wall.
    ///
    /// This deliberately does NOT call <see cref="ReefAreaKit.FindAreaWall"/>: that helper reads a
    /// candidate wall's <see cref="Transform.lossyScale"/> for its length, which is only ever correct for
    /// a hand-built test fixture. A REAL wall <c>MapRuntime.Build</c> builds (every world's, not just
    /// Ship's) has its <c>localScale</c> reset to <see cref="Vector3.one"/> once <c>ApplyBevelledBoxMesh</c>
    /// bakes its true size into the mesh (MV-778) — the real size lives in its <see cref="BoxCollider.size"/>
    /// from that point on. Reading <c>lossyScale</c> against a real wall silently resolves to ~1 m
    /// regardless of its actual run (caught by this ticket's own fail-first test: AC1 expected >= 16
    /// panels on area 8's 56 m north wall and found 1 — see Logs/MV1260-failfirst.log, quoted in the fix
    /// comment), and for a non-square wall it also makes every "along X vs along Z" orientation check
    /// resolve to the same answer regardless of the wall's real orientation, which would have silently
    /// skipped every E/W wall in all 30 areas. <see cref="ResolveWallExtent"/> below reads the collider
    /// when one is present and only falls back to <c>lossyScale</c> for a fixture that has none — this
    /// class simply does not depend on the broken path. (MV-1262 later gave <see cref="ReefAreaKit"/>'s
    /// own <c>FindAreaWall</c> the same fix, duplicated rather than shared — see that method's own doc.)
    /// </summary>
    public static class ShipAreaKit
    {
        private static readonly Wall[] AllWalls = { Wall.N, Wall.E, Wall.S, Wall.W };

        // Same tolerance ReefAreaKit.FindAreaWall uses for matching a built wall's centre line to an
        // area's authored WallCoord.
        private const float EdgeMatchTolerance = 0.75f;

        /// <summary>One <see cref="ShipKit.BuildBulkheadPanel"/> call per area per side that has a
        /// matching built <see cref="StructuralWall"/> (not required to be an outer edge, since an
        /// interior partition is "room-facing" from each of the two rooms it borders).
        /// <paramref name="a"/>/<paramref name="b"/>/the inward normal are resolved off the MATCHED
        /// wall's own built geometry (a resolved value, testing policy Tier 2), not the area's authored
        /// rectangle, so panel/rib counts track what actually got built — including a gate gap's own
        /// shorter run — rather than what was authored. Each call's own root is renamed
        /// <c>"Wall {area.id} {wall}"</c> so a test (or a future pass) can find one side of one area
        /// directly rather than re-deriving the match itself. <paramref name="wallThickness"/> is the
        /// map's own resolved value (MV-1269), sized into the wall-top cap plate so it reads as sitting on
        /// the real wall rather than a different width.</summary>
        public static void BuildWallDressing(Transform host, WorldConfig cfg, List<StructuralWall> walls, float wallHeight, float wallThickness)
        {
            if (host == null || cfg?.areas == null) return;

            var root = new GameObject("Ship Wall Dressing");
            root.transform.SetParent(host, false);
            root.AddComponent<KeepsOwnMaterial>();

            // MV-1272: a structural wall shared by two neighbouring areas (A's east IS B's west) is
            // matched and dressed once per side — the second side's cap/lamp-strip/rib would otherwise
            // land on the identical plate the first side already drew (the wall-top strobe bug). This
            // set is scoped to one BuildWallDressing call, so every wall's cap renders exactly once per
            // real build, not once ever.
            var cappedWalls = new HashSet<StructuralWall>();

            foreach (WorldArea area in cfg.areas)
            {
                if (area == null) continue;

                foreach (Wall wall in AllWalls)
                {
                    StructuralWall match = FindAreaWall(walls, area, wall);
                    if (match == null) continue;

                    (Vector2 a, Vector2 b) = WallEndpoints(match.transform, wall);
                    Vector3 inward = InwardNormal(wall);
                    Color deckGlow = MothershipDecks.LookFor(area.index).RimColor;
                    int wallSeed = area.index * 4 + (int)wall;
                    bool capVisible = cappedWalls.Add(match);

                    GameObject wallGo = ShipKit.BuildBulkheadPanel(
                        root.transform, a, b, inward, wallHeight, wallThickness, deckGlow, match.IsOuterEdge, wallSeed, capVisible);
                    wallGo.name = $"Wall {area.id} {wall}";

                    // MV-1264: the panel backing (ShipKit.BuildPanelCell) stands with its own back face
                    // flush on the real wall's face — same z-fight the floor had (MapGeometry.Floor vs
                    // ShipKit.BuildAtlasFloor), so the bare wall won on a real device and every panel
                    // MV-1260 built was invisible. Same "hide the box, keep its collider" contract
                    // DressCover uses for cover art.
                    var wallRenderer = match.GetComponent<Renderer>();
                    if (wallRenderer != null) wallRenderer.enabled = false;
                }
            }

            StaticBatchingUtility.Combine(root);
        }

        /// <summary>The built <see cref="StructuralWall"/> that IS this area's own <paramref name="wall"/>
        /// side — matched by position (nothing tags a built wall with which area/side authored it), same
        /// "best overlap wins" body <see cref="ReefAreaKit.FindAreaWall"/> uses, but resolving each
        /// candidate's length/orientation via <see cref="ResolveWallExtent"/> rather than its raw
        /// <see cref="Transform.lossyScale"/> — see this file's own header for why.</summary>
        private static StructuralWall FindAreaWall(List<StructuralWall> walls, WorldArea area, Wall wall)
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

        /// <summary>A built wall's real world-space footprint: <see cref="BoxCollider.size"/> scaled by
        /// the transform (the real size MV-778 bakes in, once <c>localScale</c> itself has been reset to
        /// one) when a collider is present, falling back to plain <see cref="Transform.lossyScale"/> for
        /// a hand-built fixture that carries none (e.g. <c>MV1255ReefWallDetailTests</c>' own probe
        /// wall). <paramref name="alongX"/> is true when the wall's run is wider in X than in Z.</summary>
        private static bool ResolveWallExtent(Transform wt, out bool alongX, out float length)
        {
            Vector3 size = wt.lossyScale;
            var collider = wt.GetComponent<BoxCollider>();
            if (collider != null) size = Vector3.Scale(collider.size, wt.lossyScale);

            alongX = size.x >= size.z;
            length = alongX ? size.x : size.z;
            return length > 0.01f;
        }

        /// <summary>The wall direction's own fixed inward normal — an area's interior is always south of
        /// its own north wall, west of its east wall, and so on, independent of which physical
        /// <see cref="StructuralWall"/> ended up matching it.</summary>
        private static Vector3 InwardNormal(Wall wall) => wall switch
        {
            Wall.N => Vector3.back,
            Wall.S => Vector3.forward,
            Wall.E => Vector3.left,
            Wall.W => Vector3.right,
            _ => Vector3.forward,
        };

        /// <summary>The matched wall's own resolved endpoints in world XZ — read off its built
        /// <see cref="ResolveWallExtent"/> length/position, same "ask the built geometry, not the author
        /// data" idiom <see cref="ReefKit.BuildCornerColumns"/> already uses, not the area's authored
        /// span (which may run past a gate gap the real built wall segment does not).</summary>
        private static (Vector2 a, Vector2 b) WallEndpoints(Transform wt, Wall wall)
        {
            ResolveWallExtent(wt, out bool alongX, out float length);

            Vector2 a = alongX
                ? new Vector2(wt.position.x - length * 0.5f, wt.position.z)
                : new Vector2(wt.position.x, wt.position.z - length * 0.5f);
            Vector2 b = alongX
                ? new Vector2(wt.position.x + length * 0.5f, wt.position.z)
                : new Vector2(wt.position.x, wt.position.z + length * 0.5f);
            return (a, b);
        }
    }
}
