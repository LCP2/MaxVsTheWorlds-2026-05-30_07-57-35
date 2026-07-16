using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.OnScreen;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// YT-98 — the HUD must expose working on-screen touch controls, wired to the SAME synthetic
    /// gamepad controls the PlayerController binds. This verifies the wiring headlessly (CI); the
    /// actual multi-touch feel on glass is Lee's device pass.
    ///
    /// The premise of the ticket ("they're built for touch already") was wrong — the joysticks were
    /// display-only visualisers with no input path. This test is what makes "built for touch" true
    /// and keeps it true.
    /// </summary>
    public sealed class TouchControlsPlayTests
    {
        private GameObject _hudGo;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _hudGo = new GameObject("HUD");
            _hudGo.AddComponent<HudController>();
            yield return null; // Awake builds the canvas + touch controls
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_hudGo != null) Object.Destroy(_hudGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MoveAndAimSticksDriveTheGamepadSticksThePlayerReads()
        {
            yield return null;

            var sticks = _hudGo.GetComponentsInChildren<OnScreenStick>(true);
            Assert.AreEqual(2, sticks.Length, "Expected exactly two on-screen sticks (move + aim).");

            Assert.IsTrue(sticks.Any(s => s.controlPath.Contains("leftStick")),
                "The move stick must drive <Gamepad>/leftStick — the control the player's Move action binds.");
            Assert.IsTrue(sticks.Any(s => s.controlPath.Contains("rightStick")),
                "The aim stick must drive <Gamepad>/rightStick — the control the player's Aim action binds.");
        }

        [UnityTest]
        public IEnumerator DashButtonDrivesTheGamepadButtonThePlayerReads()
        {
            yield return null;

            var buttons = _hudGo.GetComponentsInChildren<OnScreenButton>(true);
            Assert.IsTrue(buttons.Any(b => b.controlPath.Contains("buttonSouth")),
                "The dash button must drive <Gamepad>/buttonSouth — the control the player's Dash action binds.");
        }

        [UnityTest]
        public IEnumerator TouchControlsAreRaycastableAndHaveAnEventSystem()
        {
            yield return null;

            // On-screen controls only receive input through an EventSystem + a raycastable graphic.
            Assert.IsNotNull(Object.FindFirstObjectByType<EventSystem>(),
                "On-screen touch controls need an EventSystem to receive pointer/touch events.");

            foreach (var stick in _hudGo.GetComponentsInChildren<OnScreenStick>(true))
            {
                var img = stick.GetComponent<UnityEngine.UI.Graphic>();
                Assert.IsNotNull(img, "An on-screen stick needs a Graphic to be touchable.");
                Assert.IsTrue(img.raycastTarget, "The stick's touch surface must be a raycast target.");
            }
        }
    }
}
