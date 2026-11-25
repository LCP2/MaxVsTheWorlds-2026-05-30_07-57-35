using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1414 (the one new test, Rule 1; Tier 2 resolved values): RICOCHET's disc is ONE shape that
    /// evolves by layers with POWER. Spawns a real disc at every POWER 1..8 and reads back what was
    /// built: the flame ring's inner radius off its mesh vertices (must be r * (1 - t), absent at POWER
    /// 1, swallowing the whole body at POWER 8), the body and ring colours (full strength at every
    /// level, never averaged), and the diameter and trail time (strictly increasing, POWER 8 at most
    /// 0.55 m).
    ///
    /// Fails on base a3ae68a: <see cref="RicochetDisc.FlameRingVisible"/>,
    /// <see cref="RicochetDisc.FlameRingInnerRadius"/>, <see cref="RicochetDisc.BodyColor"/>,
    /// <see cref="RicochetDisc.FlameRingColor"/>, <see cref="RicochetDisc.TrailTime"/> and
    /// <see cref="RicochetDisc.PowerLevel"/> do not exist (the disc there picks one of four discrete
    /// marks and grows to 0.75 m).
    /// </summary>
    public sealed class MV1414RicochetFireballTests
    {
        [TearDown]
        public void TearDown() => RicochetDisc.DestroyPoolForTests();

        private static RicochetDisc SpawnAt(int power)
        {
            LogAssert.ignoreFailingMessages = true; // VfxMaterials' one-time shader-resolve log line
            try
            {
                return RicochetDisc.Spawn(Vector3.zero, Vector3.forward, speed: 0f, bounces: 2,
                    powerLevel: power, aimLevel: 2,
                    damage: 0f, bankBonusPerBounce: 0f, turnRateDegPerSec: 0f, lookaheadMetres: 0f,
                    radius: 0.25f, lifetime: 60f);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        private static void AssertColour(Color expected, Color actual, string what, int power)
        {
            Assert.AreEqual(expected.r, actual.r, 0.02f, $"{what} r at POWER {power}");
            Assert.AreEqual(expected.g, actual.g, 0.02f, $"{what} g at POWER {power}");
            Assert.AreEqual(expected.b, actual.b, 0.02f, $"{what} b at POWER {power}");
        }

        [Test]
        public void DiscIsOneShapeThatEvolvesByLayersWithPower()
        {
            float prevTrail = 0f;
            for (int power = 1; power <= 8; power++)
            {
                RicochetDisc disc = SpawnAt(power);
                Assert.IsNotNull(disc, $"POWER {power}: the pool must have room");
                disc.gameObject.SetActive(true);

                float t = (power - 1) / 7f;
                float r = disc.VisualDiameter * 0.5f;

                if (power == 1)
                {
                    Assert.IsFalse(disc.FlameRingVisible, "POWER 1 is 100% blue: no flame ring");
                }
                else
                {
                    Assert.IsTrue(disc.FlameRingVisible, $"POWER {power}: flame ring present");
                    Assert.AreEqual(r * (1f - t), disc.FlameRingInnerRadius, r * 0.02f,
                        $"POWER {power}: ring inner radius must be r * (1 - t)");
                    Assert.AreEqual(r, disc.FlameRingOuterRadius, r * 0.02f, $"POWER {power}: ring outer radius is the rim");
                    AssertColour(new Color(1.0f, 0.43f, 0.0f), disc.FlameRingColor, "ring colour", power);
                }
                if (power == 8)
                    Assert.LessOrEqual(disc.FlameRingInnerRadius, r * 0.02f, "POWER 8: ring covers the whole body");

                AssertColour(new Color(0.35f, 0.65f, 1.0f), disc.BodyColor, "body colour", power);

                // MV-1451: size is nearly fixed now (0.40 -> 0.60 m); only the trail still grows every level.
                Assert.Greater(disc.TrailTime, prevTrail, $"POWER {power}: trail time must grow every level");
                prevTrail = disc.TrailTime;

                RicochetDisc.DestroyPoolForTests();
            }

        }
    }
}
