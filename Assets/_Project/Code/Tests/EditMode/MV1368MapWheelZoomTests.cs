using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>MV-1368: wheel zoom on the map. Asserts resolved zoom and the resolved screen position of
    /// the content point under the cursor; and that a closed map ignores it.</summary>
    public sealed class MV1368MapWheelZoomTests
    {
        private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject _go;

        [SetUp] public void SetUp() { LogAssert.ignoreFailingMessages = true; Time.timeScale = 1f; MapZoomMemory.ClearForTest("backyard"); }
        [TearDown] public void TearDown() { Time.timeScale = 1f; MapZoomMemory.ClearForTest("backyard"); if (_go != null) Object.DestroyImmediate(_go); }

        private static void Set(object o, string n, object v) => typeof(MapScreen).GetField(n, F).SetValue(o, v);

        private MapScreen Open()
        {
            _go = new GameObject("MV1368 map");
            var s = _go.AddComponent<MapScreen>();
            s.Open();
            Set(s, "_contentSize", new Vector2(300f, 200f));
            Set(s, "_contentBuilt", true);
            Set(s, "_maxZoomMultiplier", 10f);
            Set(s, "_fitScale", 2f);
            s.SetZoom(2f);
            return s;
        }

        private static MethodInfo Zoom() => typeof(MapScreen).GetMethod("ZoomByNotches", BindingFlags.Public | BindingFlags.Instance);

        [Test]
        public void WheelNotch_ZoomsByStepAboutCursor_AndClosedMapIgnoresIt()
        {
            var s = Open();
            var m = Zoom();
            Assert.IsNotNull(m, "MV-1368: MapScreen has no ZoomByNotches (wheel zoom missing)");
            var content = (RectTransform)typeof(MapScreen).GetField("_content", F).GetValue(s);
            Vector2 cursor = new Vector2(120f, 40f);
            Vector2 before = content.anchoredPosition;
            float oldScale = content.localScale.x;
            Vector2 contentPt = (cursor - before) / oldScale;

            m.Invoke(s, new object[] { 1f, cursor });
            Assert.AreEqual(2f * 1.15f, s.Zoom, 0.001f, "MV-1368: +1 notch zoom");
            Vector2 after = content.anchoredPosition + contentPt * content.localScale.x;
            Assert.Less((after - cursor).magnitude, 2f, "MV-1368: point under cursor drifted to " + after);

            m.Invoke(s, new object[] { -1f, cursor });
            Assert.AreEqual(2f, s.Zoom, 0.001f, "MV-1368: -1 notch zoom");

            s.Close();
            float z = s.Zoom;
            m.Invoke(s, new object[] { 1f, cursor });
            Assert.AreEqual(z, s.Zoom, 0.0001f, "MV-1368: closed map must ignore wheel");
        }
    }
}
