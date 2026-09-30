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
    /// MV-1038 — Lee, TestFlight v0.11.2, World 1, 2026-09-30: several pickup rings on the flat World 1
    /// lawn have no cell/part prop above them. Root cause, read off <c>origin/main</c> post-MV-1042
    /// (<c>e858950</c>): <c>MapStaticBatchRoot.RegisterAtPosition</c> assumes a renderer it registers
    /// never moves once dropped — it APPENDS the new zone to a renderer's own zone list and never removes
    /// the old one, and folds any renderer that reads disabled AT REGISTRATION TIME into
    /// <c>_dressedHidden</c> forever. A pickup is POOLED though (<c>PickupDirector._cellPool</c> etc): a
    /// pickup the gate disabled for its FIRST zone (because that zone wasn't active at the time) comes
    /// back out of the pool for a SECOND drop, in a DIFFERENT zone, and <c>RegisterAtPosition</c> sees
    /// that art renderer still reading disabled from its first life and files it into
    /// <c>_dressedHidden</c> — a set <c>ApplyAreaGate</c> never revisits. The pickup's <c>GroundRing</c>
    /// keeps showing (driven independently by <c>PickupArtDirector.ShowRing</c>), but the prop above it
    /// never draws again — exactly the reported "gold ring, no prop".
    ///
    /// <see cref="MapStaticBatchRoot.RegisterPickup"/> replaces <c>RegisterAtPosition</c> as
    /// <c>PickupDirector.SpawnDrop</c>'s own registration call: it tags a pickup's renderers to ONLY its
    /// current drop's zone (replace, never append) and never files a pickup renderer into
    /// <c>_dressedHidden</c> — the one legitimate permanently-hidden pickup renderer (the art-hidden
    /// greybox <c>Visual</c>) is instead structurally excluded from the gate's bookkeeping entirely.
    /// <see cref="MapStaticBatchRoot.Unregister"/>, called from <c>PickupDirector.Collect</c>/
    /// <c>RetireCell</c> before either pushes a pickup back into its pool, is what makes the pool safe: a
    /// pooled pickup's stale zone tag never survives to poison its next placement.
    ///
    /// One consolidated test (MV-465 Rule 1) against the real shipped World 1 config and a live
    /// <c>MapStaticBatchRoot</c> — same build/reflection idiom <see cref="MV988PickupGreyboxRegateTests"/>
    /// and <c>MV972DynamicAreaGateTests</c> already use for this class. Covers both pool-return paths
    /// (<c>Collect</c>, <c>RetireCell</c>) plus the map-authored parts cache (<c>MapRuntime.BuildProps</c>'s
    /// own <c>EntityKind.Pickup</c> case, reproduced directly via <c>PlacePartsCache</c> +
    /// <c>MapRuntime.TagStatic</c> rather than depending on World 1's config happening to author a
    /// PowerupCadence coverage gap today). Tier 2 throughout: every assertion reads a resolved value off
    /// the built hierarchy (a renderer's own <c>enabled</c> flag, the gate's own <c>_rendererZones</c> tag
    /// list for it) — never an authored constant, never a rendered pixel.
    /// </summary>
    // Guards MV-1038
    public sealed class MV1038PooledPickupRegateTests
    {
        private const string ArtPrefix = "PartArt:";

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

        [Test]
        public void PooledPickup_RegatesToItsNewZoneOnly_AfterCollectRetireCellAndTheMapAuthoredCache() // Guards MV-1038
        {
            // Same collider-strip [Error] noise every full-world-build EditMode test in this suite carries.
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "World 1's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            MapZone area1 = map.Zone("area1");
            Assert.IsNotNull(area1, "setup failure: World 1 must author area1");
            MapZone farZone = null;
            foreach (MapZone z in map.zones)
            {
                // level == 0 only: ZoneAt(x, y, z) resolves a low-y drop position to the FLOOR zone under
                // any XZ footprint it shares with an overlay deck (MapData.ZoneAt's own "onDeck" rule), so
                // a deck candidate here would silently resolve the drop below to a different zone than the
                // one this test means to gate off.
                if (z == null || z.level != 0 || z.id == "area1" || map.AreLinked("area1", z.id)) continue;
                farZone = z;
                break;
            }
            Assert.IsNotNull(farZone, "setup failure: World 1 must author a floor zone with no link to area1");

            Vector3 maxZonePos = new Vector3(area1.CenterXz.x, 0.5f, area1.CenterXz.y);
            Vector3 farZonePos = new Vector3(farZone.CenterXz.x, 0.5f, farZone.CenterXz.y);

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
                // (which SpawnDrop's own RegisterPickup call needs) and gates to area1 (no
                // AreaAccumulationDirector in this scene, MapStaticBatchRoot's own documented fallback).
                InvokePrivate(batchRoot, "Start");

                // Unity does not reliably call AddComponent's own Awake/OnEnable outside Play mode — invoked
                // by hand so OnPickupRegistered is actually subscribed before SpawnDrop below.
                artGo = new GameObject("PickupArt");
                var artDirector = artGo.AddComponent<PickupArtDirector>();
                InvokePrivate(artDirector, "Awake");
                InvokePrivate(artDirector, "OnEnable");

                PickupDirector director = PickupDirector.EnsureInstalled();
                var rendererZones = (Dictionary<Renderer, List<string>>)GetPrivateField(batchRoot, "_rendererZones");

                // --- Case 1: pooled reuse after a walk-over Collect ---------------------------------
                Pickup collected = SpawnDropAt(director, PickupKind.PowerCell, farZonePos);
                Assert.IsNotNull(collected, "setup failure: SpawnDrop refused the far-zone PowerCell");
                AssertArtRendererDisabled(collected,
                    "setup failure: the far-zone drop must actually be gated OFF for this test to mean anything");

                InvokeCollect(director, collected);
                Assert.IsFalse(collected.gameObject.activeSelf, "setup failure: Collect never deactivated the pickup");

                Pickup redroppedAfterCollect = SpawnDropAt(director, PickupKind.PowerCell, maxZonePos);
                Assert.AreSame(collected, redroppedAfterCollect, "setup failure: the pool must hand back the same instance");
                AssertResolvedCorrectly(redroppedAfterCollect, area1.id, rendererZones, "a pooled reuse after Collect");

                // --- Case 2: pooled reuse after RetireCell (cap eviction / lifetime) ----------------
                Pickup retired = SpawnDropAt(director, PickupKind.PowerCell, farZonePos);
                Assert.IsNotNull(retired, "setup failure: SpawnDrop refused the far-zone PowerCell");
                AssertArtRendererDisabled(retired,
                    "setup failure: the far-zone drop must actually be gated OFF for this test to mean anything");

                InvokeRetireCell(director, retired);
                Assert.IsFalse(retired.gameObject.activeSelf, "setup failure: RetireCell never deactivated the pickup");

                Pickup redroppedAfterRetire = SpawnDropAt(director, PickupKind.PowerCell, maxZonePos);
                Assert.AreSame(retired, redroppedAfterRetire, "setup failure: the pool must hand back the same instance");
                AssertResolvedCorrectly(redroppedAfterRetire, area1.id, rendererZones, "a pooled reuse after RetireCell");

                // --- Case 3: the map-authored parts cache, after a walk-over collect -----------------
                // MapRuntime.BuildProps tags a map-authored EntityKind.Pickup's own pickups via
                // MapRuntime.TagStatic, straight into this SAME rendererZones dictionary, since the gate
                // doesn't exist yet at Build time — reproduced directly here rather than depending on
                // World 1's config happening to author a PowerupCadence coverage gap today.
                List<Pickup> cache = director.PlacePartsCache(farZonePos);
                Pickup mapAuthoredCell = cache.Find(p => p != null && p.Kind == PickupKind.PowerCell);
                Assert.IsNotNull(mapAuthoredCell, "setup failure: PlacePartsCache must include at least one PowerCell");
                InvokeTagStatic(map, mapAuthoredCell.gameObject, rendererZones);
                AssertArtRendererDisabled(mapAuthoredCell,
                    "setup failure: the map-authored far-zone drop must actually be gated OFF for this test to mean anything");

                InvokeCollect(director, mapAuthoredCell);
                Assert.IsFalse(mapAuthoredCell.gameObject.activeSelf, "setup failure: Collect never deactivated the map-authored pickup");

                Pickup redroppedMapAuthored = SpawnDropAt(director, PickupKind.PowerCell, maxZonePos);
                Assert.AreSame(mapAuthoredCell, redroppedMapAuthored, "setup failure: the pool must hand back the same instance");
                AssertResolvedCorrectly(redroppedMapAuthored, area1.id, rendererZones, "the map-authored parts cache, after a walk-over collect");
            }
            finally
            {
                // Unity does not reliably call OnDisable on DestroyImmediate outside Play mode either
                // (the same quirk the Awake/OnEnable comment above documents) — left to chance, this
                // PickupArtDirector's subscription to the STATIC Pickup.Registered event survives its own
                // destruction and fires on every later test's pickup drops for the rest of this EditMode
                // batch, in suite order. MV1038 sorts alphabetically before MV972DynamicAreaGateTests,
                // which measured exactly that: a leaked handler building designed art (and hiding the
                // greybox) for a pickup MV972's own test never expected PickupArtDirector to touch at all.
                if (artGo != null)
                {
                    var artDirector = artGo.GetComponent<PickupArtDirector>();
                    if (artDirector != null) InvokePrivate(artDirector, "OnDisable");
                    Object.DestroyImmediate(artGo);
                }
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        /// <summary>Setup-only precondition: proves the far-zone drop this test is about to pool-and-redrop
        /// really is gated off right now, so the later "back on in Max's own zone" assertion actually proves
        /// something.</summary>
        private static void AssertArtRendererDisabled(Pickup pickup, string when)
        {
            Transform art = FindArtChild(pickup.transform);
            Assert.IsNotNull(art, $"setup failure: '{pickup.name}' carries no PickupArtDirector-built art child");
            bool anyEnabled = false;
            foreach (Renderer r in art.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { anyEnabled = true; break; }
            Assert.IsFalse(anyEnabled, when);
        }

        /// <summary>MV-1038's own AC1: after a pooled reuse, every art <c>MeshRenderer</c> is enabled, the
        /// greybox is disabled, and the pickup's renderers are tagged only to the new zone.</summary>
        private static void AssertResolvedCorrectly(Pickup pickup, string expectedZoneId,
            Dictionary<Renderer, List<string>> rendererZones, string when)
        {
            Transform visual = pickup.transform.Find("Visual");
            Assert.IsNotNull(visual, $"setup failure: '{pickup.name}' carries no greybox 'Visual' child");
            var visualRenderer = visual.GetComponent<MeshRenderer>();
            Assert.IsNotNull(visualRenderer, $"setup failure: '{pickup.name}' Visual carries no MeshRenderer");
            Assert.IsFalse(visualRenderer.enabled,
                $"MV-1038: '{pickup.name}' greybox must stay hidden ({when})");

            Transform art = FindArtChild(pickup.transform);
            Assert.IsNotNull(art, $"setup failure: '{pickup.name}' carries no PickupArtDirector-built art child");
            Renderer[] artRenderers = art.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(artRenderers.Length, 0, $"setup failure: '{pickup.name}' art carries no renderers");
            foreach (Renderer r in artRenderers)
            {
                Assert.IsTrue(r.enabled,
                    $"MV-1038: '{pickup.name}' designed art renderer '{r.name}' must be enabled ({when}) — " +
                    "this is the reported ring-only regression");

                Assert.IsTrue(rendererZones.TryGetValue(r, out List<string> zones),
                    $"MV-1038: '{pickup.name}' art renderer '{r.name}' must be tracked by the gate ({when})");
                Assert.AreEqual(1, zones.Count,
                    $"MV-1038: '{pickup.name}' art renderer '{r.name}' must be tagged to exactly one zone, " +
                    $"not carry a stale zone alongside the new one ({when})");
                Assert.AreEqual(expectedZoneId, zones[0],
                    $"MV-1038: '{pickup.name}' art renderer '{r.name}' must be tagged to its NEW zone ({when})");
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

        private static void InvokeCollect(PickupDirector director, Pickup pickup)
        {
            var liveField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (System.Collections.IList)liveField.GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the collected pickup must still be live on the director");
            typeof(PickupDirector).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, pickup });
        }

        private static void InvokeRetireCell(PickupDirector director, Pickup pickup)
        {
            var liveField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (System.Collections.IList)liveField.GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the retired cell must still be live on the director");
            typeof(PickupDirector).GetMethod("RetireCell", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, pickup });
        }

        private static void InvokeTagStatic(MapData map, GameObject go, Dictionary<Renderer, List<string>> rendererZones) =>
            typeof(MapRuntime).GetMethod("TagStatic", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { map, go, rendererZones });

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        private static object GetPrivateField(object target, string fieldName) =>
            target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    }
}
