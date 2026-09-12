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

        // MV-788: full health used to read bright green — on World 2's dim palette that made every
        // untouched unit's bar the brightest thing in the room ("scattered neon blobs lying on the
        // ground", Lee). A cool, low-saturation neutral instead, so colour means HURT (yellow/orange/
        // red below) rather than "exists". Saturation (max-min)/max ≈ 0.15, under the ticket's 0.25
        // ceiling.
        private static readonly Color Green = new Color(0.58f, 0.64f, 0.68f);
        private static readonly Color Yellow = new Color(0.96f, 0.86f, 0.16f);
        private static readonly Color Orange = new Color(0.96f, 0.55f, 0.14f);
        private static readonly Color Red = new Color(0.93f, 0.22f, 0.18f);

        /// <summary>MV-788: how much further <see cref="At"/> desaturates the healthy band for a bar
        /// that is permanently on screen (Max's own, an AreaGate's) — those two sit at full health far
        /// more of the time than a robot's now-transient bar ever does, so even the cool neutral above
        /// still needs taming to sit inside the frame's palette rather than sit on top of it.</summary>
        private const float AlwaysOnHealthySaturationScale = 0.4f;

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
        /// beat). <paramref name="desaturateWhenHealthy"/> (MV-788) additionally mutes the healthy band
        /// for a bar that is always on screen (Max's own, an AreaGate's) — never applied below
        /// <see cref="Healthy"/>, where the ramp's own yellow/orange/red urgency must stay vivid.
        /// </summary>
        public static Color At(float normalized, float time, bool desaturateWhenHealthy = false)
        {
            Color band = Ramp(normalized);

            if (IsCritical(normalized))
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * FlashSpeed);
                return Color.Lerp(band, Color.white, pulse * 0.75f);
            }

            if (desaturateWhenHealthy && normalized > Healthy) return Desaturate(band, AlwaysOnHealthySaturationScale);

            return band;
        }

        /// <summary>Scales a colour's saturation toward its own luminance-preserving grey by
        /// <paramref name="scale"/> (1 = unchanged, 0 = fully grey) via HSV, so hue and value survive.</summary>
        private static Color Desaturate(Color c, float scale)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(h, s * scale, v);
        }
    }
}
