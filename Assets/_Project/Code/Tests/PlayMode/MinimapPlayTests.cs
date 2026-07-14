using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The always-on minimap (YT-73). It must track Max in real time, stay out of the way, and be
    /// the SAME map as the full-screen one — if the two ever disagreed, both would be worthless.
    /// </summary>
    public sealed class MinimapPlayTests
    {
        private GameObject _canvasGo, _playerGo, _miniGo, _mapGo;
        private Minimap _mini;
        private MapScreen _full;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _playerGo = new GameObject("Max");
            _playerGo.tag = "Player";
            _playerGo.transform.position = new Vector3(0f, 1f, -3f);

            _canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster));
            _canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var root = (RectTransform)_canvasGo.transform;

            _mapGo = new GameObject("Map Screen");
            _mapGo.transform.SetParent(root, false);
            _full = _mapGo.AddComponent<MapScreen>();
            _full.Build(root, 1920f, 1080f);

            _miniGo = new GameObject("Minimap", typeof(RectTransform));
            _mini = _miniGo.AddComponent<Minimap>();
            _mini.Build(root, _full, new Vector2(28f, -112f));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (_canvasGo != null) Object.Destroy(_canvasGo);
            if (_playerGo != null) Object.Destroy(_playerGo);
            if (_miniGo != null) Object.Destroy(_miniGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheMinimapTracksMaxAsHeMoves()
        {
            _playerGo.transform.position = new Vector3(0f, 1f, -3f);
            yield return null;
            Vector2 atStart = _mini.Map.PlayerNormalized;

            _playerGo.transform.position = new Vector3(0f, 1f, 20f);   // up the lawn
            yield return null;   // LateUpdate re-plots

            Assert.Greater(_mini.Map.PlayerNormalized.y, atStart.y,
                "Max advanced but the minimap dot didn't follow");
        }

        [UnityTest]
        public IEnumerator TheMinimapAndTheFullMapAgree()
        {
            // Two renderers of one model. If they can disagree, the model isn't shared.
            _playerGo.transform.position = new Vector3(6f, 1f, 12f);
            _full.Open();
            yield return null;
            _mini.Map.Refresh();
            _full.Map.Refresh();

            Assert.AreEqual(_full.Map.PlayerNormalized.x, _mini.Map.PlayerNormalized.x, 1e-4);
            Assert.AreEqual(_full.Map.PlayerNormalized.y, _mini.Map.PlayerNormalized.y, 1e-4);
            Assert.AreEqual(_full.Map.PlayerRoom, _mini.Map.PlayerRoom);
        }

        [UnityTest]
        public IEnumerator TappingTheMinimapOpensTheFullMap()
        {
            Assert.IsFalse(_full.IsOpen);
            _miniGo.GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.IsTrue(_full.IsOpen, "the minimap should answer the question it makes you ask");
        }

        [Test]
        public void TheMinimapIsSeeThrough_SoTheFightStaysReadable()
        {
            Assert.Less(Minimap.Opacity, 1f);
            Assert.Greater(Minimap.Opacity, 0.3f, "so faint it can't be read");
        }

        [Test]
        public void TheMinimapIsSmall_AndStaysOutOfTheThumbs()
        {
            // Bottom corners are the twin sticks; top-right is the ability column.
            var rt = (RectTransform)_miniGo.transform;
            Assert.AreEqual(new Vector2(0f, 1f), rt.anchorMin, "not anchored top-left");

            // A footprint that doesn't eat the screen (1920x1080 reference).
            Assert.Less(Minimap.PanelWidth * Minimap.PanelHeight, 1920f * 1080f * 0.05f,
                "the minimap is taking more than 5% of the screen");
        }

        [Test]
        public void TheMinimapIsTheShapeOfTheArena()
        {
            // The Backyard is long and narrow. A minimap that isn't is lying about the space — so the
            // shape is taken from the map the level is actually built from, not from a struct that can
            // only describe a corridor.
            MapData map = MapLibrary.Load(MapLibrary.BackyardSlice);
            float arena = ArenaMap.AspectRatio(ArenaMap.Bounds(map));
            float panel = Minimap.PanelWidth / Minimap.PanelHeight;

            Assert.Less(arena, 1f, "the arena should be taller than it is wide");
            Assert.Less(panel, 1f, "so the minimap panel should be too");
        }

        /// <summary>The map draws the rooms the level HAS. The shed and the nook are rooms Max can walk
        /// into and get lost in, and a map that leaves them out is a map that lies about where he can
        /// go — which is the one thing a map exists not to do.</summary>
        [Test]
        public void TheMapDrawsEveryRoomInTheLevel_IncludingTheShedAndTheNook()
        {
            MapData map = MapLibrary.Load(MapLibrary.BackyardSlice);
            MapRoom[] rooms = ArenaMap.Rooms(map);

            Assert.AreEqual(map.zones.Length, rooms.Length, "a room of the level is missing from the map");

            foreach (MapZone zone in map.zones)
                Assert.AreEqual(zone.name, rooms[System.Array.IndexOf(map.zones, zone)].Name,
                    "the map renamed a room the author named");

            Assert.AreNotEqual(-1, ArenaMap.RoomAt(new Vector2(19f, 15f), rooms), "the shed is not on the map");
            Assert.AreNotEqual(-1, ArenaMap.RoomAt(new Vector2(-16.5f, 7.5f), rooms), "the nook is not on the map");

            Rect bounds = ArenaMap.Bounds(map);
            foreach (MapRoom room in rooms)
            {
                Assert.GreaterOrEqual(room.Xz.xMin, bounds.xMin - 1e-3f, $"{room.Name} falls off the map");
                Assert.LessOrEqual(room.Xz.xMax, bounds.xMax + 1e-3f, $"{room.Name} falls off the map");
                Assert.GreaterOrEqual(room.Xz.yMin, bounds.yMin - 1e-3f, $"{room.Name} falls off the map");
                Assert.LessOrEqual(room.Xz.yMax, bounds.yMax + 1e-3f, $"{room.Name} falls off the map");
            }
        }
    }
}
