using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// YT-98 — with a simulated notch, the HUD's edge-anchored controls must inset. This drives the
    /// live <see cref="SafeArea"/> component (not just the pure maths) on the assembled HUD, using
    /// the test seam to fake a device safe area, so we know the joysticks/buttons actually move off
    /// the screen edge on a notched iPhone without needing one.
    /// </summary>
    public sealed class SafeAreaPlayTests
    {
        private GameObject _hudGo;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SafeArea.SimulatedSafeArea = null;
            SafeArea.SimulatedScreenSize = null;
            if (_hudGo != null) Object.Destroy(_hudGo);
            yield return null;
        }

        private RectTransform Find(string name)
        {
            foreach (var rt in _hudGo.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt;
            return null;
        }

        [UnityTest]
        public IEnumerator NotchInset_MovesTheSafeRootInFromEveryEdge()
        {
            // Fake a landscape iPhone with a 132px notch cut on the left and a 40px home indicator.
            SafeArea.SimulatedScreenSize = new Vector2(2400f, 1080f);
            SafeArea.SimulatedSafeArea = new Rect(132f, 40f, 2400f - 132f, 1080f - 40f);

            _hudGo = new GameObject("HUD");
            _hudGo.AddComponent<HudController>();
            yield return null; // Awake builds + SafeArea.OnEnable applies
            yield return null; // one more so Update settles

            RectTransform safe = Find("Safe Area");
            Assert.IsNotNull(safe, "The HUD must build a 'Safe Area' root for its edge-anchored controls.");

            Assert.Greater(safe.anchorMin.x, 0f, "Left edge should be inset by the notch.");
            Assert.Greater(safe.anchorMin.y, 0f, "Bottom edge should be inset by the home indicator.");
            Assert.AreEqual(132f / 2400f, safe.anchorMin.x, 1e-3f, "Left inset should match the notch fraction.");
            Assert.AreEqual(1f, safe.anchorMax.x, 1e-3f, "Right edge is uncut, so it should reach the screen edge.");
        }

        [UnityTest]
        public IEnumerator NoNotch_LeavesTheSafeRootFullScreen()
        {
            SafeArea.SimulatedScreenSize = new Vector2(2400f, 1080f);
            SafeArea.SimulatedSafeArea = new Rect(0f, 0f, 2400f, 1080f);

            _hudGo = new GameObject("HUD");
            _hudGo.AddComponent<HudController>();
            yield return null;
            yield return null;

            RectTransform safe = Find("Safe Area");
            Assert.IsNotNull(safe);
            Assert.AreEqual(0f, safe.anchorMin.x, 1e-3f);
            Assert.AreEqual(0f, safe.anchorMin.y, 1e-3f);
            Assert.AreEqual(1f, safe.anchorMax.x, 1e-3f);
            Assert.AreEqual(1f, safe.anchorMax.y, 1e-3f);
        }
    }
}
