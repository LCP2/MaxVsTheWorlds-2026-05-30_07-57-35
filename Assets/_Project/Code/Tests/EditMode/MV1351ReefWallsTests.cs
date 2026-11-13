using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1351 (Reef relook 3/4): the walls. Asserts RESOLVED geometry on the built Reef (Tier 2): on every
    /// a1 wall, rib frames sit at most 2.2 m apart (a gap that spans a gate opening is exempt), the top
    /// cable tray covers at least 90% of the wall, the plating faces into the room (winding normal dot
    /// inward &gt; 0 — Unity is clockwise-front, the MV-1268 lesson) and a1's west wall carries at least one
    /// porthole per config window range; a1 (racks), a3 (pipes) and a2 (planters) each get at least two
    /// dressing pieces of their own kind. Fails on base: no ribs, tray, portholes or new dressing exist.
    /// </summary>
    public sealed class MV1351ReefWallsTests
    {
        private static Vector3 InwardOf(string wall) => wall == "N" ? Vector3.back : wall == "S" ? Vector3.forward : wall == "E" ? Vector3.left : Vector3.right;

        [Test]
        public void ReefWalls_RibsTrayPortholesAndDressing()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            WorldArea a1 = cfg.areas.First(a => a != null && a.id == "a1");

            var host = new GameObject("MV1351 Reef Probe Root");
            try
            {
                MapRuntime.Build(map, host.transform);
                ReefAreaKit.BuildWallRelook(host.transform, cfg, map.wallHeight, map.wallThickness);
                ReefAreaKit.BuildWallDressing(host.transform, cfg, null, map.wallHeight, map.wallThickness);

                Transform[] all = host.GetComponentsInChildren<Transform>(true);
                foreach (string w in new[] { "N", "S", "E", "W" })
                {
                    Transform wall = all.First(t => t.name == $"Reef Wall a1 {w}");
                    bool alongX = w == "N" || w == "S";
                    float min = alongX ? a1.XMin : a1.ZMin, max = alongX ? a1.XMax : a1.ZMax;
                    float len = max - min;
                    float Along(Vector3 p) => alongX ? p.x : p.z;

                    var gateCentres = new List<float>();
                    foreach (WorldGate g in cfg.gates)
                        foreach (WorldGateEndpoint e in new[] { g.from, g.to })
                            if (e != null && e.area == "a1" && e.wall == w) gateCentres.Add(min + len * e.pos);

                    List<float> ribs = wall.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Wall Rib")
                        .Select(t => Along(t.position)).OrderBy(x => x).ToList();
                    Assert.GreaterOrEqual(ribs.Count, 2, $"a1 {w} needs rib frames");
                    for (int i = 1; i < ribs.Count; i++)
                    {
                        float gap = ribs[i] - ribs[i - 1];
                        bool overGate = gateCentres.Any(c => c > ribs[i - 1] && c < ribs[i]);
                        if (!overGate) Assert.LessOrEqual(gap, 2.2f, $"a1 {w}: ribs at {ribs[i - 1]:F2} and {ribs[i]:F2} are {gap:F2} m apart");
                    }

                    Renderer tray = wall.GetComponentsInChildren<Renderer>(true).First(r => r.name == "Wall Tray");
                    float trayLen = alongX ? tray.bounds.size.x : tray.bounds.size.z;
                    Assert.GreaterOrEqual(trayLen / len, 0.9f, $"a1 {w}: cable tray covers {trayLen:F1} of {len:F1} m");

                    MeshFilter plating = wall.GetComponentsInChildren<MeshFilter>(true).First(f => f.name == "Wall Plating");
                    Mesh pm = plating.sharedMesh;
                    Vector3[] v = pm.vertices; int[] tris = pm.triangles;
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        Vector3 n = Vector3.Cross(v[tris[i + 1]] - v[tris[i]], v[tris[i + 2]] - v[tris[i]]);
                        Assert.Greater(Vector3.Dot(n, InwardOf(w)), 0f, $"a1 {w}: plating triangle {i / 3} faces away from the room");
                    }
                }

                Transform west = all.First(t => t.name == "Reef Wall a1 W");
                float[] portholes = west.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Porthole")
                    .Select(t => t.position.z).ToArray();
                foreach (WorldAreaWindow win in a1.windows.Where(x => x.wall == "W"))
                {
                    float lo = a1.ZMin + (a1.ZMax - a1.ZMin) * Mathf.Min(win.from, win.to);
                    float hi = a1.ZMin + (a1.ZMax - a1.ZMin) * Mathf.Max(win.from, win.to);
                    Assert.GreaterOrEqual(portholes.Count(z => z >= lo && z <= hi), 1, $"window range {lo:F1}..{hi:F1} needs a porthole");
                }

                int Count(string areaId, string piece) => all.First(t => t.name == $"Reef Wall Dressing {areaId}")
                    .GetComponentsInChildren<Transform>(true).Count(t => t.name == piece);
                Assert.GreaterOrEqual(Count("a1", "Wall Rack"), 2, "a1 (racks)");
                Assert.GreaterOrEqual(Count("a3", "Wall PipeBundle"), 2, "a3 (pipes)");
                Assert.GreaterOrEqual(Count("a2", "Wall CoralTank"), 2, "a2 (planters)");

                Assert.AreEqual(0, host.GetComponentsInChildren<Collider>(true)
                    .Count(c => c.transform.IsChildOf(all.First(t => t.name == "Reef Wall Relook a1"))), "wall relook pieces must be collider-free");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
