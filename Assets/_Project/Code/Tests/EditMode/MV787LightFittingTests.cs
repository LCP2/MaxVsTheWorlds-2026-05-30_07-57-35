using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-787: World 2's bright colours (a lamp lens, a hazard fitting, an LED panel) were bare
    /// emissive stickers — no fitting behind them, no pool under them. This gives the drain a caged
    /// bulkhead lamp (amber, steady) every 6-9 m of wall, a hazard bulkhead (red, pulsing) at both
    /// sides of every gate and at every outfall, a cyan kerb strip along every kerb, and a real 5x3
    /// LED panel on every machinery cover piece — each fitting paired with its own additive pool.
    ///
    /// <see cref="StormdrainLightKit"/> and <see cref="LightFittingPulse"/> do not exist before this
    /// ticket — this fails to COMPILE on the base commit (517ab29, "MV-786: give cover five turned
    /// forms and the walls ribbed panels"), the same "doesn't exist there yet" failure
    /// MV786NoBoxesTests documents for its own base commit.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1) asserting RESOLVED state only (Rule 2,
    /// Tier 2): fitting density per floor m² sits in the ticket's own band; every fitting renderer that
    /// resolves brighter than 3x the floor's luminance has an additive Pool sibling within 1.2 m; red
    /// fittings are at most a fifth of all fittings and every one sits within 4 m of a gate or the
    /// outfall; two hazard bulkheads at different world positions resolve different pulse phases at
    /// t=0 while the same position always resolves the same one; and zero real
    /// <see cref="UnityEngine.Light"/> components exist anywhere the dressing pass built.
    /// </summary>
    public sealed class MV787LightFittingTests
    {
        private const float FloorAreaPerFittingMin = 28f;  // upper density bound: no more than 1 per 28 m^2
        private const float FloorAreaPerFittingMax = 55f;  // lower density bound: at least 1 per 55 m^2
        private const float PoolProximity = 1.2f;
        private const float RedFittingCap = 0.20f;
        private const float GateProximity = 4f;

        [Test]
        public void LightFittings_MeetBudgetPoolProximityRedCapAndDeterministicPhase()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV787 host").transform;
            try
            {
                MapBuild build = MapRuntime.Build(map, host);
                StormdrainDressing.Dress(host, map, build.Cover);

                Transform dressingHost = host.Find("Stormdrain Dressing");
                Assert.IsNotNull(dressingHost, "the dressing host was never built");

                List<Transform> fittings = FindFittings(dressingHost);
                Assert.IsTrue(fittings.Count > 0,
                    "World 2 must build at least one light fitting for this test to mean anything");

                AssertDensityWithinBudget(fittings, map);
                AssertEveryBrightRendererHasANearbyPool(fittings);
                AssertRedFittingsCappedAndNearAGateOrOutfall(fittings, host, map);
                AssertZeroRealLightComponents(dressingHost);
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }

            AssertHazardPulsePhaseIsDeterministicByPosition();
        }

        private static List<Transform> FindFittings(Transform dressingHost)
        {
            var result = new List<Transform>();
            foreach (Transform t in dressingHost.GetComponentsInChildren<Transform>(true))
                if (t.name == "Bulkhead Lamp" || t.name == "Hazard Bulkhead" || t.name == "LED Panel")
                    result.Add(t);
            return result;
        }

        private static void AssertDensityWithinBudget(List<Transform> fittings, MapData map)
        {
            float floorArea = 0f;
            if (map.zones != null)
                foreach (MapZone zone in map.zones)
                    if (zone != null && zone.level == 0)
                        floorArea += zone.Footprint.width * zone.Footprint.height;

            Assert.Greater(floorArea, 0f, "World 2 must author at least one floor zone for this test to mean anything");

            float minFittings = floorArea / FloorAreaPerFittingMax;
            float maxFittings = floorArea / FloorAreaPerFittingMin;

            Assert.GreaterOrEqual((float)fittings.Count, minFittings,
                $"{fittings.Count} fittings over {floorArea:F1} m^2 of floor is sparser than 1 per {FloorAreaPerFittingMax} m^2");
            Assert.LessOrEqual((float)fittings.Count, maxFittings,
                $"{fittings.Count} fittings over {floorArea:F1} m^2 of floor is denser than 1 per {FloorAreaPerFittingMin} m^2");
        }

        /// <summary>Rec709 luma over <see cref="Color.linear"/> — the same resolved-luminance method
        /// <c>MV783StormdrainPaletteTests</c>/<c>MV785SludgeFlowTests</c> already use.</summary>
        private static float Luminance(Color c)
        {
            Color lin = c.linear;
            return 0.2126f * lin.r + 0.7152f * lin.g + 0.0722f * lin.b;
        }

        private static Color ResolvedColor(Material m)
        {
            if (m == null) return Color.black;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.black;
        }

        private static void AssertEveryBrightRendererHasANearbyPool(List<Transform> fittings)
        {
            float threshold = Luminance(StormdrainKit.GroundBase) * 3f;

            var pools = new List<Transform>();
            foreach (Transform fitting in fittings)
                foreach (Transform child in fitting.GetComponentsInChildren<Transform>(true))
                    if (child.name == "Pool") pools.Add(child);
            Assert.Greater(pools.Count, 0, "no fitting built a Pool sibling at all");

            int checkedCount = 0;
            foreach (Transform fitting in fittings)
            {
                foreach (Renderer rend in fitting.GetComponentsInChildren<Renderer>(true))
                {
                    if (rend.gameObject.name != "Lens" && !rend.gameObject.name.StartsWith("Cell")) continue;

                    float luma = Luminance(ResolvedColor(rend.sharedMaterial));
                    if (luma <= threshold) continue;
                    checkedCount++;

                    Vector2 pos = new Vector2(rend.transform.position.x, rend.transform.position.z);
                    bool hasNearbyPool = pools.Any(p =>
                        Vector2.Distance(new Vector2(p.position.x, p.position.z), pos) <= PoolProximity);

                    Assert.IsTrue(hasNearbyPool,
                        $"{fitting.name}'s {rend.gameObject.name} resolves {luma:F3} (> {threshold:F3}, 3x the " +
                        $"floor's own {Luminance(StormdrainKit.GroundBase):F3}) but has no additive Pool sibling " +
                        $"within {PoolProximity} m");
                }
            }
            Assert.Greater(checkedCount, 0,
                "no fitting renderer exceeded the 3x-floor threshold — this test would pass on garbage");
        }

        private static void AssertRedFittingsCappedAndNearAGateOrOutfall(List<Transform> fittings, Transform host, MapData map)
        {
            List<Transform> redFittings = fittings.Where(f => f.name == "Hazard Bulkhead").ToList();
            float fraction = (float)redFittings.Count / fittings.Count;
            Assert.LessOrEqual(fraction, RedFittingCap,
                $"{redFittings.Count} of {fittings.Count} fittings are red ({fraction:P0}) — must be at most {RedFittingCap:P0}");

            var anchors = new List<Vector2>();
            foreach (AreaGate gate in host.GetComponentsInChildren<AreaGate>(true))
                anchors.Add(new Vector2(gate.transform.position.x, gate.transform.position.z));

            // Same id MapRuntime/StormdrainDressing both already use for the outfall gate — duplicated
            // here as a literal for the same reason those two classes each keep their own copy: this
            // test asserts on the built scene, not on either class's internals.
            MapEntity outfall = map.Entity("outfall");
            if (outfall != null) anchors.Add(new Vector2(outfall.x, outfall.z));

            Assert.Greater(anchors.Count, 0,
                "World 2 must author at least one gate or outfall for this test to mean anything");
            Assert.Greater(redFittings.Count, 0,
                "World 2 must build at least one hazard bulkhead for this test to mean anything");

            foreach (Transform red in redFittings)
            {
                Vector2 pos = new Vector2(red.position.x, red.position.z);
                bool nearAnchor = anchors.Any(a => Vector2.Distance(a, pos) <= GateProximity);
                Assert.IsTrue(nearAnchor,
                    $"{red.name} at {pos} is not within {GateProximity} m of any gate or the outfall");
            }
        }

        private static void AssertZeroRealLightComponents(Transform dressingHost)
        {
            Light[] lights = dressingHost.GetComponentsInChildren<Light>(true);
            Assert.AreEqual(0, lights.Length, "MV-787 must never add a real UnityEngine.Light component");
        }

        private static void AssertHazardPulsePhaseIsDeterministicByPosition()
        {
            float phaseA = LightFittingPulse.PhaseFor(new Vector3(3.25f, 0f, 8.75f));
            float phaseB = LightFittingPulse.PhaseFor(new Vector3(11.5f, 0f, 2.0f));
            float phaseARepeat = LightFittingPulse.PhaseFor(new Vector3(3.25f, 0f, 8.75f));

            Assert.AreEqual(phaseA, phaseARepeat, 1e-6f,
                "the same fitting position must resolve the same pulse phase every time it is built");
            Assert.AreNotEqual(phaseA, phaseB,
                "two different fitting positions must not resolve the same pulse phase");
        }
    }
}
