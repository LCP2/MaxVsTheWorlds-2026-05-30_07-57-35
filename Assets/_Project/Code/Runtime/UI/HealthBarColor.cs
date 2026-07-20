using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The shared colour language for every floating life bar (YT-121, YT-122): a bar drains
    /// green → yellow → orange → red and FLASHES once it is critical.
    ///
    /// It lives on its own, as pure functions of the fill fraction, so Max's bar and every robot's
    /// bar cannot disagree about what "nearly dead" looks like — the whole point of the ticket asking
    /// for it "reusably". A test can pin the thresholds without building a canvas.
    /// </summary>
    public static class HealthBarColor
    {
        // Thresholds, from the ticket. Green above Healthy, yellow down to Hurt, orange down to
        // Critical, red-and-flashing below it.
        public const float Healthy = 0.60f;
        public const float Hurt = 0.35f;
        public const float Critical = 0.15f;

        private static readonly Color Green = new Color(0.36f, 0.85f, 0.32f);
        private static readonly Color Yellow = new Color(0.96f, 0.86f, 0.16f);
        private static readonly Color Orange = new Color(0.96f, 0.55f, 0.14f);
        private static readonly Color Red = new Color(0.93f, 0.22f, 0.18f);

        /// <summary>How fast a critical bar pulses, radians/sec.</summary>
        private const float FlashSpeed = 9f;

        /// <summary>The band colour for a fill fraction, before any flash.</summary>
        public static Color Ramp(float normalized)
        {
            if (normalized > Healthy) return Green;
            if (normalized > Hurt) return Yellow;
            if (normalized > Critical) return Orange;
            return Red;
        }

        /// <summary>Below <see cref="Critical"/> the bar is in trouble and should flash.</summary>
        public static bool IsCritical(float normalized) => normalized <= Critical;

        /// <summary>
        /// The colour to actually draw: the band colour, pulsed toward bright white when critical so
        /// the eye is yanked to a unit about to die. <paramref name="time"/> is the clock (pass
        /// <c>Time.unscaledTime</c> so it keeps flashing even if the game is paused on a low-health
        /// beat).
        /// </summary>
        public static Color At(float normalized, float time)
        {
            Color band = Ramp(normalized);
            if (!IsCritical(normalized)) return band;

            float pulse = 0.5f + 0.5f * Mathf.Sin(time * FlashSpeed);
            return Color.Lerp(band, Color.white, pulse * 0.75f);
        }
    }
}
