using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1177: Market Street never got dressed after MV-1176 inserted the City as World 3 —
    /// <c>BackyardPath.ApplyWorldMaterials</c> had no branch for <see cref="WorldKit.City"/>, and
    /// <c>CityDressing</c> did not exist. Fails to compile on the MV-1176 merge commit for exactly the
    /// reason <c>CityKitTests</c> fails on f559a56: there is no <c>CityDressing</c> type to build
    /// against (testing policy MV-465, Rule 1's own "fail-first" proof).
    ///
    /// One consolidated test (Rule 1), asserting RESOLVED values only (Rule 2, Tier 2) against the
    /// REAL <c>city_config.json</c>, built for real through <see cref="WorldMapLoader.TryLoad"/>,
    /// <see cref="MapRuntime.Build"/>, <see cref="WorldMaterials.Apply"/> and
    /// <see cref="CityDressing.Dress"/> — never an authored constant, never a rendered pixel.
    /// </summary>
    public sealed class MarketStreetBuildTests
    {
        private const float Cell = 0.1f;
        private const float Clearance = 0.5f;

        [Test]
        public void MarketStreetIsDressedSolidWalkableAndOffTheRelayColours()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(cfg, "the City's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            // The real exit doorway (a1's own E wall, MV-1176's "mouth (50, 20.5)") must be cut before
            // MapRuntime.Build runs MapGeometry.Walls, exactly as BackyardPath.Awake does — otherwise
            // the E wall builds solid and this test's own flood fill would wrongly refuse to reach it.
            int cityWorldIndex = WorldCatalog.IndexOf(WorldIds.City);
            Assert.AreNotEqual(-1, cityWorldIndex, "WorldCatalog must carry a row for WorldIds.City");
            WorldTransitions.ApplyExitDoorway(map, cfg, cityWorldIndex);

            var host = new GameObject("MarketStreet host").transform;
            try
            {
                MapBuild build = MapRuntime.Build(map, host);
                host.gameObject.AddComponent<WorldMaterials>().Apply(BiomePalette.City);
                CityDressing.Dress(host, map, cfg, build.Cover);

                Transform dressingRoot = host.Find("City Dressing");
                Assert.IsNotNull(dressingRoot, "CityDressing.Dress must build its own 'City Dressing' host");

                var failures = new List<string>();

                AssertCoverFits(build, dressingRoot, failures);
                AssertWallsRebuilt(host, dressingRoot, map, failures);
                AssertFloodFillReachesTheExitAndClearsMostOfA1(host, cfg, failures);
                AssertShopfrontSpansTheRun(dressingRoot, failures);
                AssertNoReservedRelayColour(dressingRoot, failures);

                Assert.IsTrue(failures.Count == 0, "MV-1177 Market Street failures:\n" + string.Join("\n", failures));
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        // ---------------------------------------------------------------- AC1: cover fit

        /// <summary>LampPost is <see cref="CityKit.BuildStreetLamp"/>'s own documented exception to the
        /// fit rule (its post is deliberately thin against a 1.6 m collider it never tries to span) —
        /// the same exemption <c>CityKitTests</c> itself keeps by never calling its own <c>AssertFit</c>
        /// on the lamp. ClockPost is the same shape of exception for the same reason: a readable round
        /// clock face sits well past a 0.5 m post collider's own width, exactly like the lamp's head.</summary>
        private static readonly HashSet<CoverDressing> FitExempt =
            new HashSet<CoverDressing> { CoverDressing.LampPost, CoverDressing.ClockPost };

        /// <summary>Renderer names excluded from a prop's own fit bounds — a deliberate new form, not
        /// the regression AC1 guards against: the truck's tail-lift sits outside its collider by design
        /// (<c>CityKit.BuildTruck</c>'s own doc comment), and a floor pool is a light-spill effect, not
        /// part of a prop's solid shape.</summary>
        private static readonly HashSet<string> FitExemptRenderers = new HashSet<string> { "Pool", "Tail Lift" };

        private static void AssertCoverFits(MapBuild build, Transform dressingRoot, List<string> failures)
        {
            Transform coverHost = dressingRoot.Find("Cover");
            Assert.IsNotNull(coverHost, "CityDressing must build a 'Cover' host under 'City Dressing'");

            foreach (CoverPiece piece in build.Cover)
            {
                if (piece.Body == null) continue;
                Collider collider = piece.Body.GetComponent<Collider>();
                if (collider == null || !collider.enabled || collider.isTrigger) continue;

                if (FitExempt.Contains(piece.Cover.Dressing)) continue;

                Transform pieceRoot = coverHost.Find(piece.Cover.Name);
                if (pieceRoot == null) { failures.Add($"{piece.Cover.Name}: no art root built under Cover"); continue; }

                // The pool is a deliberate light-spill effect, not part of the prop's own solid form —
                // same "a deliberate new form, not the regression this guards against" exemption
                // StormdrainDressing's own fit pass gives the truck's tail lift.
                Renderer[] renderers = pieceRoot.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.enabled && !FitExemptRenderers.Contains(r.name)).ToArray();

                if (renderers.Length == 0) { failures.Add($"{piece.Cover.Name} ({piece.Cover.Dressing}): built no renderer"); continue; }

                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

                Vector3 size = piece.Cover.Size;
                const float minFraction = 0.90f, maxOverrun = 0.15f, minHeightFraction = 0.9f, eps = 0.001f;

                if (b.size.x < size.x * minFraction - eps)
                    failures.Add($"{piece.Cover.Name} ({piece.Cover.Dressing}): X extent {b.size.x:F2} is under 90% of {size.x:F2}");
                if (b.size.x > size.x + maxOverrun + eps)
                    failures.Add($"{piece.Cover.Name} ({piece.Cover.Dressing}): X extent {b.size.x:F2} overruns {size.x:F2} by more than {maxOverrun:F2}");
                if (b.size.z < size.z * minFraction - eps)
                    failures.Add($"{piece.Cover.Name} ({piece.Cover.Dressing}): Z extent {b.size.z:F2} is under 90% of {size.z:F2}");
                if (b.size.z > size.z + maxOverrun + eps)
                    failures.Add($"{piece.Cover.Name} ({piece.Cover.Dressing}): Z extent {b.size.z:F2} overruns {size.z:F2} by more than {maxOverrun:F2}");
                if (b.size.y < size.y * minHeightFraction - eps)
                    failures.Add($"{piece.Cover.Name} ({piece.Cover.Dressing}): height {b.size.y:F2} is under 90% of {size.y:F2}");

                if (collider.bounds.max.y > 1.6f + eps)
                    failures.Add($"{piece.Cover.Name} ({piece.Cover.Dressing}): collider top {collider.bounds.max.y:F2} is above 1.6 m");
            }

            // AC4: every LampPost piece's own Pool renderer sits under its head.
            foreach (CoverPiece piece in build.Cover)
            {
                if (piece.Body == null || piece.Cover.Dressing != CoverDressing.LampPost) continue;
                Transform pieceRoot = coverHost.Find(piece.Cover.Name);
                Renderer pool = pieceRoot?.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "Pool");
                if (pool == null) { failures.Add($"{piece.Cover.Name}: LampPost built no 'Pool' renderer"); continue; }

                Vector2 poolXz = new Vector2(pool.bounds.center.x, pool.bounds.center.z);
                Vector2 postXz = piece.Cover.CenterXz;
                if (Vector2.Distance(poolXz, postXz) > 3.5f)
                    failures.Add($"{piece.Cover.Name}: Pool centre {poolXz} is more than 3.5 m from the post at {postXz}");
                if (pool.bounds.center.y > 0.05f)
                    failures.Add($"{piece.Cover.Name}: Pool sits at y={pool.bounds.center.y:F2}, above the 0.05 m floor band");
            }
        }

        // ---------------------------------------------------------------- AC2: walls

        private static void AssertWallsRebuilt(Transform host, Transform dressingRoot, MapData map, List<string> failures)
        {
            Renderer[] dressingRenderers = dressingRoot.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();

            foreach (StructuralWall wall in host.GetComponentsInChildren<StructuralWall>(true))
            {
                Collider collider = wall.GetComponent<Collider>();
                if (collider == null) { failures.Add($"{wall.gameObject.name}: StructuralWall has no collider"); continue; }

                if (Mathf.Abs(collider.bounds.size.y - map.wallHeight) > 0.1f)
                    failures.Add($"{wall.gameObject.name}: collider height {collider.bounds.size.y:F2} != wallHeight {map.wallHeight:F2}");

                Renderer wallRenderer = wall.GetComponent<Renderer>();
                if (wallRenderer != null && wallRenderer.enabled)
                    failures.Add($"{wall.gameObject.name}: own renderer is still enabled (should be replaced by City art)");

                Bounds wb = collider.bounds;
                var wallRect = new Rect(wb.min.x, wb.min.z, wb.size.x, wb.size.z);
                bool overlapped = dressingRenderers.Any(r =>
                {
                    Bounds rb = r.bounds;
                    var rect = new Rect(rb.min.x, rb.min.z, rb.size.x, rb.size.z);
                    return wallRect.Overlaps(rect);
                });
                if (!overlapped)
                    failures.Add($"{wall.gameObject.name}: no enabled City Dressing renderer overlaps its XZ footprint");
            }
        }

        // ---------------------------------------------------------------- AC3: flood fill

        private static void AssertFloodFillReachesTheExitAndClearsMostOfA1(Transform host, WorldConfig cfg, List<string> failures)
        {
            WorldArea a1 = cfg.Area("a1");
            Assert.IsNotNull(a1, "city_config.json must author area 'a1'");

            var blockers = new List<Bounds>();
            foreach (Collider c in host.GetComponentsInChildren<Collider>(true))
            {
                if (!c.enabled || c.isTrigger) continue;
                if (c.gameObject.name == "Map Floor") continue; // the walkable ground plane, not a blocker
                blockers.Add(c.bounds);
            }

            var coverFootprints = new List<Rect>();
            // Cover footprints come straight off the authored config, not the built colliders, so the
            // "cells not under cover" denominator means the same thing before and after dressing.
            foreach (WorldCover c in a1.cover ?? System.Array.Empty<WorldCover>())
            {
                if (c == null) continue;
                coverFootprints.Add(new Rect(c.x - c.width * 0.5f, c.z - c.depth * 0.5f, c.width, c.depth));
            }

            int nx = Mathf.Max(1, Mathf.RoundToInt(a1.size.w / Cell));
            int nz = Mathf.Max(1, Mathf.RoundToInt(a1.size.d / Cell));
            var blocked = new bool[nx, nz];
            var underCover = new bool[nx, nz];

            for (int ix = 0; ix < nx; ix++)
            {
                float x = a1.XMin + (ix + 0.5f) * Cell;
                for (int iz = 0; iz < nz; iz++)
                {
                    float z = a1.ZMin + (iz + 0.5f) * Cell;
                    var p = new Vector2(x, z);

                    foreach (Rect cf in coverFootprints)
                        if (cf.Contains(p)) { underCover[ix, iz] = true; break; }

                    foreach (Bounds b in blockers)
                    {
                        if (x >= b.min.x - Clearance && x <= b.max.x + Clearance &&
                            z >= b.min.z - Clearance && z <= b.max.z + Clearance)
                        { blocked[ix, iz] = true; break; }
                    }
                }
            }

            int IxOf(float x) => Mathf.Clamp(Mathf.FloorToInt((x - a1.XMin) / Cell), 0, nx - 1);
            int IzOf(float z) => Mathf.Clamp(Mathf.FloorToInt((z - a1.ZMin) / Cell), 0, nz - 1);

            var startXz = new Vector2(6.6f, 3.5f);
            var endXz = new Vector2(49.5f, 20.5f);
            int startIx = IxOf(startXz.x), startIz = IzOf(startXz.y);

            if (blocked[startIx, startIz])
            {
                failures.Add($"a1's own g0 mouth at {startXz} is blocked");
                return;
            }

            var visited = new bool[nx, nz];
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

            int endIx = IxOf(endXz.x), endIz = IzOf(endXz.y);
            if (!visited[endIx, endIz])
                failures.Add($"the exit door cell at {endXz} is not reached from the g0 mouth at {startXz}");

            int candidates = 0, reached = 0;
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                {
                    if (underCover[ix, iz]) continue;
                    candidates++;
                    if (visited[ix, iz]) reached++;
                }

            float fraction = candidates == 0 ? 1f : reached / (float)candidates;
            if (fraction < 0.85f)
                failures.Add($"only {fraction:P0} of a1's cells not under cover were reached (need >= 85%)");
        }

        private static void TryVisit(int x, int z, int nx, int nz, bool[,] blocked, bool[,] visited, Queue<(int, int)> queue)
        {
            if (x < 0 || z < 0 || x >= nx || z >= nz || visited[x, z] || blocked[x, z]) return;
            visited[x, z] = true;
            queue.Enqueue((x, z));
        }

        // ---------------------------------------------------------------- AC5: shopfront run

        private static void AssertShopfrontSpansTheRun(Transform dressingRoot, List<string> failures)
        {
            Renderer[] facades = dressingRoot.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "Facade").ToArray();
            if (facades.Length == 0) { failures.Add("no shopfront 'Facade' renderers were built along a1's N wall"); return; }

            Bounds b = facades[0].bounds;
            for (int i = 1; i < facades.Length; i++) b.Encapsulate(facades[i].bounds);

            if (b.size.x < 44f * 0.9f)
                failures.Add($"shopfront facades span {b.size.x:F1} m, under 90% of the 44 m run");
        }

        // ---------------------------------------------------------------- AC6: relay colour guard

        private static void AssertNoReservedRelayColour(Transform dressingRoot, List<string> failures)
        {
            foreach (Renderer r in dressingRoot.GetComponentsInChildren<Renderer>(true))
            {
                Material mat = r.sharedMaterial;
                if (mat == null || mat.shader == null || !mat.shader.name.Contains("Unlit")) continue;

                Color c = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor")
                        : mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                        : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.clear;

                if (CityKit.IsReservedRelayColour(c))
                    failures.Add($"{r.name} (material '{mat.name}', colour {c}) wears a reserved relay colour");
            }
        }
    }
}
