using NUnit.Framework;
using UnityEngine;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The part-ready HUD chip (YT-147): it must FLASH, and it must wear the shared collectible orange
    /// — not the old static gold that read as yellow. Pure functions, so the flash and the colour are
    /// pinned without building a canvas.
    /// </summary>
    public sealed class PartAlertFlashTests
    {
        [Test]
        public void TheChipWearsTheSharedCollectibleGlowColour_NotAMatchedCopy()
        {
            // At full beat the chip is exactly the on-ground pickup aura's colour — asserted as a
            // RELATIONSHIP, so an art retune of the collectible orange moves the HUD with it and this
            // stays green (the whole reason the HUD sources the constant instead of copying the value).
            Color full = HudController.PartAlertColor(1f);
            Color glow = PickupArtDirector.CollectibleGlow;
            Assert.That(full.r, Is.EqualTo(glow.r).Within(0.001f), "chip red drifted from the pickup glow");
            Assert.That(full.g, Is.EqualTo(glow.g).Within(0.001f), "chip green drifted from the pickup glow");
            Assert.That(full.b, Is.EqualTo(glow.b).Within(0.001f), "chip blue drifted from the pickup glow");
        }

        [Test]
        public void TheChipReadsAsOrange_NotYellow()
        {
            // The ticket's core ask: get it off yellow. Yellow has green ~= red; orange has green well
            // below red and very little blue. The old gold (0.98,0.72,0.22) had g/r ~= 0.74 and would
            // fail this; the shared orange (g/r ~= 0.52) passes.
            Color c = HudController.PartAlertColor(1f);
            Assert.That(c.g, Is.LessThan(c.r * 0.65f), $"g/r={c.g / c.r:0.00} — reads as yellow, not orange");
            Assert.That(c.b, Is.LessThan(0.3f), "too much blue to read as a warm collectible orange");
        }

        [Test]
        public void TheChipActuallyFlashes_BrightAtThePeakAndDimInTheTrough()
        {
            float dim = Brightness(HudController.PartAlertColor(0f));
            float bright = Brightness(HudController.PartAlertColor(1f));
            Assert.That(bright, Is.GreaterThan(dim * 1.4f),
                "the chip barely changes between trough and peak — that is a static badge, not a flash");
        }

        [Test]
        public void TheFlashSweepsTheFullRangeOverTime_AndStaysNormalised()
        {
            // A quarter period apart, the beat moves from mid to peak — it is time-driven, not frozen.
            float period = 2f * Mathf.PI / 6f;
            float a = HudController.PartAlertFlash(0f);
            float b = HudController.PartAlertFlash(period * 0.25f);
            Assert.That(Mathf.Abs(a - b), Is.GreaterThan(0.3f), "the flash is not advancing with time");

            for (float ti = 0f; ti < 4f; ti += 0.05f)
            {
                float v = HudController.PartAlertFlash(ti);
                Assert.That(v, Is.InRange(0f, 1f), $"flash left 0..1 at t={ti:0.00} (v={v:0.00})");
            }
        }

        private static float Brightness(Color c) => (c.r + c.g + c.b) * c.a;
    }
}
