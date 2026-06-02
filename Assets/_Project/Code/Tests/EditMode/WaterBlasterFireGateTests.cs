using NUnit.Framework;
using MaxWorlds.Combat;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Regression guard (YT-36): the Water Blaster must NOT discharge when no aim
    /// input is held. The bug was gamepad right-stick drift reading non-zero with
    /// no input, driving IsFiring true. These tests lock the fire gate: emission
    /// (and therefore every damage tick + VFX spawn, which are downstream of it)
    /// only happens when the trigger is actively held AND energy is available.
    /// </summary>
    public sealed class WaterBlasterFireGateTests
    {
        [Test]
        public void NoAimHeld_DoesNotEmit_EvenWithFullEnergy()
        {
            // firingHeld=false models "no aim input" -> must never emit.
            Assert.IsFalse(WaterBlaster.ShouldEmit(firingHeld: false, hasEnergy: true),
                "Blaster emitted with no aim input held — auto-discharge regression.");
        }

        [Test]
        public void AimHeld_NoEnergy_DoesNotEmit()
        {
            Assert.IsFalse(WaterBlaster.ShouldEmit(firingHeld: true, hasEnergy: false));
        }

        [Test]
        public void AimHeld_WithEnergy_Emits()
        {
            Assert.IsTrue(WaterBlaster.ShouldEmit(firingHeld: true, hasEnergy: true));
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
