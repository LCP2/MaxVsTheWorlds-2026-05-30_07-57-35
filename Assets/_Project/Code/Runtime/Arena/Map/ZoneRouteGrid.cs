using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// The way around a hedge, inside a single room (MV-476).
    ///
    /// <see cref="MapRoutes"/> answers "which doorway next" and says so out loud that it deliberately
    /// does NOT avoid cover — cover sits inside a room, it was sparse, and a chaser already rounded it
    /// (<see cref="MaxWorlds.Enemies.ObstacleSteering"/>). Areas now hold enough authored hedge to form
    /// a maze, so a beeline inside a room presses into a hedge instead of going round it. This is the
    /// thing that solves THAT, without replacing <see cref="MapRoutes"/>: it rasterises the current
    /// zone's own authored cover onto a coarse grid and runs A* across it, purely to get from wherever
    /// a robot stands to wherever <see cref="MapRoutes"/> says to head next.
    ///
    /// Not a NavMesh — no bake, no GameObject, no physics query, no clock. Just static maths over the
    /// same <see cref="MapData"/> everything else already reads, in the same idiom as
    /// <see cref="MapRoutes"/> itself: fully unit-testable without a single GameObject.
    /// </summary>
    public static class ZoneRouteGrid
    {
        /// <summary>Edge length of one occupancy cell, in metres. Coarse on purpose — this is "does a
        /// body fit here", not sub-metre placement.</summary>
        public const float CellSize = 0.5f;

        /// <summary>How far every obstacle is inflated so a path solved over cell CENTRES is one a
        /// body can actually walk without its edge clipping the real geometry — the widest melee
        /// archetype's own collider radius (Brute, 0.6 m, the same constant
        /// <see cref="MapRoutes.MinGateClearance"/> is derived from). Kept as a fixed number rather
        /// than a live reference for the same reason <c>MapRoutes.MinGateClearance</c> is: this class
        /// stays map maths, and enemy archetypes are Gameplay's concern.</summary>
        public const float ObstacleInflation = 0.6f;

        /// <summary>How many times a zone's occupancy grid has actually been rasterised from the map's
        /// cover entities. The performance claim is "once per zone per invalidation, not once per
        /// robot per frame" — this is how a test says so, the same idiom as <see cref="MapRoutes.Searches"/>.
        /// A* itself still runs fresh per call (the start is a moving robot, so there is nothing to
        /// cache there) — this counts only the expensive part, building the grid.</summary>
        public static int GridBuilds { get; private set; }

        private static MapData _solvedFor;
        private static readonly Dictionary<string, ZoneGrid> _grids = new Dictionary<string, ZoneGrid>(8);

        /// <summary>Drop every solved grid. A level that is rebuilt or re-gated has to be re-rasterised
        /// — <see cref="MaxWorlds.Enemies.EnemyNavigation"/> calls this everywhere it calls
        /// <see cref="MapRoutes.Forget"/>, so the two caches never disagree about what the level looks
        /// like right now.</summary>
        public static void Forget()
        {
            _solvedFor = null;
            _grids.Clear();
        }

        /// <summary>
        /// The next point to walk toward, inside <paramref name="zone"/>, to make progress from
        /// <paramref name="from"/> toward <paramref name="target"/> — both read as world XZ (x = world
        /// X, y = world Z, the same convention <see cref="MapRoutes"/> uses). <paramref name="target"/>
        /// need not itself be inside the zone (<see cref="MapRoutes"/> aims a cross-room waypoint
        /// through the wall into the next room on purpose, MV-493) — it is used UNCLAMPED for the
        /// line-of-sight check and as the answer itself; it is only clamped into the zone to pick an
        /// A* goal CELL when the grid solver actually has to route around cover, because a cell index
        /// has to land inside the grid it indexes. The clamp never reaches the return value.
        ///
        /// Returns <paramref name="target"/> unchanged — byte-identical, no clamp, no grid built at all
        /// — when nothing authored in this zone stands between the two points: decision #5, a solved
        /// path must never add a detour on open ground, and MV-493's fix on top of it, a robot must
        /// never be aimed at the wall line it is meant to walk through.
        ///
        /// Returns null when the zone's grid has no route at all between the two points — a sealed
        /// pocket, or a target buried inside inflated cover. The caller falls back to today's direct
        /// steering (decision #10); this class never keeps a robot waiting and never invents a point to
        /// send it to that isn't actually reachable.
        /// </summary>
        public static Vector2? NextStep(MapData map, MapZone zone, Vector2 from, Vector2 target)
        {
            if (map == null || zone == null) return null;

            ZoneGrid grid = GetOrBuild(map, zone);
            if (grid == null) return null;

            if (grid.LineClear(from, target)) return target;

            // Only reached when cover is actually in the way: pick the in-zone cell nearest the
            // doorway mouth (or wherever target actually sits) to route the A* search at — never the
            // through-doorway target itself, which by construction sits outside this zone.
            Vector3 clamped = zone.Clamp(new Vector3(target.x, 0f, target.y), 0f);
            var approach = new Vector2(clamped.x, clamped.z);

            List<Vector2Int> path = grid.FindPath(from, approach);
            if (path == null || path.Count == 0) return null;

            Vector2Int stepCell = path.Count > 1 ? path[1] : path[0];
            return grid.CellCenter(stepCell);
        }

        private static ZoneGrid GetOrBuild(MapData map, MapZone zone)
        {
            if (!ReferenceEquals(_solvedFor, map))
            {
                _grids.Clear();
                _solvedFor = map;
            }

            if (_grids.TryGetValue(zone.id, out ZoneGrid grid)) return grid;

            grid = ZoneGrid.Build(map, zone);
            _grids[zone.id] = grid;
            GridBuilds++;
            return grid;
        }

        /// <summary>One zone's rasterised cover, and the A* over it. Everything here is plain arrays
        /// and value maths — no GameObject ever enters this class.</summary>
        private sealed class ZoneGrid
        {
            private const float Sqrt2 = 1.41421356f;

            private readonly int _cols;
            private readonly int _rows;
            private readonly float _originX;
            private readonly float _originZ;
            private readonly bool[] _blocked;

            private ZoneGrid(int cols, int rows, float originX, float originZ, bool[] blocked)
            {
                _cols = cols; _rows = rows; _originX = originX; _originZ = originZ; _blocked = blocked;
            }

            public static ZoneGrid Build(MapData map, MapZone zone)
            {
                int cols = Mathf.Max(1, Mathf.CeilToInt(zone.width / CellSize));
                int rows = Mathf.Max(1, Mathf.CeilToInt(zone.depth / CellSize));
                var blocked = new bool[cols * rows];
                var grid = new ZoneGrid(cols, rows, zone.XMin, zone.ZMin, blocked);

                if (map.entities != null)
                {
                    foreach (MapEntity e in map.entities)
                    {
                        if (e == null || e.Kind != EntityKind.Cover) continue;
                        if (!zone.Contains(e.x, e.z)) continue;   // cover sits inside a room (MapRoutes)

                        Rect fp = e.ToCover().Footprint;
                        var inflated = new Rect(
                            fp.xMin - ObstacleInflation, fp.yMin - ObstacleInflation,
                            fp.width + ObstacleInflation * 2f, fp.height + ObstacleInflation * 2f);

                        grid.Rasterise(inflated);
                    }
                }

                return grid;
            }

            private void Rasterise(Rect inflated)
            {
                for (int row = 0; row < _rows; row++)
                for (int col = 0; col < _cols; col++)
                {
                    if (_blocked[Index(col, row)]) continue;
                    if (inflated.Contains(CellCenter(new Vector2Int(col, row)))) _blocked[Index(col, row)] = true;
                }
            }

            public Vector2 CellCenter(Vector2Int cell) =>
                new Vector2(_originX + (cell.x + 0.5f) * CellSize, _originZ + (cell.y + 0.5f) * CellSize);

            private Vector2Int CellOf(Vector2 world)
            {
                int col = Mathf.Clamp(Mathf.FloorToInt((world.x - _originX) / CellSize), 0, _cols - 1);
                int row = Mathf.Clamp(Mathf.FloorToInt((world.y - _originZ) / CellSize), 0, _rows - 1);
                return new Vector2Int(col, row);
            }

            private int Index(int col, int row) => row * _cols + col;
            private bool IsBlocked(int col, int row) => _blocked[Index(col, row)];
            private Vector2Int CellFromIndex(int idx) => new Vector2Int(idx % _cols, idx / _cols);

            /// <summary>True when a straight line from <paramref name="from"/> to <paramref name="to"/>
            /// crosses no blocked cell — sampled well inside a single cell's width so a wall it skims
            /// past can't be stepped over between samples.</summary>
            public bool LineClear(Vector2 from, Vector2 to)
            {
                float dist = Vector2.Distance(from, to);
                if (dist < 1e-4f) return true;

                int steps = Mathf.Max(1, Mathf.CeilToInt(dist / (CellSize * 0.5f)));
                for (int i = 0; i <= steps; i++)
                {
                    Vector2 p = Vector2.Lerp(from, to, (float)i / steps);
                    Vector2Int cell = CellOf(p);
                    if (IsBlocked(cell.x, cell.y)) return false;
                }
                return true;
            }

            /// <summary>8-connected A*, Euclidean heuristic, no corner-cutting: a diagonal step is only
            /// taken when both of its orthogonal flanks are open, so a path can never squeeze through
            /// the gap between two blocked cells that only touch at a corner. Returns the cell path
            /// from <paramref name="fromWorld"/>'s own cell to <paramref name="toWorld"/>'s, inclusive
            /// of both — or null if no such path exists at all.</summary>
            public List<Vector2Int> FindPath(Vector2 fromWorld, Vector2 toWorld)
            {
                Vector2Int start = CellOf(fromWorld);
                Vector2Int goal = CellOf(toWorld);

                if (start == goal) return new List<Vector2Int> { start };
                if (IsBlocked(goal.x, goal.y)) return null;

                int count = _cols * _rows;
                var gScore = new float[count];
                var cameFrom = new int[count];
                var visited = new bool[count];
                for (int i = 0; i < count; i++) { gScore[i] = float.PositiveInfinity; cameFrom[i] = -1; }

                int startIdx = Index(start.x, start.y);
                int goalIdx = Index(goal.x, goal.y);
                gScore[startIdx] = 0f;

                // (f, idx): idx is unique per node, so this never collides on equal f — a plain
                // balanced-tree open set with O(log n) insert/remove/min, no bespoke heap needed for a
                // grid this small (a fight room, not a level).
                var open = new SortedSet<(float f, int idx)> { (Heuristic(start, goal), startIdx) };

                while (open.Count > 0)
                {
                    (float f, int idx) current = open.Min;
                    open.Remove(current);

                    int curIdx = current.idx;
                    if (curIdx == goalIdx) return Reconstruct(cameFrom, curIdx);
                    if (visited[curIdx]) continue;
                    visited[curIdx] = true;

                    Vector2Int cur = CellFromIndex(curIdx);

                    for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;

                        int nc = cur.x + dx, nr = cur.y + dz;
                        if (nc < 0 || nc >= _cols || nr < 0 || nr >= _rows) continue;
                        if (IsBlocked(nc, nr)) continue;

                        // No corner-cutting: a diagonal move needs BOTH orthogonal flanks open, not
                        // just the diagonal target cell, or a path could thread the gap between two
                        // obstacles that only touch at their corners.
                        if (dx != 0 && dz != 0 && (IsBlocked(cur.x + dx, cur.y) || IsBlocked(cur.x, cur.y + dz)))
                            continue;

                        int neighborIdx = Index(nc, nr);
                        if (visited[neighborIdx]) continue;

                        float stepCost = (dx != 0 && dz != 0) ? CellSize * Sqrt2 : CellSize;
                        float tentativeG = gScore[curIdx] + stepCost;

                        if (tentativeG < gScore[neighborIdx])
                        {
                            var neighborCell = new Vector2Int(nc, nr);
                            if (!float.IsPositiveInfinity(gScore[neighborIdx]))
                                open.Remove((gScore[neighborIdx] + Heuristic(neighborCell, goal), neighborIdx));

                            gScore[neighborIdx] = tentativeG;
                            cameFrom[neighborIdx] = curIdx;
                            open.Add((tentativeG + Heuristic(neighborCell, goal), neighborIdx));
                        }
                    }
                }

                return null;   // no path at all — a sealed pocket
            }

            private static float Heuristic(Vector2Int a, Vector2Int b)
            {
                float dx = a.x - b.x, dz = a.y - b.y;
                return Mathf.Sqrt(dx * dx + dz * dz) * CellSize;
            }

            private List<Vector2Int> Reconstruct(int[] cameFrom, int endIdx)
            {
                var path = new List<Vector2Int>();
                for (int cur = endIdx; cur != -1; cur = cameFrom[cur]) path.Add(CellFromIndex(cur));
                path.Reverse();
                return path;
            }
        }
    }
}
