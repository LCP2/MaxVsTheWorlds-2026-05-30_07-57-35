using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1206 (Lee, 17 Oct: "Could you have the map zoom remembered? Every time I go into the map,
    /// I have to keep on zooming in and in to find myself. Don't track where Max is, just keep the
    /// level of zoom."). Before this ticket, <c>MapScreen.ResetView</c> hard-reset <c>_zoom</c> to 1
    /// on every <see cref="MapScreen.Open"/>, so the fit-the-whole-world framing came back on every
    /// reopen no matter what the player had just pinched to.
    ///
    /// Sets the zoom through <see cref="MapScreen.SetZoom"/> — the same setter a live pinch calls
    /// (<c>ApplyPinch</c>) — so this can never diverge from how a real gesture updates the zoom.
    /// <c>_maxZoomMultiplier</c> is pinned via reflection to a value well above the ticket's own 2.5
    /// probe, rather than depending on a real loaded map's own computed cap, which has moved before
    /// (MV-1140, MV-1200) and is not what this ticket is about.
    ///
    /// No <c>BackyardPath</c> in this fixture, so <c>MapScreen</c> resolves the "backyard" world id —
    /// <c>WorldCatalog.Get</c>'s own row-0 default with no world resolved (same fallback every other
    /// per-world field already uses for a bare EditMode fixture).
    /// </summary>
    public sealed class MV1206MapZoomPersistTests
    {
        private static readonly FieldInfo MaxZoomField =
            typeof(MapScreen).GetField("_maxZoomMultiplier", BindingFlags.NonPublic | BindingFlags.Instance);

        private const string WorldId = "backyard";

        private GameObject _screenGo;
        private MapScreen _screen;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            Time.timeScale = 1f;
            MapZoomMemory.ClearForTest(WorldId);
            _screenGo = new GameObject("MV1206 map screen");
            _screen = _screenGo.AddComponent<MapScreen>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            MapZoomMemory.ClearForTest(WorldId);
            if (_screenGo != null) Object.DestroyImmediate(_screenGo);
        }

        [Test]
        public void ZoomPersistsPerWorldAcrossCloseAndReopen_ThenDefaultsTo1OnceCleared()
        {
            Assert.IsNotNull(MaxZoomField, "setup failure: MapScreen._maxZoomMultiplier went missing");

            _screen.Open();
            MaxZoomField.SetValue(_screen, 10f); // headroom above the 2.5 probe below

            _screen.SetZoom(2.5f); // the same setter ApplyPinch calls
            _screen.Close();

            _screen.Open();
            Assert.AreEqual(2.5f, _screen.Zoom, 0.001f,
                "MV-1206: zoom must survive a close/reopen");

            _screen.Close();
            MapZoomMemory.ClearForTest(WorldId);
            _screen.Open();
            Assert.AreEqual(1f, _screen.Zoom, 0.001f,
                "MV-1206: with nothing stored, a reopen must still give the opening fit (1)");
        }
    }
}
