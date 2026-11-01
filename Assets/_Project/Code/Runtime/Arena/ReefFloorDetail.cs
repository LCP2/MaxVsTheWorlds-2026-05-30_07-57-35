using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// World 3's floor-detail pass (MV-1256, Change items 3-5): auto-placed drainage grates and loose
    /// decal clutter on the open floor, plus the flooded-deck re-skin that replaces
    /// <see cref="MapRuntime"/>'s Stormdrain-shared acid-green sludge tint with the Reef's own
    /// dark-teal water for this biome only.
    ///
    /// Split the same way <see cref="ReefHydroponics"/> already keeps from <see cref="ReefKit"/>: WHERE
    /// a piece goes lives here (needs <see cref="MapData"/>/<see cref="WorldConfig"/>/cover — Arena/
    /// Gameplay-assembly types), WHAT gets built there is one of <see cref="ReefKit"/>'s own mesh/
    /// material-only methods — <c>MaxWorlds.Rendering.asmdef</c> references only Core and the render
    /// pipeline packages, never this assembly, so the dependency can only run this direction.
    /// <see cref="PlaceGrates"/> reuses <see cref="ReefHydroponics.PointObstacles"/>/
    /// <see cref="ReefHydroponics.ClearsEverything"/> directly (ticket: "using the existing
    /// ReefHydroponics clearance helper or an equivalent") rather than re-deriving the same clearance
    /// maths a second time.
    /// </summary>
    public static class ReefFloorDetail
    {
        // ---- Change item 3: grates -----------------------------------------------------------------

        /// <summary>Ticket: "about 1 per 40 m2 of open floor" — a deterministic grid scan at this
        /// spacing (its own square footprint is ~40 m2) lands close to that density without a separate
        /// target-count/selection pass on top, same "deterministic grid, not a random scatter" idiom
        /// <see cref="ReefHydroponics.PlaceBeds"/> already established for this project.</summary>
        private static readonly float GrateGridStep = Mathf.Sqrt(40f);

        private const float GrateZoneEdgeMargin = 1.5f;
        private const int GrateParticulateCount = 3;

        /// <summary>Places every grate this map has room for and returns how many it built.</summary>
        public static int PlaceGrates(Transform host, MapData map, WorldConfig cfg, IReadOnlyList<CoverPiece> cover)
        {
            if (host == null || map?.zones == null) return 0;

            List<Vector2> pointObstacles = ReefHydroponics.PointObstacles(map, cfg);
            var placed = new List<Vector2>();
            Transform root = null;
            int count = 0;

            foreach (MapZone zone in map.zones)
            {
                if (zone == null || zone.level != 0) continue;

                for (float z = zone.ZMin + GrateZoneEdgeMargin; z <= zone.ZMax - GrateZoneEdgeMargin; z += GrateGridStep)
                {
                    for (float x = zone.XMin + GrateZoneEdgeMargin; x <= zone.XMax - GrateZoneEdgeMargin; x += GrateGridStep)
                    {
                        var candidate = new Vector2(x, z);
                        if (!ReefHydroponics.ClearsEverything(candidate, cover, pointObstacles, placed)) continue;

                        if (root == null)
                        {
                            root = new GameObject("Floor Grates").transform;
                            root.SetParent(host, false);
                        }

                        GameObject grate = ReefKit.BuildFloorGrate(root, new Vector3(candidate.x, 0f, candidate.y));

                        int seed = unchecked((int)StableHash($"grate_{candidate.x:F2}_{candidate.y:F2}"));
                        SludgeBubbles.Attach(grate.transform, "Particulate", new Vector2(0.3f, 0.3f),
                            -0.04f, seed, GrateParticulateCount, WorldMaterials.ReefGrateGlowCyan);

                        placed.Add(candidate);
                        count++;
                    }
                }
            }

            return count;
        }

        // ---- Change item 4: decals -----------------------------------------------------------------

        private const float DecalGridStep = 5f;           // sparser than grates — ticket: "sparse"
        private const float DecalClearance = 1f;           // clear of cover's own footprint only
        private const float DecalZoneEdgeMargin = 1.5f;
        private const float CableRunLength = 1.4f;

        /// <summary>Places loose cable runs and debris scraps on the open floor and returns how many it
        /// built — "sparse" (ticket, Change item 4) kept literal by skipping most of the same
        /// deterministic grid <see cref="PlaceGrates"/> scans, rather than authoring a second, denser
        /// grid just for this.</summary>
        public static int PlaceDecals(Transform host, MapData map, WorldConfig cfg, IReadOnlyList<CoverPiece> cover)
        {
            if (host == null || map?.zones == null) return 0;

            Transform root = null;
            int count = 0;

            foreach (MapZone zone in map.zones)
            {
                if (zone == null || zone.level != 0) continue;

                for (float z = zone.ZMin + DecalZoneEdgeMargin; z <= zone.ZMax - DecalZoneEdgeMargin; z += DecalGridStep)
                {
                    for (float x = zone.XMin + DecalZoneEdgeMargin; x <= zone.XMax - DecalZoneEdgeMargin; x += DecalGridStep)
                    {
                        var candidate = new Vector2(x, z);
                        if (!ClearsCover(candidate, cover)) continue;

                        uint h = StableHash($"decal_{candidate.x:F2}_{candidate.y:F2}");
                        if (h % 3u != 0u) continue;   // sparse: keep only 1 of every 3 valid grid points

                        if (root == null)
                        {
                            root = new GameObject("Floor Decals").transform;
                            root.SetParent(host, false);
                        }

                        float yaw = h % 360u;
                        var center = new Vector3(candidate.x, 0f, candidate.y);

                        if (h % 2u == 0u)
                        {
                            var dir = new Vector3(Mathf.Cos(yaw * Mathf.Deg2Rad), 0f, Mathf.Sin(yaw * Mathf.Deg2Rad));
                            ReefKit.BuildCableRunDecal(root, center - dir * (CableRunLength * 0.5f), center + dir * (CableRunLength * 0.5f));
                        }
                        else
                        {
                            ReefKit.BuildDebrisScrap(root, center, (int)(h % 2u), yaw);
                        }

                        count++;
                    }
                }
            }

            return count;
        }

        private static bool ClearsCover(Vector2 candidate, IReadOnlyList<CoverPiece> cover)
        {
            foreach (CoverPiece piece in cover)
                if (piece.Cover.DistanceTo(candidate) < DecalClearance) return false;
            return true;
        }

        // ---- MV-1339, Change item 3: grating patches --------------------------------------------

        private const float GratingPatchGridStep = 2f;      // scan step while hunting for a clear spot
        private const float GratingPatchZoneEdgeMargin = 2.5f; // half the 4x3 m patch's own diagonal, plus a margin

        /// <summary>Places about one 4x3 m grating patch per area, "placed away from gates and
        /// garrisons" (ticket, Change item 3) — reuses <see cref="ReefHydroponics.PointObstacles"/>/
        /// <see cref="ReefHydroponics.ClearsEverything"/> directly, same clearance helper
        /// <see cref="PlaceGrates"/> already reuses for the identical "gate mouths, factories,
        /// Replicators and garrison points" exclusion list. One deterministic grid scan per
        /// <see cref="WorldArea"/> (not per <see cref="MapZone"/> — the ticket's own "per area"), first
        /// clear point wins, same "deterministic grid, not a random scatter" idiom <see cref="PlaceGrates"/>
        /// keeps. Returns how many it built.</summary>
        public static int PlaceGratingPatches(Transform host, MapData map, WorldConfig cfg, IReadOnlyList<CoverPiece> cover)
        {
            if (host == null || cfg?.areas == null) return 0;

            List<Vector2> pointObstacles = ReefHydroponics.PointObstacles(map, cfg);
            var placed = new List<Vector2>();
            Transform root = null;
            int count = 0;

            foreach (WorldArea area in cfg.areas)
            {
                if (area == null) continue;

                Vector2? spot = null;
                for (float z = area.ZMin + GratingPatchZoneEdgeMargin; z <= area.ZMax - GratingPatchZoneEdgeMargin && spot == null; z += GratingPatchGridStep)
                for (float x = area.XMin + GratingPatchZoneEdgeMargin; x <= area.XMax - GratingPatchZoneEdgeMargin && spot == null; x += GratingPatchGridStep)
                {
                    var candidate = new Vector2(x, z);
                    if (ReefHydroponics.ClearsEverything(candidate, cover, pointObstacles, placed)) spot = candidate;
                }

                if (spot == null) continue;

                if (root == null)
                {
                    root = new GameObject("Floor Grating Patches").transform;
                    root.SetParent(host, false);
                }

                bool rotated = StableHash($"grating_{area.id}") % 2u == 0u;
                ReefKit.BuildFloorGratingPatch(root, new Vector3(spot.Value.x, 0f, spot.Value.y), rotated);

                placed.Add(spot.Value);
                count++;
            }

            return count;
        }

        /// <summary>Stable, cross-platform string hash (FNV-1a) — same algorithm/reasoning as
        /// <c>ReefKit.StableHash</c> (a private member of a different assembly's class, so this is its
        /// own small copy rather than a cross-assembly dependency for one pure function).</summary>
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

        // ---- Change item 5: flooded-deck water re-skin ----------------------------------------------

        /// <summary>Slow UV drift for the Reef's own caustic-shimmer texture (MV-1256) — far slower than
        /// <c>MapRuntime.SludgeScrollSpeed</c> (0, 0.12): a flat flow current reads right for an open
        /// drain, but this is meant to read as barely-moving water.</summary>
        private static readonly Vector2 FloodShimmerScrollSpeed = new Vector2(0.015f, 0.025f);

        /// <summary>Re-skins every flooded-deck tile World 3 already built (MV-1256, Change item 5) —
        /// found by its own <see cref="SludgeFlow"/> component, the same "already tagged, just read it"
        /// idiom other systems (<c>DeckVisibility</c>, <c>BackyardWear</c>) already use for these same
        /// tiles rather than searching by name. The gameplay 0.6x slow
        /// (<c>WorldConfig.sludgeSpeedMultiplier</c>, <c>MapGeometry.SpeedMultiplierAt</c>) is driven
        /// entirely by the tile's own <see cref="MapEntity"/> footprint, never by this renderer or its
        /// material, so re-skinning the look here cannot touch it (ticket's own "do not re-raise").
        /// Each tile already carries <see cref="KeepsOwnMaterial"/> (<c>MapRuntime.Tint</c> added it
        /// when it first built the tile) so neither the generic biome sweep nor the runtime surface
        /// sweep can repaint it back — this only needs to guard the same invariant, not re-establish
        /// it.</summary>
        public static void DressFlood(Transform host)
        {
            if (host == null) return;

            foreach (SludgeFlow flow in host.GetComponentsInChildren<SludgeFlow>(true))
            {
                Renderer r = flow.GetComponent<Renderer>();
                if (r == null) continue;

                Material water = WorldMaterials.M_FloodWater;
                if (water == null) continue;

                r.sharedMaterial = water;
                if (r.gameObject.GetComponent<KeepsOwnMaterial>() == null) r.gameObject.AddComponent<KeepsOwnMaterial>();
                flow.Configure(r, water, FloodShimmerScrollSpeed);
            }
        }
    }
}
