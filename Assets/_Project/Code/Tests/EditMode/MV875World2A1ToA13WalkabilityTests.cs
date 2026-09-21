using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-875 — proves gate-to-gate walkability the way a playtest would find it missing, not by
    /// reading an authored number back off itself (testing policy Tier 1, banned): for each of World
    /// 2's a1..a13, rasterise the area at 0.1 m, block every cell within 0.5 m (Max's own
    /// CharacterController radius, <c>Backyard_Slice.unity</c>) of cover, a replicator's own body, or a
    /// solid (non-doorway) stretch of the area's own wall, then flood fill from the mouth of the gate
    /// that leads IN from the previous area. On base commit 68d962b this fails for real: a11's east
    /// gate is sealed by a standpipe wall that leaves a 1-cell (1.0 m) slot at its south end — exactly
    /// Max's width, zero clearance (see the fix comment for the quoted failure). MV-875 repairs that
    /// fault, plus a9's and a12's own pre-existing siblings, as part of a full a1-a13 re-conversion from
    /// Lee's workbook.
    ///
    /// a13 is an authored maze of 1-cell (1.0 m) lanes against Max's 1.0 m width — deliberately not
    /// fully walkable, and that is Lee's design call, not a defect (ticket comment, 2026-09-21T17:39:
    /// "AC1 was wrong, not a13... a13 is permanently exempt from all connectivity, reachability and
    /// walkability assertions"). So for a13 this test keeps only the lane-clear-of-cover check (a
    /// replicator's IN lane must not overlap cover) and drops both the out-gate-reached and the
    /// replicator-lane-reached checks. a1..a12 keep the full check, unchanged: every non-DECK gate out
    /// of the area and every replicator's IN lane must be both reached from the area's own entry mouth
    /// and clear of cover.
    /// </summary>
    public sealed class MV875World2A1ToA13WalkabilityTests
    {
        private const float Cell = 0.1f;

        /// <summary>Max's CharacterController radius (Backyard_Slice.unity) — the clearance every
        /// blocked cell below is measured against.</summary>
        private const float Clearance = 0.5f;

        private const int LastArea = 13;

        [Test]
        public void EveryCombatAreaA1ToA13FloodFillsGateToGateAndReplicatorLanesAreReachedAndClear()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            WorldArea entry = cfg.areas.First(a => a.IsEntryRole);
            int areaCount = cfg.dials.areaCount;
            string entryZoneId = (entry.index >= 1 && entry.index <= areaCount) ? $"area{entry.index}" : entry.id;

            var failures = new List<string>();

            for (int index = 1; index <= LastArea; index++)
            {
                string zoneId = $"area{index}";
                MapZone zone = map.Zone(zoneId);
                Assert.IsNotNull(zone, $"MV-875: no zone for area index {index} ('{zoneId}')");

                var holesByWall = new Dictionary<Wall, List<Span>>();
                (string other, Vector2 mouth)? inGate = null;
                var outGates = new List<(string other, Vector2 mouth, string gateId)>();

                foreach (MapLink link in map.links)
                {
                    if (link == null || (link.from != zoneId && link.to != zoneId)) continue;

                    // A [DECK] gate is built above deck height and sealed below it by its own sill
                    // (MapGeometry.DeckGateSill) — at floor level, where Max actually walks, it is a
                    // wall, not a doorway.
                    MapEntity gate = map.Entity(link.gate);
                    if (gate == null || gate.level > 0) continue;

                    if (!MapGeometry.Doorway(map, link, out bool alongX, out float coord, out Span hole)) continue;

                    Wall wall = ResolveWall(zone, alongX, coord);
                    if (!holesByWall.TryGetValue(wall, out List<Span> spans)) holesByWall[wall] = spans = new List<Span>();
                    spans.Add(hole);

                    Vector2 mouth = alongX ? new Vector2(hole.Mid, coord) : new Vector2(coord, hole.Mid);
                    string other = link.from == zoneId ? link.to : link.from;

                    if (OrderRank(other, entryZoneId, areaCount) < index) inGate = (other, mouth);
                    else outGates.Add((other, mouth, gate.id));
                }

                Assert.IsTrue(inGate.HasValue, $"MV-875: area '{zoneId}' has no gate leading in from a lower-order area");

                var blockers = new List<ArenaCover>();
                foreach (MapEntity e in map.entities)
                {
                    if (e == null) continue;
                    if (e.Kind != EntityKind.Cover && e.Kind != EntityKind.Replicator) continue;
                    ArenaCover body = e.ToCover();
                    if (zone.Footprint.Overlaps(body.Footprint)) blockers.Add(body);
                }

                int nx = Mathf.Max(1, Mathf.RoundToInt(zone.width / Cell));
                int nz = Mathf.Max(1, Mathf.RoundToInt(zone.depth / Cell));
                var blocked = new bool[nx, nz];

                for (int ix = 0; ix < nx; ix++)
                {
                    float x = zone.XMin + (ix + 0.5f) * Cell;
                    for (int iz = 0; iz < nz; iz++)
                    {
                        float z = zone.ZMin + (iz + 0.5f) * Cell;
                        bool cellBlocked =
                            (x - zone.XMin < Clearance && !OpenAt(holesByWall, Wall.W, z)) ||
                            (zone.XMax - x < Clearance && !OpenAt(holesByWall, Wall.E, z)) ||
                            (z - zone.ZMin < Clearance && !OpenAt(holesByWall, Wall.S, x)) ||
                            (zone.ZMax - z < Clearance && !OpenAt(holesByWall, Wall.N, x));

                        if (!cellBlocked)
                        {
                            var p = new Vector2(x, z);
                            foreach (ArenaCover blocker in blockers)
                            {
                                if (blocker.DistanceTo(p) < Clearance) { cellBlocked = true; break; }
                            }
                        }

                        blocked[ix, iz] = cellBlocked;
                    }
                }

                int IxOf(float x) => Mathf.Clamp(Mathf.FloorToInt((x - zone.XMin) / Cell), 0, nx - 1);
                int IzOf(float z) => Mathf.Clamp(Mathf.FloorToInt((z - zone.ZMin) / Cell), 0, nz - 1);

                var visited = new bool[nx, nz];
                int startIx = IxOf(inGate.Value.mouth.x), startIz = IzOf(inGate.Value.mouth.y);

                if (blocked[startIx, startIz])
                {
                    failures.Add($"area '{zoneId}': its own entry mouth from '{inGate.Value.other}' " +
                                 $"at ({inGate.Value.mouth.x:0.##}, {inGate.Value.mouth.y:0.##}) is blocked");
                    continue;
                }

                visited[startIx, startIz] = true;
                var queue = new Queue<(int x, int z)>();
                queue.Enqueue((startIx, startIz));
                while (queue.Count > 0)
                {
                    (int cx, int cz) = queue.Dequeue();
                    TryVisit(cx + 1, cz, nx, nz, blocked, visited, queue);
                    TryVisit(cx - 1, cz, nx, nz, blocked, visited, queue);
                    TryVisit(cx, cz + 1, nx, nz, blocked, visited, queue);
                    TryVisit(cx, cz - 1, nx, nz, blocked, visited, queue);
                }

                // a13 is an authored maze, permanently exempt from gate-to-gate and replicator-lane
                // reachability (ticket comment, 2026-09-21T17:39): only the lane-clear-of-cover check
                // below still applies to it.
                bool checkReachability = index != LastArea;

                if (checkReachability)
                {
                    foreach (var (other, mouth, gateId) in outGates)
                    {
                        bool reached = visited[IxOf(mouth.x), IzOf(mouth.y)];
                        if (!reached)
                            failures.Add($"area '{zoneId}': gate '{gateId}' to '{other}' at " +
                                         $"({mouth.x:0.##}, {mouth.y:0.##}) is not reached from its own entry mouth");
                    }
                }

                foreach (MapEntity r in map.entities)
                {
                    if (r == null || r.Kind != EntityKind.Replicator || !zone.Contains(r.x, r.z)) continue;

                    Rect lane = InLaneRect(r);
                    bool laneClear = true;
                    foreach (MapEntity c in map.entities)
                    {
                        if (c == null || c.Kind != EntityKind.Cover) continue;
                        if (lane.Overlaps(c.ToCover().Footprint)) { laneClear = false; break; }
                    }

                    if (!checkReachability)
                    {
                        if (!laneClear)
                            failures.Add($"area '{zoneId}': replicator '{r.id}' IN lane clear=False");
                        continue;
                    }

                    // The lane's own midpoint (1.5 m out from the box face) — far enough from the box's
                    // own footprint that it is never blocked by the box it belongs to (the lane starts
                    // AT that face by definition), unlike either end of the 3 m lane.
                    Vector2 dir = FacingDir(r.facing);
                    Vector2 faceCenter = r.CenterXz + dir * (r.width * 0.5f);
                    Vector2 laneMid = faceCenter + dir * (MapValidation.ReplicatorLaneDepth * 0.5f);
                    int lix = IxOf(laneMid.x), liz = IzOf(laneMid.y);
                    bool laneReached = visited[lix, liz] && !blocked[lix, liz];

                    if (!laneClear || !laneReached)
                        failures.Add($"area '{zoneId}': replicator '{r.id}' IN lane clear={laneClear} reached={laneReached}");
                }
            }

            Assert.IsTrue(failures.Count == 0, "MV-875 walkability failures:\n" + string.Join("\n", failures));
        }

        private static void TryVisit(int x, int z, int nx, int nz, bool[,] blocked, bool[,] visited, Queue<(int, int)> queue)
        {
            if (x < 0 || z < 0 || x >= nx || z >= nz || visited[x, z] || blocked[x, z]) return;
            visited[x, z] = true;
            queue.Enqueue((x, z));
        }

        private static bool OpenAt(Dictionary<Wall, List<Span>> holesByWall, Wall wall, float coord)
        {
            if (!holesByWall.TryGetValue(wall, out List<Span> spans)) return false;
            foreach (Span s in spans) if (s.Contains(coord)) return true;
            return false;
        }

        private static Wall ResolveWall(MapZone zone, bool alongX, float coord)
        {
            const float eps = 0.05f;
            return alongX
                ? (Mathf.Abs(coord - zone.ZMin) <= eps ? Wall.S : Wall.N)
                : (Mathf.Abs(coord - zone.XMin) <= eps ? Wall.W : Wall.E);
        }

        private static int OrderRank(string zoneId, string entryZoneId, int areaCount)
        {
            if (zoneId == entryZoneId) return 0;
            if (zoneId.StartsWith("area", StringComparison.Ordinal) &&
                int.TryParse(zoneId.Substring(4), out int n) && n >= 1 && n <= areaCount)
                return n;
            return int.MaxValue;
        }

        /// <summary>Mirrors <see cref="MapValidation"/>'s own MV-860 IN-lane geometry (that method is
        /// private) — the lane a lured robot walks in on, in front of a Replicator's own IN face.</summary>
        private static Vector2 FacingDir(string facing) => facing switch
        {
            "N" => new Vector2(0f, 1f),
            "E" => new Vector2(1f, 0f),
            "W" => new Vector2(-1f, 0f),
            _ => new Vector2(0f, -1f),
        };

        private static Rect InLaneRect(MapEntity r)
        {
            Vector2 dir = FacingDir(r.facing);
            Vector2 faceCenter = r.CenterXz + dir * (r.width * 0.5f);
            Vector2 farCenter = faceCenter + dir * MapValidation.ReplicatorLaneDepth;
            bool alongZ = Mathf.Abs(dir.y) > 0.5f;
            float width = MapValidation.ReplicatorLaneWidth;
            return alongZ
                ? new Rect(r.CenterXz.x - width * 0.5f, Mathf.Min(faceCenter.y, farCenter.y), width, MapValidation.ReplicatorLaneDepth)
                : new Rect(Mathf.Min(faceCenter.x, farCenter.x), r.CenterXz.y - width * 0.5f, MapValidation.ReplicatorLaneDepth, width);
        }
    }
}
