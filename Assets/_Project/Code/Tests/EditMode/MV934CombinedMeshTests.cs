using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-934 — World 2's active area set carried ~10k separate renderers even after MV-882's
    /// <c>StaticBatchingUtility.Combine</c>: that call shares one vertex buffer across a material's
    /// renderers but still submits one draw call per original renderer, so it never actually reduced the
    /// set-pass count (measured live at a10: 10,487 enabled renderers, 5,968 set-pass calls). The fix
    /// (<see cref="MapStaticBatchRoot.CombineZoneGeometry"/>, reflection-invoked below via
    /// <c>Start</c>) folds each (zone, material) group of static geometry into one real
    /// <see cref="Mesh.CombineMeshes"/> result — this test proves that invariant directly off the built
    /// hierarchy.
    ///
    /// Fails on the base commit this branch was cut from: <c>CombineZoneGeometry</c> does not exist
    /// there, so <c>Start()</c> never creates a single "Combined ..." GameObject, and this test's own
    /// first assertion ("at least one combined mesh exists for area10") fails outright. See the fix
    /// commit / Jira comment for that base-commit failure output.
    ///
    /// One consolidated test (MV-465 Rule 1), against the real shipped World 2 config, built the same
    /// Build -> Dress -> Start order <c>BackyardPath.Awake</c> uses for World 2 (same trick
    /// <c>MV904DressingGateCoverageTests</c> already uses). Both assertions read RESOLVED values off the
    /// built hierarchy (Rule 2, Tier 2) — the actual combined GameObjects and their actual shared
    /// materials — never an authored constant, never a rendered pixel. Restricted to renderers named
    /// "Combined ..." (this ticket's own output) rather than every renderer tagged to the zone, because
    /// a ramp/grate/deck-slab/gate is deliberately left OUT of the combine (it moves or repaints) and can
    /// legitimately share a material with another of its own kind — that is not the invariant this
    /// ticket's AC is about, which is scoped to "its static geometry" specifically (Rule 3: a measured
    /// property, not a presence check dressed up as one).
    /// </summary>
    public sealed class MV934CombinedMeshTests
    {
        [Test]
        public void CombinedZoneGeometry_YieldsAtMostOneRenderMeshPerMaterial()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see other World2 EditMode tests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV934 Host");
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                // The exact BackyardPath.Awake order for World 2: Build, THEN Dress, both before
                // MapStaticBatchRoot.Start() (and its CombineZoneGeometry/first ApplyAreaGate call) has
                // ever run — same order MV904DressingGateCoverageTests already establishes.
                StormdrainDressing.Dress(host.transform, map, built.Cover);

                Transform mapRoot = host.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");

                InvokePrivate(batchRoot, "Start");
                batchRoot.ApplyAreaGate("area10");

                var rendererZones =
                    (Dictionary<Renderer, List<string>>)GetPrivateField(batchRoot, "_rendererZones");
                Assert.IsNotNull(rendererZones, "setup failure: MapStaticBatchRoot never carried its own rendererZones");

                // MV-934 fix item 6 (report per-zone renderer counts before/after): logged, not asserted
                // (Rule 3 -- an assertion here would just be a presence check on a debug number). "before"
                // is this ticket's own live-WebGL evidence (enabled 10487/30425, setp 5968 at a10); this
                // is the closest "after" figure EditMode can produce -- combined-mesh count vs. everything
                // still individually gated (ramps/grates/deck slabs by design, and every SludgeFlowRig-
                // driven band/chevron/foam piece, excluded because it moves every frame -- see
                // CombineZoneGeometry's own doc).
                int combinedCount = 0, combinedEnabled = 0, individualCount = 0, individualEnabled = 0;
                foreach (KeyValuePair<Renderer, List<string>> pair in rendererZones)
                {
                    if (pair.Key == null || !pair.Value.Contains("area10")) continue;
                    bool isCombined = pair.Key.name.StartsWith("Combined ");
                    if (isCombined) { combinedCount++; if (pair.Key.enabled) combinedEnabled++; }
                    else { individualCount++; if (pair.Key.enabled) individualEnabled++; }
                }
                Debug.Log("MV-934: area10 post-combine — " +
                    $"combined meshes {combinedEnabled}/{combinedCount} enabled, " +
                    $"still-individual renderers (ramps/grates/deck slabs/SludgeFlowRig pieces) " +
                    $"{individualEnabled}/{individualCount} enabled.");

                var byMaterial = new Dictionary<Material, int>();
                foreach (KeyValuePair<Renderer, List<string>> pair in rendererZones)
                {
                    Renderer r = pair.Key;
                    if (r == null || !r.name.StartsWith("Combined ")) continue;
                    if (!pair.Value.Contains("area10")) continue;

                    Material mat = r.sharedMaterial;
                    byMaterial.TryGetValue(mat, out int count);
                    byMaterial[mat] = count + 1;
                }

                Assert.IsNotEmpty(byMaterial,
                    "MV-934: area10 must carry at least one combined static mesh -- found none, so this " +
                    "test proves nothing (and fails outright on the pre-fix base commit, where " +
                    "CombineZoneGeometry does not exist).");

                foreach (KeyValuePair<Material, int> entry in byMaterial)
                    Assert.AreEqual(1, entry.Value,
                        $"MV-934: area10's static geometry must combine to at most one render mesh per " +
                        $"material -- material '{entry.Key?.name}' has {entry.Value} separate combined " +
                        "meshes.");
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
