using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Dev;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-505 — the MV-503/MV-504 <c>[MV-503]</c> diagnostic lines only ever reached a desktop browser
    /// console; the bug they exist to pin down reproduces on a phone, where nobody could read them.
    /// This proves the new on-screen overlay actually captures those lines (AC1), stays inert while
    /// hidden (AC2), and is reachable by a real touch/pointer target rather than a key press (AC3) —
    /// all three against the same <see cref="Mv503DiagnosticOverlay"/> instance, which is why they
    /// share one test class rather than three unrelated ones.
    /// </summary>
    public sealed class Mv505DiagnosticOverlayTests
    {
        private GameObject _go;
        private Mv503DiagnosticOverlay _overlay;

        // Neither Awake nor OnEnable is reliably invoked for AddComponent outside Play mode — same
        // empirical finding MV503StuckDiagnosticTests/WeaponsButtonAlertTests already carry.
        private static void InvokeLifecycle(Object component, string methodName) =>
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("MV-505 Overlay Probe");
            _overlay = _go.AddComponent<Mv503DiagnosticOverlay>();
            InvokeLifecycle(_overlay, "Awake");
            InvokeLifecycle(_overlay, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            InvokeLifecycle(_overlay, "OnDisable");
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void CapturesMv503Lines_AndIgnoresOthers()
        {
            Debug.Log("[MV-503] handoff: cc.enabled=True isGrounded=True pos=(0.0, 0.0, 0.0)");
            Debug.Log("some unrelated log line");

            Assert.That(_overlay.Lines, Has.Count.EqualTo(1),
                "a non-[MV-503] log must never be captured");
            Assert.That(_overlay.Lines[0], Does.StartWith("[MV-503] handoff:"),
                "the captured line must be the resolved buffer content, not just proof a line arrived");
        }

        [Test]
        public void WhileHidden_BuildsNoDisplayText_EvenWithLinesAlreadyCaptured()
        {
            Debug.Log("[MV-503] stuck: cc.enabled=False actualDelta=(0.0000, 0.0000, 0.0000)");

            Assert.IsFalse(_overlay.Visible, "must default to hidden");
            Assert.IsNull(_overlay.BuildOverlayText(),
                "a hidden overlay must build nothing at all, not an empty string");
        }

        [Test]
        public void HudHelpIcon_IsATouchTarget_AndTogglesTheOverlay()
        {
            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");
            try
            {
                var helpIcon = FindRect(hudGo, "Icon ?");
                Assert.IsNotNull(helpIcon, "MV-505 must hook the existing '?' utility icon, not add a new one");

                var button = helpIcon.GetComponent<Button>();
                Assert.IsNotNull(button, "the '?' icon must be a real touch/pointer target, not only a key press");

                Assert.IsFalse(_overlay.Visible, "precondition: overlay starts hidden");
                button.onClick.Invoke();
                Assert.IsTrue(_overlay.Visible, "tapping '?' must show the MV-503 overlay");
                button.onClick.Invoke();
                Assert.IsFalse(_overlay.Visible, "a second tap must hide it again");
            }
            finally
            {
                InvokeLifecycle(hud, "OnDisable");
                Object.DestroyImmediate(hudGo);
            }
        }

        private static RectTransform FindRect(GameObject go, string name)
        {
            foreach (var rt in go.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt;
            return null;
        }
    }
}
