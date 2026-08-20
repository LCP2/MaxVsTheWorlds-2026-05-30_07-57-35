using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.UI;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-502 — BLOCKER: on a real iPhone/iPad TestFlight build Max turned crisply but barely moved
    /// and never fired. <c>HudController.AddOnScreenStick</c> drove
    /// <c>UnityEngine.InputSystem.OnScreen.OnScreenStick.movementRange</c> as a flat 90
    /// canvas-local-unit constant, "tuned on device" per its own (stale) comment — a device pass that
    /// never actually happened.
    ///
    /// Instrumented first, per the ticket's own instruction: canvas-local units are ALREADY
    /// resolution-independent for a pad-relative comparison — <c>RectTransformUtility.
    /// ScreenPointToLocalPointInRectangle</c> (what <c>OnScreenStick</c> itself uses) converts a
    /// screen-space drag through the canvas's live scale factor before comparing it to
    /// <c>movementRange</c>, so "drag to the true geometric edge of the pad" always produces the SAME
    /// local-unit delta on every device — there is no simulated-resolution regression to reproduce
    /// here, and simulating one would be testing a bug that provably doesn't exist in this geometry.
    /// The REAL, measurable defect: the old flat 90 required a drag reaching 90/65 = 69% of the way to
    /// the true edge of a pad whose resolved half-extent is only 65 local units
    /// (<see cref="HudController.JoystickPadLocalSize"/> / 2) — that demands near-edge precision from
    /// a thumb on an invisible touch target that's barely above Apple's 44pt tap-target floor even at
    /// the smallest supported phone (AC4 below). Any shortfall from that precision is then AMPLIFIED
    /// by <c>PlayerController</c>'s own <c>stickDeadzone(min=0.2)</c> processor, which rescales
    /// (raw - 0.2)/0.8 — a raw 0.6 becomes 0.5 (right at the fire gate) and a raw 0.4 becomes 0.25
    /// (never fires) — while <c>Update</c>'s facing check only needs sqrMagnitude &gt; 0.04 (~0.2 raw)
    /// to turn crisply. That's exactly Lee's reported symptom set from one cause.
    ///
    /// The fix, <c>HudController.ComputeMovementRange</c>, reaches full deflection at a fraction of
    /// the pad's own resolved half-extent instead of the old ungrounded constant — comfortably short
    /// of the edge, and identical on every device by construction (no live Canvas/Screen dependency,
    /// so no dependency on Screen.dpi either).
    /// </summary>
    public sealed class OnScreenStickDeflectionTests
    {
        [Test]
        public void OnScreenStickReachesFullDeflectionWithinAComfortableFractionOfThePad()
        {
            const float oldMovementRangeLocalUnits = 90f; // the flat constant AddOnScreenStick used before this fix
            float padHalfExtent = HudController.JoystickPadLocalSize * 0.5f;

            // A thumb drag reaching HALF the pad's own half-extent — comfortably short of needing
            // pixel-perfect precision at the target's true edge.
            float comfortableDrag = padHalfExtent * 0.5f;

            float oldMagnitude = Mathf.Clamp01(comfortableDrag / oldMovementRangeLocalUnits);
            Assert.That(oldMagnitude, Is.LessThan(0.95f),
                "sanity: the pre-fix flat 90-unit movementRange required dragging closer to the pad's true edge than this comfortable thumb travel reaches");

            // AC1/AC2: full deflection is reached well within the pad, on every device — proven by
            // construction (no Canvas/Screen input to this formula at all), not retuned per device.
            float newMovementRange = HudController.ComputeMovementRange(padHalfExtent);
            float newMagnitude = Mathf.Clamp01(comfortableDrag / newMovementRange);
            Assert.That(newMagnitude, Is.GreaterThanOrEqualTo(0.95f),
                "a comfortable, sub-edge thumb drag must reach full deflection after the fix");

            // AC3: aim fires well before full deflection — half of THAT comfortable drag still clears
            // the 0.5 aim-activate threshold PlayerController gates directly on the stick's own magnitude.
            float halfDragMagnitude = Mathf.Clamp01((comfortableDrag * 0.5f) / newMovementRange);
            Assert.That(halfDragMagnitude, Is.GreaterThanOrEqualTo(0.5f),
                "a quarter-pad-half-extent drag must already clear the 0.5 aim-activate threshold");

            // AC4: the invisible touch pad's own RESOLVED size clears Apple's 44x44pt floor at the
            // smallest supported device — same physicalScale idiom as MV-472's RigBoardChromeTests
            // (deviceHeightPt / 1080), asserted against the RESOLVED pad size, not the authored one.
            const float iPhoneHeightPt = 393f;
            const float refH = 1080f;
            float physicalScale = iPhoneHeightPt / refH;
            Assert.That(HudController.JoystickPadLocalSize * physicalScale, Is.GreaterThanOrEqualTo(44f),
                "touch pad's resolved on-screen size must clear Apple's 44pt floor at iPhone's worst-case height");

            // AC5 regression guard: this fix must not be a threshold climbdown — aimActivateThreshold
            // stays 0.5f (the stickDeadzone(min=0.2) binding itself lives in PlayerController.Awake and
            // is exercised by every existing test that constructs a PlayerController, so it isn't
            // re-pinned here).
            var go = new GameObject("MV502-PlayerController-Test", typeof(CharacterController));
            try
            {
                var pc = go.AddComponent<PlayerController>();
                FieldInfo field = typeof(PlayerController).GetField("aimActivateThreshold",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(field, Is.Not.Null, "aimActivateThreshold field must still exist");
                Assert.That((float)field.GetValue(pc), Is.EqualTo(0.5f),
                    "aimActivateThreshold must stay 0.5f — MV-502 change item 4 forbids lowering it to mask the deflection fix");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
