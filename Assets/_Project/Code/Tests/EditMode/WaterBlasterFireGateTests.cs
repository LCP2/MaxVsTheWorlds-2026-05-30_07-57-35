using NUnit.Framework;
using MaxWorlds.Combat;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Regression guard (YT-36): the Water Blaster must NOT discharge when no aim
    /// input is held. The bug was gamepad right-stick drift reading non-zero with
    /// no input, driving IsFiring true. MV-290: the primary never depletes, so
    /// holding the trigger is the whole fire gate now.
    /// </summary>
    public sealed class WaterBlasterFireGateTests
    {
        [Test]
        public void NoAimHeld_DoesNotEmit()
        {
            // firingHeld=false models "no aim input" -> must never emit.
            Assert.IsFalse(WaterBlaster.ShouldEmit(firingHeld: false),
                "Blaster emitted with no aim input held — auto-discharge regression.");
        }

        [Test]
        public void AimHeld_Emits()
        {
            Assert.IsTrue(WaterBlaster.ShouldEmit(firingHeld: true));
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
