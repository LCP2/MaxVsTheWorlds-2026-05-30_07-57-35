using NUnit.Framework;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-327: the Abilities screen's SUPERCELL tray grew a pulsing glow ring so a cashable Supercell
    /// reads as prominent at a glance, same as the overworld's own part-alert badge. MV-515: the gate
    /// is now "cashing one is actually possible" (<c>cashable</c>), not merely "one is banked". Pure
    /// function, so the beat and the actionability gate are pinned without building a canvas.
    /// </summary>
    public sealed class WeaponsScreenPartsGlowTests
    {
        [Test]
        public void StaysOffWhenNotCashable()
        {
            for (float ti = 0f; ti < 4f; ti += 0.3f)
                Assert.That(WeaponsScreen.SupercellsGlowAlpha(ti, cashable: false), Is.EqualTo(0f),
                    $"the glow must stay off while cashing is not possible, at t={ti:0.00}");
        }

        [Test]
        public void PulsesOnceCashingIsPossible()
        {
            float dim = WeaponsScreen.SupercellsGlowAlpha(0f, cashable: true);
            float bright = WeaponsScreen.SupercellsGlowAlpha(Mathf.PI / 12f, cashable: true); // quarter period: sin peaks here
            Assert.That(bright, Is.GreaterThan(dim), "the glow barely changes — that reads as static, not a pulse");
        }

        [Test]
        public void StaysNormalisedAcrossTime()
        {
            for (float ti = 0f; ti < 4f; ti += 0.05f)
            {
                float v = WeaponsScreen.SupercellsGlowAlpha(ti, cashable: true);
                Assert.That(v, Is.InRange(0f, 1f), $"glow alpha left 0..1 at t={ti:0.00} (v={v:0.00})");
            }
        }
    }
}
