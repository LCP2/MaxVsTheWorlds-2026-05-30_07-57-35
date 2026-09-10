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

            return new DressReport(kerbs, pipes, lamps, soffits, coverProps, tiles, kinds.Count);
        }

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
                    StormdrainKit.BuildStandpipe(parent, at, wallHeight, wallHeight);
                    return true;
                case CoverDressing.Hedge:
                    StormdrainKit.BuildDebrisRake(parent, at, size);
                    return true;
                case CoverDressing.Planter:
                    StormdrainKit.BuildSiltBin(parent, at, size);
                    return true;
                case CoverDressing.Shed:
                case CoverDressing.Machinery:
                    StormdrainKit.BuildPumpHousing(parent, at, size);
                    return true;
                default:
                    StormdrainKit.BuildSiltSacks(parent, at, size, seed);
                    return true;
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
