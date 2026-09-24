using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-906 — a16 (Gantry Run) is a 106 m x 12 m corridor dressed by <see cref="StormdrainDressing"/>
    /// at the SAME per-metre density every ordinary (12-36 m) World 2 room gets, so it alone produces a
    /// renderer count several times any neighbour's — the reason a10's whole gate-linked neighbourhood
    /// (MV-904) carries such a large enabled count. This is a content-density question, not a tagging bug
    /// (MV-904's fix is correct and unrelated) — the fix here is to widen the dressing generator's own
    /// spacings for a face this long, never to touch <c>world2_config.json</c> or any other authored
    /// layout.
    ///
    /// Fails on the base commit this branch was cut from (558a98b): a16's dressing renderer count on that
    /// commit is <see cref="BaselineA16DressingCount"/>, captured from this exact test's own base-commit
    /// run — see the fix commit / Jira comment for the quoted failure output.
    ///
    /// Reads every count off the built hierarchy, tagged by <see cref="MapStaticBatchRoot"/>'s own
    /// per-renderer zone index (the same mechanism MV-904 fixed) — Rule 2, Tier 2: a resolved value, not
    /// an authored constant.
    /// </summary>
    public sealed class MV906LongCorridorDressingThinningTests
    {
        // MV-906 AC2 baseline — measured directly on this branch's own base commit 558a98b, same
        // Build -> Dress -> Start -> tag-read setup this test performs below. Captured from this exact
        // test's own base-commit failure output (cc-verify Logs\editmode-results.xml, 558a98b) — see the
        // fix commit for the full quote.
        private const int BaselineA16DressingCount = 761;

        [Test]
        public void Area16DressingRendererCount_DropsAtLeast40PercentFromBase_AndStaysDressedAtEndsAndIntervals()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see other World2 EditMode tests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            // WorldMapLoader remaps the JSON's short authored id ("a16") to "area16" for any area within
            // the world's dials.areaCount (WorldMapLoader.TryLoad) — the same "areaN" convention every
            // MapZone.id in this runtime carries and MV904's own test already relies on.
            MapZone a16 = map.Zone("area16");
            Assert.IsNotNull(a16, "setup failure: World 2 must carry a zone called 'area16' (Gantry Run)");

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV906 Host");
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                // The exact BackyardPath.Awake order for World 2: Build, THEN Dress, both before
                // MapStaticBatchRoot.Start() (and its own dressing tagging pass) has ever run.
                StormdrainDressing.Dress(host.transform, map, built.Cover);

                Transform mapRoot = host.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");

                InvokePrivate(batchRoot, "Start");

                var rendererZones =
                    (Dictionary<Renderer, List<string>>)GetPrivateField(batchRoot, "_rendererZones");
                Assert.IsNotNull(rendererZones, "setup failure: MapStaticBatchRoot never carried its own rendererZones");

                Transform dressingRoot = host.transform.Find("Stormdrain Dressing");
                Assert.IsNotNull(dressingRoot, "World 2 must build its own Stormdrain Dressing host");

                int a16Count = 0;
                // a16 runs along X (origin x=90, width=106) — four equal 26.5 m bins across its own
                // length, so "regular intervals" (AC2) is a real spatial spread, not a single clump.
                var bins = new bool[4];
                float binWidth = a16.width / bins.Length;

                foreach (KeyValuePair<Renderer, List<string>> pair in rendererZones)
                {
                    Renderer r = pair.Key;
                    if (r == null || pair.Value == null || !pair.Value.Contains("area16")) continue;
                    if (!r.transform.IsChildOf(dressingRoot)) continue;

                    a16Count++;

                    // MV-938: a sludge tile's flow dressing is now ONE mesh spanning the tile's whole
                    // footprint rather than 43 small pieces scattered across it, so a wide tile's single
                    // Transform position under-represents which bins it actually covers. Bin membership
                    // is resolved off the renderer's own world-space bounds (Rule 2/3: a measured
                    // geometric property) rather than its pivot position, so a renderer that spans
                    // several bins marks all of them, exactly as its visible geometry does.
                    Bounds b = r.bounds;
                    int firstBin = Mathf.Clamp(Mathf.FloorToInt((b.min.x - a16.XMin) / binWidth), 0, bins.Length - 1);
                    int lastBin = Mathf.Clamp(Mathf.FloorToInt((b.max.x - a16.XMin) / binWidth), 0, bins.Length - 1);
                    for (int bin = firstBin; bin <= lastBin; bin++)
                        bins[bin] = true;
                }

                float reduction = BaselineA16DressingCount > 0
                    ? 1f - (float)a16Count / BaselineA16DressingCount
                    : 0f;

                Debug.Log("MV-906 AC1: a16 dressing renderer count " +
                    $"{a16Count} vs base-commit baseline {BaselineA16DressingCount} " +
                    $"— reduction {reduction:P1} (must be >= 40.0%).");

                Assert.GreaterOrEqual(reduction, 0.4f,
                    $"MV-906 AC2: a16's dressing renderer count must drop at least 40% from the base-commit " +
                    $"baseline ({BaselineA16DressingCount}) — measured {a16Count} ({reduction:P1} reduction).");

                Assert.Greater(a16Count, 0,
                    "MV-906 AC2: a16 must still carry SOME dressing — it must read as a dressed gantry, not a bare slab");

                for (int i = 0; i < bins.Length; i++)
                    Assert.IsTrue(bins[i],
                        $"MV-906 AC2: a16's dressing must be non-zero across its whole length at regular intervals — " +
                        $"bin {i} of {bins.Length} (x in [{a16.XMin + i * binWidth:F1}, {a16.XMin + (i + 1) * binWidth:F1}]) was empty");
            }
            finally
            {
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(target, null);

        private static object GetPrivateField(object target, string fieldName) =>
            target.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(target);
    }
}
