using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.Upgrades;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1038 — a pickup returned to its pool (a walk-over <c>Collect</c>, or <c>RetireCell</c>'s cap/
    /// lifetime eviction) came back from a later drop with its designed art permanently gated off — only
    /// its <see cref="MaxWorlds.VFX.PickupArtDirector"/>-driven <c>GroundRing</c> ever showed — whenever
    /// its art had been disabled by an ordinary <see cref="MapStaticBatchRoot.ApplyAreaGate"/> zone
    /// change in a PREVIOUS life. Neither pool-return path ever stripped the pickup's renderers out of
    /// <see cref="MapStaticBatchRoot"/>'s own bookkeeping, so <see cref="MapStaticBatchRoot.RegisterAtPosition"/>
    /// — reading whatever enabled/disabled state each renderer already carried in — APPENDED the new
    /// drop's zone onto the stale one instead of replacing it, and folded the (merely gated-off, not
    /// actually retired) art renderer into <c>_dressedHidden</c> as if it were permanently-hidden
    /// dressing, which the gate then skips forever.
    ///
    /// Fails on 5a1a831 (base commit, before this fix): a PowerCell dropped outside the active set, then
    /// <c>RetireCell</c>d back to the pool and redropped in Max's own zone, keeps its art renderer
    /// <c>enabled == false</c> — the reported "ring shows, cell/part doesn't" defect — and carries a
    /// stale two-entry zone list instead of just the new drop's own zone.
    ///
    /// One consolidated test (MV-465 Rule 1), against the real shipped World 1 config — the same build/
    /// reflection idiom <c>MV988PickupGreyboxRegateTests</c> and <c>MV972DynamicAreaGateTests</c> already
    /// use for this class. Asserts RESOLVED values throughout (Rule 2, Tier 2): a pickup's own art/
    /// greybox <c>MeshRenderer.enabled</c> and its own tagged zone list, read off the built hierarchy and
    /// <see cref="MapStaticBatchRoot"/>'s own state after real <c>SpawnDrop</c>/<c>RetireCell</c>/
    /// <c>Collect</c> calls — never an authored constant, never a rendered pixel.
    /// </summary>
    public sealed class MV1038PooledPickupRegateTests
    {
        private const string ArtPrefix = "PartArt:";   // mirrors PickupArtDirector's own private const

        [SetUp]
        public void SetUp()
        {
            PickupWallet.Reset();
            Pickup.ResetRegistry();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            foreach (var a in Object.FindObjectsByType<PickupArtDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(a.gameObject);
            Pickup.ResetRegistry();
            PickupWallet.Reset();
        }

        // Guards MV-1038
        [Test]
        public void PooledPickup_RegainsGatedArtAndSingleZoneTag_AfterReturningToPoolFromOutsideActiveSet()
        {
            // Same collider-strip [Error] noise every full-world-build EditMode test in this suite carries.
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "World 1's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            MapZone area1 = map.Zone("area1");
            Assert.IsNotNull(area1, "setup failure: World 1 must author area1");
            string farZoneId = null;
            foreach (MapZone z in map.zones)
            {
                if (z == null || z.id == "area1" || map.AreLinked("area1", z.id)) continue;
                farZoneId = z.id;
                break;
            }
            Assert.IsNotNull(farZoneId, "setup failure: World 1 must author a zone with no link to area1");
            MapZone farZone = map.Zone(farZoneId);
            Assert.IsNotNull(farZone, "setup failure: farZoneId must resolve to a real zone");

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV1038 Host");
            GameObject artGo = null;
            try
            {
                MapRuntime.Build(map, host.transform);
                Transform mapRoot = host.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");

                // MapStaticBatchRoot.Start() — never invoked automatically outside Play mode. Sets Active
                // (which RegisterPickup needs) and gates to area1 (no AreaAccumulationDirector in this
                // scene, MapStaticBatchRoot's own documented fallback).
                InvokePrivate(batchRoot, "Start");

                // Unity does not reliably call AddComponent's own Awake/OnEnable outside Play mode —
                // invoked by hand so OnPickupRegistered is actually subscribed before any SpawnDrop below.
                artGo = new GameObject("PickupArt");
                var artDirector = artGo.AddComponent<PickupArtDirector>();
                InvokePrivate(artDirector, "Awake");
                InvokePrivate(artDirector, "OnEnable");

                PickupDirector director = PickupDirector.EnsureInstalled();
                Vector3 farPos = new Vector3(farZone.CenterXz.x, 0.5f, farZone.CenterXz.y);
                Vector3 nearPos = new Vector3(area1.CenterXz.x, 0.5f, area1.CenterXz.y);

                // --- Case 1: an ordinary drop, gated off, RetireCell'd, then redropped in Max's zone ---
                Pickup cell = SpawnDropAt(director, PickupKind.PowerCell, farPos);
                Assert.IsNotNull(cell, "setup failure: SpawnDrop refused the PowerCell");
                AssertArtRendererDisabled(cell, "fresh PowerCell dropped outside the active set");

                RetireCellAt(director, cell);
                SpawnDropAt(director, PickupKind.PowerCell, nearPos);   // pops the same pooled instance

                AssertGreyboxHiddenAndArtVisible(cell, "PowerCell, redropped in Max's own zone after RetireCell");
                AssertTaggedOnlyToZone(batchRoot, cell, "area1");

                // --- Case 2: the map-authored parts cache, returned via a walk-over Collect -------------
                List<Pickup> cache = director.PlacePartsCache(farPos);
                Pickup cachedCell = null;
                foreach (Pickup p in cache)
                    if (p.Kind == PickupKind.PowerCell) { cachedCell = p; break; }
                Assert.IsNotNull(cachedCell, "setup failure: PlacePartsCache must include a PowerCell");
                AssertArtRendererDisabled(cachedCell, "map-authored PowerCell placed outside the active set");

                CollectAt(director, cachedCell);
                SpawnDropAt(director, PickupKind.PowerCell, nearPos);   // pops the same pooled instance

                AssertGreyboxHiddenAndArtVisible(cachedCell, "map-authored PowerCell, redropped after a walk-over collect");
                AssertTaggedOnlyToZone(batchRoot, cachedCell, "area1");
            }
            finally
            {
                if (artGo != null) Object.DestroyImmediate(artGo);
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        private static void AssertArtRendererDisabled(Pickup pickup, string when)
        {
            Transform art = FindArtChild(pickup.transform);
            Assert.IsNotNull(art, $"setup failure: '{pickup.name}' carries no PickupArtDirector-built art child");
            bool anyEnabled = false;
            foreach (Renderer r in art.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { anyEnabled = true; break; }
            Assert.IsFalse(anyEnabled,
                $"setup failure: '{pickup.name}' art should read gated-off outside the active set ({when})");
        }

        private static void AssertGreyboxHiddenAndArtVisible(Pickup pickup, string when)
        {
            Transform visual = pickup.transform.Find("Visual");
            Assert.IsNotNull(visual, $"setup failure: '{pickup.name}' carries no greybox 'Visual' child");
            var visualRenderer = visual.GetComponent<MeshRenderer>();
            Assert.IsNotNull(visualRenderer, $"setup failure: '{pickup.name}' Visual carries no MeshRenderer");
            Assert.IsFalse(visualRenderer.enabled,
                $"MV-1038: '{pickup.name}' greybox must stay hidden ({when})");

            Transform art = FindArtChild(pickup.transform);
            Assert.IsNotNull(art, $"setup failure: '{pickup.name}' carries no PickupArtDirector-built art child");
            bool anyArtRendererEnabled = false;
            foreach (Renderer r in art.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { anyArtRendererEnabled = true; break; }
            Assert.IsTrue(anyArtRendererEnabled,
                $"MV-1038: '{pickup.name}' designed art must be enabled again ({when}) — this is the reported ring-only regression");
        }

        private static void AssertTaggedOnlyToZone(MapStaticBatchRoot batchRoot, Pickup pickup, string expectedZoneId)
        {
            var field = typeof(MapStaticBatchRoot).GetField("_rendererZones", BindingFlags.NonPublic | BindingFlags.Instance);
            var rendererZones = (Dictionary<Renderer, List<string>>)field.GetValue(batchRoot);

            foreach (Renderer r in pickup.GetComponentsInChildren<Renderer>(true))
            {
                bool isHiddenGreybox = r.transform.parent == pickup.transform && r.transform.name == "Visual" && !r.enabled;
                if (isHiddenGreybox)
                {
                    // MapStaticBatchRoot.RegisterPickup deliberately leaves an art-hidden greybox OUT of
                    // its own bookkeeping so no later ApplyAreaGate call can ever re-enable it (MV-988).
                    Assert.IsFalse(rendererZones.ContainsKey(r),
                        $"MV-1038: '{pickup.name}' hidden greybox must not be tagged at all");
                    continue;
                }

                Assert.IsTrue(rendererZones.TryGetValue(r, out List<string> zones),
                    $"MV-1038: '{pickup.name}' renderer '{r.name}' must still be tagged after the redrop");
                Assert.AreEqual(1, zones.Count,
                    $"MV-1038: '{pickup.name}' renderer '{r.name}' carries a stale zone from a previous life: [{string.Join(",", zones)}]");
                Assert.AreEqual(expectedZoneId, zones[0],
                    $"MV-1038: '{pickup.name}' renderer '{r.name}' tagged to the wrong zone");
            }
        }

        private static Transform FindArtChild(Transform pickup)
        {
            for (int i = 0; i < pickup.childCount; i++)
            {
                var c = pickup.GetChild(i);
                if (c.name.StartsWith(ArtPrefix)) return c;
            }
            return null;
        }

        private static Pickup SpawnDropAt(PickupDirector director, PickupKind kind, Vector3 pos) =>
            (Pickup)typeof(PickupDirector).GetMethod("SpawnDrop", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { kind, pos, default(PartKind), default(AbilityKind) });

        private static void RetireCellAt(PickupDirector director, Pickup p)
        {
            List<Pickup> live = GetLive(director);
            int index = live.IndexOf(p);
            Assert.GreaterOrEqual(index, 0, "setup failure: pickup must be live before RetireCell");
            typeof(PickupDirector).GetMethod("RetireCell", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, p });
        }

        private static void CollectAt(PickupDirector director, Pickup p)
        {
            List<Pickup> live = GetLive(director);
            int index = live.IndexOf(p);
            Assert.GreaterOrEqual(index, 0, "setup failure: pickup must be live before Collect");
            typeof(PickupDirector).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, p });
        }

        private static List<Pickup> GetLive(PickupDirector director) =>
            (List<Pickup>)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);
    }
}
