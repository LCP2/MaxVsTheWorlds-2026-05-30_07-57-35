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
    /// MV-1232 (one new EditMode test, MV-465 Rule 1) — the Mothership's Hangar Bay, area 1, built for
    /// real. Lee's own design image (<c>MV-1232.png</c>, BUILD MODE: INDICATIVE) is the layout's source
    /// of truth; the ticket's own prose gives exact numbers for the gates, the two dropships and the
    /// cradle, and counts for the garrison roster.
    ///
    /// Fails on base commit 881c23e: <c>mothership_config.json</c>'s "a1" is still MV-1225's placeholder
    /// (48x30 m, no cover/garrison/cradle at all), so every count below reads 0 on that commit; the
    /// cradle assertions also fail to COMPILE there (no <c>WorldArea.cradles</c>, <c>WorldCradle</c>,
    /// <c>MapBuild.Cradles</c> or <c>AssemblyCradle</c>-from-config exist yet).
    ///
    /// Tier 2 (resolved values), testing policy v2: cover/cradle positions are read off the BUILT
    /// collider bounds (<see cref="MapRuntime.Build"/> + the real <see cref="ShipDressing.Dress"/>
    /// pass), never the raw authored JSON re-read against itself; the flood fill walks the actually
    /// built physics colliders at Max's own CharacterController radius/height — the same idiom
    /// <c>MV900World2WalkabilityTests</c> established. The garrison kind-count check reads the real
    /// shipped file via <see cref="WorldLibrary.Load"/> (not a hand-typed literal), the same "resolved
    /// off the real file" idiom <c>MV1225MothershipCatalogRowTests</c> already uses for its own area
    /// count — there is no further "engine resolution" step between an authored garrison entry and its
    /// count; the list length IS the content.
    /// </summary>
    public sealed class MV1232HangarBayTests
    {
        private const float Cell = 0.25f;

        // Max (Greybox)'s own CharacterController, Backyard_Slice.unity.
        private const float Radius = 0.5f;
        private const float Height = 2f;

        private const int OpeningSearchRadiusCells = 6; // 1.5 m either side of an authored mouth/point

        private static readonly (string id, float x, float z, float w, float d)[] ExpectedCover =
        {
            ("a1_dropship1", 11f, 19f, 10f, 4f),
            ("a1_dropship2", 29f, 9f, 10f, 4f),
            ("a1_droppod1", 27f, 3f, 1.4f, 1.4f),
            ("a1_droppod2", 30f, 3f, 1.4f, 1.4f),
            ("a1_droppod3", 33f, 3f, 1.4f, 1.4f),
            ("a1_droppod4", 36f, 3f, 1.4f, 1.4f),
            ("a1_droppod5", 39f, 3f, 1.4f, 1.4f),
            ("a1_droppod6", 42f, 3f, 1.4f, 1.4f),
            ("a1_crate1", 23f, 3f, 3f, 3f),
            ("a1_crate2", 46f, 3f, 3f, 3f),
        };

        private static readonly (string kind, int count)[] ExpectedGarrison =
        {
            ("rusher", 6), ("turret", 2), ("bruiser", 3), ("gunner", 2), ("launcher", 1),
        };

        [Test]
        public void HangarBay_HoldsItsDrawnLayout_AndFloodFillsInToCradleAndBothGates()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
            Assert.IsNotNull(cfg, "the Mothership's own shipped config must load for this test to mean anything");

            WorldArea hangarBay = cfg.Area("a1");
            Assert.IsNotNull(hangarBay, "area 'a1' must exist");
            Assert.AreEqual(48f, hangarBay.size.w, 0.01f, "Hangar Bay width must be 48 m, as drawn");
            Assert.AreEqual(30f, hangarBay.size.d, 0.01f, "Hangar Bay depth must be 30 m, as drawn");

            Assert.AreEqual(14, hangarBay.garrison.Length, "the Hangar Bay must garrison exactly 14 robots, as drawn");
            foreach (var (kind, count) in ExpectedGarrison)
            {
                int actual = hangarBay.garrison.Count(g => g.kind == kind);
                Assert.AreEqual(count, actual, $"garrison must hold exactly {count} {kind}(s), as drawn");
            }

            Assert.AreEqual(1, hangarBay.cradles.Length, "the Hangar Bay must author exactly one Assembly Cradle");
            WorldCradle cradleCfg = hangarBay.cradles[0];
            Assert.AreEqual(42f, cradleCfg.x - hangarBay.origin.x, 0.01f, "the cradle sits at local x 42, as drawn");
            Assert.AreEqual(13f, cradleCfg.z - hangarBay.origin.z, 0.01f, "the cradle sits at local z 13, as drawn");
            Assert.AreEqual("W", cradleCfg.facing, "the cradle must face W, as drawn");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            var root = new GameObject("MV-1232 Hangar Bay Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                ShipDressing.Dress(root.transform, cfg, built.Cover);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                // ---- cover: exactly the drawn pieces, at their drawn rects, +/-1 cm ----
                // MV-1233 extended this config to 30 areas, so built.Cover now also carries every
                // other area's own cover -- scope the count to a1's own pieces (id prefix) rather
                // than the whole build's.
                List<CoverPiece> a1Cover = built.Cover.Where(p => p.Cover.Name.StartsWith("a1_")).ToList();
                Assert.AreEqual(ExpectedCover.Length, a1Cover.Count,
                    "the Hangar Bay must hold exactly the drawn cover pieces, no more, no fewer");
                foreach (var (id, x, z, w, d) in ExpectedCover)
                {
                    CoverPiece piece = built.Cover.Find(p => p.Cover.Name == id);
                    Assert.IsNotNull(piece.Body, $"cover '{id}' was never built");
                    Bounds b = piece.Body.GetComponent<Collider>().bounds;
                    Assert.AreEqual(x, b.center.x, 0.01f, $"cover '{id}' built X");
                    Assert.AreEqual(z, b.center.z, 0.01f, $"cover '{id}' built Z");
                    Assert.AreEqual(w, b.size.x, 0.01f, $"cover '{id}' built width");
                    Assert.AreEqual(d, b.size.z, 0.01f, $"cover '{id}' built depth");
                }

                // ---- the cradle: built for real, its out pad and 3x2 m lane clear of every cover rect ----
                // MV-1233: built.Cradles now carries all 30 areas' cradles -- a1's own is the one
                // standing at its drawn position (world x=47, z=13, asserted above).
                List<AssemblyCradle> a1Cradles = built.Cradles
                    .Where(c => Vector3.Distance(c.transform.position, new Vector3(47f, c.transform.position.y, 13f)) < 1f)
                    .ToList();
                Assert.AreEqual(1, a1Cradles.Count, "the Hangar Bay must build exactly one Assembly Cradle");
                AssemblyCradle cradle = a1Cradles[0];
                Vector3 cradlePos = cradle.transform.position;
                Vector3 outDir = cradle.transform.forward;
                Assert.Less(outDir.x, -0.9f, "the cradle must face W (-X)");

                const float cradleHalf = WorldMapLoader.CradleSize * 0.5f;
                const float outPadHalf = 1f;    // AssemblyCradle's own 2x2 m out pad
                const float laneHalfLen = 1.5f, laneHalfWidth = 1f; // the ticket's own 3x2 m lane

                Vector3 outPadCenter = cradlePos + outDir * (cradleHalf + outPadHalf);
                var outPadRect = new Rect(outPadCenter.x - outPadHalf, outPadCenter.z - outPadHalf,
                    outPadHalf * 2f, outPadHalf * 2f);

                Vector3 laneCenter = outPadCenter + outDir * (outPadHalf + laneHalfLen);
                var laneRect = new Rect(laneCenter.x - laneHalfLen, laneCenter.z - laneHalfWidth,
                    laneHalfLen * 2f, laneHalfWidth * 2f);

                foreach (CoverPiece piece in built.Cover)
                {
                    Bounds b = piece.Body.GetComponent<Collider>().bounds;
                    var coverRect = new Rect(b.min.x, b.min.z, b.size.x, b.size.z);
                    Assert.IsFalse(outPadRect.Overlaps(coverRect),
                        $"the cradle's out pad must be clear of cover '{piece.Cover.Name}'");
                    Assert.IsFalse(laneRect.Overlaps(coverRect),
                        $"the cradle's lane must be clear of cover '{piece.Cover.Name}'");
                }

                // ---- flood fill: the IN gate reaches the cradle's out pad and both gates ----
                MapLink inLink = map.links.FirstOrDefault(l => l != null && l.gate == "g0");
                MapLink outLink = map.links.FirstOrDefault(l => l != null && l.gate == "g1");
                Assert.IsNotNull(inLink, "gate g0 (IN) must exist");
                Assert.IsNotNull(outLink, "gate g1 (OUT) must exist");

                Vector2 inMouth = GateMouth(map, inLink);
                Vector2 outMouth = GateMouth(map, outLink);
                var cradleLane = new Vector2(outPadCenter.x, outPadCenter.z);

                HashSet<(int, int)> open = Rasterize(hangarBay.Footprint);

                (int, int)? inCell = FindNearestOpen(inMouth, open);
                Assert.IsNotNull(inCell, "the IN gate has no walkable cell within 1.5 m of it");

                var visited = new HashSet<(int, int)> { inCell.Value };
                var queue = new Queue<(int, int)>();
                queue.Enqueue(inCell.Value);
                while (queue.Count > 0)
                {
                    (int gx, int gz) c = queue.Dequeue();
                    TryVisit((c.gx + 1, c.gz), open, visited, queue);
                    TryVisit((c.gx - 1, c.gz), open, visited, queue);
                    TryVisit((c.gx, c.gz + 1), open, visited, queue);
                    TryVisit((c.gx, c.gz - 1), open, visited, queue);
                }

                AssertReached(outMouth, open, visited, "the OUT gate");
                AssertReached(cradleLane, open, visited, "the cradle's out pad");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AssertReached(Vector2 point, HashSet<(int, int)> open, HashSet<(int, int)> visited, string label)
        {
            (int, int)? cell = FindNearestOpen(point, open);
            Assert.IsNotNull(cell, $"{label} has no walkable cell within 1.5 m of it");
            Assert.IsTrue(visited.Contains(cell.Value), $"{label} is not reached from the IN gate by flood fill");
        }

        private static Vector2 GateMouth(MapData map, MapLink link)
        {
            Assert.IsTrue(MapGeometry.Doorway(map, link, out bool alongX, out float coord, out Span hole),
                $"gate '{link.gate}' has no resolvable doorway");
            return alongX ? new Vector2(hole.Mid, coord) : new Vector2(coord, hole.Mid);
        }

        private static HashSet<(int, int)> Rasterize(Rect rect)
        {
            var open = new HashSet<(int, int)>();
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
                    if (!IsBlocked(cx, cz)) open.Add((gx, gz));
                }
            }
            return open;
        }

        private static void TryVisit((int gx, int gz) k, HashSet<(int, int)> open, HashSet<(int, int)> visited,
            Queue<(int, int)> queue)
        {
            if (visited.Contains(k) || !open.Contains(k)) return;
            visited.Add(k);
            queue.Enqueue(k);
        }

        private static (int, int)? FindNearestOpen(Vector2 point, HashSet<(int, int)> open)
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
            int count = Physics.OverlapCapsuleNonAlloc(p0, p1, Radius, s_overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (s_overlapBuffer[i].GetComponent<StructuralFloor>() == null) return true;
            return false;
        }
    }
}
