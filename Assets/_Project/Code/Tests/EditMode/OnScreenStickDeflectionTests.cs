using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;
using MaxWorlds.UI;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-502 — BLOCKER: on a real iPhone/iPad TestFlight build Max turned crisply but barely moved
    /// and never fired, even though the identical code worked on the WebGL build (played with a
    /// keyboard, so the touch path was never exercised there).
    ///
    /// Root cause, read directly from the Input System package source in this repo
    /// (<c>Library/PackageCache/com.unity.inputsystem@.../InputSystem/Plugins/OnScreen/
    /// OnScreenStick.cs</c>): <c>OnScreenStick</c>'s class remarks document that when the Input
    /// System auto-switches the active device, any currently running input from the previously
    /// active device is cancelled — for an on-screen stick that can mean <c>OnPointerUp</c> fires and
    /// the stick snaps back to centre even though the finger never actually lifted. On a real
    /// touchscreen, every finger produces BOTH real Touchscreen input and this stick's own synthetic
    /// Gamepad output, which reads as a device switch and triggers exactly that cancellation
    /// mid-drag. <c>useIsolatedInputActions</c> (default false) is the documented fix — it drives the
    /// stick off local, uncancellable input actions instead.
    ///
    /// That single mechanism explains both reported symptoms from one cause, given how
    /// <c>PlayerController.Update</c> uses the vector: <c>_facing</c> is only ever overwritten by a
    /// non-trivial move/aim vector, never reset, so a momentary deflection before the snap-back
    /// leaves Max turned and stuck that way; <c>planarVel</c> is read fresh every frame, so it drops
    /// straight back to zero once the stick snaps to centre — Max stops moving; and
    /// <c>IsAiming</c>'s 0.5 threshold flickers true then false and never holds long enough to latch
    /// the gadget's auto-fire.
    ///
    /// A resolution-dependent theory (that the pads' <c>movementRange</c> under-delivered on a phone)
    /// was raised and disproven earlier on this same ticket: <c>OnScreenStick.OnDrag</c> converts its
    /// drag delta through <c>RectTransformUtility.ScreenPointToLocalPointInRectangle</c> — canvas
    /// LOCAL space — before comparing it to <c>movementRange</c>, so that ratio is scale-invariant.
    /// This test's regression guard pins <c>movementRange</c> at its original 90f specifically so a
    /// future change doesn't quietly reintroduce that already-disproven "fix".
    /// </summary>
    public sealed class OnScreenStickDeflectionTests
    {
        [Test]
        public void MoveAndAimSticksUseIsolatedInputActionsWithAnExplicitBehaviour()
        {
            var hudGo = new GameObject("HUD-MV502-Test");
            try
            {
                var hud = hudGo.AddComponent<HudController>();
                InvokeLifecycle(hud, "Awake");

                // AC1/AC2: both the move and aim pads, asserted individually — resolved component
                // state on the components HudController actually built, not the presence of a source
                // line.
                var sticks = hudGo.GetComponentsInChildren<OnScreenStick>(true);
                Assert.That(sticks.Length, Is.EqualTo(2), "expected exactly two on-screen sticks (move + aim)");

                var moveStick = sticks.FirstOrDefault(s => s.controlPath.Contains("leftStick"));
                var aimStick = sticks.FirstOrDefault(s => s.controlPath.Contains("rightStick"));
                Assert.That(moveStick, Is.Not.Null, "the move stick must drive <Gamepad>/leftStick");
                Assert.That(aimStick, Is.Not.Null, "the aim stick must drive <Gamepad>/rightStick");

                foreach (var (stick, label) in new[] { (moveStick, "move"), (aimStick, "aim") })
                {
                    Assert.That(stick.useIsolatedInputActions, Is.True,
                        $"{label} stick: a real device switch elsewhere must not cancel this stick's drag mid-touch (MV-502 root cause)");
                    Assert.That(stick.behaviour, Is.EqualTo(OnScreenStick.Behaviour.RelativePositionWithStaticOrigin),
                        $"{label} stick: behaviour must be set explicitly so a package upgrade can't silently change the serialized default");

                    // AC4 (movementRange half): regression guard against the earlier, disproven
                    // "movementRange is resolution-dependent" theory on this same ticket — it must
                    // stay at its original 90f, not be retuned as part of this fix.
                    Assert.That(stick.movementRange, Is.EqualTo(90f),
                        $"{label} stick: movementRange must stay 90f — MV-502's actual fix is useIsolatedInputActions, not a range retune");
                }
            }
            finally
            {
                Object.DestroyImmediate(hudGo);
            }

            // AC3: the ability joysticks (Sentinel/Teleport/WaterBalloon, all via
            // AbilityJoystickControlBase) do NOT use OnScreenControl/OnScreenStick at all — they
            // drive their own IPointerDownHandler/IDragHandler/IPointerUpHandler directly, so the
            // device-switch auto-cancel this ticket fixes never applies to them. Asserted as a class
            // hierarchy fact rather than skipped.
            Assert.That(typeof(OnScreenControl).IsAssignableFrom(typeof(MaxWorlds.UI.AbilityJoystickControlBase)), Is.False,
                "AbilityJoystickControlBase (Sentinel/Teleport/WaterBalloon) must not be an OnScreenControl — confirms MV-502's device-switch-cancel root cause cannot apply to it");

            // AC4 (PlayerController half): the fix must not be a threshold/binding climbdown —
            // aimActivateThreshold stays 0.5f and both Move/Aim gamepad bindings still carry
            // stickDeadzone(min=0.2).
            var pcGo = new GameObject("PC-MV502-Test", typeof(CharacterController));
            try
            {
                var pc = pcGo.AddComponent<PlayerController>();
                InvokeLifecycle(pc, "Awake");

                FieldInfo thresholdField = typeof(PlayerController).GetField("aimActivateThreshold",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(thresholdField, Is.Not.Null, "aimActivateThreshold field must still exist");
                Assert.That((float)thresholdField.GetValue(pc), Is.EqualTo(0.5f),
                    "aimActivateThreshold must stay 0.5f — this fix must not mask the symptom by lowering it");

                foreach (string fieldName in new[] { "_move", "_aim" })
                {
                    FieldInfo actionField = typeof(PlayerController).GetField(fieldName,
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    var action = (InputAction)actionField.GetValue(pc);
                    bool hasDeadzonedGamepadBinding = action.bindings.Any(b =>
                        b.path != null && b.path.Contains("Gamepad") && b.processors == "stickDeadzone(min=0.2)");
                    Assert.That(hasDeadzonedGamepadBinding, Is.True,
                        $"{fieldName}'s gamepad binding must still carry stickDeadzone(min=0.2) — unchanged by this fix");
                }
            }
            finally
            {
                Object.DestroyImmediate(pcGo);
            }
        }

        private static void InvokeLifecycle(Object component, string methodName)
        {
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        }
    }
}
