using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1163 — <c>MapScreen.AddCoverMarker</c> set <c>sizeDelta = (entity.depth, entity.width)</c>,
    /// swapping the two axes under a stale comment claiming the map is rotated. Every non-square cover
    /// piece in every world was therefore drawn turned 90 degrees about its own centre: World 2's
    /// <c>a8_cover3</c> (2 m wide / 23 m deep, runs top to bottom in the game) was drawn 23 wide and 2
    /// tall on the MAP.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values (Rule 2, Tier 2):
    /// for every cover marker the real, shipped World 1/2/3 configs produce, the marker's resolved
    /// <c>sizeDelta</c> has X equal to its entity's <c>width</c> and Y equal to its <c>depth</c> (each
    /// floored at 1.2, matching <c>AddCoverMarker</c>'s own floor) — and specifically, World 2's
    /// <c>a8_cover3</c> resolves taller than it is wide, by at least 5x.
    /// </summary>
    public sealed class MV1163CoverMarkerOrientationTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo MapField = typeof(BackyardPath).GetField("_map", NonPublicInstance);

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static List<MapEntity> CoverEntitiesInOrder(MapData map) =>
            map.entities.Where(e => e != null && e.Kind == EntityKind.Cover).ToList();

        private static List<Image> CoverMarkersInOrder(GameObject screenGo) =>
            screenGo.GetComponentsInChildren<Image>(true).Where(i => i.gameObject.name == "Cover").ToList();

        [Test]
        public void CoverMarkers_ResolveWidthAlongX_DepthAlongY_AcrossWorlds1To3()
        {
            Assert.IsNotNull(MapField, "BackyardPath._map went missing");

            (string key, string label)[] worlds =
            {
                (WorldLibrary.World1, "World 1"),
                (WorldLibrary.World2, "World 2"),
                (WorldLibrary.World3, "World 3"),
            };

            bool checkedA8Cover3 = false;

            foreach ((string key, string label) in worlds)
            {
                WorldConfig cfg = WorldLibrary.Load(key);
                Assert.IsNotNull(cfg, $"{label}'s own shipped config must load for this test to mean anything");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

                var pathGo = new GameObject($"MV1163 {label} path");
                _spawned.Add(pathGo);
                var path = pathGo.AddComponent<BackyardPath>();
                MapField.SetValue(path, map);

                var screenGo = new GameObject($"MV1163 {label} map screen");
                _spawned.Add(screenGo);
                var screen = screenGo.AddComponent<MapScreen>();
                screen.Open();

                List<MapEntity> entities = CoverEntitiesInOrder(map);
                List<Image> markers = CoverMarkersInOrder(screenGo);
                Assert.AreEqual(entities.Count, markers.Count,
                    $"{label}: must draw exactly one Cover marker per Cover entity");

                for (int i = 0; i < entities.Count; i++)
                {
                    MapEntity entity = entities[i];
                    Vector2 size = markers[i].rectTransform.sizeDelta;
                    float expectedX = Mathf.Max(1.2f, entity.width);
                    float expectedY = Mathf.Max(1.2f, entity.depth);
                    Assert.AreEqual(expectedX, size.x, 1e-3f,
                        $"{label} '{entity.id}': resolved marker width must equal entity.width (X), not entity.depth");
                    Assert.AreEqual(expectedY, size.y, 1e-3f,
                        $"{label} '{entity.id}': resolved marker height must equal entity.depth (Y), not entity.width");

                    if (label == "World 2" && entity.id == "a8_cover3")
                    {
                        checkedA8Cover3 = true;
                        Assert.GreaterOrEqual(size.y, size.x * 5f,
                            "World 2 'a8_cover3' (2 m wide / 23 m deep) must resolve at least 5x taller than wide, " +
                            "matching its run top-to-bottom in the level — not drawn wide and short.");
                    }
                }
            }

            Assert.IsTrue(checkedA8Cover3, "setup failure: World 2's shipped config must still author 'a8_cover3'");
        }
    }
}
