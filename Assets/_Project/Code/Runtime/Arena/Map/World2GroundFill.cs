using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// MV-1112 (Lee, 2026-10-06): "If World 2 is a sewer, the area to the left of start should be
    /// solid ground." Standing in World 2's entry stub, everything beyond its wall used to be the
    /// camera's own clear colour -- a flat blue-violet void, because nothing ever built geometry
    /// outside each area's own footprint. This fills every square metre of World 2 that is not inside
    /// a walkable area with a solid ground mass, flush with the existing floor slab at y = 0 and
    /// topped at the map's own <see cref="MapData.wallHeight"/>: around the whole layout (at least
    /// <see cref="ExtendMargin"/> m past the outermost wall, on every side) and in every gap between
    /// areas that are not adjacent -- which already covers both world-join corridors' World 2 ends
    /// and the entry stub's own surroundings, since those are nothing more than "outside every zone's
    /// own footprint" from this builder's point of view; no corridor-specific case is needed.
    ///
    /// Each fill slab stays its own renderer rather than one <c>Mesh.CombineMeshes</c> result (the
    /// technique <see cref="MapStaticBatchRoot"/> itself uses, MV-934): a combined mesh's own
    /// <c>Renderer.bounds</c> collapses to a single bounding box, which would silently claim coverage
    /// over every gap this builder deliberately leaves open (an area's own open-air interior) the
    /// moment two slabs either side of that gap shared one mesh. Every slab shares one of two
    /// materials, so URP's own SRP Batcher already folds them into a small, fixed number of draw calls
    /// without needing that merge. Static (<c>isStatic</c>) and built with no collider at all (never
    /// even an instantiated one to strip) -- same contract <see cref="MaxWorlds.Intro.WorldJoinSequence"/>'s
    /// own ground apron keeps for the same reason: decoration only, never reachable by Max or a robot
    /// (the walls already block), never re-tinted by the world's own biome sweep
    /// (<see cref="KeepsOwnMaterial"/>).
    ///
    /// World 2 only -- called once from <see cref="MaxWorlds.Arena.BackyardPath"/>'s own World-2
    /// dressing branch, the same place <see cref="StormdrainDressing.Dress"/> is. Worlds 1 and 3 never
    /// call this.
    /// </summary>
    public static class World2GroundFill
    {
        /// <summary>At least this far past the outermost wall, on every side (ticket's own number).</summary>
        public const float ExtendMargin = 40f;

        /// <summary>Width of the lighter strip where the fill meets a wall's own top (ticket's own number).</summary>
        public const float CopingWidth = 0.3f;

        private const float CopingThickness = 0.08f;

        /// <summary>Sits a hair proud of the fill's own top so the two faces are never exactly
        /// coplanar -- the standard anti-z-fight margin this codebase already reaches for elsewhere
        /// (e.g. <c>WorldJoinSequence</c>'s own proud offsets). Small enough that the coping still
        /// reads as flush with the wall top, within AC1's own 0.02 m tolerance on where the ground
        /// mass's top sits.</summary>
        private const float CopingProud = 0.01f;

        public static List<GameObject> Build(MapData map, Transform parent)
        {
            var built = new List<GameObject>();
            if (map?.zones == null || map.zones.Length == 0) return built;

            Rect bounds = map.Bounds();
            var outer = new Rect(bounds.xMin - ExtendMargin, bounds.yMin - ExtendMargin,
                bounds.width + ExtendMargin * 2f, bounds.height + ExtendMargin * 2f);

            // MV-697: an overlay zone (level > 0, a deck built as a same-footprint overlay of another
            // zone) shares its base zone's own XZ footprint exactly -- excluding the level-0 zone
            // already excludes its overlay, so overlays are skipped here the same way
            // MapGeometry.Walls skips them.
            var footprints = new List<Rect>();
            foreach (MapZone z in map.zones)
            {
                if (z == null || z.level > 0) continue;
                float m = map.wallThickness * 0.5f;
                footprints.Add(new Rect(z.XMin - m, z.ZMin - m, z.width + m * 2f, z.depth + m * 2f));
            }

            var root = new GameObject("World 2 Ground Fill").transform;
            root.SetParent(parent, false);

            Color fillColor = Darken(BiomePalette.Stormdrain.Wall, 0.7f);
            Material fillMat = MaterialLibrary.Tinted(SurfaceKind.Wall, fillColor);
            Material copingMat = MaterialLibrary.Tinted(SurfaceKind.Wall, BiomePalette.Stormdrain.Wall);

            foreach ((Vector2 min, Vector2 max) in FillRects(outer, footprints))
            {
                var size = new Vector3(max.x - min.x, map.wallHeight, max.y - min.y);
                var center = new Vector3((min.x + max.x) * 0.5f, map.wallHeight * 0.5f, (min.y + max.y) * 0.5f);
                built.Add(BuildBox(root, "Ground Fill", center, size, fillMat));
            }

            foreach (WallFace face in MapGeometry.Faces(map))
            {
                // Only the outer hull gets a coping -- a party wall between two rooms has no
                // "outside" to cap, and a [DECK] gate sill (MV-859) is always authored with a room on
                // both sides, so it never reaches here either.
                if (face.FacesRoom) continue;
                if (face.Length <= 0.01f) continue;

                Vector2 mid = (face.A + face.B) * 0.5f + face.Out * (CopingWidth * 0.5f);
                bool alongX = Mathf.Abs(face.Direction.x) > Mathf.Abs(face.Direction.y);
                Vector3 size = alongX
                    ? new Vector3(face.Length, CopingThickness, CopingWidth)
                    : new Vector3(CopingWidth, CopingThickness, face.Length);
                var center = new Vector3(mid.x, map.wallHeight + CopingProud - CopingThickness * 0.5f, mid.y);
                built.Add(BuildBox(root, "Ground Fill Coping", center, size, copingMat));
            }

            return built;
        }

        private static Color Darken(Color c, float factor) => new Color(c.r * factor, c.g * factor, c.b * factor, c.a);

        /// <summary>Axis-aligned decomposition of <paramref name="outer"/> minus every rect in
        /// <paramref name="holes"/>: every X/Z boundary any hole introduces becomes a grid line, each
        /// resulting cell is kept only if its own centre falls outside every hole, and adjacent kept
        /// cells on the same Z band are merged into one run -- the same "chop at every line, merge
        /// what's left" idea <see cref="MapGeometry.Walls"/> itself solves the wall layout with, one
        /// dimension up.</summary>
        private static IEnumerable<(Vector2 min, Vector2 max)> FillRects(Rect outer, List<Rect> holes)
        {
            var xs = new List<float> { outer.xMin, outer.xMax };
            var zs = new List<float> { outer.yMin, outer.yMax };
            foreach (Rect h in holes)
            {
                if (h.xMin > outer.xMin && h.xMin < outer.xMax) xs.Add(h.xMin);
                if (h.xMax > outer.xMin && h.xMax < outer.xMax) xs.Add(h.xMax);
                if (h.yMin > outer.yMin && h.yMin < outer.yMax) zs.Add(h.yMin);
                if (h.yMax > outer.yMin && h.yMax < outer.yMax) zs.Add(h.yMax);
            }
            xs = xs.Distinct().OrderBy(v => v).ToList();
            zs = zs.Distinct().OrderBy(v => v).ToList();

            for (int zi = 0; zi < zs.Count - 1; zi++)
            {
                float z0 = zs[zi], z1 = zs[zi + 1];
                if (z1 - z0 <= 0.001f) continue;
                float zMid = (z0 + z1) * 0.5f;

                int runStart = -1;
                for (int xi = 0; xi < xs.Count; xi++)
                {
                    bool fill = false;
                    if (xi < xs.Count - 1)
                    {
                        float x0 = xs[xi], x1 = xs[xi + 1];
                        if (x1 - x0 > 0.001f)
                        {
                            float xMid = (x0 + x1) * 0.5f;
                            fill = !holes.Any(h => h.Contains(new Vector2(xMid, zMid)));
                        }
                    }

                    if (fill)
                    {
                        if (runStart < 0) runStart = xi;
                    }
                    else if (runStart >= 0)
                    {
                        yield return (new Vector2(xs[runStart], z0), new Vector2(xs[xi], z1));
                        runStart = -1;
                    }
                }
            }
        }

        private static GameObject BuildBox(Transform root, string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.isStatic = true;
            go.transform.SetParent(root, worldPositionStays: true);
            go.transform.position = center;
            go.transform.localScale = size;

            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Object.Destroy(collider);
                else Object.DestroyImmediate(collider);
            }

            var rend = go.GetComponent<MeshRenderer>();
            if (mat != null) rend.sharedMaterial = mat;
            go.AddComponent<KeepsOwnMaterial>();

            return go;
        }
    }
}
