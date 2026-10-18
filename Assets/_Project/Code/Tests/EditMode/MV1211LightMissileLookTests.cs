using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1211 (the one new test, Rule 1): proves LIGHT MISSILE's ball look (POWER's own visible change)
    /// resolves off POWER rather than being an authored constant (MV-465 Tier 2) — a spawned ball's own
    /// resolved renderer bounds diameter (read off the halo's live <see cref="Renderer"/> after
    /// <c>ApplyPowerVisual</c> has scaled it, not the float that produced the scale) and spike count, at
    /// POWER levels 1, 4 and 8.
    ///
    /// Fails on base commit f113ad6 (main HEAD before this ticket): <see cref="LightMissileBall.Spawn"/>
    /// has no powerLevel parameter at all, and <see cref="LightMissileBall"/> exposes no
    /// SpikeCount/ResolvedBallBounds — it still builds MV-1210's placeholder fixed-size violet sphere.
    /// </summary>
    public sealed class MV1211LightMissileLookTests
    {
        [TearDown]
        public void TearDown() => LightMissileBall.DestroyPoolForTests();

        [Test]
        public void BallDiameterAndSpikeCountResolveFromPowerLevel()
        {
            AssertBallAtPowerLevel(1, 0.45f, 4);
            AssertBallAtPowerLevel(4, 0.60f, 6);
            AssertBallAtPowerLevel(8, 0.80f, 8);
        }

        private static void AssertBallAtPowerLevel(int powerLevel, float expectedDiameter, int expectedSpikes)
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

            Assert.AreEqual(expectedSpikes, ball.SpikeCount,
                $"POWER {powerLevel} must resolve {expectedSpikes} spikes");

            float resolvedDiameter = ball.ResolvedBallBounds.size.x;
            Assert.AreEqual(expectedDiameter, resolvedDiameter, 0.02f,
                $"POWER {powerLevel}'s resolved renderer-bounds diameter must be ~{expectedDiameter}m, " +
                $"was {resolvedDiameter:0.000}m");
        }
    }
}
