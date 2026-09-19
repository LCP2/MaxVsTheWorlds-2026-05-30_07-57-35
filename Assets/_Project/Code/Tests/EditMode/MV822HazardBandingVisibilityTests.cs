using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Factories;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-822: World 2's MV-803 hazard banding was built everywhere the ticket's own diagnosis names,
    /// but nobody could see any of it. Four independent causes, one ticket because they share one
    /// symptom: (1) <c>MapRuntime.BuildSludge</c> built an opaque slab at y 0-0.05 over every sludge
    /// rect, including channel-eligible ones, covering the MV-801 trough and its bands sitting below
    /// y 0; (2) <c>AreaGate</c>'s own hazard stripe was switched active only while <c>Locked</c>, and
    /// only a condition-gated gate ever locks, so 22 of World 2's 25 gate bands were built and then
    /// switched off forever; (3) the pump band sat at 0.84*r while the housing's own hex-prism forward
    /// CORNER (a real vertex — <see cref="CharacterMeshes.Prism"/>'s own <c>+ PI/sides</c> rotation) sits
    /// at ~0.985*r, so the band's middle third was inside the body; (4) the Replicator's own base bands
    /// were 0.21 m tall (a ~5 px sliver) and the ones on the hatch/output faces sat directly under
    /// RampIn/RampOut's own sloped deck. Fails on base commit 302e10e: none of the four fixes exist yet
    /// — see the fix comment for the captured failure output.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1) over a real World 2 area (a3) and gate
    /// (g4), asserting five RESOLVED values (Rule 2, Tier 2), never an authored constant and never a
    /// rendered pixel (Rule 3): (a) a3_sludge1's own MapRuntime slab renderer is not active; (b) a3's
    /// channel hazard stripes are active and nothing else active covers their XZ from above; (c) g4's
    /// hazard stripe renderer is active while <see cref="AreaGate.Locked"/> is false; (d) a pump
    /// housing's own hazard band resolves an inner edge at or beyond the housing's own radius; (e) a
    /// Replicator's own base bands resolve at least 0.42 m tall and never intersect its own RampIn.
    ///
    /// Retargeted from g3 to g4 by MV-852 (World 2 re-layout), which removed g3 (a3 E -> a4) along with
    /// every other gate touching a3's old east/deck connections; g4 (a3 S -> a5 N) is untouched.
    /// </summary>
    public sealed class MV822HazardBandingVisibilityTests
    {
        [Test]
        public void World2HazardBanding_SlabHidden_GateAndPumpAndReplicatorBandsVisible()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV822 host").transform;
            try
            {
                MapBuild build = MapRuntime.Build(map, host);
                StormdrainDressing.Dress(host, map, build.Cover);

                MapEntity a3Sludge = map.entities.First(e => e != null && e.id == "a3_sludge1");
                var a3Rect = new Rect(a3Sludge.x - a3Sludge.width * 0.5f, a3Sludge.z - a3Sludge.depth * 0.5f,
                    a3Sludge.width, a3Sludge.depth);
                Vector3 a3Center = new Vector3(a3Sludge.x, 0f, a3Sludge.z);

                // ---- (a) the MapRuntime slab under a3_sludge1's own channel is not active ----
                SludgeFlow a3Slab = host.GetComponentsInChildren<SludgeFlow>(true)
                    .FirstOrDefault(f => f.gameObject.name == "a3_sludge1");
                Assert.IsNotNull(a3Slab, "MapRuntime never built a sludge slab for a3_sludge1");
                Renderer slabRend = a3Slab.GetComponent<Renderer>();
                Assert.IsNotNull(slabRend, "a3_sludge1's slab carries no renderer");
                bool slabActiveAndCoveringChannel = slabRend.enabled && slabRend.gameObject.activeInHierarchy
                    && OverlapsXz(slabRend.bounds, a3Rect);
                Assert.IsFalse(slabActiveAndCoveringChannel,
                    "a3_sludge1's MapRuntime slab is still an active renderer covering its own channel -- " +
                    "the trough and its bands beneath it can never be seen");

                // ---- (b) every channel hazard stripe under a3 is active, and nothing active covers it ----
                Transform sludgeHost = host.Find("Stormdrain Dressing/Sludge");
                Assert.IsNotNull(sludgeHost, "the sludge dressing host was never built");
                Transform a3Tile = FindTileNear(sludgeHost, a3Center);
                Assert.IsNotNull(a3Tile, "a3's sludge tile was never built");
                Transform trough = a3Tile.Find("Channel Trough");
                Assert.IsNotNull(trough, "a3_sludge1 must be channel-eligible and carry a Channel Trough");

                var stripes = trough.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.StartsWith("Stripe")).ToList();
                Assert.Greater(stripes.Count, 0, "a3's channel trough built no hazard stripes -- this test proves nothing");

                // Restricted to the map's own single global floor slab plus the floor composition pass
                // (bays/joints/stains, MV-801's own channelRects exclusion) -- the two things that would
                // be a genuine regression of change 1 if either ever painted over a channel. A Deck,
                // Ramp, Grate or Hatch legitimately crosses OVER a channel by design (a bridge is the
                // point of a deck) and a wall-mounted fitting (a kerb guide-rail light) shares no floor
                // plane with a stripe at all -- IsFloorObstacle's own exemption list, reused here rather
                // than re-litigated, is exactly why those never count as "covering" for this check.
                Transform mapRoot = host.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                Transform mapFloor = mapRoot.Find("Map Floor");
                Assert.IsNotNull(mapFloor, "MapRuntime never built its own Map Floor");
                Transform floorComposition = host.Find("Stormdrain Dressing/Floor Composition");
                Assert.IsNotNull(floorComposition, "the floor composition host was never built");

                Renderer[] nearbyActive = mapFloor.GetComponentsInChildren<Renderer>(true)
                    .Concat(floorComposition.GetComponentsInChildren<Renderer>(true))
                    .Where(r => r.enabled && r.gameObject.activeInHierarchy)
                    .ToArray();

                foreach (Transform stripe in stripes)
                {
                    var stripeRend = stripe.GetComponent<Renderer>();
                    Assert.IsNotNull(stripeRend, $"{stripe.name} carries no renderer");
                    Assert.IsTrue(stripeRend.enabled && stripe.gameObject.activeInHierarchy,
                        $"{stripe.name} must be an active renderer");

                    Bounds sb = stripeRend.bounds;
                    foreach (Renderer other in nearbyActive)
                    {
                        if (other == stripeRend) continue;
                        if (IsDescendantOf(other.transform, a3Tile)) continue; // the tile's own companion parts
                        if (!OverlapsXz(other.bounds, sb)) continue;
                        Assert.IsFalse(other.bounds.min.y > sb.max.y,
                            $"{other.name} sits above {stripe.name} covering the same ground -- the stripe is hidden");
                    }
                }

                // ---- (c) g4's hazard stripe is active while unlocked ----
                AreaGate g4 = host.GetComponentsInChildren<AreaGate>(true).First(g => g.name == "g4");
                g4.Locked = false;
                g4.ApplyStormdrainGateSkin();
                Transform gateDressing = host.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == "g4 (Stormdrain Gate Dressing)");
                Assert.IsNotNull(gateDressing, "g4 never built its Stormdrain gate dressing");
                Transform gateBand = gateDressing.Find("Hazard Banding");
                Assert.IsNotNull(gateBand, "g4 never built its hazard banding");
                Assert.IsTrue(gateBand.gameObject.activeSelf,
                    "g4's hazard banding must be active while the gate is unlocked");

                // ---- (d) a pump housing's own hazard band clears its own radius ----
                var pumpHost = new GameObject("MV822 pump host").transform;
                try
                {
                    var pumpSize = new Vector3(1.4f, 1.4f, 1.4f);
                    float r = Mathf.Min(pumpSize.x, pumpSize.z) * 0.5f;
                    GameObject pump = StormdrainKit.BuildPumpHousing(pumpHost, Vector3.zero, pumpSize);
                    Transform pumpBand = pump.transform.Find("Hazard Banding");
                    Assert.IsNotNull(pumpBand, "BuildPumpHousing never built a hazard band");
                    Transform pumpPlate = pumpBand.Find("Plate");
                    Assert.IsNotNull(pumpPlate, "the pump's hazard band never built a Plate");
                    Renderer plateRend = pumpPlate.GetComponent<Renderer>();
                    Assert.IsNotNull(plateRend, "the pump's hazard plate carries no renderer");

                    float innerEdgeRadius = Mathf.Abs(plateRend.bounds.max.z);
                    Assert.GreaterOrEqual(innerEdgeRadius, r,
                        $"the pump band's resolved inner edge ({innerEdgeRadius:F3} m) must clear the housing's own radius ({r:F3} m)");
                }
                finally
                {
                    Object.DestroyImmediate(pumpHost.gameObject);
                }

                // ---- (e) a Replicator's own base bands are tall enough and clear of RampIn ----
                GameObject repGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                try
                {
                    // Replicator.BuildBody destroys the LED primitive's stock collider via
                    // Object.Destroy, edit-mode-illegal and always logs an [Error] -- same shape every
                    // other Replicator EditMode test in this suite carries.
                    LogAssert.ignoreFailingMessages = true;

                    repGo.name = "MV822 Replicator";
                    repGo.transform.position = new Vector3(90210f, 0f, -40404f); // far off any real geometry
                    repGo.transform.localScale = new Vector3(2f, 1.5f, 2f); // the shipped Replicator footprint
                    var replicator = repGo.AddComponent<Replicator>();
                    replicator.Build(); // AddComponent's own Awake never runs outside Play mode

                    Transform bodyRoot = repGo.transform.Find("Body");
                    Assert.IsNotNull(bodyRoot, "Replicator.Build never built its Body root");
                    Transform rampIn = bodyRoot.Find("RampIn");
                    Assert.IsNotNull(rampIn, "the Replicator body never built a RampIn");
                    Bounds rampBounds = CombinedBounds(rampIn);

                    var bandRoots = bodyRoot.Cast<Transform>().Where(t => t.name == "Hazard Banding").ToList();
                    Assert.Greater(bandRoots.Count, 0, "the Replicator body built no hazard banding at all");

                    foreach (Transform band in bandRoots)
                    {
                        Bounds bandBounds = CombinedBounds(band);
                        Assert.GreaterOrEqual(bandBounds.size.y, 0.42f,
                            $"a Replicator hazard band resolves {bandBounds.size.y:F3} m tall, under the 0.42 m floor");
                        Assert.IsFalse(rampBounds.Intersects(bandBounds),
                            "a Replicator hazard band's resolved bounds intersect RampIn's -- the ramp covers the band");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(repGo);
                }
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        private static Transform FindTileNear(Transform sludgeHost, Vector3 center)
        {
            foreach (Transform tile in sludgeHost)
            {
                var flatTile = new Vector3(tile.position.x, 0f, tile.position.z);
                var flatCenter = new Vector3(center.x, 0f, center.z);
                if (Vector3.Distance(flatTile, flatCenter) < 0.5f) return tile;
            }
            return null;
        }

        private static bool IsDescendantOf(Transform t, Transform ancestor)
        {
            for (Transform cur = t; cur != null; cur = cur.parent)
                if (cur == ancestor) return true;
            return false;
        }

        private static bool OverlapsXz(Bounds a, Rect r) =>
            a.max.x > r.xMin && a.min.x < r.xMax && a.max.z > r.yMin && a.min.z < r.yMax;

        private static bool OverlapsXz(Bounds a, Bounds b) =>
            a.max.x > b.min.x && a.min.x < b.max.x && a.max.z > b.min.z && a.min.z < b.max.z;

        private static Bounds CombinedBounds(Transform root)
        {
            Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }
    }
}
