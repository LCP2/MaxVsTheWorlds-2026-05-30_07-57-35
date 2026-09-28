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
    /// MV-988 — a dropped pickup's greybox <c>Visual</c> box drew again alongside its designed art the
    /// moment <see cref="MapStaticBatchRoot.RegisterAtPosition"/> (MV-972) or a later
    /// <see cref="MapStaticBatchRoot.ApplyAreaGate"/> zone change ran, because
    /// <c>PickupArtDirector.HideGreybox</c> disables that renderer BEFORE either ever sees it — inside the
    /// same <c>Pickup.Place</c> call <c>PickupDirector.SpawnDrop</c> makes, just before it registers the
    /// pickup's renderers with the gate — and, unlike <see cref="MapStaticBatchRoot.Start"/>'s own one-time
    /// dressing snapshot (MV-890), <c>RegisterAtPosition</c> never folded an already-disabled renderer into
    /// <c>_dressedHidden</c>. It just stamped every renderer <c>visible</c> off the pickup's own zone,
    /// greybox included, and every later gate call re-stamped it the same way.
    ///
    /// Fails on 8c86feb: before this fix, the freshly-registered greybox <c>Visual</c> renderer reads
    /// <c>enabled == true</c> the instant <see cref="PickupDirector.SpawnDrop"/> returns — the un-gated
    /// stamp <c>RegisterAtPosition</c> applies on top of <c>HideGreybox</c>'s own disable — alongside the
    /// designed art <c>PickupArtDirector</c> built in the same call.
    ///
    /// One consolidated test (MV-465 Rule 1), against the real shipped World 1 config — the same
    /// build/reflection idiom <c>MV890AreaGateDressingPairingTests</c> and <c>MV972DynamicAreaGateTests</c>
    /// already use for this class, not a hand-built fixture. Asserts RESOLVED values throughout (Rule 2,
    /// Tier 2): a pickup's own <c>Visual</c>/art <c>MeshRenderer.enabled</c>, read off the built hierarchy
    /// after a real <c>PickupDirector.SpawnDrop</c> call and again after a real <c>ApplyAreaGate</c> zone
    /// round-trip — never an authored constant, never a rendered pixel.
    /// </summary>
    public sealed class MV988PickupGreyboxRegateTests
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

        [Test]
        public void DroppedPickup_GreyboxStaysHiddenAndArtStaysVisible_ThroughRegistrationAndAreaGateRoundTrip()
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

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV988 Host");
            GameObject artGo = null;
            try
            {
                MapRuntime.Build(map, host.transform);
                Transform mapRoot = host.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");

                // MapStaticBatchRoot.Start() — never invoked automatically outside Play mode. Sets Active
                // (which SpawnDrop's own RegisterAtPosition call needs) and gates to area1 (no
                // AreaAccumulationDirector in this scene, MapStaticBatchRoot's own documented fallback).
                InvokePrivate(batchRoot, "Start");

                // Unity does not reliably call AddComponent's own Awake/OnEnable outside Play mode
                // (see MV626PickupArtCachingTests' own doc comment) — invoked by hand so
                // OnPickupRegistered is actually subscribed to Pickup.Registered before SpawnDrop below.
                artGo = new GameObject("PickupArt");
                var artDirector = artGo.AddComponent<PickupArtDirector>();
                InvokePrivate(artDirector, "Awake");
                InvokePrivate(artDirector, "OnEnable");

                PickupDirector director = PickupDirector.EnsureInstalled();
                Vector3 dropPos = new Vector3(area1.CenterXz.x, 0.5f, area1.CenterXz.y);

                Pickup cell = SpawnDropAt(director, PickupKind.PowerCell, dropPos);
                Pickup supercell = SpawnDropAt(director, PickupKind.Supercell, dropPos);
                Assert.IsNotNull(cell, "setup failure: SpawnDrop refused the PowerCell");
                Assert.IsNotNull(supercell, "setup failure: SpawnDrop refused the Supercell");

                AssertGreyboxHiddenAndArtVisible(cell, "PowerCell, right after SpawnDrop");
                AssertGreyboxHiddenAndArtVisible(supercell, "Supercell, right after SpawnDrop");

                // Drive the gate out of area1 and back. RegisterAtPosition's own MV-972 stamp already
                // reproduces the bug before a single ApplyAreaGate call runs; this proves the fix holds
                // through the gate's ordinary zone-change path too (MV-890's _dressedHidden mechanism,
                // now also reached by a renderer RegisterAtPosition folded in, not only Start's snapshot).
                batchRoot.ApplyAreaGate(farZoneId);
                batchRoot.ApplyAreaGate("area1", dropPos);

                AssertGreyboxHiddenAndArtVisible(cell, "PowerCell, after an area-gate round trip");
                AssertGreyboxHiddenAndArtVisible(supercell, "Supercell, after an area-gate round trip");
            }
            finally
            {
                if (artGo != null) Object.DestroyImmediate(artGo);
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        private static void AssertGreyboxHiddenAndArtVisible(Pickup pickup, string when)
        {
            Transform visual = pickup.transform.Find("Visual");
            Assert.IsNotNull(visual, $"setup failure: '{pickup.name}' carries no greybox 'Visual' child");
            var visualRenderer = visual.GetComponent<MeshRenderer>();
            Assert.IsNotNull(visualRenderer, $"setup failure: '{pickup.name}' Visual carries no MeshRenderer");
            Assert.IsFalse(visualRenderer.enabled,
                $"MV-988: '{pickup.name}' greybox must stay hidden ({when}) — this is the reported grey box redraw");

            Transform art = FindArtChild(pickup.transform);
            Assert.IsNotNull(art, $"setup failure: '{pickup.name}' carries no PickupArtDirector-built art child");
            bool anyArtRendererEnabled = false;
            foreach (Renderer r in art.GetComponentsInChildren<Renderer>(true))
            {
                if (r.enabled) { anyArtRendererEnabled = true; break; }
            }
            Assert.IsTrue(anyArtRendererEnabled,
                $"MV-988: '{pickup.name}' designed art must stay visible ({when})");
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

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);
    }
}
