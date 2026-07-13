using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// The ceiling every albedo in the Backyard has to live under, and the one way to get under it.
    ///
    /// The yard is lit by a key brighter than 1× (<see cref="BackyardLook.KeyIntensity"/>, currently
    /// 1.8), and URP/Lit multiplies albedo by the light. So a surface painted much past ~0.55 per
    /// channel doesn't render as a bright version of its colour — it renders past 1.0, clips, and
    /// comes out WHITE. YT-75 shipped a garden kit painted for an unlit look and the yard grew a
    /// fence that was cream in the sun and brown in the shade: one material, two fences.
    ///
    /// This was found once, in the kit importer, and then needed a second time the moment YT-77 began
    /// generating albedo textures at runtime — a grain's bright end is the first thing to cross the
    /// line. Two copies of a rule like this is how the rule stops being true in one of them, so it
    /// lives here, and the importer defers to it.
    /// </summary>
    public static class SunlitAlbedo
    {
        /// <summary>The brightest any channel of any albedo in this biome may be.
        ///
        /// Not derived from the key at runtime on purpose: it is a decision about how much headroom
        /// the grade and the bloom get, and a key tuned down for one shot shouldn't silently license
        /// a brighter fence. The tests assert the two stay compatible.</summary>
        public const float Ceiling = 0.6f;

        /// <summary>
        /// Bring a colour under the ceiling by scaling it, NOT by clamping each channel.
        ///
        /// The difference matters. Clamping (1.0, 0.56, 0.38) per channel gives (0.6, 0.56, 0.38) —
        /// the red collapses onto the green and a warm timber turns into a muddy olive. Dividing all
        /// three by the peak keeps every ratio between them intact, so the colour gets DARKER and
        /// stays the colour it was. Hue and internal contrast survive; only the brightness gives.
        /// </summary>
        public static Color Clamp(Color c)
        {
            float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (peak <= Ceiling) return c;

            float k = Ceiling / peak;
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }
    }
}
