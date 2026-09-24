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
    /// MV-900 AC3 — proves Max can physically get from every opening of every World 2 area-level to
    /// every other opening in it, measured on the BUILT colliders (<see cref="MapRuntime.Build"/> +
    /// <see cref="StormdrainDressing.Dress"/>), not the authored MapData graph MV-875 already checks
    /// (Tier 2, resolved values — testing policy v2). Replicator bodies are destroyed straight after
    /// build ("Replicators removed", this ticket's own Robot placement note): a Replicator standing in
    /// a lane must never read as a permanent blocker.
    ///
    /// A "unit" is one area-level (scoping-pass comment, 2026-09-24): every non-overlay area's own
    /// floor (its own <see cref="MapZone"/>) is one unit; every area that authors its own
    /// <c>decks</c> gets one deck unit PER PHYSICALLY-CONNECTED CLUSTER of its own deck rects — an
    /// overlay area's decks (a15/a17) ARE that area's whole deck surface, an in-place deck (a10-a14,
    /// a16, a18, a21) is layered on the same zone as its own floor. Clustering (rather than one deck
    /// unit per area) matters for a21: its four corner pads are genuinely disjoint platforms, each
    /// reached only by its own dedicated ramp and never meant to interconnect — merging them into one
    /// unit and demanding mutual reachability was this test's own bug on the first pass, not a config
    /// defect (a17's bending walkway rects DO all touch and still resolve to a single cluster). A
    /// unit's openings are every gate mouth built at that level (floor gates for a floor unit,
    /// "[DECK]" gates for a deck unit — <see cref="MapEntity.level"/>), routed to whichever cluster's
    /// footprint actually contains the mouth, plus every ramp endpoint authored in that area
    /// (<see cref="MapGeometry.Ramps"/>'s bottom -&gt; the floor unit, top -&gt; its containing deck
    /// cluster). The walkable surface is rasterised on a 0.25 m grid and probed with
    /// <see cref="Physics.CheckCapsule"/> at Max's own radius/height (0.5 m / 2 m — "Max (Greybox)"'s
    /// CharacterController, <c>Backyard_Slice.unity</c>), then flood-filled; every opening must land in
    /// the same connected component as every other opening of its own unit.
    ///
    /// a13's MV-875 exemption from full reachability does NOT carry over here (this ticket's own AC3
    /// wording; scoping-pass comment #3) — it is asserted for real, because proving it now connects is
    /// this ticket's own point. Fails on 98b769e (pre-V8 <c>world2_config.json</c>): a13's west and east
    /// halves are sealed by <c>a13_cover1</c> (see the fix comment for the quoted failure). Passes once
    /// the shipped config removes that cover block (Lee's 2026-09-24 sign-off, carried unchanged through
    /// V9/V10 — only a13's Replicator layout moved between those revisions).
    /// </summary>
    public sealed class MV900World2WalkabilityTests
    {
        private const float Cell = 0.25f;

        // Max's own CharacterController ("Max (Greybox)", Backyard_Slice.unity): radius 0.5, height 2.
        private const float Radius = 0.5f;
        private const float Height = 2f;

        private const float OpeningSearchRadiusCells = 6; // 1.5 m either side of an authored mouth/endpoint

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

        private sealed class Unit
        {
            public string Key;
            public readonly List<(Rect rect, float groundY)> Patches = new List<(Rect, float)>();
            public readonly List<Vector2> Openings = new List<Vector2>();
        }

        [Test]
        public void EveryWorld2AreaLevelFloodFillsGateToGateAndRampToRamp()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            var root = new GameObject("MV900 World2 Root").transform;
            try
            {
                MapBuild built = MapRuntime.Build(map, root);
                StormdrainDressing.Dress(root, map, built.Cover);
                foreach (AreaGate gate in Object.FindObjectsByType<AreaGate>(FindObjectsSortMode.None))
                    gate.ApplyStormdrainGateSkin();

                foreach (Replicator r in built.Replicators.ToList())
                    if (r != null) Object.DestroyImmediate(r.gameObject);

                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                Dictionary<string, Unit> units = BuildUnits(cfg, map);
                var failures = new List<string>();
                foreach (Unit u in units.Values)
                {
                    if (u.Openings.Count == 0) continue; // nothing authored to prove reachable for this unit
                    RunUnit(u, failures);
                }

                Assert.IsTrue(failures.Count == 0, "MV-900 walkability failures:\n" + string.Join("\n", failures));
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        // ---------------------------------------------------------------- unit assembly

        private static Dictionary<string, Unit> BuildUnits(WorldConfig cfg, MapData map)
        {
            int areaCount = cfg.dials.areaCount;
            var zoneIdOf = new Dictionary<string, string>();
            foreach (WorldArea a in cfg.areas)
                zoneIdOf[a.id] = (a.index >= 1 && a.index <= areaCount) ? $"area{a.index}" : a.id;
            var ownerOfZone = new Dictionary<string, string>();
            foreach (var kv in zoneIdOf) ownerOfZone[kv.Value] = kv.Key;

            var units = new Dictionary<string, Unit>();

            // Floor units: every non-overlay area's own zone.
            foreach (WorldArea a in cfg.areas)
            {
                if (!string.IsNullOrEmpty(a.overlays)) continue;
                MapZone zone = map.Zone(zoneIdOf[a.id]);
                Assert.IsNotNull(zone, $"MV-900: no zone for area '{a.id}'");
                var u = new Unit { Key = $"{a.id}:floor" };
                u.Patches.Add((zone.Footprint, 0f));
                units[u.Key] = u;
            }

            // Deck units: every area that authors its own decks — overlay (a15/a17) or in-place
            // (a10-a14, a16, a18, a21) alike. An area's own `decks` array is NOT necessarily one
            // walkable unit: a21's four corner pads are genuinely disjoint platforms, each reached by
            // its own dedicated ramp and never meant to interconnect. Cluster each area's deck rects by
            // physical adjacency (touch/overlap) into separate units instead of lumping every deck an
            // area authors into one — a17's bending walkway rects DO all touch and still resolve to a
            // single connected unit, so this only changes behaviour for genuinely disjoint layouts.
            Dictionary<string, DeckSlab> deckSlabs = MapGeometry.Decks(map).ToDictionary(d => d.Id);
            var deckUnitsByArea = new Dictionary<string, List<Unit>>();
            foreach (WorldArea a in cfg.areas)
            {
                if (a.decks == null || a.decks.Length == 0) continue;
                var rects = new List<(Rect rect, float topY)>();
                foreach (WorldDeck d in a.decks)
                {
                    if (d == null || !deckSlabs.TryGetValue(d.id, out DeckSlab slab)) continue;
                    var rect = new Rect(slab.Center.x - slab.Size.x * 0.5f, slab.Center.z - slab.Size.z * 0.5f,
                                         slab.Size.x, slab.Size.z);
                    rects.Add((rect, slab.TopY));
                }
                if (rects.Count == 0) continue;

                int[] parent = Enumerable.Range(0, rects.Count).ToArray();
                int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
                const float touchEps = 0.1f;
                for (int i = 0; i < rects.Count; i++)
                {
                    for (int j = i + 1; j < rects.Count; j++)
                    {
                        Rect r1 = rects[i].rect, r2 = rects[j].rect;
                        bool touches = r1.xMin - touchEps <= r2.xMax && r2.xMin - touchEps <= r1.xMax &&
                                       r1.yMin - touchEps <= r2.yMax && r2.yMin - touchEps <= r1.yMax;
                        if (touches) parent[Find(i)] = Find(j);
                    }
                }

                var groups = new Dictionary<int, Unit>();
                for (int i = 0; i < rects.Count; i++)
                {
                    int root = Find(i);
                    if (!groups.TryGetValue(root, out Unit u))
                        groups[root] = u = new Unit { Key = $"{a.id}:deck[{root}]" };
                    u.Patches.Add(rects[i]);
                }
                deckUnitsByArea[a.id] = groups.Values.ToList();
                foreach (Unit u in groups.Values) units[u.Key] = u;
            }

            // Gate openings — floor gates feed the owner's single floor unit; "[DECK]" gates
            // (MapEntity.level > 0) feed whichever of the owner's deck-unit CLUSTERS actually contains
            // the mouth point.
            foreach (MapLink link in map.links)
            {
                if (link == null) continue;
                MapEntity gate = map.Entity(link.gate);
                if (gate == null) continue;
                if (!MapGeometry.Doorway(map, link, out bool alongX, out float coord, out Span hole)) continue;

                Vector2 mouth = alongX ? new Vector2(hole.Mid, coord) : new Vector2(coord, hole.Mid);
                if (gate.level > 0)
                {
                    AddDeckOpening(deckUnitsByArea, ownerOfZone, link.from, mouth);
                    AddDeckOpening(deckUnitsByArea, ownerOfZone, link.to, mouth);
                }
                else
                {
                    AddFloorOpening(units, ownerOfZone, link.from, mouth);
                    AddFloorOpening(units, ownerOfZone, link.to, mouth);
                }
            }

            // Ramp openings — the bottom feeds the ramp's own area's floor unit, the top feeds whichever
            // deck-unit cluster actually contains the ramp's top landing (WorldMapLoader only ever
            // matches a ramp against decks authored in its own area's `decks` array).
            foreach (RampSlab ramp in MapGeometry.Ramps(map))
            {
                WorldArea owner = cfg.areas.FirstOrDefault(a =>
                    (a.ramps ?? System.Array.Empty<WorldRamp>()).Any(r => r != null && r.id == ramp.Id));
                if (owner == null) continue;
                if (units.TryGetValue($"{owner.id}:floor", out Unit floorUnit))
                    floorUnit.Openings.Add(new Vector2(ramp.BottomCenter.x, ramp.BottomCenter.z));
                if (deckUnitsByArea.TryGetValue(owner.id, out List<Unit> deckUnits))
                    AddToNearestCluster(deckUnits, new Vector2(ramp.TopCenter.x, ramp.TopCenter.z));
            }

            return units;
        }

        private static void AddFloorOpening(Dictionary<string, Unit> units, Dictionary<string, string> ownerOfZone,
            string zoneId, Vector2 point)
        {
            if (!ownerOfZone.TryGetValue(zoneId, out string ownerId)) return;
            if (units.TryGetValue($"{ownerId}:floor", out Unit u)) u.Openings.Add(point);
        }

        private static void AddDeckOpening(Dictionary<string, List<Unit>> deckUnitsByArea,
            Dictionary<string, string> ownerOfZone, string zoneId, Vector2 point)
        {
            if (!ownerOfZone.TryGetValue(zoneId, out string ownerId)) return;
            if (!deckUnitsByArea.TryGetValue(ownerId, out List<Unit> deckUnits)) return;
            AddToNearestCluster(deckUnits, point);
        }

        /// <summary>A gate mouth or ramp landing belongs to whichever cluster's own footprint actually
        /// contains it; falling back to the nearest cluster covers float-rounding at a shared edge.</summary>
        private static void AddToNearestCluster(List<Unit> deckUnits, Vector2 point)
        {
            Unit owner = deckUnits.FirstOrDefault(u => u.Patches.Any(p => p.rect.Contains(point)));
            if (owner == null)
            {
                owner = deckUnits
                    .OrderBy(u => u.Patches.Min(p => DistanceToRect(p.rect, point)))
                    .FirstOrDefault();
            }
            owner?.Openings.Add(point);
        }

        private static float DistanceToRect(Rect r, Vector2 p)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
            float dz = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ---------------------------------------------------------------- per-unit flood fill

        private static void RunUnit(Unit u, List<string> failures)
        {
            var open = new HashSet<(int gx, int gz)>();
            var cellY = new Dictionary<(int gx, int gz), float>();

            foreach (var (rect, groundY) in u.Patches)
            {
                int gxMin = Mathf.FloorToInt(rect.xMin / Cell);
                int gxMax = Mathf.CeilToInt(rect.xMax / Cell) - 1;
                int gzMin = Mathf.FloorToInt(rect.yMin / Cell);
                int gzMax = Mathf.CeilToInt(rect.yMax / Cell) - 1;

                for (int gx = gxMin; gx <= gxMax; gx++)
                {
                    float cx = (gx + 0.5f) * Cell;
                    for (int gz = gzMin; gz <= gzMax; gz++)
                    {
                        float cz = (gz + 0.5f) * Cell;
                        if (!rect.Contains(new Vector2(cx, cz))) continue;
                        var key = (gx, gz);
                        if (cellY.ContainsKey(key)) continue; // an earlier patch already claimed this cell
                        cellY[key] = groundY;
                        if (!IsBlocked(cx, cz, groundY)) open.Add(key);
                    }
                }
            }

            var visited = new HashSet<(int gx, int gz)>();
            var starts = new List<((int gx, int gz) key, Vector2 opening)>();

            foreach (Vector2 opening in u.Openings)
            {
                (int gx, int gz)? found = FindNearestOpen(opening, open);
                if (found == null)
                {
                    failures.Add($"unit '{u.Key}': opening at ({opening.x:0.##}, {opening.y:0.##}) " +
                                 "has no walkable cell within 1.5 m of it");
                    continue;
                }
                starts.Add((found.Value, opening));
            }

            if (starts.Count == 0) return;

            var queue = new Queue<(int gx, int gz)>();
            visited.Add(starts[0].key);
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
                    failures.Add($"unit '{u.Key}': opening at ({opening.x:0.##}, {opening.y:0.##}) is not reached " +
                                 $"from its own opening at ({starts[0].opening.x:0.##}, {starts[0].opening.y:0.##})");
            }
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

        private static bool IsBlocked(float x, float z, float groundY)
        {
            var p0 = new Vector3(x, groundY + Radius, z);
            var p1 = new Vector3(x, groundY + Height - Radius, z);
            return Physics.CheckCapsule(p0, p1, Radius, ~0, QueryTriggerInteraction.Ignore);
        }
    }
}
