using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-887 — World 2 built every one of its 30,726 renderers enabled for the whole run, no matter
    /// which of its 26+ areas Max was actually standing in (MV-886's own census measured it: identical
    /// in a2, a8 and a10). Fails on the base commit named in the PR: before this ticket,
    /// <see cref="MapStaticBatchRoot"/> never disabled a renderer for standing in a distant area, so a
    /// renderer resolved to a zone with no MapLink to area1 read enabled regardless of where Max stood.
    ///
    /// One consolidated test (MV-465 Rule 1), against the real shipped World 2 config: builds the world
    /// (<see cref="MapRuntime.Build"/> attaches its own <see cref="MapStaticBatchRoot"/>, whose Start() —
    /// never invoked automatically outside Play mode — is reflection-invoked here to run the initial
    /// gate, the same trick MV833RampZoneTests already uses for AreaAccumulationDirector.Update()).
    /// Asserts three RESOLVED values (Rule 2, Tier 2), picked generically off the shipped
    /// <c>map.links</c> graph rather than a hard-coded area number (area ids get renumbered — see
    /// MV833RampZoneTests' own comment history on exactly that): a renderer physically in area1 is
    /// enabled; a renderer in one of area1's real MapLink neighbours is enabled; a renderer in a zone
    /// with no link to area1 is disabled. A fourth assertion (Rule 3: a measured property, not mere
    /// presence) proves item 3's "renderers only" rule: every collider anywhere in the built map stays
    /// enabled throughout.
    /// </summary>
    public sealed class MV887AreaRendererGateTests
    {
        [Test]
        public void OnlyCurrentAreaAndNeighboursStayRendered_CollidersUntouched()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see other World2 EditMode tests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var root = new GameObject("MV887 Root");
            try
            {
                MapRuntime.Build(map, root.transform);

                Transform mapRoot = root.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");

                // MapStaticBatchRoot.Start() — where the initial area gate is applied — never fires on
                // its own outside Play mode. Same reflection trick MV833RampZoneTests already uses for
                // AreaAccumulationDirector's own Update().
                InvokePrivate(batchRoot, "Start");

                // Not every zone carries a gated renderer — area1's own entry stub, for one, is
                // deliberately built empty (AreaAccumulationDirector.FillArea's own "the lead-in room
                // must stay empty" rule) — so this picks the first LINKED (resp. UNLINKED) zone that
                // actually has one to assert against, rather than assuming the first zone found in
                // authoring order does.
                string neighbourId = null;
                Renderer neighbourRenderer = null;
                string farId = null;
                Renderer farRenderer = null;
                foreach (MapZone z in map.zones)
                {
                    if (z == null || z.id == "area1") continue;
                    if (map.AreLinked("area1", z.id))
                    {
                        if (neighbourRenderer != null) continue;
                        Renderer candidate = FindGatedRenderer(mapRoot, map, z.id);
                        if (candidate != null) { neighbourId = z.id; neighbourRenderer = candidate; }
                    }
                    else
                    {
                        if (farRenderer != null) continue;
                        Renderer candidate = FindGatedRenderer(mapRoot, map, z.id);
                        if (candidate != null) { farId = z.id; farRenderer = candidate; }
                    }
                }
                Assert.IsNotNull(neighbourRenderer, "setup failure: no MapLink neighbour of area1 has a gated renderer to assert against");
                Assert.IsNotNull(farRenderer, "setup failure: no zone with no link to area1 has a gated renderer to assert against");

                Renderer currentRenderer = FindGatedRenderer(mapRoot, map, "area1");
                Assert.IsNotNull(currentRenderer, "setup failure: area1 must contain at least one gated renderer");

                Assert.IsTrue(currentRenderer.enabled,
                    "MV-887: a renderer in Max's own starting area (area1) must stay enabled");
                Assert.IsTrue(neighbourRenderer.enabled,
                    $"MV-887: a renderer in {neighbourId} (a real MapLink neighbour of area1) must stay enabled");
                Assert.IsFalse(farRenderer.enabled,
                    $"MV-887: a renderer in {farId} (no MapLink to area1) must be disabled");

                foreach (Collider c in mapRoot.GetComponentsInChildren<Collider>(true))
                    Assert.IsTrue(c.enabled, $"MV-887: '{c.name}' collider must stay enabled — the gate touches renderers only (item 3)");
            }
            finally
            {
                Object.DestroyImmediate(root);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        /// <summary>The first renderer under <paramref name="mapRoot"/> that MV-887's own gate actually
        /// tags and resolves to <paramref name="zoneId"/> — skips anything the gate deliberately never
        /// touches (the map-spanning floor, and gameplay actors: replicators, hutches, bosses, area
        /// gates), so a factory or gate that happens to stand in the far zone can never produce a false
        /// pass on "must be disabled". Also skips a <see cref="StructuralWall"/>: a wall sitting exactly
        /// on a shared boundary can legitimately belong to MORE than one zone (the production gate tags
        /// it for every zone it borders — see MapRuntime.TagWallZones — so a wall between the current
        /// area and an otherwise-unlinked one correctly stays enabled), whereas this single-point
        /// <see cref="MapData.ZoneAt(float, float, float)"/> probe only ever names one of them — picking
        /// a non-wall example keeps this assertion unambiguous.</summary>
        private static Renderer FindGatedRenderer(Transform mapRoot, MapData map, string zoneId)
        {
            foreach (Renderer r in mapRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "Map Floor") continue;
                if (r.GetComponent<StructuralWall>() != null) continue;
                if (r.GetComponentInParent<Replicator>() != null) continue;
                if (r.GetComponentInParent<MowerHutch>() != null) continue;
                if (r.GetComponentInParent<BigBermudaBoss>() != null) continue;
                if (r.GetComponentInParent<AreaGate>() != null) continue;

                Vector3 p = r.transform.position;
                MapZone zone = map.ZoneAt(p.x, p.y, p.z);
                if (zone != null && zone.id == zoneId) return r;
            }
            return null;
        }

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(target, null);
    }
}
