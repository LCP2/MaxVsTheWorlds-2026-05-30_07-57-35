using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-755: World 2 never got a kit (MV-690's documented scope cut) and then lost the garden props
    /// it was borrowing from World 1 (MV-750's own <c>OnlyTheGardenWorldGetsTheGardenDressing</c>) — what
    /// was left was a recoloured greybox: a flat floor, thin walls, grey cover blocks. This lands
    /// <see cref="MaxWorlds.Rendering.StormdrainKit"/> + <see cref="StormdrainDressing"/>, the Stormdrain
    /// counterpart of World 3's <see cref="MaxWorlds.Rendering.ReefKit"/> + <see cref="ReefDressing"/>.
    ///
    /// <see cref="StormdrainDressing"/> does not exist before this ticket, so this fails to COMPILE on
    /// the base commit — the same "doesn't exist there yet" failure MV744World3DressingKitTests and
    /// MV713ReefKitTests document for their own base commits.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1: at most one new test per ticket — the
    /// ticket's own text asked for six; that text does not override the standing policy) asserting
    /// RESOLVED state only, on DENSITY and VARIETY rather than presence (Rule 3 — a presence count lets
    /// a skeleton pass; MV-712 shipped a world with zero cover and met every criterion phrased that way):
    /// the exact kerb count against every qualifying wall face, that cover dressing spans at least 3
    /// distinct kinds with every block dressed, that every sludge tile gets a flow chevron, that nothing
    /// built here carries a collider, that a second <c>Dress()</c> call rebuilds rather than doubles the
    /// count, and that World 1 gets none of it.
    /// </summary>
    public sealed class MV755StormdrainDressingTests
    {
        [Test]
        public void StormdrainDressesWallsCoverAndSludge_ByResolvedCounts_AndOnlyForWorld2()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData w2map, out string reason), reason);

            var host = new GameObject("MV755 host").transform;
            try
            {
                MapBuild build = MapRuntime.Build(w2map, host);

                // ---- kerbs: one per qualifying face, not "some" ----
                int expectedKerbs = MapGeometry.Faces(w2map).Count(f => f.FacesRoom && f.Length >= 1.2f);

                StormdrainDressing.DressReport report1 = StormdrainDressing.Dress(host, w2map, build.Cover);
                Assert.AreEqual(expectedKerbs, report1.Kerbs,
                    "every room-facing wall face at least 1.2 m long must get exactly one kerb");

                // ---- cover variety is real, not one reskin standing in for every class ----
                Assert.GreaterOrEqual(report1.DistinctCoverKinds, 3,
                    "the drain kit must dress at least 3 distinct cover kinds, not repeat one reskin");
                Assert.AreEqual(build.Cover.Count, report1.CoverProps,
                    "every cover block in the map must be dressed; none left grey");

                // ---- sludge tiles, each carrying a flow chevron ----
                int expectedSludge = w2map.entities.Count(e => e != null && e.Kind == EntityKind.Sludge);
                Assert.AreEqual(expectedSludge, report1.SludgeTiles,
                    "every Sludge entity in the map must get exactly one dressed tile");

                Transform sludgeHost = host.Find("Stormdrain Dressing/Sludge");
                Assert.IsNotNull(sludgeHost, "the sludge dressing host was never built");
                foreach (Transform tile in sludgeHost)
                {
                    bool hasChevron = tile.GetComponentsInChildren<Transform>(true)
                        .Any(t => t.name.StartsWith("Chevron"));
                    Assert.IsTrue(hasChevron, $"{tile.name} carries no flow chevron");
                }

                // ---- nothing built here carries a collider ----
                Transform dressingHost = host.Find("Stormdrain Dressing");
                Assert.IsNotNull(dressingHost, "the dressing host was never built");
                Assert.IsEmpty(dressingHost.GetComponentsInChildren<UnityEngine.Collider>(true),
                    "dressing is scenery — the map's own wall/cover boxes are the only colliders allowed");

                // ---- a second Dress() call rebuilds; it does not double every piece ----
                StormdrainDressing.DressReport report2 = StormdrainDressing.Dress(host, w2map, build.Cover);
                Assert.AreEqual(report1.Total, report2.Total,
                    "calling Dress() twice must leave the same total piece count as calling it once");
                int hostCount = host.GetComponentsInChildren<Transform>(true)
                    .Count(t => t.name == "Stormdrain Dressing");
                Assert.AreEqual(1, hostCount, "a second Dress() call left two dressing hosts side by side");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }

            // ---- only World 2 gets the drain kit — mirrors MV-750's own world-scoping test ----
            WorldConfig w1cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(w1cfg, "World 1's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w1cfg, out MapData w1map, out string w1reason), w1reason);

            var w1go = new GameObject("MV755 world1 host");
            try
            {
                MapBuild w1build = MapRuntime.Build(w1map, w1go.transform);

                var path = w1go.AddComponent<BackyardPath>();
                typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(path, w1map);
                typeof(BackyardPath).GetMethod("ApplyWorldMaterials", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(path, new object[] { 0, w1go.transform, w1build.Cover });

                Assert.IsNull(w1go.transform.Find("Stormdrain Dressing"),
                    "World 1 must not get the Stormdrain kit");
            }
            finally
            {
                Object.DestroyImmediate(w1go);
            }
        }
    }
}
