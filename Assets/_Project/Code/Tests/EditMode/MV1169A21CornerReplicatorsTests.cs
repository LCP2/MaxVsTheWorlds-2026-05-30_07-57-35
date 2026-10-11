using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1169 AC1 — World 2's a21 ("The Wet Well") previously authored zero Replicators. Proves the
    /// shipped config now gives it one per corner (built through the real loader —
    /// <see cref="WorldMapLoader.TryLoad"/>, Tier 2 resolved values, not authored numbers read back at
    /// themselves): one Replicator per quadrant within 10 m of that quadrant's own area corner, none
    /// overlapping a21's own sludge/deck/ramp/cover geometry, and each one's intake point reachable by
    /// a flood fill of the built colliders at Max's radius, starting just inside the west gate (g24).
    ///
    /// Fails on 06c0566 (pre-MV-1169 world2_config.json, a21 authors no Replicators) — the
    /// <c>reps.Count == 4</c> assertion fails with "Expected: 4 But was: 0". See the fix comment for the
    /// quoted failure.
    /// </summary>
    public sealed class MV1169A21CornerReplicatorsTests
    {
        private const float Cell = 0.25f;

        // Max's own CharacterController ("Max (Greybox)", Backyard_Slice.unity), same as MV-900.
        private const float Radius = 0.5f;
        private const float Height = 2f;

        private const float OpeningSearchRadiusCells = 6;
        private const float CornerBudget = 10f;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        [Test]
        public void A21HasFourCornerReplicatorsClearOfHazardsAndReachableFromG24()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            WorldArea a21 = cfg.areas.FirstOrDefault(a => a.id == "a21");
            Assert.IsNotNull(a21, "MV-1169: world2_config.json must author area 'a21'");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            List<MapEntity> reps = MapValidation.Kind(map, EntityKind.Replicator)
                .Where(r => r.id != null && r.id.StartsWith("a21_rep"))
                .OrderBy(r => r.id)
                .ToList();
            Assert.AreEqual(4, reps.Count, "MV-1169: a21 must have exactly 4 Replicators, one per corner");

            // ---------------------------------------------------------------- quadrant + corner distance
            Vector2 center = a21.CenterXz;
            Vector2[] corners =
            {
                new Vector2(a21.XMin, a21.ZMin), // quadrant 0: SW
                new Vector2(a21.XMax, a21.ZMin), // quadrant 1: SE
                new Vector2(a21.XMin, a21.ZMax), // quadrant 2: NW
                new Vector2(a21.XMax, a21.ZMax), // quadrant 3: NE
            };

            var quadrantsUsed = new HashSet<int>();
            var violations = new List<string>();
            foreach (MapEntity r in reps)
            {
                Vector2 p = r.CenterXz;
                int quadrant = (p.x >= center.x ? 1 : 0) + (p.y >= center.y ? 2 : 0);
                float dist = Vector2.Distance(p, corners[quadrant]);
                if (dist > CornerBudget)
                {
                    violations.Add($"'{r.id}' at ({p.x:0.#}, {p.y:0.#}) is {dist:0.##} m from its quadrant " +
                                   $"corner ({corners[quadrant].x:0.#}, {corners[quadrant].y:0.#}) — must be " +
                                   $"within {CornerBudget:0.#} m");
                }
                quadrantsUsed.Add(quadrant);
            }
            Assert.AreEqual(4, quadrantsUsed.Count,
                "MV-1169: a21's four Replicators must occupy four distinct quadrants, not double up on one corner");

            // ---------------------------------------------------------------- clear of sludge/decks/ramps/cover
            List<Rect> sludgeRects = map.SludgeEntities()
                .Where(e => e.id != null && e.id.StartsWith("a21_"))
                .Select(e => e.ToCover().Footprint).ToList();
            List<Rect> deckRects = MapGeometry.Decks(map)
                .Where(d => d.Id != null && d.Id.StartsWith("a21_"))
                .Select(d => new Rect(d.Center.x - d.Size.x * 0.5f, d.Center.z - d.Size.z * 0.5f, d.Size.x, d.Size.z))
                .ToList();
            List<Rect> rampRects = (a21.ramps ?? System.Array.Empty<WorldRamp>())
                .Where(r => r != null)
                .Select(r => a21.WorldRectOf(r.x, r.z, r.w, r.d))
                .ToList();
            List<Rect> coverRects = MapValidation.Kind(map, EntityKind.Cover)
                .Where(c => c.id != null && c.id.StartsWith("a21_"))
                .Select(c => c.ToCover().Footprint)
                .ToList();

            foreach (MapEntity r in reps)
            {
                Rect footprint = r.ToCover().Footprint;
                CheckNoOverlap(r.id, "sludge", footprint, sludgeRects, violations);
                CheckNoOverlap(r.id, "a deck", footprint, deckRects, violations);
                CheckNoOverlap(r.id, "a ramp", footprint, rampRects, violations);
                CheckNoOverlap(r.id, "cover", footprint, coverRects, violations);
            }

            Assert.IsTrue(violations.Count == 0,
                "MV-1169 a21 Replicator placement failures:\n" + string.Join("\n", violations));

            // ---------------------------------------------------------------- reachable from g24
            MapZone floor = map.Zone($"area{a21.index}");
            Assert.IsNotNull(floor, $"MV-1169: zone 'area{a21.index}' for a21 was never built");

            Vector2? gateMouth = FindGateMouth(map, "g24");
            Assert.IsNotNull(gateMouth, "MV-1169: gate 'g24' (a21's west gate) must resolve a doorway");

            var root = new GameObject("MV1169 World2 a21 Root").transform;
            try
            {
                MapBuild built = MapRuntime.Build(map, root);
                StormdrainDressing.Dress(root, map, built.Cover);
                foreach (AreaGate gate in Object.FindObjectsByType<AreaGate>(FindObjectsSortMode.None))
                    gate.ApplyStormdrainGateSkin();

                // A Replicator standing in a lane must never read as a permanent blocker (MV-900).
                foreach (Replicator rep in built.Replicators.ToList())
                    if (rep != null) Object.DestroyImmediate(rep.gameObject);

                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                var openings = new List<Vector2> { gateMouth.Value };
                foreach (MapEntity r in reps) openings.Add(IntakePoint(r));

                List<string> reachFailures = RunFloodFill(floor.Footprint, openings);
                Assert.IsTrue(reachFailures.Count == 0,
                    "MV-1169 a21 reachability failures:\n" + string.Join("\n", reachFailures));
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        private static void CheckNoOverlap(string id, string label, Rect footprint, List<Rect> zones, List<string> violations)
        {
            foreach (Rect z in zones)
            {
                if (footprint.Overlaps(z))
                {
                    violations.Add($"'{id}' at {footprint.center} overlaps {label} at {z.center}");
                    return;
                }
            }
        }

        /// <summary>The point just outside a Replicator's own IN face — where Max/a robot actually
        /// stands to queue — matching <see cref="WorldReplicator.facing"/>'s own convention.</summary>
        private static Vector2 IntakePoint(MapEntity r)
        {
            Vector2 dir = r.facing switch
            {
                "N" => new Vector2(0f, 1f),
                "E" => new Vector2(1f, 0f),
                "W" => new Vector2(-1f, 0f),
                _ => new Vector2(0f, -1f),
            };
            return r.CenterXz + dir * (r.width * 0.5f + 0.5f);
        }

        private static Vector2? FindGateMouth(MapData map, string gateId)
        {
            foreach (MapLink link in map.links)
            {
                if (link == null || link.gate != gateId) continue;
                if (!MapGeometry.Doorway(map, link, out bool alongX, out float coord, out Span hole)) continue;
                return alongX ? new Vector2(hole.Mid, coord) : new Vector2(coord, hole.Mid);
            }
            return null;
        }

        // ---------------------------------------------------------------- single-patch flood fill
        // Same rasterise-then-BFS technique as MV900World2WalkabilityTests, scoped to one area's own
        // floor rect instead of every World 2 unit — a21's own floor is one contiguous MapZone, so one
        // patch covers it.
        private static List<string> RunFloodFill(Rect floorRect, List<Vector2> openings)
        {
            var failures = new List<string>();
            var open = new HashSet<(int gx, int gz)>();

            int gxMin = Mathf.FloorToInt(floorRect.xMin / Cell);
            int gxMax = Mathf.CeilToInt(floorRect.xMax / Cell) - 1;
            int gzMin = Mathf.FloorToInt(floorRect.yMin / Cell);
            int gzMax = Mathf.CeilToInt(floorRect.yMax / Cell) - 1;

            for (int gx = gxMin; gx <= gxMax; gx++)
            {
                float cx = (gx + 0.5f) * Cell;
                for (int gz = gzMin; gz <= gzMax; gz++)
                {
                    float cz = (gz + 0.5f) * Cell;
                    if (!floorRect.Contains(new Vector2(cx, cz))) continue;
                    if (!IsBlocked(cx, cz)) open.Add((gx, gz));
                }
            }

            var starts = new List<((int gx, int gz) key, Vector2 opening)>();
            foreach (Vector2 opening in openings)
            {
                (int gx, int gz)? found = FindNearestOpen(opening, open);
                if (found == null)
                {
                    failures.Add($"opening at ({opening.x:0.##}, {opening.y:0.##}) has no walkable cell within 1.5 m of it");
                    continue;
                }
                starts.Add((found.Value, opening));
            }
            if (starts.Count == 0) return failures;

            var visited = new HashSet<(int, int)> { starts[0].key };
            var queue = new Queue<(int, int)>();
            queue.Enqueue(starts[0].key);
            while (queue.Count > 0)
            {
                (int gx, int gz) c = queue.Dequeue();
                TryVisit((c.gx + 1, c.gz), open, visited, queue);
                TryVisit((c.gx - 1, c.gz), open, visited, queue);
                TryVisit((c.gx, c.gz + 1), open, visited, queue);
                TryVisit((c.gx, c.gz - 1), open, visited, queue);
            }

            foreach (var (key, opening) in starts)
            {
                if (!visited.Contains(key))
                    failures.Add($"opening at ({opening.x:0.##}, {opening.y:0.##}) is not reached from g24");
            }
            return failures;
        }

        private static void TryVisit((int gx, int gz) k, HashSet<(int, int)> open, HashSet<(int, int)> visited,
            Queue<(int, int)> queue)
        {
            if (visited.Contains(k) || !open.Contains(k)) return;
            visited.Add(k);
            queue.Enqueue(k);
        }

        private static (int gx, int gz)? FindNearestOpen(Vector2 point, HashSet<(int, int)> open)
        {
            int cgx = Mathf.FloorToInt(point.x / Cell);
            int cgz = Mathf.FloorToInt(point.y / Cell);
            var center = (cgx, cgz);
            if (open.Contains(center)) return center;

            for (int ring = 1; ring <= OpeningSearchRadiusCells; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    for (int dz = -ring; dz <= ring; dz++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;
                        var k = (cgx + dx, cgz + dz);
                        if (open.Contains(k)) return k;
                    }
                }
            }
            return null;
        }

        private static readonly Collider[] s_overlapBuffer = new Collider[32];

        private static bool IsBlocked(float x, float z)
        {
            var p0 = new Vector3(x, Radius, z);
            var p1 = new Vector3(x, Height - Radius, z);
            // MV-1087: a StructuralFloor-marked collider is a mover's own stepOffset's job to climb,
            // not a wall this flat capsule-overlap check can see past (same exemption as MV-900).
            int count = Physics.OverlapCapsuleNonAlloc(p0, p1, Radius, s_overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (s_overlapBuffer[i].GetComponent<StructuralFloor>() == null) return true;
            return false;
        }
    }
}
