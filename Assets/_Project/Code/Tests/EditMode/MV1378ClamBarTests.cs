using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1378: with Max standing 2 m south of a Hatchery Clam, the clam's health/timer bar was hidden under
    /// Max's nameplate. The ONE new test: after both bars have run their LateUpdate, the clam bar's resolved
    /// screen rect must not intersect Max's nameplate rect. Resolved values only (Tier 2).
    /// </summary>
    public sealed class MV1378ClamBarTests
    {
        private sealed class FakeMax : MonoBehaviour, IHealthReadout
        {
            public float HealthNormalized => 1f;
            public float HealthCurrent => 100f;
            public string ReadoutName => "MAX";
            public bool IsAlive => true;
        }

        private static void Invoke(Object o, string m) =>
            o.GetType().GetMethod(m, BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(o, null);

        private static Rect ScreenRect(RectTransform rt, Camera cam)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>Union of every active graphic's resolved screen rect under <paramref name="root"/> — the
        /// whole plate (bar, name text, gauges, phase band), not just its background rect.</summary>
        private static Rect PlateRect(Transform root, Camera cam)
        {
            bool any = false;
            Rect u = default;
            foreach (var g in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
            {
                Rect r = ScreenRect(g.rectTransform, cam);
                u = any ? Rect.MinMaxRect(Mathf.Min(u.xMin, r.xMin), Mathf.Min(u.yMin, r.yMin),
                                          Mathf.Max(u.xMax, r.xMax), Mathf.Max(u.yMax, r.yMax)) : r;
                any = true;
            }
            Assert.IsTrue(any, "setup failure: plate has no graphics");
            return u;
        }

        [Test]
        public void ClamBar_DoesNotOverlapMaxNameplate_WhenMaxIsTwoMetresSouth()
        {
            LogAssert.ignoreFailingMessages = true;
            WorldDefinition row = WorldCatalog.Get(WorldCatalog.IndexOf(WorldIds.Reef));
            WorldConfig cfg = WorldLibrary.Load(row.ConfigKey);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject("MV1378 Probe Root");
            GameObject camGo = null, maxGo = null;
            try
            {
                camGo = new GameObject("MV1378 Cam", typeof(Camera));
                camGo.tag = "MainCamera";
                MapBuild build = MapRuntime.Build(map, root.transform);
                Assert.IsTrue(build.Actors.TryGetValue("a1_shed", out GameObject shed), "a1 shed did not build");
                Vector3 p = shed.transform.position;
                // The game's fixed ~72 deg top-down camera, looking at the clam.
                camGo.transform.position = p + new Vector3(0f, 9.5f, -3.1f);
                camGo.transform.LookAt(p, Vector3.up);
                var cam = camGo.GetComponent<Camera>();
                cam.pixelRect = new Rect(0, 0, 1920, 1080);

                var clam = shed.GetComponent<MowerHutch>();
                Invoke(clam, "Awake");
                clam.ApplyHatcheryClamBehavior();

                maxGo = new GameObject("MV1378 Max");
                maxGo.tag = "Player";
                maxGo.transform.position = new Vector3(p.x, 0f, p.z - 2f);
                var fake = maxGo.AddComponent<FakeMax>();
                WorldHealthBar maxBar = WorldHealthBar.Attach(maxGo, fake, 1.35f, 2.1f, alwaysShow: true, isPlayerBar: true);
                Assert.IsNotNull(maxBar, "setup failure: Max's bar did not attach");

                Invoke(clam, "LateUpdate");
                Invoke(maxBar, "LateUpdate");
                Canvas.ForceUpdateCanvases();

                Rect clamRect = PlateRect((Transform)typeof(MowerHutch).GetField("_barPivot", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(clam), cam);
                // Resolved draw order: whichever canvas each plate actually renders on after override-sorting.
                Transform pivot = (Transform)typeof(MowerHutch).GetField("_barPivot", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(clam);
                int clamOrder = pivot.GetComponentInChildren<Canvas>(true).sortingOrder;
                int maxOrder = maxBar.SortingOrder;
                Assert.Greater(clamOrder, maxOrder,
                    $"clam bar draws at sortingOrder {clamOrder}, Max's nameplate at {maxOrder}: the clam bar must draw on top");
                Rect maxRect = PlateRect(maxBar.BarRectTransform, cam);
                Assert.IsFalse(clamRect.Overlaps(maxRect),
                    $"clam bar screen rect {clamRect} intersects Max's nameplate rect {maxRect}");
            }
            finally
            {
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (camGo != null) Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(root);
                FactoryCensus.Reset();
            }
        }
    }
}
