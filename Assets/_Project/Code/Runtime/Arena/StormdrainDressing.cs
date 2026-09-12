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

            DressWallPanels(root, host, map);

            int coverProps = 0;
            var kinds = new HashSet<CoverDressing>();
            var props = new GameObject("Cover").transform;
            props.SetParent(root, false);

            if (cover != null)
            {
                foreach (CoverPiece piece in cover)
                {
                    if (piece.Body == null) continue;
                    if (!BuildFor(props, piece)) continue;

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

        /// <summary>Builds every floor-level zone's cast bays and cracks (MV-784, changes 1 and 3),
        /// panel joints (change 2), and silt/standing-water stains (change 4) — skipped for a
        /// <see cref="MapZone.level"/> &gt; 0 zone (a deck overlay shares its target's floor, MV-697, so
        /// it never gets a second pass of it).</summary>
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

                foreach (Bay bay in BayRects(zoneRect, zone.id))
                {
                    StormdrainKit.BuildBay(floorHost, bay.Rect, bay.Tone);
                    if (bay.HasCrack)
                        StormdrainKit.BuildCrack(floorHost,
                            new Vector3(bay.Rect.center.x, 0f, bay.Rect.center.y), bay.CrackHash);
                }

                foreach (Rect seg in JointRects(zoneRect, obstacles))
                    StormdrainKit.BuildPanelJoint(floorHost, seg);

                foreach (Stain silt in SiltRects(zoneRect, zone.id, obstacles))
                    StormdrainKit.BuildSiltStain(floorHost,
                        new Vector3(silt.Center.x, 0f, silt.Center.y), silt.CoreRadius, silt.Seed);

                foreach (Stain water in WaterRects(zoneRect, zone.id, obstacles))
                    StormdrainKit.BuildWaterStain(floorHost,
                        new Vector3(water.Center.x, 0f, water.Center.y), water.CoreRadius, water.Seed);
            }
        }

        // ---------------------------------------------------------------- cast bays and cracks (MV-784)

        private const float BayPitch = JointSpacing; // same 3.2 m grid the joints already phase off
        private const float BayInset = StormdrainKit.BayInset;
        private const float BayDarkThreshold = 0.33f;
        private const float BayLightThreshold = 0.78f;
        private const float BayCrackThreshold = 0.62f;

        /// <summary>One cast bay (MV-784, change 1): its own inset footprint, the tone its grid-coordinate
        /// hash resolved to, and whether a second, different-salted hash gave it a crack (change 3) —
        /// carrying that hash forward so the crack's own rotation is "the same hash", per the ticket.
        /// </summary>
        public readonly struct Bay
        {
            public readonly Rect Rect;
            public readonly Color Tone;
            public readonly bool HasCrack;
            public readonly float CrackHash;

            public Bay(Rect rect, Color tone, bool hasCrack, float crackHash)
            {
                Rect = rect; Tone = tone; HasCrack = hasCrack; CrackHash = crackHash;
            }
        }

        /// <summary>Every cast bay in one floor zone (MV-784, change 1) — a <see cref="BayPitch"/> grid
        /// local to the zone's own origin, so a zone's own bay count always matches
        /// floor(width/pitch) * floor(height/pitch) exactly (the ticket's own acceptance count), rather
        /// than a world-anchored grid that could clip a partial row/column at the zone edge. Each bay's
        /// tone and crack are both a hash of the bay's own grid coordinates AND the zone's id, so two
        /// zones never tile identically — never <see cref="UnityEngine.Random"/>, so the same map always
        /// casts the same bays.</summary>
        public static List<Bay> BayRects(Rect zone, string zoneId)
        {
            var result = new List<Bay>();
            int cols = Mathf.FloorToInt(zone.width / BayPitch);
            int rows = Mathf.FloorToInt(zone.height / BayPitch);
            if (cols <= 0 || rows <= 0) return result;

            int zoneSeed = DeterministicSeed(zoneId, zone);
            float size = BayPitch - BayInset;

            for (int col = 0; col < cols; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    var rect = new Rect(zone.xMin + col * BayPitch + BayInset * 0.5f,
                                         zone.yMin + row * BayPitch + BayInset * 0.5f, size, size);

                    float toneHash = BayHash(zoneSeed, col, row, 0);
                    Color tone = toneHash < BayDarkThreshold ? StormdrainKit.GroundDry
                               : toneHash > BayLightThreshold ? StormdrainKit.GroundAccent
                               : StormdrainKit.GroundBase;

                    float crackHash = BayHash(zoneSeed, col, row, 1);
                    result.Add(new Bay(rect, tone, crackHash > BayCrackThreshold, crackHash));
                }
            }
            return result;
        }

        /// <summary>Deterministic 0..1 from a zone seed, a bay's own grid coordinates, and a salt (never
        /// <see cref="UnityEngine.Random"/>) — same integer-mix idiom
        /// <c>MaxWorlds.Enemies.SludgePuddle.Hash01</c> already uses, extended to four inputs so the
        /// tone draw (salt 0) and the crack draw (salt 1) off the same bay never move in lockstep.
        /// </summary>
        private static float BayHash(int zoneSeed, int col, int row, int salt)
        {
            unchecked
            {
                int h = zoneSeed;
                h = h * 374761393 + col * 668265263;
                h = h * 1274126177 + row * 374761393;
                h = h * 668265263 + salt * 1013904223;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        // ---------------------------------------------------------------- silt/water stains (MV-784)

        private const int SiltGridSize = 3;  // 3x3 -> nine per area
        private const int WaterGridSize = 2; // 2x2 -> four per area
        private const int SiltSalt = 101;
        private const int WaterSalt = 202;

        /// <summary>One silt drift or standing-water pool's placement (MV-784, change 4): its centre,
        /// its own core radius, and the seed its builder derives every per-segment jitter from.</summary>
        public readonly struct Stain
        {
            public readonly Vector2 Center;
            public readonly float CoreRadius;
            public readonly float Seed;

            public Stain(Vector2 center, float coreRadius, float seed)
            {
                Center = center; CoreRadius = coreRadius; Seed = seed;
            }
        }

        /// <summary>Nine silt drifts per area (MV-784, change 4), one per cell of a 3x3 jittered grid
        /// across the zone so they never clump.</summary>
        public static List<Stain> SiltRects(Rect zone, string zoneId, IReadOnlyList<Rect> obstacles) =>
            StainGrid(zone, zoneId, obstacles, SiltGridSize, SiltSalt);

        /// <summary>Four standing-water pools per area (MV-784, change 4), one per cell of a 2x2
        /// jittered grid across the zone.</summary>
        public static List<Stain> WaterRects(Rect zone, string zoneId, IReadOnlyList<Rect> obstacles) =>
            StainGrid(zone, zoneId, obstacles, WaterGridSize, WaterSalt);

        private static List<Stain> StainGrid(Rect zone, string zoneId, IReadOnlyList<Rect> obstacles,
                                              int gridSize, int salt)
        {
            var result = new List<Stain>();
            int zoneSeed = DeterministicSeed(zoneId, zone) + salt;
            float cellW = zone.width / gridSize;
            float cellH = zone.height / gridSize;

            for (int cx = 0; cx < gridSize; cx++)
            {
                for (int cz = 0; cz < gridSize; cz++)
                {
                    int cellSeed = zoneSeed * 31 + cx * 7 + cz;
                    float radius = StormdrainKit.StainCoreRadiusMin + Frac(cellSeed, 3) *
                        (StormdrainKit.StainCoreRadiusMax - StormdrainKit.StainCoreRadiusMin);

                    var cell = new Rect(zone.xMin + cx * cellW, zone.yMin + cz * cellH, cellW, cellH);
                    if (TryPlaceStain(cell, radius, obstacles, cellSeed, out Vector2 center))
                        result.Add(new Stain(center, radius, cellSeed));
                }
            }
            return result;
        }

        private static bool TryPlaceStain(Rect cell, float radius, IReadOnlyList<Rect> obstacles, int seed,
                                          out Vector2 center)
        {
            float margin = Mathf.Min(Mathf.Max(radius, Mathf.Min(cell.width, cell.height) * 0.15f),
                                     Mathf.Min(cell.width, cell.height) * 0.49f);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                float tx = Frac(seed, attempt * 2 + 1);
                float tz = Frac(seed, attempt * 2 + 2);
                float cx = Mathf.Lerp(cell.xMin + margin, cell.xMax - margin, tx);
                float cz = Mathf.Lerp(cell.yMin + margin, cell.yMax - margin, tz);

                var footprint = new Rect(cx - radius, cz - radius, radius * 2f, radius * 2f);
                if (Overlaps(footprint, obstacles)) continue;

                center = new Vector2(cx, cz);
                return true;
            }

            center = default;
            return false;
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

        /// <summary>Maps a cover piece's authored dressing class onto its drain equivalent (MV-786:
        /// five turned forms — Standpipe cluster, Collapsed grating, Silt hopper, Pump set, Burst
        /// main). Every class has one — including <see cref="CoverDressing.None"/>, which here falls
        /// back to Burst main, same as anything that fails to parse. That is the difference from
        /// <see cref="ReefDressing"/>, which deliberately dresses only one class: World 3's ticket said
        /// place nothing where there is no equivalent, and the result is a world of grey boxes. World 2
        /// is not repeating that.</summary>
        private static bool BuildFor(Transform parent, CoverPiece piece)
        {
            ArenaCover c = piece.Cover;
            Vector3 at = new Vector3(c.CenterXz.x, 0f, c.CenterXz.y);
            Vector3 size = c.Size;

            switch (c.Dressing)
            {
                case CoverDressing.Tree:
                    GameObject standpipe = StormdrainKit.BuildStandpipe(parent, at, size);
                    standpipe.transform.rotation = Quaternion.Euler(0f, DeterministicYaw(at), 0f);
                    return true;
                case CoverDressing.Hedge:
                    GameObject grating = StormdrainKit.BuildCollapsedGrating(parent, at, size);
                    grating.transform.rotation = Quaternion.Euler(0f, DeterministicYaw(at), 0f);
                    return true;
                case CoverDressing.Planter:
                    GameObject hopper = StormdrainKit.BuildSiltHopper(parent, at, size);
                    hopper.transform.rotation = Quaternion.Euler(0f, DeterministicYaw(at), 0f);
                    return true;
                case CoverDressing.Shed:
                case CoverDressing.Machinery:
                    // Heavy fixed machinery, not loose debris — stays square.
                    StormdrainKit.BuildPumpHousing(parent, at, size);
                    return true;
                default:
                    GameObject burstMain = StormdrainKit.BuildBurstMain(parent, at, size);
                    burstMain.transform.rotation = Quaternion.Euler(0f, DeterministicYaw(at), 0f);
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

        /// <summary>MV-786, change 2: restructures every already-built <see cref="StructuralWall"/>
        /// from one long slab into panels, ribs, pilasters, a coping and a kerb — same "keep the
        /// collider, replace the art" contract the cover pass keeps. <see cref="MapGeometry.Walls"/> is
        /// a pure function of <paramref name="map"/>, so calling it again here reproduces the exact
        /// same segments <c>MapRuntime.Build</c> already built under <paramref name="host"/>, and their
        /// shared <see cref="WallSegment.Name"/> is what lines the two up.</summary>
        private static void DressWallPanels(Transform root, Transform host, MapData map)
        {
            List<WallSegment> segments = MapGeometry.Walls(map);
            if (segments.Count == 0) return;

            var byName = new Dictionary<string, StructuralWall>();
            foreach (StructuralWall wall in host.GetComponentsInChildren<StructuralWall>(true))
                byName[wall.gameObject.name] = wall;

            var panelHost = new GameObject("Wall Panels").transform;
            panelHost.SetParent(root, false);

            foreach (WallSegment seg in segments)
            {
                if (!byName.TryGetValue(seg.Name, out StructuralWall wall) || wall == null) continue;

                var rend = wall.GetComponent<Renderer>();
                Material wallMat = rend != null ? rend.sharedMaterial : null;
                if (rend != null) rend.enabled = false;

                StormdrainKit.BuildWallPanels(panelHost, seg.Center, seg.Size, seg.AlongX, wallMat);
            }
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
