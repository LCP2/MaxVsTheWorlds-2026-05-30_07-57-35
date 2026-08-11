using NUnit.Framework;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-327: the Abilities screen's PARTS chip grew a pulsing glow ring so a banked, spendable part
    /// reads as prominent at a glance, same as the overworld's own part-alert badge. Pure function, so
    /// the beat and the "only when actionable" gate are pinned without building a canvas.
    /// </summary>
    public sealed class WeaponsScreenPartsGlowTests
    {
        [Test]
        public void StaysOffWithNothingBanked()
        {
            for (float ti = 0f; ti < 4f; ti += 0.3f)
                Assert.That(WeaponsScreen.PartsGlowAlpha(ti, 0), Is.EqualTo(0f),
                    $"the glow must stay off with nothing banked, at t={ti:0.00}");
        }

        [Test]
        public void PulsesOnceAPartIsBanked()
        {
            float dim = WeaponsScreen.PartsGlowAlpha(0f, 1);
            float bright = WeaponsScreen.PartsGlowAlpha(Mathf.PI / 12f, 1); // quarter period: sin peaks here
            Assert.That(bright, Is.GreaterThan(dim), "the glow barely changes — that reads as static, not a pulse");
        }

        [Test]
        public void StaysNormalisedAcrossTime()
        {
            for (float ti = 0f; ti < 4f; ti += 0.05f)
            {
                float v = WeaponsScreen.PartsGlowAlpha(ti, 3);
                Assert.That(v, Is.InRange(0f, 1f), $"glow alpha left 0..1 at t={ti:0.00} (v={v:0.00})");
            }
        }
    }
}
