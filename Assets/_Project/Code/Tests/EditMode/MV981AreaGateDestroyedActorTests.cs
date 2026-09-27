using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-981 — TestFlight v0.10.0, World 2: the whole first deck area strobed dark ~4x/second and
    /// ~95% of robots drew as nothing but their ground rings. Root cause: <see cref="CorrosionPuddle"/>
    /// registers with <see cref="MapStaticBatchRoot"/>'s gate on spawn (<c>RegisterGatedActorAtPosition</c>)
    /// but nothing ever unregistered it on expiry — <c>Destroy(gameObject)</c> left a dangling dictionary
    /// entry. The next <see cref="MapStaticBatchRoot.ApplyAreaGate"/> call (the ordinary zone-crossing
    /// path, or the throttled <c>TickDynamicGate</c>) read <c>ZoneGatePosition</c> off that destroyed
    /// puddle and threw a <c>MissingReferenceException</c> mid-enumeration — aborting the whole call
    /// BEFORE <c>_currentGateZoneId</c> was ever updated (freezing it on a stale zone, which
    /// <c>TickDynamicGate</c>/<c>TickGateSelfHeal</c> then fought over every frame — the strobe) and
    /// before every robot registered after the dead puddle in the dictionary's own insertion order was
    /// ever visited (the invisible robots).
    ///
    /// One consolidated test (MV-465 Rule 1) against World 2's real shipped a11 room: a real
    /// <see cref="CorrosionPuddle"/> spawned at a11's own centre (the exact <c>CorrosiveGlob</c> impact
    /// registration path), five robots registered after it in a different, gate-unrelated real zone,
    /// the puddle destroyed exactly the way its own expiry does, then Max moved off a11 entirely and the
    /// gate re-applied via both the crossing call and the throttled tick. Asserts RESOLVED values
    /// throughout (Rule 2, Tier 2): the call never throws, <c>_currentGateZoneId</c> actually lands on
    /// and stays on Max's own new zone, that zone's own combined static mesh never goes dark across 20
    /// ticks, and every one of the five robots still carries an enabled body renderer — never an
    /// authored constant, never a rendered pixel.
    ///
    /// Fails on the base commit this branch was cut from: <see cref="MapStaticBatchRoot.ApplyAreaGate"/>
    /// has no null/destroyed-actor guard and no <c>UnregisterGatedActor</c> exists for
    /// <see cref="CorrosionPuddle.OnDestroy"/> to call, so the crossing-path <c>ApplyAreaGate</c> call
    /// below throws a <c>MissingReferenceException</c> straight out of the test (caught by
    /// <c>Assert.DoesNotThrow</c>) — see the fix commit / Jira comment for that base-commit failure
    /// output.
    /// </summary>
    public sealed class MV981AreaGateDestroyedActorTests
    {
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown() => RobotEnemy.ResetRegistry();

        [Test]
        public void DestroyedPuddleNeverAbortsGate_RobotsRegisteredAfterItStayVisible()
        {
            // Same collider-strip [Error] noise every full-World2-build EditMode test in this suite carries.
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            MapZone a11 = map.Zone("area11");
            Assert.IsNotNull(a11, "setup failure: World 2 must author area11 (the puddle's own zone)");

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV981 Host");
            GameObject playerGo = null;
            var scratch = new List<GameObject>();
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);
                Transform mapRoot = host.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");

                playerGo = new GameObject("Player") { tag = "Player" };
                playerGo.transform.position = new Vector3(a11.x, 0.5f, a11.z);

                // MapStaticBatchRoot.Start() — never invoked automatically outside Play mode. Also sets
                // MapStaticBatchRoot.Active, which CorrosionPuddle.Spawn/RobotEnemy.SetAreaIndex register
                // themselves with below.
                InvokePrivate(batchRoot, "Start");

                var rendererZones = (Dictionary<Renderer, List<string>>)GetPrivateField(batchRoot, "_rendererZones");
                Assert.IsNotNull(rendererZones, "setup failure: MapStaticBatchRoot never carried its own rendererZones");

                MapZone targetZone = FindUnrelatedZoneWithCombinedRenderer(map, a11, rendererZones, out Renderer targetRenderer);
                Assert.IsNotNull(targetZone,
                    "setup failure: World 2 must have a zone unrelated to area11 (not linked, not footprint-sharing, its own combined mesh) to move Max to");
                Assert.IsNotNull(targetRenderer, "setup failure: the target zone has no combined static mesh to assert against");

                var puddlePos = new Vector3(a11.x, 0.5f, a11.z);
                var maxPos = new Vector3(targetZone.x, 0.5f, targetZone.z);

                // The puddle — registered via the exact production path a CorrosiveGlob impact uses
                // (CorrosionPuddle.Spawn -> MapStaticBatchRoot.RegisterGatedActorAtPosition).
                CorrosionPuddle puddle = CorrosionPuddle.Spawn(puddlePos, radius: 1.5f, duration: 4f);
                scratch.Add(puddle.gameObject);

                // Five robots, registered AFTER the puddle — the exact dictionary insertion order MV-981's
                // own root cause names ("every robot registered after the dead puddle is never reached
                // again"). Tagged to the target zone, not area11, so a fixed gate correctly lights them up
                // once Max stands there.
                var robots = new RobotEnemy[5];
                for (int i = 0; i < robots.Length; i++)
                {
                    robots[i] = NewRobot(scratch, maxPos + new Vector3(i * 1.5f, 0f, 0f));
                    robots[i].SetAreaIndex(targetZone.AreaIndex);
                }

                // Kill the puddle exactly the way its own expiry does (Tick(), _remaining <= 0f, calls
                // Destroy(gameObject)/DestroyImmediate) — ordinary Unity teardown, nothing test-only.
                Object.DestroyImmediate(puddle.gameObject);

                // Move Max off area11 entirely.
                playerGo.transform.position = maxPos;

                Assert.IsFalse(map.AreLinked(targetZone.id, a11.id),
                    "setup failure: the target zone must not be gate-linked to area11 for this to prove area11 left the active set");

                // --- the ordinary zone-crossing path ---
                Assert.DoesNotThrow(() => batchRoot.ApplyAreaGate(targetZone.id, maxPos),
                    "MV-981: ApplyAreaGate must never throw off a destroyed actor's dangling registration");

                Assert.AreEqual(targetZone.id, GetPrivateField(batchRoot, "_currentGateZoneId"),
                    "MV-981: _currentGateZoneId must land on Max's own new zone after the crossing call");
                AssertActiveSetExcludes(batchRoot, a11.id);
                Assert.IsTrue(targetRenderer.enabled,
                    "MV-981: the current zone's own combined mesh must be enabled right after the crossing call");

                // --- the throttled dynamic-gate path (TickDynamicGate), 20 consecutive ticks — the
                // strobe's own reproduction (TickGateSelfHeal/TickDynamicGate re-fighting a stale zone
                // ~4x/second) would show up here as the renderer or _currentGateZoneId flipping.
                for (int tick = 0; tick < 20; tick++)
                {
                    Assert.DoesNotThrow(() => InvokePrivate(batchRoot, "TickDynamicGate"),
                        $"MV-981: TickDynamicGate must never throw off the same dangling registration (tick {tick})");
                    Assert.AreEqual(targetZone.id, GetPrivateField(batchRoot, "_currentGateZoneId"),
                        $"MV-981: _currentGateZoneId must stay pinned on Max's own zone across every tick (tick {tick})");
                    Assert.IsTrue(targetRenderer.enabled,
                        $"MV-981: the current zone's own combined mesh must never go dark — the reported strobe (tick {tick})");
                }

                foreach (RobotEnemy r in robots)
                    Assert.IsTrue(AnyRendererEnabled(r),
                        $"MV-981: robot '{r.name}', registered after the dead puddle, must still be reachable and visible in its own active zone");
            }
            finally
            {
                foreach (GameObject go in scratch) if (go != null) Object.DestroyImmediate(go);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        /// <summary>A real World 2 zone that cannot re-enter the active set through any path
        /// <see cref="MapStaticBatchRoot.ApplyAreaGate"/> uses for <paramref name="a11"/> specifically
        /// (not gate-linked, not footprint-sharing, not itself a level&gt;0 overlay) and that already owns
        /// at least one "Combined " static mesh of its own (<c>MapStaticBatchRoot.CombineZoneGeometry</c>'s
        /// own output) to assert the strobe against.</summary>
        private static MapZone FindUnrelatedZoneWithCombinedRenderer(MapData map, MapZone a11,
            Dictionary<Renderer, List<string>> rendererZones, out Renderer combinedRenderer)
        {
            foreach (MapZone z in map.zones)
            {
                if (z == null || z.id == a11.id || z.level > 0 || z.AreaIndex <= 0) continue;
                if (map.AreLinked(z.id, a11.id)) continue;
                if (MapZone.ShareFootprint(z, a11)) continue;

                foreach (KeyValuePair<Renderer, List<string>> pair in rendererZones)
                {
                    if (pair.Key == null || !pair.Key.name.StartsWith("Combined ")) continue;
                    if (!pair.Value.Contains(z.id)) continue;
                    combinedRenderer = pair.Key;
                    return z;
                }
            }
            combinedRenderer = null;
            return null;
        }

        private static void AssertActiveSetExcludes(MapStaticBatchRoot batchRoot, string zoneId)
        {
            var active = (HashSet<string>)GetPrivateField(batchRoot, "_activeZoneIds");
            Assert.IsNotNull(active, "setup failure: MapStaticBatchRoot never carried its own active zone set");
            Assert.IsFalse(active.Contains(zoneId),
                $"MV-981: '{zoneId}' must have left the active set once Max moved away from it");
        }

        /// <summary>Same bare-robot construction idiom MV820ReplicatorQueueingTests' own NewRobot uses
        /// (Awake/OnEnable never run as a side effect of AddComponent outside Play mode, so _cc is
        /// stamped and OnEnable invoked directly) — built on a primitive rather than a bare GameObject so
        /// it carries a real Renderer for RefreshBodyVisibility/AnyRendererEnabled to actually measure.</summary>
        private RobotEnemy NewRobot(List<GameObject> scratch, Vector3 position)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "MV981 Robot";
            scratch.Add(go);
            Collider stray = go.GetComponent<Collider>();
            if (stray != null) Object.DestroyImmediate(stray);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Rusher);
            OnEnableMethod.Invoke(e, null);
            go.transform.position = position;
            return e;
        }

        private static bool AnyRendererEnabled(RobotEnemy r)
        {
            foreach (Renderer rend in r.GetComponentsInChildren<Renderer>(true))
                if (rend != null && rend.enabled) return true;
            return false;
        }

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        private static object GetPrivateField(object target, string fieldName) =>
            target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(target);
    }
}
