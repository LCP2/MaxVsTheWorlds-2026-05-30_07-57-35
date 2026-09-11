using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// World 2's dressing pass — the Stormdrain counterpart of <see cref="BackyardDressing"/> and
    /// <see cref="ReefDressing"/>, and the thing World 2 has never had.
    ///
    /// Called from <see cref="BackyardPath"/>'s existing per-world sweep hook, exactly where
    /// <c>ApplyReefKit</c> is called for World 3, so it runs after <see cref="WorldMaterials.Apply"/>
    /// has already painted the biome and overrides rather than races it.
    ///
    /// Two passes, and the split matters. The WALL pass walks
    /// <see cref="MapGeometry.Faces"/> — the seam that struct was built for ("hand the art layer these
    /// and it can dress ANY map") — and hangs a kerb, a pipe bank and lamps on every face that looks
    /// into a room. The COVER pass swaps each grey block's renderer for a piece of drain machinery,
    /// keeping the block's own collider, the same contract every <c>DressCover</c> case keeps.
    ///
    /// Nothing here is authored per area. A drain that needed twenty-three hand-placed prop lists
    /// would never get built, and the moment the level moved it would be wrong.
    /// </summary>
    public static class StormdrainDressing
    {
        /// <summary>Below this a room is a connector stub, and dressing every face of it just fills
        /// the doorway with pipework the player has to walk through.</summary>
        private const float MinFaceLength = 1.2f;

        /// <summary>What one pass placed, so an EditMode test can assert on density and variety rather
        /// than on presence (<c>feedback_count_based_acs_pass_a_stub</c>: an AC that counts lets a
        /// skeleton pass, so the test asserts pieces-per-metre and how many DISTINCT kinds appeared).</summary>
        public readonly struct DressReport
        {
            public readonly int Kerbs, Pipes, Lamps, Soffits, CoverProps, SludgeTiles;
            public readonly int DistinctCoverKinds;

            public DressReport(int kerbs, int pipes, int lamps, int soffits,
                               int coverProps, int sludgeTiles, int distinctCoverKinds)
            {
                Kerbs = kerbs; Pipes = pipes; Lamps = lamps; Soffits = soffits;
                CoverProps = coverProps; SludgeTiles = sludgeTiles;
                DistinctCoverKinds = distinctCoverKinds;
            }

            public int Total => Kerbs + Pipes + Lamps + Soffits + CoverProps + SludgeTiles;
        }

        /// <summary>Dresses the whole drain. Idempotent per load — it builds under one named host, and
        /// a second call replaces that host rather than doubling every pipe in the world.</summary>
        public static DressReport Dress(Transform host, MapData map, IReadOnlyList<CoverPiece> cover)
        {
            if (host == null || map == null) return default;

            Transform existing = host.Find("Stormdrain Dressing");
            if (existing != null)
            {
                if (Application.isPlaying) Object.Destroy(existing.gameObject);
                else Object.DestroyImmediate(existing.gameObject);
            }

            var root = new GameObject("Stormdrain Dressing").transform;
            root.SetParent(host, false);
            root.gameObject.AddComponent<KeepsOwnMaterial>();

            int kerbs = 0, pipes = 0, lamps = 0, soffits = 0;

            var walls = new GameObject("Walls").transform;
            walls.SetParent(root, false);

            int seed = 0;
            foreach (WallFace face in MapGeometry.Faces(map))
            {
                seed++;
                if (!face.FacesRoom) continue;
                if (face.Length < MinFaceLength) continue;

                int before = walls.childCount;
                StormdrainKit.DressWallFace(walls, face.A, face.B, face.Out, map.wallHeight, seed);
                StormdrainKit.BuildSoffit(walls, face.A, face.B, face.Out, map.wallHeight);

                // Counted off what was actually created, not off what the call was asked to create —
                // DressWallFace declines short faces and skips lamps that would land in a doorway.
                for (int i = before; i < walls.childCount; i++)
                {
                    string n = walls.GetChild(i).name;
                    if (n.StartsWith("Kerb")) kerbs++;
                    else if (n.StartsWith("Pipe") || n.StartsWith("Collar")) pipes++;
                    else if (n.StartsWith("Wall Lamp")) lamps++;
                    else if (n.StartsWith("Soffit")) soffits++;
                }
            }

            int coverProps = 0;
            var kinds = new HashSet<CoverDressing>();
            var props = new GameObject("Cover").transform;
            props.SetParent(root, false);

            if (cover != null)
            {
                int i = 0;
                foreach (CoverPiece piece in cover)
                {
                    i++;
                    if (piece.Body == null) continue;
                    if (!BuildFor(props, piece, i, map.wallHeight)) continue;

                    // The block's own box stays the collider — only its art is replaced. Same
                    // contract ReefDressing keeps, and the reason a re-dressed room still plays
                    // identically to the greybox it was tuned on.
                    var rend = piece.Body.GetComponent<Renderer>();
                    if (rend != null) rend.enabled = false;

                    kinds.Add(piece.Cover.Dressing);
                    coverProps++;
                }
            }

            int tiles = DressSludge(root, map);

            DressFloorComposition(root, map);

            return new DressReport(kerbs, pipes, lamps, soffits, coverProps, tiles, kinds.Count);
        }

        // ---------------------------------------------------------------- floor composition (MV-781)

        /// <summary>Panel joints every this many metres, phased off world position (not each area's own
        /// origin) so the grid is continuous across an area boundary.</summary>
        private const float JointSpacing = 3.2f;

        /// <summary>The entity kinds whose rects a joint or patch must never cross (the ticket's own
        /// list) — all already resolved to world-space centre/size by <see cref="WorldMapLoader"/>.</summary>
        private static bool IsFloorObstacle(EntityKind kind) =>
            kind == EntityKind.Grate || kind == EntityKind.Deck || kind == EntityKind.Ramp ||
            kind == EntityKind.Hatch || kind == EntityKind.Sludge;

        private static List<Rect> FloorObstacles(MapData map)
        {
            var rects = new List<Rect>();
            if (map.entities == null) return rects;
            foreach (MapEntity e in map.entities)
            {
                if (e == null || !IsFloorObstacle(e.Kind)) continue;

                // A Grate's x/z is its authored MIN CORNER (WorldGrate's own doc comment; WorldMapLoader
                // carries it onto the entity unchanged) — every other obstacle kind here (Deck/Ramp/
                // Hatch/Sludge) is centre-authored by WorldMapLoader, so only Grate needs the different
                // corner-to-rect conversion.
                Rect r = e.Kind == EntityKind.Grate
                    ? new Rect(e.x, e.z, e.width, e.depth)
                    : new Rect(e.x - e.width * 0.5f, e.z - e.depth * 0.5f, e.width, e.depth);
                rects.Add(r);
            }
            return rects;
        }

        /// <summary>Builds every floor-level zone's panel joints (change 2) and silt/standing-water
        /// patches (change 3) — skipped for a <see cref="MapZone.level"/> &gt; 0 zone (a deck overlay
        /// shares its target's floor, MV-697, so it never gets a second pass of it).</summary>
        private static void DressFloorComposition(Transform root, MapData map)
        {
            if (map.zones == null) return;

            var floorHost = new GameObject("Floor Composition").transform;
            floorHost.SetParent(root, false);

            List<Rect> obstacles = FloorObstacles(map);

            foreach (MapZone zone in map.zones)
            {
                if (zone == null || zone.level > 0) continue;
                Rect zoneRect = zone.Footprint;

                foreach (Rect seg in JointRects(zoneRect, obstacles))
                    StormdrainKit.BuildPanelJoint(floorHost, seg);

                foreach ((Rect rect, bool isWater) in PatchRects(zoneRect, zone.id, obstacles))
                    StormdrainKit.BuildFloorPatch(floorHost, rect, isWater);
            }
        }

        /// <summary>Panel-joint segments for one floor zone's rect (MV-781, change 2): a recessed line
        /// every <see cref="JointSpacing"/> metres on both axes, split into one segment per bay so a
        /// segment that would cross an obstacle can be dropped without breaking the rest of the line.
        /// Pure function of its inputs — calling it twice for the same zone/obstacles is how
        /// <c>MV781FloorCompositionTests</c> proves the layout is deterministic, not re-rolled.</summary>
        public static List<Rect> JointRects(Rect zone, IReadOnlyList<Rect> obstacles)
        {
            var result = new List<Rect>();
            AddJointAxis(result, zone, obstacles, alongX: true);
            AddJointAxis(result, zone, obstacles, alongX: false);
            return result;
        }

        private static void AddJointAxis(List<Rect> result, Rect zone, IReadOnlyList<Rect> obstacles, bool alongX)
        {
            // alongX: the joint LINE runs parallel to Z at a fixed X (a "vertical" line on the floor
            // plan); otherwise the line runs parallel to X at a fixed Z.
            float lineMin = alongX ? zone.xMin : zone.yMin;
            float lineMax = alongX ? zone.xMax : zone.yMax;
            float spanMin = alongX ? zone.yMin : zone.xMin;
            float spanMax = alongX ? zone.yMax : zone.xMax;

            float firstLine = Mathf.Ceil((lineMin + 0.01f) / JointSpacing) * JointSpacing;
            for (float line = firstLine; line < lineMax - 0.01f; line += JointSpacing)
            {
                float firstBay = Mathf.Floor(spanMin / JointSpacing) * JointSpacing;
                for (float bayStart = firstBay; bayStart < spanMax - 0.01f; bayStart += JointSpacing)
                {
                    float segMin = Mathf.Max(bayStart, spanMin);
                    float segMax = Mathf.Min(bayStart + JointSpacing, spanMax);
                    if (segMax - segMin < 0.05f) continue;

                    Rect seg = alongX
                        ? new Rect(line - StormdrainKit.PanelJointWidth * 0.5f, segMin, StormdrainKit.PanelJointWidth, segMax - segMin)
                        : new Rect(segMin, line - StormdrainKit.PanelJointWidth * 0.5f, segMax - segMin, StormdrainKit.PanelJointWidth);

                    if (Overlaps(seg, obstacles)) continue;
                    result.Add(seg);
                }
            }
        }

        private static bool Overlaps(Rect r, IReadOnlyList<Rect> obstacles)
        {
            foreach (Rect o in obstacles)
                if (r.Overlaps(o)) return true;
            return false;
        }

        private const float PatchMinSize = 2f;
        private const float PatchMaxSize = 4f;
        private const int PatchCandidateGrid = 9; // a 3x3 interior grid of candidate centres

        /// <summary>Silt/standing-water patches for one floor zone (MV-781, change 3): 2 to 4 flat
        /// patches, alternating silt/water, deterministic from the zone's own id and rect (which are
        /// themselves derived from the area's index and origin — <see cref="WorldMapLoader"/> resolves
        /// a combat area's <see cref="MapZone.id"/> to "area{index}" — so hashing them gives back
        /// exactly the "area index and origin" determinism the ticket asks for, never
        /// <see cref="UnityEngine.Random"/>), that avoid every authored obstacle.</summary>
        public static List<(Rect rect, bool isWater)> PatchRects(Rect zone, string zoneId, IReadOnlyList<Rect> obstacles)
        {
            var result = new List<(Rect, bool)>();
            int seed = DeterministicSeed(zoneId, zone);
            int count = 2 + (seed % 3); // 2..4

            for (int i = 0; i < count; i++)
            {
                float size = PatchMinSize + Frac(seed, i * 7 + 1) * (PatchMaxSize - PatchMinSize);
                if (TryPlacePatch(zone, size, obstacles, seed, i, out Rect rect))
                    result.Add((rect, (i & 1) == 1));
            }
            return result;
        }

        private static bool TryPlacePatch(Rect zone, float size, IReadOnlyList<Rect> obstacles, int seed, int index, out Rect placed)
        {
            float half = size * 0.5f;
            float marginX = Mathf.Max(half, zone.width * 0.22f);
            float marginZ = Mathf.Max(half, zone.height * 0.22f);

            for (int attempt = 0; attempt < PatchCandidateGrid; attempt++)
            {
                int slot = (seed + index * 3 + attempt) % PatchCandidateGrid;
                float tx = (slot % 3 + 1) / 4f;   // 0.25, 0.5, 0.75
                float tz = (slot / 3 + 1) / 4f;

                float cx = Mathf.Lerp(zone.xMin + marginX, zone.xMax - marginX, tx);
                float cz = Mathf.Lerp(zone.yMin + marginZ, zone.yMax - marginZ, tz);

                var candidate = new Rect(cx - half, cz - half, size, size);
                if (candidate.xMin < zone.xMin || candidate.xMax > zone.xMax ||
                    candidate.yMin < zone.yMin || candidate.yMax > zone.yMax) continue;
                if (Overlaps(candidate, obstacles)) continue;

                placed = candidate;
                return true;
            }

            placed = default;
            return false;
        }

        /// <summary>Deterministic, non-negative hash of a zone's id and resolved world rect — the
        /// "area index and origin" input the ticket's determinism rule asks for, expressed off what a
        /// zone actually carries rather than reaching back into the raw <c>WorldArea</c> it came from.</summary>
        private static int DeterministicSeed(string zoneId, Rect zone)
        {
            unchecked
            {
                int hash = 17;
                if (!string.IsNullOrEmpty(zoneId))
                    foreach (char c in zoneId) hash = hash * 31 + c;
                hash = hash * 31 + Mathf.RoundToInt(zone.x * 100f);
                hash = hash * 31 + Mathf.RoundToInt(zone.y * 100f);
                return hash & 0x7fffffff;
            }
        }

        /// <summary>Deterministic 0..1 from an integer seed and a salt — the same golden-ratio-hash
        /// idiom <c>StormdrainKit.Frac</c>/<c>StormdrainDressing.DeterministicYaw</c> already use for
        /// "same input, same output, never <see cref="UnityEngine.Random"/>", salted so two different
        /// draws off the same seed don't move in lockstep.</summary>
        private static float Frac(int seed, int salt) =>
            Mathf.Abs((seed * 0.6180339887f + salt * 0.3247179572f) % 1f);

        /// <summary>Maps a cover piece's authored dressing class onto its drain equivalent. Every class
        /// has one — including <see cref="CoverDressing.None"/>, which in World 1 means "a bare crate"
        /// and here means silt sacks. That is the difference from <see cref="ReefDressing"/>, which
        /// deliberately dresses only one class: World 3's ticket said place nothing where there is no
        /// equivalent, and the result is a world of grey boxes. World 2 is not repeating that.</summary>
        private static bool BuildFor(Transform parent, CoverPiece piece, int seed, float wallHeight)
        {
            ArenaCover c = piece.Cover;
            Vector3 at = new Vector3(c.CenterXz.x, 0f, c.CenterXz.y);
            Vector3 size = c.Size;

            switch (c.Dressing)
            {
                case CoverDressing.Tree:
                    // Wall-height-proportional (MV-765), not the cover block's own authored size.y.
                    GameObject standpipe = StormdrainKit.BuildStandpipe(parent, at, wallHeight, wallHeight);
                    standpipe.transform.rotation = Quaternion.Euler(0f, DeterministicYaw(at), 0f);
                    return true;
                case CoverDressing.Hedge:
                    GameObject rake = StormdrainKit.BuildDebrisRake(parent, at, size);
                    Vector2 rakeLean = DeterministicLean(at);
                    rake.transform.rotation = Quaternion.Euler(rakeLean.x, DeterministicYaw(at), rakeLean.y);
                    return true;
                case CoverDressing.Planter:
                    GameObject bin = StormdrainKit.BuildSiltBin(parent, at, size);
                    bin.transform.rotation = Quaternion.Euler(0f, DeterministicYaw(at), 0f);
                    return true;
                case CoverDressing.Shed:
                case CoverDressing.Machinery:
                    // Heavy fixed machinery, not loose debris — stays square (MV-778 change 3 lists
                    // standpipes, debris rakes, silt sacks and silt bins only).
                    StormdrainKit.BuildPumpHousing(parent, at, size);
                    return true;
                default:
                    GameObject sacks = StormdrainKit.BuildSiltSacks(parent, at, size, seed);
                    Vector2 sackLean = DeterministicLean(at);
                    sacks.transform.rotation = Quaternion.Euler(sackLean.x, DeterministicYaw(at), sackLean.y);
                    return true;
            }
        }

        /// <summary>
        /// MV-778, change 3 ("break the axis"): nothing in the drain sat off 90 degrees, which reads
        /// as machine-generated. This is the ticket's own formula, hashed from world position — not
        /// <see cref="Random"/> — so the level lays out identically every run: the same map always
        /// jitters the same piece the same way.
        /// </summary>
        private static float DeterministicYaw(Vector3 at)
            => ((Mathf.Abs(at.x * 73.1f + at.z * 149.7f) % 1f) - 0.5f) * 24f;

        /// <summary>Up to 6 degrees of lean on X and on Z (MV-778) — for the loose debris and sacks
        /// only, never for anything with a fixed footprint. Different hash constants than
        /// <see cref="DeterministicYaw"/> so a piece's yaw and its lean don't move in lockstep.</summary>
        private static Vector2 DeterministicLean(Vector3 at)
        {
            float lx = ((Mathf.Abs(at.x * 191.3f + at.z * 269.9f) % 1f) - 0.5f) * 12f;
            float lz = ((Mathf.Abs(at.x * 337.9f + at.z * 431.3f) % 1f) - 0.5f) * 12f;
            return new Vector2(lx, lz);
        }

        /// <summary>The id <c>MapRuntime</c> already uses for the gate the sludge grades toward. Reused
        /// here as the downstream anchor, so the chevrons point the same way the tone gradient does
        /// rather than disagreeing with it.</summary>
        private const string OutfallGateId = "outfall";

        private static int DressSludge(Transform root, MapData map)
        {
            if (map.entities == null) return 0;

            var host = new GameObject("Sludge").transform;
            host.SetParent(root, false);

            MapEntity outfall = map.Entity(OutfallGateId);
            int tiles = 0, seed = 0;

            foreach (MapEntity e in map.entities)
            {
                seed++;
                if (e == null || e.Kind != EntityKind.Sludge) continue;

                Vector3 center = new Vector3(e.x, 0f, e.z);
                Vector3 flow = outfall != null
                    ? new Vector3(outfall.x - e.x, 0f, outfall.z - e.z)
                    : Vector3.forward;

                StormdrainKit.DressSludgeTile(host, center, e.width, e.depth, flow, seed);
                tiles++;
            }

            return tiles;
        }
    }
}
