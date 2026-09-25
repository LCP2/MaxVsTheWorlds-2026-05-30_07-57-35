using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-939 — MV-934's <see cref="MapStaticBatchRoot"/>.CombineZoneGeometry (MapRuntime.cs) builds
    /// brand-new "Combined ..." GameObjects for World 2's static geometry, each already carrying the
    /// correct per-piece tint as its own <c>sharedMaterial</c>. But <c>WorldMaterials.Apply</c> and
    /// <see cref="RuntimeSurfaceDirector"/>.Sweep both blanket-repaint any <c>MeshRenderer</c> that isn't
    /// tagged <see cref="KeepsOwnMaterial"/> (or one of their other exclusions) with a generic,
    /// bounds-classified material. The ORIGINAL per-piece GameObjects carried that tag (MapRuntime.Tint
    /// adds it directly; the Stormdrain kit's pieces inherit it from "Stormdrain Dressing"'s own root),
    /// but the new combined GameObject that replaces them never did — so whichever sweep's own
    /// Start()/Awake() happens to run after MapStaticBatchRoot's Start() (Unity does not guarantee that
    /// order; MaterialLibrary.cs's own MV-738 doc names the same race for World 1) repaints every combined
    /// mesh with its shape-classified SurfaceKind's single Neutral material — why World 2's walls, pipes
    /// and props all turned the same flat grey in TestFlight 0.9.9 (0.9.9 contains MV-934).
    ///
    /// Fails on the pre-fix commit, where <c>CombineZoneGeometry</c> never adds
    /// <see cref="KeepsOwnMaterial"/> to the combined GameObject: see the fix commit / Jira comment for
    /// that base-commit failure output.
    ///
    /// One consolidated test (MV-465 Rule 1), built the same Build -> Dress -> Start order every other
    /// World 2 EditMode test in this suite uses, then runs the REAL <see cref="RuntimeSurfaceDirector"/>
    /// sweep this bug depends on over the same hierarchy. Reads a RESOLVED value off the built hierarchy
    /// (Rule 2, Tier 2) — the actual <c>sharedMaterial</c> a combined renderer carries after that sweep
    /// runs, compared by reference against the tint it was combined with — never an authored constant,
    /// never a rendered pixel.
    /// </summary>
    public sealed class MV939CombinedMeshKeepsOwnMaterialTests
    {
        [Test]
        public void CombinedZoneMesh_KeepsItsOwnTint_AfterRuntimeSurfaceSweepRuns()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see other World2 EditMode tests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV939 Host");
            var sweepHost = new GameObject("MV939 Sweep");
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                // The exact BackyardPath.Awake order for World 2: Build, THEN Dress, both before
                // MapStaticBatchRoot.Start() (and its CombineZoneGeometry) has ever run — same order
                // MV904DressingGateCoverageTests/MV934CombinedMeshTests already establish.
                StormdrainDressing.Dress(host.transform, map, built.Cover);

                Transform mapRoot = host.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");

                InvokePrivate(batchRoot, "Start");
                batchRoot.ApplyAreaGate("area10");
                Debug.Log($"MV-939 {FrameCost.AreaCensusLine()}");

                var rendererZones =
                    (Dictionary<Renderer, List<string>>)GetPrivateField(batchRoot, "_rendererZones");
                Assert.IsNotNull(rendererZones, "setup failure: MapStaticBatchRoot never carried its own rendererZones");

                var combined = new List<(Renderer renderer, Material tint)>();
                foreach (KeyValuePair<Renderer, List<string>> pair in rendererZones)
                {
                    Renderer r = pair.Key;
                    if (r == null || !r.name.StartsWith("Combined ")) continue;
                    if (!pair.Value.Contains("area10")) continue;
                    combined.Add((r, r.sharedMaterial));
                }
                Assert.IsNotEmpty(combined,
                    "MV-939: area10 must carry at least one combined static mesh for this test to mean anything.");

                // The real sweep this bug depends on — RuntimeSurfaceDirector.Sweep, run over the SAME
                // hierarchy the map just built, exactly the one pass it makes once from its own Start()
                // every time the game boots (MV-527).
                var director = sweepHost.AddComponent<RuntimeSurfaceDirector>();
                InvokePrivate(director, "Sweep");

                foreach ((Renderer renderer, Material tint) in combined)
                {
                    Assert.AreSame(tint, renderer.sharedMaterial,
                        $"MV-939: '{renderer.name}' lost its own tint ('{tint?.name}') to RuntimeSurfaceDirector's " +
                        $"generic sweep (now '{renderer.sharedMaterial?.name}') — a combined zone mesh must carry " +
                        "KeepsOwnMaterial the same way every other piece that keeps its own material already does.");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(sweepHost);
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
