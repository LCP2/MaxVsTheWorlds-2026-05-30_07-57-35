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
    /// MV-1350 (Reef relook 2/4): the deck. Asserts RESOLVED geometry on the built Reef area a1 (Tier 2):
    /// one deck mesh whose quads cover at least 98% of the area floor, use at least 6 atlas variants and
    /// all face up (winding normal y &gt; 0 — Unity is clockwise-front, the MV-1268 lesson); no caustics
    /// overlay object exists; every floor prop is within 2.5 m of a wall, has no collider and is under
    /// 0.15 m tall. Fails on base: there is no deck (and the caustics overlay objects still exist).
    /// </summary>
    public sealed class MV1350ReefDeckTests
    {
        [Test]
        public void ReefA1Deck_CoversFloor_SixVariants_FacesUp_NoCaustics_PropsOnlyInWallBand()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            WorldArea a1 = cfg.areas.First(a => a != null && a.id == "a1");

            var host = new GameObject("MV1350 Reef Probe Root");
            try
            {
                MapRuntime.Build(map, host.transform);
                ReefFloorDetail.PlaceDeck(host.transform, cfg);

                Transform deckRoot = host.GetComponentsInChildren<Transform>(true).First(t => t.name == "Deck a1");
                MeshFilter[] deckFilters = deckRoot.GetComponentsInChildren<MeshFilter>(true)
                    .Where(f => f.name == "Reef Deck a1").ToArray();
                Assert.AreEqual(1, deckFilters.Length, "area a1 must have exactly one deck mesh");

                Mesh mesh = deckFilters[0].sharedMesh;
                Vector3[] v = mesh.vertices;
                Vector2[] uv = mesh.uv;
                int[] tris = mesh.triangles;

                float quadArea = 0f;
                var cells = new HashSet<int>();
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    Assert.Greater(n.y, 0f, $"triangle {i / 3} must face up (winding normal y > 0), got {n}");
                    quadArea += n.magnitude * 0.5f;
                    cells.Add(ReefDeckKit.CellOfUv((uv[tris[i]] + uv[tris[i + 1]] + uv[tris[i + 2]]) / 3f));
                }

                float floor = (a1.XMax - a1.XMin) * (a1.ZMax - a1.ZMin);
                Assert.GreaterOrEqual(quadArea / floor, 0.98f, $"deck quads cover {quadArea:F1} of {floor:F1} m2");
                Assert.GreaterOrEqual(cells.Count, 6, $"deck must use at least 6 plate variants, used {cells.Count}");

                Assert.IsFalse(host.GetComponentsInChildren<Transform>(true).Any(t => t.name.Contains("Caustics")),
                    "no caustics overlay object may exist");

                Transform propsRoot = deckRoot.GetComponentsInChildren<Transform>(true).First(t => t.name == "Reef Deck Props a1");
                Assert.AreEqual(0, propsRoot.GetComponentsInChildren<Collider>(true).Length, "floor props must have no collider");
                int props = 0;
                foreach (MeshRenderer r in propsRoot.GetComponentsInChildren<MeshRenderer>(true))
                {
                    props++;
                    Vector3 p = r.transform.position;
                    float toWall = Mathf.Min(Mathf.Min(p.x - a1.XMin, a1.XMax - p.x), Mathf.Min(p.z - a1.ZMin, a1.ZMax - p.z));
                    Assert.LessOrEqual(toWall, 2.5f, $"prop {r.name} at {p} is {toWall:F2} m from the nearest wall");
                    Assert.Less(r.bounds.size.y, 0.15f, $"prop {r.name} is {r.bounds.size.y:F3} m tall");
                }
                Assert.That(props, Is.InRange(3, 5), "3-5 floor props per area");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
