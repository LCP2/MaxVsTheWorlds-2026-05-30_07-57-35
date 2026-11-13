using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Player;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1357: the map must open with Max's marker at the viewport centre, wherever Max stands, at the
    /// loaded zoom. Asserts the RESOLVED marker offset from viewport centre (content pan + scaled
    /// content-space position), not any authored constant. On main <c>ResetView</c> set pan to zero and
    /// <c>ClampPan</c> pinned it there at fit zoom, so a corner position landed far from centre.
    /// </summary>
    public sealed class MV1357MapOpensCentredOnMaxTests
    {
        private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject _screenGo, _playerGo;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            Time.timeScale = 1f;
            MapZoomMemory.ClearForTest("backyard");
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            MapZoomMemory.ClearForTest("backyard");
            if (_screenGo != null) Object.DestroyImmediate(_screenGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        private static void Set(object o, string name, object v) => typeof(MapScreen).GetField(name, F).SetValue(o, v);

        private Vector2 OpenAndMeasureMarkerOffset(float savedZoom)
        {
            if (savedZoom > 0f) MapZoomMemory.Save("backyard", savedZoom);
            _screenGo = new GameObject("MV1357 map");
            var screen = _screenGo.AddComponent<MapScreen>();
            screen.Open(); // builds the canvas; no map in this fixture so content is empty
            _playerGo = new GameObject("MV1357 player");
            var player = _playerGo.AddComponent<PlayerController>();
            // World bounds 300 x 200 m; Max in the far corner (bottom-left-ish of the world).
            var bounds = new Rect(0f, 0f, 300f, 200f);
            _playerGo.transform.position = new Vector3(10f, 0f, 10f);
            var size = new Vector2(300f, 200f);
            Set(screen, "_worldBounds", bounds);
            Set(screen, "_contentSize", size);
            Set(screen, "_contentBuilt", true);
            Set(screen, "_player", player);
            Set(screen, "_maxZoomMultiplier", 10f);
            Set(screen, "_fitScale", 2f);
            typeof(MapScreen).GetMethod("ResetView", F).Invoke(screen, null);

            var content = (RectTransform)typeof(MapScreen).GetField("_content", F).GetValue(screen);
            float scale = content.localScale.x;
            Vector2 n = MinimapModel.NormalizedPosition(bounds, 10f, 10f);
            Vector2 cp = new Vector2(n.x * size.x, n.y * size.y);
            return content.anchoredPosition + (cp - size * 0.5f) * scale;
        }

        [Test]
        public void CornerMax_AtFitZoomAndAtSavedZoom_MarkerIsAtViewportCentre()
        {
            Vector2 fit = OpenAndMeasureMarkerOffset(0f);
            Assert.Less(fit.magnitude, 2f, "MV-1357: marker offset from viewport centre at fit zoom was " + fit);
            Object.DestroyImmediate(_screenGo); Object.DestroyImmediate(_playerGo);
            Vector2 saved = OpenAndMeasureMarkerOffset(3f);
            Assert.Less(saved.magnitude, 2f, "MV-1357: marker offset from viewport centre at saved zoom was " + saved);
        }
    }
}
