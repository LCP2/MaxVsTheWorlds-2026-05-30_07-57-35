using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1164: Lee, 11 Oct — since MV-1087's kerbs went in around World 2 a8's sludge, there is no
    /// way past the vertical pipe barrier (<c>a8_cover4</c>/<c>a8_cover5</c>, x 189-191) without
    /// Teleport: that barrier blocks every z from 71 up to the area's own north wall, and the ONLY gap
    /// in it is the sludge channel itself (z 68-71) — which, before this ticket, is raw hazard the
    /// whole way across, not a route anyone would call walkable.
    ///
    /// Floods a8's own floor at Max's body radius (CharacterController "Max (Greybox)": radius 0.5,
    /// height 2 — same convention <see cref="MV900World2WalkabilityTests"/> already uses), but with a
    /// BLOCKED test that is hazard-aware, not collider-only: a cell is blocked when it carries a real,
    /// non-<see cref="StructuralFloor"/> collider (the ordinary MV900 definition) OR reads as floor
    /// sludge (<see cref="MapSludgeDamage.IsInFloorSludge"/>) at the height a mover actually stands at
    /// there — floor level, or a StructuralFloor piece's own resolved top when one covers the cell,
    /// the same height a real CharacterController's own collision response would rest it at — a cell
    /// no one would call walkable, not just one nothing physically stands in. MV900's own
    /// collider-only definition would NOT fail on base here (sludge carries no collider at all, only a
    /// damage/slow effect), so it is not reused as-is; this is a deliberately different, hazard-aware
    /// BLOCKED test the way AC1 asks for, not a re-run of MV900 with the same predicate.
    ///
    /// Fails on base commit (pre-MV-1164) because the channel at x 186-194 is still raw sludge the
    /// whole width across — see the fix comment for the quoted failure output. Passes once the
    /// authored long-crossing override (<c>world2_config.json</c>'s <c>a8_sludge2.crossingAt</c> /
    /// <c>crossingLength</c>) gives that stretch a dry plank, built through
    /// <see cref="MaxWorlds.Rendering.StormdrainKit"/>'s own MV-1087 crossing kit (not special-cased
    /// code) — see <see cref="WorldSludge.crossingAt"/>'s own doc.
    ///
    /// Replicators are destroyed straight after build, same reasoning
    /// <see cref="MV900World2WalkabilityTests"/> already documents: a Replicator standing in its own
    /// lane must never read as a permanent blocker. The waypoint this test proves reachable is the
    /// replicator's own AUTHORED position, not its (now-gone) body.
    /// </summary>
    public sealed class MV1164A8BottomChannelWalkabilityTests
    {
        private const float Cell = 0.25f;

        // Max's own CharacterController ("Max (Greybox)", Backyard_Slice.unity): radius 0.5, height 2 —
        // same convention MV900World2WalkabilityTests already uses.
        private const float Radius = 0.5f;
        private const float Height = 2f;
        private const float GroundY = 0f;

        private const float OpeningSearchRadiusCells = 6; // 1.5 m either side of an authored mouth/point

        [Test]
        public void A8_WestGateReachesReplicatorAndEastGate_AlongTheBottomChannel()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            WorldArea a8 = cfg.areas.FirstOrDefault(a => a.id == "a8");
            Assert.IsNotNull(a8, "a8 must exist in World 2's own shipped config for this test to mean anything");
            var areaRect = new Rect(a8.origin.x, a8.origin.z, a8.size.w, a8.size.d);

            MapLink g10Link = map.links.FirstOrDefault(l => l != null && l.gate == "g10");
            MapLink g11Link = map.links.FirstOrDefault(l => l != null && l.gate == "g11");
            Assert.IsNotNull(g10Link, "g10 must be an authored link for this test to mean anything");
            Assert.IsNotNull(g11Link, "g11 must be an authored link for this test to mean anything");
            Assert.IsTrue(MapGeometry.Doorway(map, g10Link, out bool g10AlongX, out float g10Coord, out Span g10Hole),
                "g10's own doorway failed to resolve");
            Assert.IsTrue(MapGeometry.Doorway(map, g11Link, out bool g11AlongX, out float g11Coord, out Span g11Hole),
                "g11's own doorway failed to resolve");
            Vector2 g10Mouth = g10AlongX ? new Vector2(g10Hole.Mid, g10Coord) : new Vector2(g10Coord, g10Hole.Mid);
            Vector2 g11Mouth = g11AlongX ? new Vector2(g11Hole.Mid, g11Coord) : new Vector2(g11Coord, g11Hole.Mid);

            MapEntity rep = map.Entity("a8_rep1");
            Assert.IsNotNull(rep, "a8_rep1 must be authored for this test to mean anything");
            var repPoint = new Vector2(rep.x, rep.z);

            var root = new GameObject("MV1164 a8 Root").transform;
            try
            {
                MapBuild built = MapRuntime.Build(map, root);
                StormdrainDressing.Dress(root, map, built.Cover);

                foreach (Replicator r in built.Replicators.ToList())
                    if (r != null) Object.DestroyImmediate(r.gameObject);

                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                var open = new HashSet<(int gx, int gz)>();
                int gxMin = Mathf.FloorToInt(areaRect.xMin / Cell);
                int gxMax = Mathf.CeilToInt(areaRect.xMax / Cell) - 1;
                int gzMin = Mathf.FloorToInt(areaRect.yMin / Cell);
                int gzMax = Mathf.CeilToInt(areaRect.yMax / Cell) - 1;
                for (int gx = gxMin; gx <= gxMax; gx++)
                {
                    float cx = (gx + 0.5f) * Cell;
                    for (int gz = gzMin; gz <= gzMax; gz++)
                    {
                        float cz = (gz + 0.5f) * Cell;
                        if (!IsBlocked(map, cx, cz)) open.Add((gx, gz));
                    }
                }

                (int, int)? g10Cell = FindNearestOpen(g10Mouth, open);
                (int, int)? g11Cell = FindNearestOpen(g11Mouth, open);
                (int, int)? repCell = FindNearestOpen(repPoint, open);
                Assert.IsNotNull(g10Cell, $"g10's own mouth {g10Mouth} has no walkable cell within 1.5 m of it");
                Assert.IsNotNull(g11Cell, $"g11's own mouth {g11Mouth} has no walkable cell within 1.5 m of it");
                Assert.IsNotNull(repCell, $"a8_rep1's own position {repPoint} has no walkable cell within 1.5 m of it");

                var visited = new HashSet<(int, int)>();
                var queue = new Queue<(int, int)>();
                visited.Add(g10Cell.Value);
                queue.Enqueue(g10Cell.Value);
                while (queue.Count > 0)
                {
                    (int gx, int gz) c = queue.Dequeue();
                    TryVisit((c.gx + 1, c.gz), open, visited, queue);
                    TryVisit((c.gx - 1, c.gz), open, visited, queue);
                    TryVisit((c.gx, c.gz + 1), open, visited, queue);
                    TryVisit((c.gx, c.gz - 1), open, visited, queue);
                }

                Assert.IsTrue(visited.Contains(repCell.Value),
                    "g10's own opening never reaches a8_rep1's own position -- the replicator side is unreachable");
                Assert.IsTrue(visited.Contains(g11Cell.Value),
                    "g10's own opening never reaches g11's own opening -- the replicator side is unreachable");
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        private static readonly Collider[] s_overlapBuffer = new Collider[32];

        private static bool IsBlocked(MapData map, float x, float z)
        {
            var p0 = new Vector3(x, GroundY + Radius, z);
            var p1 = new Vector3(x, GroundY + Height - Radius, z);
            // MV-1087 convention (MV900World2WalkabilityTests): a StructuralFloor-marked collider (a
            // channel kerb/crossing) is a mover's own CharacterController.stepOffset's job to climb,
            // never a wall this flat capsule-overlap check can see past. MV-1164: its own resolved top
            // is also where a REAL mover's feet actually rest while standing on it (ordinary collision
            // response lifts a CharacterController onto whatever it's grounded on) -- tracked here as
            // standY so the sludge check below samples the height a mover would actually occupy, not a
            // fixed floor-level Y that would make a dry plank over the channel still read as sludge.
            int count = Physics.OverlapCapsuleNonAlloc(p0, p1, Radius, s_overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            float standY = GroundY;
            for (int i = 0; i < count; i++)
            {
                Collider c = s_overlapBuffer[i];
                if (c.GetComponent<StructuralFloor>() == null) return true;
                standY = Mathf.Max(standY, c.bounds.max.y);
            }

            // MV-1164: unlike MV900, a cell no one would call "walkable" also includes raw floor
            // sludge -- the thing this ticket's own fix removes from the bottom channel's route.
            return MapSludgeDamage.IsInFloorSludge(map, new Vector3(x, standY, z));
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

        private static void TryVisit((int gx, int gz) k, HashSet<(int, int)> open, HashSet<(int, int)> visited,
            Queue<(int, int)> queue)
        {
            if (visited.Contains(k) || !open.Contains(k)) return;
            visited.Add(k);
            queue.Enqueue(k);
        }
    }
}
