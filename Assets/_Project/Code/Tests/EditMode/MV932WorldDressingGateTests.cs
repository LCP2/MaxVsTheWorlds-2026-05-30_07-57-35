using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-932 — the area renderer gate (<see cref="MapStaticBatchRoot.ApplyAreaGate"/>, MV-887/890/909/
    /// 920/925) only ever disables a renderer it TAGGED with a zone, and before this ticket only World
    /// 2's "Stormdrain Dressing" was ever registered with it. World 3's hull circuit spine and cover
    /// props built under the same area root as everything else and so were always enabled, everywhere,
    /// for the whole run; World 1's dressing (<see cref="BackyardDressing"/>, <see cref="BackyardHomeShed"/>,
    /// <see cref="BackyardEntryDoor"/>) builds on its own scene-root GameObject, entirely outside the map's
    /// own area root, so it was neither counted nor gated at all.
    ///
    /// Fails on the base commit this branch was cut from: before MV-932, <see cref="MapStaticBatchRoot"/>
    /// never tagged a World 1 or World 3 dressing renderer, so <c>ApplyAreaGate</c> left every one of
    /// them enabled regardless of which zone it actually stood in — both assertions below ("two links
    /// away is disabled") fail on that commit because <c>.enabled</c> reads true for both worlds.
    ///
    /// One consolidated test (MV-465 Rule 1) against the real shipped World 1 and World 3 configs,
    /// asserting a RESOLVED value (Rule 2, Tier 2): a dressing renderer's own <c>Renderer.enabled</c>
    /// after the gate has run, for a zone the graph (<c>map.links</c>) puts exactly two hops from area1
    /// (never active, per <see cref="MapStaticBatchRoot.ApplyAreaGate"/>'s own doc) versus area1 itself
    /// (always active).
    /// </summary>
    public sealed class MV932WorldDressingGateTests
    {
        [Test]
        public void TwoLinksAwayDisabled_CurrentZoneEnabled_ForWorld1AndWorld3Dressing()
        {
            // Same collider-strip [Error] noise every full-world-build EditMode test in this suite carries.
            LogAssert.ignoreFailingMessages = true;

            AssertWorld1Dressing();
            AssertWorld3Dressing();
        }

        private static void AssertWorld1Dressing()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "World 1's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV932 W1 Host");
            var pathGo = new GameObject("MV932 W1 Path");
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                var path = pathGo.AddComponent<BackyardPath>();
                SetField(path, "_map", map);
                SetField(path, "_build", built);

                // The exact self-install idiom BackyardDressing/BackyardHomeShed/BackyardEntryDoor's own
                // [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] uses — that attribute never fires in an
                // EditMode test, so it's reproduced directly. Unity does NOT run Awake on AddComponent
                // outside Play mode (same fact MV611DormantAreaGateTests' own doc comment records), so
                // each Awake is reflection-invoked, the same idiom that test uses for RobotEnemy.
                InvokePrivate(new GameObject("Backyard Dressing").AddComponent<BackyardDressing>(), "Awake");
                InvokePrivate(new GameObject("BackyardHomeShed").AddComponent<BackyardHomeShed>(), "Awake");
                InvokePrivate(new GameObject("BackyardEntryDoor").AddComponent<BackyardEntryDoor>(), "Awake");

                MapStaticBatchRoot batchRoot = GetBatchRoot(host, map);
                InvokePrivate(batchRoot, "Start");

                // BackyardPath.Awake (fired above by AddComponent) may have built its own stray
                // AreaAccumulationDirector for whatever world it resolved by default — pin the gate to
                // area1 explicitly rather than trust Start()'s own "read PhysicalArea off whichever
                // director FindFirstObjectByType happens to find" default, so this test's "current zone"
                // is deterministic regardless of that side effect.
                batchRoot.ApplyAreaGate("area1");
                Debug.Log($"MV-932 census W1 a1: {FrameCost.AreaCensusLine()}");

                Dictionary<Renderer, List<string>> zones = RendererZones(batchRoot);

                var dressingRoots = new List<Transform>();
                foreach (var dressing in Object.FindObjectsByType<BackyardDressing>(FindObjectsSortMode.None))
                {
                    Transform kitProps = dressing.transform.Find("Kit Props");
                    if (kitProps != null) dressingRoots.Add(kitProps);
                }
                foreach (var shed in Object.FindObjectsByType<BackyardHomeShed>(FindObjectsSortMode.None))
                {
                    Transform maxsShed = shed.transform.Find("MaxsShed");
                    if (maxsShed != null) dressingRoots.Add(maxsShed);
                }
                foreach (var door in Object.FindObjectsByType<BackyardEntryDoor>(FindObjectsSortMode.None))
                {
                    Transform entryDoor = door.transform.Find("EntryDoor");
                    if (entryDoor != null) dressingRoots.Add(entryDoor);
                }

                AssertGateAffectsDressing("World 1", batchRoot, map, zones, dressingRoots);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(pathGo);
                foreach (var d in Object.FindObjectsByType<BackyardDressing>(FindObjectsSortMode.None)) Object.DestroyImmediate(d.gameObject);
                foreach (var s in Object.FindObjectsByType<BackyardHomeShed>(FindObjectsSortMode.None)) Object.DestroyImmediate(s.gameObject);
                foreach (var e in Object.FindObjectsByType<BackyardEntryDoor>(FindObjectsSortMode.None)) Object.DestroyImmediate(e.gameObject);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        private static void AssertWorld3Dressing()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV932 W3 Host");
            var pathGo = new GameObject("MV932 W3 Path");
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                var path = pathGo.AddComponent<BackyardPath>();

                // Reproduces BackyardPath.Awake's own World 3 branch (worldIndex >= 2), the same
                // reflection idiom MV755StormdrainDressingTests already uses for the World 2 branch.
                typeof(BackyardPath).GetMethod("ApplyWorldMaterials", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(path, new object[] { 2, host.transform, built.Cover });

                MapStaticBatchRoot batchRoot = GetBatchRoot(host, map);
                InvokePrivate(batchRoot, "Start");

                // Same determinism fix as the World 1 half above — pin to area1 explicitly rather than
                // trust whatever stray AreaAccumulationDirector BackyardPath.Awake's own default-world
                // resolution may have left behind.
                batchRoot.ApplyAreaGate("area1");
                Debug.Log($"MV-932 census W3 a1: {FrameCost.AreaCensusLine()}");

                Dictionary<Renderer, List<string>> zones = RendererZones(batchRoot);

                var dressingRoots = new List<Transform>();
                Transform circuitSpine = host.transform.Find("Circuit Spine");
                if (circuitSpine != null) dressingRoots.Add(circuitSpine);
                Transform reefProps = host.transform.Find("Reef Props");
                if (reefProps != null) dressingRoots.Add(reefProps);

                AssertGateAffectsDressing("World 3", batchRoot, map, zones, dressingRoots);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(pathGo);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        private static MapStaticBatchRoot GetBatchRoot(GameObject host, MapData map)
        {
            Transform mapRoot = host.transform.Find($"Map: {map.name}");
            Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
            var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
            Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");
            return batchRoot;
        }

        /// <summary>Asserts the gate's measured effect on whichever dressing renderer this world's own
        /// roots produced: one belonging to area1 (current zone, per <c>MapStaticBatchRoot.Start</c>'s own
        /// "no <see cref="MaxWorlds.Enemies.AreaAccumulationDirector"/> in this scene, default to area1"
        /// fallback) stays enabled, and one belonging to a zone the map's own link graph puts exactly two
        /// hops from area1 — never part of <c>ApplyAreaGate</c>'s active set — is disabled.</summary>
        private static void AssertGateAffectsDressing(string worldLabel, MapStaticBatchRoot batchRoot, MapData map,
            Dictionary<Renderer, List<string>> zones, List<Transform> dressingRoots)
        {
            var activeZoneIds = (HashSet<string>)typeof(MapStaticBatchRoot)
                .GetField("_activeZoneIds", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(batchRoot);

            Renderer currentRenderer = FindDressingRendererForZone(dressingRoots, zones, "area1");
            Assert.IsNotNull(currentRenderer,
                $"{worldLabel}: setup failure — no dressing renderer was tagged to area1 for this test to mean anything");
            Assert.IsTrue(currentRenderer.enabled,
                $"{worldLabel}: a dressing renderer in Max's own starting area (area1) must stay enabled");

            // A zone two link-hops away can still legitimately share a wall (and so a two-sided-tagged
            // renderer) with something in the active set — that renderer correctly stays enabled, it is
            // just not a valid "must be disabled" example. Only a renderer tagged EXCLUSIVELY to a zone
            // outside the active set proves the gate actually disabled it for being far away.
            List<string> farZones = ZonesAtLinkDistance(map, "area1", 2);
            Renderer farRenderer = null;
            string farId = null;
            foreach (string zoneId in farZones)
            {
                if (activeZoneIds.Contains(zoneId)) continue;
                Renderer candidate = FindExclusiveDressingRendererForZone(dressingRoots, zones, zoneId, activeZoneIds);
                if (candidate == null) continue;
                farRenderer = candidate;
                farId = zoneId;
                break;
            }
            Assert.IsNotNull(farRenderer,
                $"{worldLabel}: setup failure — no zone two link-hops from area1 has a dressing renderer tagged exclusively to it");
            Assert.IsFalse(farRenderer.enabled,
                $"{worldLabel}: a dressing renderer in {farId} (two link-hops from area1) must be disabled");
        }

        private static Renderer FindDressingRendererForZone(List<Transform> roots,
            Dictionary<Renderer, List<string>> zones, string zoneId)
        {
            foreach (Transform root in roots)
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (zones.TryGetValue(r, out List<string> ids) && ids.Contains(zoneId))
                        return r;
                }
            }
            return null;
        }

        private static Renderer FindExclusiveDressingRendererForZone(List<Transform> roots,
            Dictionary<Renderer, List<string>> zones, string zoneId, HashSet<string> activeZoneIds)
        {
            foreach (Transform root in roots)
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (zones.TryGetValue(r, out List<string> ids) && ids.Contains(zoneId)
                        && !ids.Exists(activeZoneIds.Contains))
                        return r;
                }
            }
            return null;
        }

        /// <summary>Every zone exactly <paramref name="distance"/> link-hops from
        /// <paramref name="fromId"/> over <c>map.links</c> — a plain BFS, so "two links away" is read off
        /// the map's own graph rather than assumed from authoring order (zone ids get renumbered — see
        /// MV833RampZoneTests' own comment history on exactly that).</summary>
        private static List<string> ZonesAtLinkDistance(MapData map, string fromId, int distance)
        {
            var dist = new Dictionary<string, int> { [fromId] = 0 };
            var queue = new Queue<string>();
            queue.Enqueue(fromId);

            while (queue.Count > 0)
            {
                string cur = queue.Dequeue();
                if (map.links == null) break;

                foreach (MapLink link in map.links)
                {
                    if (link == null) continue;
                    string other = link.from == cur ? link.to : link.to == cur ? link.from : null;
                    if (other == null || dist.ContainsKey(other)) continue;
                    dist[other] = dist[cur] + 1;
                    queue.Enqueue(other);
                }
            }

            var result = new List<string>();
            foreach (var kv in dist)
                if (kv.Value == distance) result.Add(kv.Key);
            return result;
        }

        private static Dictionary<Renderer, List<string>> RendererZones(MapStaticBatchRoot batchRoot) =>
            (Dictionary<Renderer, List<string>>)typeof(MapStaticBatchRoot)
                .GetField("_rendererZones", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(batchRoot);

        private static void SetField(object target, string fieldName, object value) =>
            target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(target, value);

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);
    }
}
