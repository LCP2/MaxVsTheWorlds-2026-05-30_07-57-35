using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>The surfaces a biome's material set covers.</summary>
    public enum SurfaceKind
    {
        Ground,
        Wall,
        Prop,
    }

    /// <summary>
    /// A biome's colour identity (YT-50). Colours come from the Art Bible's per-biome table
    /// (Art Direction &amp; UI): Backyard is golden #F4C95D with grass #7CB342.
    ///
    /// <see cref="Tint"/> is the single tunable the AC asks for: it multiplies every surface in
    /// the biome at once, so the whole arena can be pushed warmer/cooler/darker with one value
    /// without re-authoring anything.
    /// </summary>
    [System.Serializable]
    public struct BiomePalette
    {
        /// <summary>The single biome-wide tint. Multiplies every surface colour below.</summary>
        public Color Tint;

        public Color GroundBase;      // the darker, dominant ground colour
        public Color GroundAccent;    // patches mottled through it by the noise texture
        public Color Wall;
        public Color Prop;

        /// <summary>How many times the ground texture repeats across the plane. Low enough that
        /// the mottling reads as terrain, not as wallpaper.</summary>
        public float GroundTiling;

        public float Smoothness;      // stylised = matte; a shiny greybox looks like plastic

        /// <summary>
        /// Backyard — cut grass at golden hour (YT-69).
        ///
        /// The previous pass read as "vomit": a mustard-olive. The mistake was in the ACCENT, not the
        /// base. It was a khaki (0.45, 0.42, 0.25) — red and green almost equal, which is the
        /// definition of mustard. Mottled through a dark olive base and then pushed by a warm key and
        /// a saturation boost, the whole floor landed in the one part of the spectrum that reads as
        /// bile. Nothing about it was grass.
        ///
        /// The fix is to keep BOTH tones unambiguously green (green channel clearly dominant) and put
        /// the variation in VALUE — shaded turf vs sunlit turf — rather than swinging the hue toward
        /// yellow. The Biomes doc asks for "golden hour, saturated yellow-greens": that's a sunlit
        /// green, and the gold belongs to the LIGHT. Paint the grass green; let the key make it
        /// golden.
        ///
        /// Kept mid-value on purpose: Max is warm red and the robots are cold steel, and both need to
        /// pop against the floor rather than fight it.
        /// </summary>
        public static BiomePalette Backyard => new BiomePalette
        {
            Tint = Color.white,                              // neutral: the palette below is already on-model
            // Deliberately a LAWN, not AstroTurf. The first pass at these was brighter and read as
            // fluorescent — under a 2.2-intensity warm key with a saturation boost on top, a vivid
            // albedo doesn't stay vivid, it goes neon. The paint stays a touch restrained precisely
            // so the lighting can make it sing.
            GroundBase = new Color(0.15f, 0.29f, 0.12f),     // shaded turf — green, not olive
            GroundAccent = new Color(0.32f, 0.50f, 0.18f),   // sunlit turf — brighter, still clearly green
            Wall = new Color(0.36f, 0.27f, 0.18f),           // fence/soil browns
            Prop = new Color(0.46f, 0.44f, 0.40f),
            GroundTiling = 5f,
            Smoothness = 0.06f,
        };

        public Color ColorFor(SurfaceKind kind)
        {
            Color c;
            switch (kind)
            {
                case SurfaceKind.Ground: c = GroundBase; break;
                case SurfaceKind.Wall: c = Wall; break;
                default: c = Prop; break;
            }
            return c * Tint;
        }
    }
}
