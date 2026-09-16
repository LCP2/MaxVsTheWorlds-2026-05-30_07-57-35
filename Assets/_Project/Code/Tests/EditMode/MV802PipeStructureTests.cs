using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-802, "Pipes as structure" (design approved by Lee 2026-09-15, "Stormdrain Pass 4" review).
    /// The camera is a fixed 60 degree top-down rig at 26.02 m, so anything overhead sits ON the
    /// gameplay at that angle — overhead mains must hug a wall line and cross a room only at its far
    /// end, never over the playable middle. That single geometric constraint is what this test
    /// guards, plus the two invariants the rest of the ticket depends on: dressing never touches a
    /// collider, and the new "pipe" cover class actually resolves and reuses the cover piece's own
    /// footprint rather than adding one.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED state (Tier 2): runs
    /// the real <see cref="StormdrainDressing.Dress"/> over World 2's own shipped geometry/cover and
    /// reads back <see cref="Renderer.bounds"/> and live <see cref="Collider"/> queries — never the
    /// authored placement list.
    /// </summary>
    public sealed class MV802PipeStructureTests
    {
        [Test]
        public void OverheadStructureStaysAtWallsOrRoomEnds_CollidersUntouched_AndAPipeCoverResolves()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV802 host").transform;
            try
            {
                MapBuild build = MapRuntime.Build(map, host);
                int collidersBefore = host.GetComponentsInChildren<Collider>(true).Length;

                // ---- bullet 3 setup: find a 'pipe' cover piece and its collider BEFORE the dressing
                // pass touches anything, so "unchanged" below compares two resolved snapshots, not an
                // authored constant. ----
                CoverPiece? pipePiece = null;
                foreach (CoverPiece p in build.Cover)
                    if (p.Cover.Dressing == CoverDressing.Pipe) { pipePiece = p; break; }

                Assert.IsTrue(pipePiece.HasValue,
                    "World 2 must author at least one 'pipe' cover piece for this test to mean anything");
                Collider pipeCollider = pipePiece.Value.Body.GetComponent<Collider>();
                Assert.IsNotNull(pipeCollider, "a pipe-dressed cover piece must keep its own collider");
                Bounds pipeColliderBefore = pipeCollider.bounds;

                StormdrainDressing.Dress(host, map, build.Cover);

                Transform dressingHost = host.Find("Stormdrain Dressing");
                Assert.IsNotNull(dressingHost, "the dressing host was never built");

                // ---- bullet 2: dressing never adds, removes or resizes a collider anywhere under the
                // map host — only the cover piece's own renderer is ever swapped out. ----
                int collidersAfter = host.GetComponentsInChildren<Collider>(true).Length;
                Assert.AreEqual(collidersBefore, collidersAfter,
                    "StormdrainDressing.Dress must never add, remove or resize a collider");

                // ---- bullet 3: the pipe cover's own collider is byte-identical to its pre-dressing
                // footprint — no collider added, moved or resized for it specifically. ----
                Assert.AreEqual(pipeColliderBefore.center, pipeCollider.bounds.center,
                    "the pipe cover piece's collider centre must be unchanged from its authored footprint");
                Assert.AreEqual(pipeColliderBefore.size, pipeCollider.bounds.size,
                    "the pipe cover piece's collider size must be unchanged from its authored footprint");

                // ---- bullet 1: nothing overhead crosses the playable middle. Every pipe renderer whose
                // bounds centre sits above 1.5 m must be either within 1.7 m of a wall face or within
                // 2.5 m of the area's near or far end. MV-819 raised StormdrainKit.OverheadMainInset from
                // 0.95 to 1.65 m (the ticket's own worked example, pulling mains out from under the
                // soffit) — this threshold was the OLD inset's own 1.6 m, now stale by construction: a
                // correctly-inset main sits ~1.65 m out, which the old constant would always fail. 1.7 m
                // is the new inset plus a small margin for endpoint/rounding slack, not a loosened gate —
                // it stays well under the 2.5 m "near a zone end" branch, so this still catches a main
                // that actually drifts into the room's own middle. ----
                var faces = MapGeometry.Faces(map).Where(f => f.FacesRoom && f.Length >= 1.2f).ToList();
                Assert.IsNotEmpty(faces, "World 2's map must have at least one dressable wall face for this test to mean anything");

                var zoneRects = map.zones.Where(z => z != null).Select(z => z.Footprint).ToList();
                Assert.IsNotEmpty(zoneRects, "World 2's map must have at least one zone for this test to mean anything");

                // Scoped to this ticket's own "Overhead" host, not the whole dressing host — a pre-
                // existing amber/red bulkhead lamp already mounts its own "Bracket" above 1.5 m (MV-787)
                // at a gate, nowhere near a qualifying MapGeometry wall face, and is no part of what this
                // AC guards.
                Transform overheadHost = dressingHost.Find("Overhead");
                Assert.IsNotNull(overheadHost, "the overhead structure host was never built");

                Renderer[] overheadRenderers = overheadHost.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.bounds.center.y > 1.5f)
                    .ToArray();
                Assert.IsNotEmpty(overheadRenderers,
                    "World 2 must build at least one overhead pipe piece for this test to mean anything");

                foreach (Renderer r in overheadRenderers)
                {
                    var c2 = new Vector2(r.bounds.center.x, r.bounds.center.z);

                    float nearestWall = faces.Min(f => DistanceToSegment(c2, f.A, f.B));
                    float nearestEnd = zoneRects.Min(z => DistanceToNearestZoneEnd(c2, z));

                    Assert.IsTrue(nearestWall <= 1.7f || nearestEnd <= 2.5f,
                        $"{r.name} at {r.bounds.center} sits {r.bounds.center.y:F2} m up, {nearestWall:F2} m " +
                        $"from the nearest wall face and {nearestEnd:F2} m from the nearest zone end — it is " +
                        "crossing the playable middle");
                }
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Distance from <paramref name="p"/> to the nearer of a zone rect's own X-ends or
        /// Z-ends, counted only along whichever axis <paramref name="p"/> actually lies within the
        /// zone's own span for (a point outside both spans is not "at an end" of this zone at all).</summary>
        private static float DistanceToNearestZoneEnd(Vector2 p, Rect zone)
        {
            float best = float.MaxValue;
            bool withinZ = p.y >= zone.yMin - 0.5f && p.y <= zone.yMax + 0.5f;
            bool withinX = p.x >= zone.xMin - 0.5f && p.x <= zone.xMax + 0.5f;

            if (withinZ) best = Mathf.Min(best, Mathf.Min(Mathf.Abs(p.x - zone.xMin), Mathf.Abs(p.x - zone.xMax)));
            if (withinX) best = Mathf.Min(best, Mathf.Min(Mathf.Abs(p.y - zone.yMin), Mathf.Abs(p.y - zone.yMax)));
            return best;
        }
    }
}
