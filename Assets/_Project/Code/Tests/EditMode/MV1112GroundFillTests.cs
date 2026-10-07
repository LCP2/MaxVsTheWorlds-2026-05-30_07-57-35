using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.CameraRig;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1112 (Lee, 2026-10-06): standing in World 2's entry stub, everything beyond its wall used to
    /// be the camera's own clear colour — a flat blue-violet void, because nothing ever built geometry
    /// outside an area's own footprint. <see cref="World2GroundFill"/> fixes this by filling every gap
    /// with a solid ground mass, topped at the map's own wallHeight.
    ///
    /// Fail-first (base d30d293): <c>World2GroundFill</c> does not exist there, so this test does not
    /// even compile on that commit — see the fix comment for the quoted compiler error.
    ///
    /// One consolidated test (MV-465 Rule 1), carrying every sub-check the ticket's AC1 lists, all
    /// against RESOLVED values (Tier 2) read off the real built hierarchy: the real World 2 config
    /// (<see cref="WorldLibrary.World2"/>), the real loader, the real <see cref="MapRuntime.Build"/> +
    /// <see cref="World2GroundFill.Build"/> pass (the exact call <c>BackyardPath.Awake</c> makes), and
    /// a real <see cref="FixedAngleCameraRig"/>'s own <c>RestingPose</c> for the camera-ray sub-check.
    /// Never an authored constant, never a reflection-set private field.
    /// </summary>
    public sealed class MV1112GroundFillTests
    {
        [Test]
        public void GroundFill_CoversGapsUpToWallHeight_AndNoRayReachesTheVoid()
        {
            // Every full-World2-build EditMode test in this suite carries this same BuildBody
            // collider-strip [Error] noise (see other World2 tests' own note) — not this ticket's concern.
            LogAssert.ignoreFailingMessages = true;

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV1112 Host");
            try
            {
                WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
                Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

                MapRuntime.Build(map, host.transform);

                // The exact call BackyardPath.Awake makes for World 2, right after StormdrainDressing.
                List<GameObject> built = World2GroundFill.Build(map, host.transform);
                Assert.IsNotEmpty(built, "World 2 must build at least one ground-fill piece for this test to mean anything");

                Rect bounds = map.Bounds();
                var outer = new Rect(bounds.xMin - World2GroundFill.ExtendMargin, bounds.yMin - World2GroundFill.ExtendMargin,
                    bounds.width + World2GroundFill.ExtendMargin * 2f, bounds.height + World2GroundFill.ExtendMargin * 2f);

                var footprints = new List<Rect>();
                foreach (MapZone z in map.zones)
                {
                    if (z == null || z.level > 0) continue;
                    footprints.Add(z.Footprint);
                }
                var excludedForCoverage = new List<Rect>();
                foreach (MapZone z in map.zones)
                {
                    if (z == null || z.level > 0) continue;
                    float m = map.wallThickness * 0.5f;
                    excludedForCoverage.Add(new Rect(z.XMin - m, z.ZMin - m, z.width + m * 2f, z.depth + m * 2f));
                }

                var fillRenderers = built
                    .Select(go => go.GetComponent<Renderer>())
                    .Where(r => r != null)
                    .ToList();
                Assert.IsTrue(fillRenderers.All(r => r.enabled), "every ground-fill piece must be an enabled renderer");

                // ---- sub-check: top is at wallHeight within 0.02 ----
                foreach (Renderer r in fillRenderers)
                {
                    Assert.That(r.bounds.max.y, Is.EqualTo(map.wallHeight).Within(0.02f),
                        $"{r.name} resolves its top to {r.bounds.max.y:F3} m — must sit at wallHeight ({map.wallHeight:F3} m) within 0.02 m");
                }

                // ---- sub-check: overlaps no area's walkable footprint by more than the wall thickness ----
                foreach (Renderer r in fillRenderers)
                {
                    var box = new Rect(r.bounds.min.x, r.bounds.min.z, r.bounds.size.x, r.bounds.size.z);
                    foreach (Rect footprint in footprints)
                    {
                        float overlapW = Mathf.Min(box.xMax, footprint.xMax) - Mathf.Max(box.xMin, footprint.xMin);
                        float overlapD = Mathf.Min(box.yMax, footprint.yMax) - Mathf.Max(box.yMin, footprint.yMin);
                        if (overlapW <= 0f || overlapD <= 0f) continue;   // no overlap at all
                        Assert.LessOrEqual(overlapW, map.wallThickness + 0.02f,
                            $"{r.name} overlaps a walkable footprint by {overlapW:F2} m in X — more than the wall thickness ({map.wallThickness} m)");
                        Assert.LessOrEqual(overlapD, map.wallThickness + 0.02f,
                            $"{r.name} overlaps a walkable footprint by {overlapD:F2} m in Z — more than the wall thickness ({map.wallThickness} m)");
                    }
                }

                // ---- sub-check: the union of resolved renderer bounds covers (map bounds + 40 m) minus the area footprints ----
                const float gridStep = 10f;
                int uncovered = 0;
                for (float x = outer.xMin + gridStep * 0.5f; x < outer.xMax; x += gridStep)
                {
                    for (float z = outer.yMin + gridStep * 0.5f; z < outer.yMax; z += gridStep)
                    {
                        var p = new Vector2(x, z);
                        if (excludedForCoverage.Any(ex => ex.Contains(p))) continue;   // inside (or wall-adjacent to) a walkable area — not this builder's job

                        var probe = new Vector3(x, map.wallHeight * 0.5f, z);
                        bool covered = fillRenderers.Any(r => r.bounds.Contains(probe));
                        if (!covered) uncovered++;
                    }
                }
                Assert.AreEqual(0, uncovered, "every sampled point outside an area's footprint (map bounds + 40 m) must be covered by a ground-fill renderer's bounds");

                // ---- sub-check: from the default play camera over 12 sample points along the outer
                // walls (including the start room's west wall), every one of 25 screen-grid rays hits
                // either world geometry or the ground mass's bounds — none reaches the clear colour.
                List<WallFace> outerFaces = MapGeometry.Faces(map).Where(f => !f.FacesRoom && f.Length > 0.01f).ToList();
                Assert.GreaterOrEqual(outerFaces.Count, 12, "World 2 must author at least 12 outer wall faces for this test to mean anything");

                MapZone stub = map.zones.FirstOrDefault(z => z != null && z.id == "stub");
                Assert.IsNotNull(stub, "World 2 must author its own entry stub for this test to mean anything");
                WallFace stubWest = outerFaces.First(f =>
                    Mathf.Abs(f.Out.x + 1f) < 0.01f && Mathf.Abs((f.A.x + f.B.x) * 0.5f - stub.XMin) < 1f);

                var samplePoints = new List<Vector3>();
                int step = Mathf.Max(1, outerFaces.Count / 12);
                for (int i = 0; i < outerFaces.Count && samplePoints.Count < 12; i += step)
                {
                    Vector2 mid = (outerFaces[i].A + outerFaces[i].B) * 0.5f;
                    samplePoints.Add(new Vector3(mid.x, map.wallHeight * 0.5f, mid.y));
                }
                Vector2 stubMid = (stubWest.A + stubWest.B) * 0.5f;
                var stubPoint = new Vector3(stubMid.x, map.wallHeight * 0.5f, stubMid.y);
                if (!samplePoints.Any(p => Vector3.Distance(p, stubPoint) < 0.5f))
                    samplePoints[0] = stubPoint;

                Renderer[] allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);

                var camGo = new GameObject("MV1112 Probe Camera");
                try
                {
                    var cam = camGo.AddComponent<Camera>();
                    cam.fieldOfView = 40f;    // the shipped lens (FixedAngleCameraRig's own doc: "~40 deg vertical, 16:9")
                    cam.aspect = 16f / 9f;

                    var rig = camGo.AddComponent<FixedAngleCameraRig>();

                    float[] viewportSteps = { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };
                    int rayMisses = 0;
                    foreach (Vector3 target in samplePoints)
                    {
                        rig.RestingPose(target, out Vector3 camPos, out Quaternion camRot);
                        cam.transform.SetPositionAndRotation(camPos, camRot);

                        foreach (float u in viewportSteps)
                        {
                            foreach (float v in viewportSteps)
                            {
                                Ray ray = cam.ViewportPointToRay(new Vector3(u, v, 0f));
                                bool hit = allRenderers.Any(r => r != null && r.enabled && r.bounds.IntersectRay(ray));
                                if (!hit) rayMisses++;
                            }
                        }
                    }
                    Assert.AreEqual(0, rayMisses,
                        "every one of the 25 screen-grid rays at each of the 12 outer-wall sample points must hit world geometry or the ground mass's bounds — none may reach the clear colour");
                }
                finally
                {
                    Object.DestroyImmediate(camGo);
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }
    }
}
