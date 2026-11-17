using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1385: the teleport landing circle was drawn 0.01 above the surface, under the Reef deck / wear /
    /// current-lane layers that exist only inside areas. Tier 2: aim inside W4 a1 and compare the circle's
    /// resolved world Y and material render queue with every floor-overlay renderer's resolved bounds/queue.
    /// </summary>
    public sealed class MV1385TeleportLandingAboveReefFloorTests
    {
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        [TearDown]
        public void Clear() { EnemyNavigation.Reset(); WeaponSystemState.Reset(); RigState.Reset(); }

        [Test]
        public void TeleportLandingCircleInsideReefA1_DrawsAboveEveryFloorOverlay()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            WorldArea a1 = cfg.areas.First(a => a != null && a.id == "a1");

            var host = new GameObject("MV1385 Reef Probe Root");
            var pathGo = new GameObject("MV1385-backyard-path");
            var originGo = new GameObject("MV1385 Origin");
            var padGo = new GameObject("MV1385 Pad", typeof(RectTransform), typeof(Image));
            try
            {
                MapRuntime.Build(map, host.transform);
                ReefFloorDetail.PlaceDeck(host.transform, cfg);

                EnemyNavigation.Reset();
                BackyardPathMapField.SetValue(pathGo.AddComponent<BackyardPath>(), map);

                foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
                WeaponSystemState.Acquire(AbilityKind.Teleport);
                var control = padGo.AddComponent<TeleportJoystickControl>();
                var knob = new GameObject("MV1385 Knob", typeof(RectTransform)).GetComponent<RectTransform>();
                knob.SetParent(padGo.transform, false);
                control.Init(knob, originGo.transform, null);

                originGo.transform.forward = Vector3.forward;
                originGo.transform.position = new Vector3((a1.XMin + a1.XMax) * 0.5f, 0f, (a1.ZMin + a1.ZMax) * 0.5f);
                control.OnPointerDown(new PointerEventData(EventSystem.current) { position = Vector2.zero });
                Assert.IsTrue(control.LandingCircleVisible, "setup: the landing circle must be showing");

                Vector3 p = control.LandingCirclePosition;
                float top = float.MinValue; int maxQueue = int.MinValue; int count = 0;
                foreach (Renderer r in host.GetComponentsInChildren<Renderer>(true))
                {
                    string n = r.name;
                    bool overlay = n == "Map Floor" || n.StartsWith("Reef Deck ") && !n.StartsWith("Reef Deck Props")
                        || n.StartsWith("Reef Wear ") || r.GetComponent<MapCurrentLaneVisual>() != null
                        || r.GetComponent<SludgeFlow>() != null;
                    if (!overlay) continue;
                    Bounds b = r.bounds;
                    if (p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue;
                    count++;
                    top = Mathf.Max(top, b.max.y);
                    if (r.sharedMaterial != null) maxQueue = Mathf.Max(maxQueue, r.sharedMaterial.renderQueue);
                }
                Assert.Greater(count, 1, "probe must find floor overlays under the aim point");
                Assert.GreaterOrEqual(p.y, top + 0.01f - 1e-4f,
                    $"landing circle world y {p.y:F4} must be >= 0.01 m above the highest floor overlay top {top:F4}");
                Assert.GreaterOrEqual(control.LandingCircleRenderQueue, maxQueue,
                    "landing circle render queue must be >= every floor overlay's");
            }
            finally
            {
                Object.DestroyImmediate(padGo);
                Object.DestroyImmediate(originGo);
                Object.DestroyImmediate(pathGo);
                Object.DestroyImmediate(host);
            }
        }
    }
}
