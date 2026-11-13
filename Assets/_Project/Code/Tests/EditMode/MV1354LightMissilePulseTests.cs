using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1354 (the one new test, Rule 1; Tier 2): POWER must not change the Light Missile ball's size.
    /// Spawns a ball at POWER 1 and POWER 8, pins both to pulse phase 0, and asserts the resolved halo
    /// <see cref="Renderer.bounds"/> sizes agree within 2%. Fails on base (L8 0.80 m against L1 0.45 m).
    /// </summary>
    public sealed class MV1354LightMissilePulseTests
    {
        [TearDown]
        public void TearDown() => LightMissileBall.DestroyPoolForTests();

        [Test]
        public void HaloBoundsAreTheSameSizeAtPower1AndPower8()
        {
            float l1 = HaloSizeAt(1);
            float l8 = HaloSizeAt(8);
            Assert.AreEqual(l1, l8, l1 * 0.02f,
                $"POWER 8 halo bounds ({l8:0.000} m) must equal POWER 1's ({l1:0.000} m) within 2% at pulse phase 0");
        }

        private static float HaloSizeAt(int powerLevel)
        {
            LogAssert.ignoreFailingMessages = true; // VfxMaterials' one-time shader-resolve log line
            LightMissileBall ball;
            try
            {
                ball = LightMissileBall.Spawn(Vector3.zero, Vector3.forward, speed: 9f, damage: 30f,
                    trackingBase: 170f, areaIndex: 0, powerLevel: powerLevel);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
            Assert.IsNotNull(ball, $"test precondition: the pool must have room for a ball at POWER {powerLevel}");
            ball.SetPulsePhaseForTests(0f);
            float size = ball.ResolvedBallBounds.size.x;
            ball.gameObject.SetActive(false);
            LightMissileBall.ClearAll();
            return size;
        }
    }
}
