using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1175: <see cref="CityKit"/> did not exist before this ticket — this is the ticket's own
    /// Rule 1 fail-first test (testing policy MV-465), failing on base commit f559a56 because
    /// <c>CityKit</c> has no type to compile against.
    ///
    /// One consolidated test builds every public builder once into a fresh host and asserts on the
    /// BUILT (Tier 2, resolved) objects: no collider anywhere, every solid prop's renderers fit its own
    /// <c>colliderSize</c> (AC1's own 90%/+0.15 m/90%-tall window), no unlit/emissive/additive material
    /// wears a reserved relay colour, the street lamp's pool sits under its own head, a 44 m shopfront
    /// run yields 6 units whose facades span the run, and <see cref="BiomePalette.City"/>'s own value
    /// tiers hold.
    /// </summary>
    public sealed class CityKitTests
    {
        [Test]
        public void EveryKitPieceIsColliderFreeFittedAndOffTheRelayColours()
        {
            var host = new GameObject("CityKit Test Host").transform;
            try
            {
                // ---- floor paint ----
                CityKit.BuildPavement(host, new Rect(0f, 0f, 10f, 6f));
                CityKit.BuildKerbLine(host, new Vector2(0f, 0f), new Vector2(10f, 0f));
                CityKit.BuildEdgeLine(host, new Vector2(0f, 1f), new Vector2(10f, 1f));
                CityKit.BuildDashedLine(host, new Vector2(0f, 2f), new Vector2(10f, 2f));
                CityKit.BuildZebra(host, new Rect(0f, 10f, 4f, 3f));
                CityKit.BuildHatchBox(host, new Rect(0f, 14f, 4f, 3f));
                CityKit.BuildBayOutline(host, new Rect(0f, 18f, 4f, 2f));
                CityKit.BuildManhole(host, new Vector3(5f, 0f, 20f));
                CityKit.BuildDrainGrate(host, new Rect(5f, 21f, 1f, 0.5f));
                CityKit.BuildPuddle(host, new Vector3(6f, 0f, 20f), new Vector2(1.5f, 1.0f));
                CityKit.BuildLaneArrow(host, new Vector3(7f, 0f, 20f), new Vector2(0f, 1f));
                CityKit.BuildDeck(host, new Rect(0f, 24f, 6f, 3f));
                CityKit.BuildPad(host, new Rect(0f, 28f, 2f, 1.5f));

                // ---- building faces ----
                GameObject shopHost = CityKit.BuildShopfrontRun(host, new Vector2(0f, 30f), new Vector2(44f, 30f),
                    new Vector3(0f, 0f, -1f), 3.0f, 7);
                CityKit.BuildBuildingEnd(host, new Vector2(0f, 34f), new Vector2(6f, 34f),
                    new Vector3(0f, 0f, -1f), 3.0f, CityKit.Bricks[0], 3);
                CityKit.BuildCutaway(host, new Vector2(0f, 38f), new Vector2(6f, 38f), new Vector3(0f, 0f, 1f));

                // ---- street lamp ----
                var lampFoot = new Vector3(20f, 0f, 20f);
                var lampArm = new Vector3(1f, 0f, 0f);
                GameObject lamp = CityKit.BuildStreetLamp(host, lampFoot, lampArm);

                // ---- solid props (Change item 7) ----
                var carSize = new Vector3(1.8f, 1.5f, 4.2f);
                GameObject car = CityKit.BuildCar(host, new Vector3(0f, 0f, 40f), carSize);

                var truckSize = new Vector3(2.4f, 2.8f, 6.5f);
                GameObject truck = CityKit.BuildTruck(host, new Vector3(5f, 0f, 40f), truckSize);

                var busSize = new Vector3(2.6f, 3.0f, 10.0f);
                GameObject bus = CityKit.BuildBus(host, new Vector3(15f, 0f, 40f), busSize);

                var barrierSize = new Vector3(2.0f, 1.0f, 0.4f);
                GameObject barrier = CityKit.BuildConcreteBarrier(host, new Vector3(30f, 0f, 40f), barrierSize);

                var fenceSize = new Vector3(3.0f, 1.8f, 0.15f);
                GameObject fence = CityKit.BuildWireFence(host, new Vector3(34f, 0f, 40f), fenceSize);

                var bollardSize = new Vector3(0.3f, 0.9f, 0.3f);
                GameObject bollard = CityKit.BuildBollard(host, new Vector3(38f, 0f, 40f), bollardSize);

                var benchSize = new Vector3(1.6f, 0.9f, 0.6f);
                GameObject bench = CityKit.BuildBench(host, new Vector3(42f, 0f, 40f), benchSize);

                var binSize = new Vector3(0.5f, 0.8f, 0.5f);
                GameObject bin = CityKit.BuildBin(host, new Vector3(46f, 0f, 40f), binSize);

                var postBoxSize = new Vector3(0.5f, 1.2f, 0.5f);
                GameObject postBox = CityKit.BuildPostBox(host, new Vector3(50f, 0f, 40f), postBoxSize);

                var tubSize = new Vector3(0.8f, 0.5f, 0.8f);
                GameObject tub = CityKit.BuildTub(host, new Vector3(54f, 0f, 40f), tubSize);

                var planterSize = new Vector3(1.4f, 0.5f, 0.5f);
                GameObject planter = CityKit.BuildPlanterBox(host, new Vector3(58f, 0f, 40f), planterSize);

                var aboardSize = new Vector3(0.7f, 1.1f, 0.6f);
                GameObject aboard = CityKit.BuildABoard(host, new Vector3(62f, 0f, 40f), aboardSize);

                // ---- AC1: no collider anywhere under the host ----
                Assert.IsEmpty(host.GetComponentsInChildren<Collider>(true),
                    "CityKit must never build a collider — colliders come from the map's own cover entries");

                // ---- AC1: every solid prop's renderers fit its own colliderSize ----
                AssertFit(car.transform, carSize);
                AssertFit(truck.transform, truckSize, "Tail Lift"); // deliberately outside the collider, ticket change item 7
                AssertFit(bus.transform, busSize);
                AssertFit(barrier.transform, barrierSize);
                AssertFit(fence.transform, fenceSize);
                AssertFit(bollard.transform, bollardSize);
                AssertFit(bench.transform, benchSize);
                AssertFit(bin.transform, binSize);
                AssertFit(postBox.transform, postBoxSize);
                AssertFit(tub.transform, tubSize);
                AssertFit(planter.transform, planterSize);
                AssertFit(aboard.transform, aboardSize);

                // ---- AC1: no unlit/emissive/additive material wears a reserved relay colour ----
                foreach (Renderer r in host.GetComponentsInChildren<Renderer>(true))
                {
                    Material mat = r.sharedMaterial;
                    if (mat == null || mat.shader == null || !mat.shader.name.Contains("Unlit")) continue;

                    Color c = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor")
                            : mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                            : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.clear;

                    Assert.IsFalse(CityKit.IsReservedRelayColour(c),
                        $"{r.name} (material '{mat.name}', colour {c}) wears a reserved relay colour");
                }

                // ---- AC1: the street lamp's own Pool renderer ----
                Renderer poolRenderer = lamp.transform.GetComponentsInChildren<Renderer>(true)
                    .FirstOrDefault(r => r.name == "Pool");
                Assert.IsNotNull(poolRenderer, "BuildStreetLamp must build a renderer named 'Pool'");

                Vector3 headGroundPoint = lampFoot + Vector3.up * 4f + lampArm.normalized * 2.2f;
                var poolCentreXZ = new Vector2(poolRenderer.bounds.center.x, poolRenderer.bounds.center.z);
                var headXZ = new Vector2(headGroundPoint.x, headGroundPoint.z);
                Assert.LessOrEqual(Vector2.Distance(poolCentreXZ, headXZ), 0.1f,
                    "the street lamp's pool must sit under the head's own ground point");
                Assert.LessOrEqual(poolRenderer.bounds.center.y, 0.05f, "the pool must sit flat on the floor");
                Assert.GreaterOrEqual(poolRenderer.bounds.size.x, 6.0f, "the pool must spread at least 6 m across X");
                Assert.GreaterOrEqual(poolRenderer.bounds.size.z, 6.0f, "the pool must spread at least 6 m across Z");

                // ---- AC1: a 44 m shopfront run yields 6 units whose facades span the run ----
                Renderer[] facades = shopHost.transform.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.name == "Facade").ToArray();
                Assert.AreEqual(6, facades.Length, "a 44 m shopfront run must yield round(44 / 7) = 6 units");

                Bounds facadeBounds = facades[0].bounds;
                foreach (Renderer f in facades) facadeBounds.Encapsulate(f.bounds);
                Assert.GreaterOrEqual(facadeBounds.size.x, 44f * 0.9f,
                    "the facades' union X extent must span at least 90% of the 44 m run");

                // ---- AC1: BiomePalette.City's own value tiers ----
                BiomePalette palette = BiomePalette.City;
                float groundLum = Luminance(palette.GroundBase);
                float wallLum = Luminance(palette.Wall);
                float stoneLum = Luminance(palette.Stone);
                Assert.GreaterOrEqual(wallLum, groundLum * 1.6f,
                    $"Wall luminance {wallLum:F3} must be at least 1.6x GroundBase luminance {groundLum:F3}");
                Assert.Greater(stoneLum, groundLum,
                    $"Stone luminance {stoneLum:F3} must exceed GroundBase luminance {groundLum:F3}");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>The ticket's own AC1 fit window: the union of a prop's own (non-skipped) renderers
        /// must span at least 90% and at most <paramref name="colliderSize"/> + 0.15 m on X and Z, and
        /// stand at least 90% of <paramref name="colliderSize"/>'s own height.</summary>
        private static void AssertFit(Transform propRoot, Vector3 colliderSize, string skipRendererName = null)
        {
            Renderer[] renderers = propRoot.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.enabled && r.name != skipRendererName).ToArray();
            Assert.IsNotEmpty(renderers, $"{propRoot.name} built no renderers");

            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);

            Assert.GreaterOrEqual(b.size.x, colliderSize.x * 0.90f, $"{propRoot.name} X extent {b.size.x:F2} is below 90% of {colliderSize.x}");
            Assert.LessOrEqual(b.size.x, colliderSize.x + 0.15f, $"{propRoot.name} X extent {b.size.x:F2} exceeds {colliderSize.x} + 0.15");
            Assert.GreaterOrEqual(b.size.z, colliderSize.z * 0.90f, $"{propRoot.name} Z extent {b.size.z:F2} is below 90% of {colliderSize.z}");
            Assert.LessOrEqual(b.size.z, colliderSize.z + 0.15f, $"{propRoot.name} Z extent {b.size.z:F2} exceeds {colliderSize.z} + 0.15");
            Assert.GreaterOrEqual(b.size.y, colliderSize.y * 0.9f, $"{propRoot.name} height {b.size.y:F2} is below 90% of {colliderSize.y}");
        }
    }
}
