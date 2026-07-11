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
        /// Backyard — grass going dry in patches.
        ///
        /// These are knocked well back from the Art Bible's identity swatches (#7CB342 grass,
        /// #F4C95D golden). Those are UI/key-art colours; used raw on a big ground plane, under a
        /// warm key and a saturation boost, they come out as loud yellow-green camouflage. The
        /// direction is "stylised but grounded — NOT flat cartoon", so the surface stays muted and
        /// the golden identity comes from the light, not from neon paint.
        /// </summary>
        public static BiomePalette Backyard => new BiomePalette
        {
            Tint = Color.white,                              // neutral: the palette below is already on-model
            GroundBase = new Color(0.26f, 0.31f, 0.16f),     // deep grass
            GroundAccent = new Color(0.45f, 0.42f, 0.25f),   // dry khaki patches
            Wall = new Color(0.34f, 0.29f, 0.23f),           // fence/soil browns
            Prop = new Color(0.46f, 0.44f, 0.40f),
            GroundTiling = 8f,
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
