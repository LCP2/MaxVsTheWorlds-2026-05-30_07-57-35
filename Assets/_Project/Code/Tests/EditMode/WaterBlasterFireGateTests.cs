using NUnit.Framework;
using MaxWorlds.Combat;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Regression guard (YT-36): the Water Blaster must NOT discharge when no aim
    /// input is held. The bug was gamepad right-stick drift reading non-zero with
    /// no input, driving IsFiring true. MV-299 (reinstating the tank MV-290 cut):
    /// the gate is now trigger held AND water available, not the trigger alone.
    /// </summary>
    public sealed class WaterBlasterFireGateTests
    {
        [Test]
        public void NoAimHeld_DoesNotEmit()
        {
            // firingHeld=false models "no aim input" -> must never emit, even with water available.
            Assert.IsFalse(WaterBlaster.ShouldEmit(firingHeld: false, hasWater: true),
                "Blaster emitted with no aim input held — auto-discharge regression.");
        }

        [Test]
        public void AimHeld_Emits()
        {
            Assert.IsTrue(WaterBlaster.ShouldEmit(firingHeld: true, hasWater: true));
        }

        [Test]
        public void AimHeldButNoWater_DoesNotEmit()
        {
            // MV-299: an empty tank stops the stream even while the trigger is held.
            Assert.IsFalse(WaterBlaster.ShouldEmit(firingHeld: true, hasWater: false),
                "Blaster emitted with an empty tank — the primary must stall at empty.");
        }

        [Test]
        public void IsFiring_DefaultsFalse_OnFreshInstance()
        {
            // Unbound/idle blaster must default to not firing (no auto-discharge).
            var go = new UnityEngine.GameObject("wb_test");
            var wb = go.AddComponent<WaterBlaster>();
            try
            {
                Assert.IsFalse(wb.IsFiring, "Fresh WaterBlaster defaulted to IsFiring=true — would auto-discharge.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
