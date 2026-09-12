using NUnit.Framework;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The shared life-bar colour ramp (YT-121, YT-122). Pure functions, so the thresholds every
    /// bar in the game shares can be pinned without building a canvas.
    /// </summary>
    public sealed class HealthBarColorTests
    {
        [Test]
        public void ItRampsFromACoolNeutralThroughYellowOrangeRedAsItDrains()
        {
            // MV-788: full health is a desaturated cool neutral, not bright green — colour means HURT,
            // not "exists". A representative fill in each of the other bands still checks the expected
            // dominant channel.
            AssertDesaturatedNeutral(HealthBarColor.Ramp(1.0f));
            AssertDesaturatedNeutral(HealthBarColor.Ramp(0.80f));

            AssertYellow(HealthBarColor.Ramp(0.50f));   // between Hurt (0.35) and Healthy (0.60)
            AssertOrange(HealthBarColor.Ramp(0.25f));   // between Critical (0.15) and Hurt (0.35)
            AssertRed(HealthBarColor.Ramp(0.10f));      // below Critical
        }

        [Test]
        public void TheBandsChangeAtTheDocumentedThresholds()
        {
            Assert.That(HealthBarColor.Ramp(0.61f), Is.EqualTo(HealthBarColor.Ramp(1f)), "just above 60% is green");
            Assert.That(HealthBarColor.Ramp(0.59f), Is.Not.EqualTo(HealthBarColor.Ramp(1f)), "just below 60% is not green");
            Assert.That(HealthBarColor.Ramp(0.16f), Is.Not.EqualTo(HealthBarColor.Ramp(0.10f)), "16% is orange, not red");
        }

        [Test]
        public void OnlyACriticalBarFlashes()
        {
            Assert.That(HealthBarColor.IsCritical(0.16f), Is.False, "16% is orange, not critical");
            Assert.That(HealthBarColor.IsCritical(0.15f), Is.True, "at the threshold it is critical");
            Assert.That(HealthBarColor.IsCritical(0.05f), Is.True);
        }

        [Test]
        public void ACriticalBarActuallyChangesColourOverTime()
        {
            // The flash: sampled at two points a quarter-period apart, the drawn colour must differ,
            // or "flashing" is a no-op.
            Color a = HealthBarColor.At(0.08f, 0f);
            Color b = HealthBarColor.At(0.08f, Mathf.PI / 9f); // quarter of the 9 rad/s period
            Assert.That(a, Is.Not.EqualTo(b), "a critical bar must visibly pulse");
        }

        [Test]
        public void AHealthyBarDoesNotFlash()
        {
            Color a = HealthBarColor.At(0.9f, 0f);
            Color b = HealthBarColor.At(0.9f, 5f);
            Assert.That(a, Is.EqualTo(b), "a healthy bar must sit still — only critical flashes");
        }

        /// <summary>MV-788: "healthy" is now a cool, low-saturation neutral rather than a green-dominant
        /// colour — the ticket's own AC3 formula (max-min)/max, which must sit under 0.25.</summary>
        private static void AssertDesaturatedNeutral(Color c)
        {
            float max = Mathf.Max(c.r, c.g, c.b);
            float min = Mathf.Min(c.r, c.g, c.b);
            float saturation = max > 0f ? (max - min) / max : 0f;
            Assert.That(saturation, Is.LessThan(0.25f),
                $"expected a desaturated neutral (saturation < 0.25), got {c} (saturation {saturation:0.000})");
        }
        private static void AssertYellow(Color c) =>
            Assert.That(c.r, Is.GreaterThan(0.8f).And.GreaterThan(c.b).And.EqualTo(c.g).Within(0.25f),
                        $"expected yellow (r≈g, both high), got {c}");
        private static void AssertOrange(Color c) =>
            Assert.That(c.r, Is.GreaterThan(c.g).And.GreaterThan(c.b),
                        $"expected orange (r>g>b), got {c}");
        private static void AssertRed(Color c) =>
            Assert.That(c.r, Is.GreaterThan(0.8f).And.GreaterThan(c.g * 2f),
                        $"expected red (r dominant), got {c}");
    }
}
