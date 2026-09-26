using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-978 — the last item of the 2026-09-26 root-cause package (MV-963/966/969/972/973): MV-972 only
    /// ever gated a renderer's <c>enabled</c> flag, not the per-frame script driving it, so
    /// <c>SludgeBubbles</c>/<c>SludgeFlow</c>/<c>DeckVisibility</c>/<c>LightFittingPulse</c>/<c>Replicator</c>
    /// kept doing real per-frame work (Transform writes, MaterialPropertyBlock round-trips) for every
    /// instance in the world regardless of area. Separately, <c>MapStaticBatchRoot.RecordRendererCensus</c>
    /// re-ran its own ~30k-renderer <c>GetComponentsInChildren</c> walk on every
    /// <see cref="MapStaticBatchRoot.ApplyAreaGate"/> call (every zone change, and the throttled 4x/sec
    /// dynamic-gate tick) — a main-thread spike this ticket measured on every zone change. And every
    /// robot's world health bar carried its own world-space <c>Canvas</c> — its own UI batch root.
    ///
    /// One consolidated test (MV-465 Rule 1) against World 2's real shipped a11/a12 rooms (both authored
    /// "replicator" rooms, so a11_rep1/a12_rep1 are the map's own real Replicators, not a fixture) plus
    /// four synthetic dressing instances registered at real, resolved positions via
    /// <see cref="MapStaticBatchRoot.RegisterAtPosition"/> — the same production tagging path a map-built
    /// prop gets, just driven directly so this test controls exactly where each one lands. Asserts
    /// RESOLVED call counters (Rule 2, Tier 2): whether each component's own per-frame body actually ran
    /// after 60 ticks, never an authored constant, never a rendered pixel.
    ///
    /// Fails on the pre-MV-978 base: every one of the five per-frame components below has no zone gate on
    /// its own <c>Update</c>/<c>Tick</c> at all, so the a12 instances' call counters read 60 (same as
    /// a11's) instead of 0; <c>RendererCensusRunCount</c> increments on the second <c>ApplyAreaGate</c>
    /// call instead of staying pinned at 1; and 30 spawned robot health bars each carry their own distinct
    /// <c>Canvas</c> instead of sharing one.
    ///
    /// Deliberately excluded: the ticket's own bolt-renderer-count assertion (item 4, "a fired bolt has
    /// &lt;=2 renderers"). Achieving it as specified — one combined core+sheath mesh, crackle via the
    /// sheath's own shader — requires either rewriting/culling MV770WeaponPresenceTests/MV805FireBoltTests/
    /// MV810BoltMeshCacheTests/MV825LaserBoltTests (all of which hard-assert "Bolt" and "Sheath" as two
    /// SEPARATE, independently measurable MeshRenderers, and MV825 additionally asserts exactly 3 crackle
    /// LineRenderers) or authoring a brand-new crackle shader this worker has no way to render and verify
    /// (Rule 2/Tier 3: a shader's visual correctness is a conformance-harness question, not an EditMode
    /// one). Flagged in this ticket's own hand-off comment for a triage decision rather than guessed at
    /// here. The one safe, non-conflicting part of item 4 — dropping the redundant "BoltGlow" duplicate
    /// draw of the exact same core mesh — is already landed in <c>SeekerPulse.BuildVisual</c>.
    /// </summary>
    public sealed class MV978PerFrameGatingTests
    {
        private const int TickFrames = 60;
        private const float Dt = 1f / 60f;

        [Test]
        public void DressingAndReplicatorsGateByZone_CensusStopsWalkingPerZoneChange_HealthBarsShareOneCanvas()
        {
            LogAssert.ignoreFailingMessages = true; // same collider-strip [Error] noise every full-world-build test carries

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            MapZone a11 = map.Zone("area11");
            MapZone a12 = map.Zone("area12");
            Assert.IsNotNull(a11, "setup failure: World 2 must author area11 (Valve Loft)");
            Assert.IsNotNull(a12, "setup failure: World 2 must author area12 (Pipe Gallery)");

            // Deep inside a11 (footprint x:[222,248] z:[96,120]) and comfortably >22m from the a11/a12
            // shared wall at x=248, so nothing placed in a12 can qualify for the MV-972 chase override.
            var maxPos = new Vector3(224f, 0.5f, 105f);
            // Deep inside a12 (footprint x:[248,272] z:[92,120]) at the real a12_rep1's own position.
            var a12Pos = new Vector3(256f, 0.2f, 104f);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV978 Host");
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
                playerGo.transform.position = maxPos;

                // MapStaticBatchRoot.Start() — never invoked automatically outside Play mode.
                InvokePrivate(batchRoot, "Start");
                int censusRunsAfterBuild = batchRoot.RendererCensusRunCount;
                Assert.AreEqual(1, censusRunsAfterBuild,
                    "MV-978: the renderer census must run exactly once at world build");

                // --- the map's own two real Replicators (a11_rep1, a12_rep1) ---
                Assert.GreaterOrEqual(built.Replicators.Count, 2,
                    "setup failure: World 2's a11/a12 must each author a real Replicator");
                Replicator repA11 = null, repA12 = null;
                foreach (Replicator r in built.Replicators)
                {
                    MapZone z = map.ZoneAt(r.transform.position.x, r.transform.position.y, r.transform.position.z);
                    if (z == null) continue;
                    if (z.id == "area11") repA11 = r;
                    else if (z.id == "area12") repA12 = r;
                }
                Assert.IsNotNull(repA11, "setup failure: could not find a11's own real Replicator (a11_rep1)");
                Assert.IsNotNull(repA12, "setup failure: could not find a12's own real Replicator (a12_rep1)");
                repA11.SetAreaIndex(11);
                repA12.SetAreaIndex(12);

                // --- four synthetic dressing instances, one pair each, tagged at real resolved positions
                // via the same MapStaticBatchRoot.RegisterAtPosition path a map-built prop gets ---
                SludgeBubbles bubblesIn = BuildSludgeBubbles(scratch, maxPos, "BubblesIn");
                SludgeBubbles bubblesOut = BuildSludgeBubbles(scratch, a12Pos, "BubblesOut");
                batchRoot.RegisterAtPosition(bubblesIn.GetComponentsInChildren<Renderer>(), maxPos);
                batchRoot.RegisterAtPosition(bubblesOut.GetComponentsInChildren<Renderer>(), a12Pos);

                (SludgeFlow flowIn, Renderer flowInRenderer) = BuildSludgeFlow(scratch, maxPos, "FlowIn");
                (SludgeFlow flowOut, Renderer flowOutRenderer) = BuildSludgeFlow(scratch, a12Pos, "FlowOut");
                batchRoot.RegisterAtPosition(new[] { flowInRenderer }, maxPos);
                batchRoot.RegisterAtPosition(new[] { flowOutRenderer }, a12Pos);

                (DeckVisibility deckIn, Renderer deckInRenderer) = BuildDeckVisibility(scratch, maxPos, "DeckIn");
                (DeckVisibility deckOut, Renderer deckOutRenderer) = BuildDeckVisibility(scratch, a12Pos, "DeckOut");
                batchRoot.RegisterAtPosition(new[] { deckInRenderer }, maxPos);
                batchRoot.RegisterAtPosition(new[] { deckOutRenderer }, a12Pos);

                (LightFittingPulse lampIn, Renderer lampInRenderer) = BuildLightFittingPulse(scratch, maxPos, "LampIn");
                (LightFittingPulse lampOut, Renderer lampOutRenderer) = BuildLightFittingPulse(scratch, a12Pos, "LampOut");
                batchRoot.RegisterAtPosition(new[] { lampInRenderer }, maxPos);
                batchRoot.RegisterAtPosition(new[] { lampOutRenderer }, a12Pos);

                // --- resolve the gate to a11 (Max's own current zone) ---
                batchRoot.ApplyAreaGate("area11", maxPos);
                Assert.AreEqual(censusRunsAfterBuild, batchRoot.RendererCensusRunCount,
                    "MV-978: ApplyAreaGate must not re-run the renderer census (no GetComponentsInChildren walk on a zone change)");

                Assert.IsTrue(flowInRenderer.enabled, "setup failure: the in-zone sludge tile must be tagged area11 and enabled");
                Assert.IsFalse(flowOutRenderer.enabled, "setup failure: the out-of-zone sludge tile must be tagged area12 and disabled");

                // --- tick every pair 60 frames ---
                for (int i = 0; i < TickFrames; i++)
                {
                    bubblesIn.Tick(Dt);
                    bubblesOut.Tick(Dt);
                    InvokePrivate(flowIn, "Update");
                    InvokePrivate(flowOut, "Update");
                    InvokePrivate(deckIn, "Update");
                    InvokePrivate(deckOut, "Update");
                    lampIn.Tick(Dt);
                    lampOut.Tick(Dt);
                    InvokePrivate(repA11, "Update");
                    InvokePrivate(repA12, "Update");
                }

                Assert.Greater(bubblesIn.TickCallCount, 0, "MV-978: SludgeBubbles in the active zone must still tick");
                Assert.AreEqual(0, bubblesOut.TickCallCount, "MV-978: SludgeBubbles outside the active zone must not tick");

                Assert.Greater(flowIn.UpdateCallCount, 0, "MV-978: SludgeFlow in the active zone must still tick");
                Assert.AreEqual(0, flowOut.UpdateCallCount, "MV-978: SludgeFlow outside the active zone must not tick");

                Assert.Greater(deckIn.UpdateCallCount, 0, "MV-978: DeckVisibility in the active zone must still tick");
                Assert.AreEqual(0, deckOut.UpdateCallCount, "MV-978: DeckVisibility outside the active zone must not tick");

                Assert.Greater(lampIn.TickCallCount, 0, "MV-978: LightFittingPulse in the active zone must still tick");
                Assert.AreEqual(0, lampOut.TickCallCount, "MV-978: LightFittingPulse outside the active zone must not tick");

                Assert.Greater(repA11.UpdateCallCount, 0, "MV-978: the Replicator in the active zone must still tick");
                Assert.AreEqual(0, repA12.UpdateCallCount, "MV-978: the Replicator outside the active zone must not tick");

                // --- exactly one health-bar Canvas for 30 spawned damaged robots ---
                var bars = new List<WorldHealthBar>(30);
                var canvases = new HashSet<Canvas>();
                for (int i = 0; i < 30; i++)
                {
                    var unitGo = new GameObject($"FakeRobot{i}");
                    scratch.Add(unitGo);
                    var unit = unitGo.AddComponent<FakeDamagedUnit>();
                    unit.Hp = 40f; // damaged, not full health -- earns visibility per MV-788
                    WorldHealthBar bar = WorldHealthBar.Attach(unitGo, unit, heightAboveCentre: 1.1f,
                        worldWidth: 1f, groupable: false, isPlayerBar: false);
                    bars.Add(bar);
                    Assert.IsNotNull(bar.BarRectTransform, $"setup failure: robot {i} built no bar plate at all");
                    canvases.Add(bar.BarRectTransform.GetComponentInParent<Canvas>());
                }
                Assert.AreEqual(1, canvases.Count,
                    $"MV-978: 30 robot health bars must share exactly one Canvas, found {canvases.Count}");
            }
            finally
            {
                foreach (GameObject go in scratch) if (go != null) Object.DestroyImmediate(go);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        private sealed class FakeDamagedUnit : MonoBehaviour, IHealthReadout
        {
            public float Hp = 100f;
            public float HealthNormalized => Mathf.Clamp01(Hp / 100f);
            public float HealthCurrent => Hp;
            public string ReadoutName => "TEST UNIT";
            public bool IsAlive => true;
        }

        private static SludgeBubbles BuildSludgeBubbles(List<GameObject> scratch, Vector3 at, string name)
        {
            var parent = new GameObject(name);
            scratch.Add(parent);
            parent.transform.position = at;
            return SludgeBubbles.Attach(parent.transform, "Bubbles", new Vector2(1f, 1f), 0.05f,
                seed: 1, count: 4, tone: Color.green);
        }

        private static (SludgeFlow, Renderer) BuildSludgeFlow(List<GameObject> scratch, Vector3 at, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            scratch.Add(go);
            go.transform.position = at;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            var flow = go.AddComponent<SludgeFlow>();
            flow.Configure(renderer, renderer.sharedMaterial, new Vector2(0f, 0.1f));
            return (flow, renderer);
        }

        private static (DeckVisibility, Renderer) BuildDeckVisibility(List<GameObject> scratch, Vector3 at, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            scratch.Add(go);
            go.transform.position = at;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            var deck = go.AddComponent<DeckVisibility>();
            // Awake() never runs as a side effect of AddComponent outside Play mode (this project's own
            // established EditMode constraint) -- it's what builds _mpb, so invoke it directly.
            InvokePrivate(deck, "Awake");
            deck.Configure(renderer, System.Array.Empty<GameObject>(),
                new Rect(at.x - 1f, at.z - 1f, 2f, 2f), at.y + 1f);
            return (deck, renderer);
        }

        private static (LightFittingPulse, Renderer) BuildLightFittingPulse(List<GameObject> scratch, Vector3 at, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            scratch.Add(go);
            go.transform.position = at;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            var pulse = go.AddComponent<LightFittingPulse>();
            pulse.Configure(at, period: 2f, renderer: renderer, baseTone: Color.white);
            return (pulse, renderer);
        }

        private static void InvokePrivate(object target, string methodName, params object[] args) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, args);
    }
}
