using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// Every tunable in the Backyard's look, in one place (YT-49).
    ///
    /// Direction (Art Direction &amp; UI, Art Bible v1): stylised but grounded — Hades /
    /// Dead Cells adjacent, NOT flat cartoon. Saturated colour with deep shadows and warm
    /// rim light, subtle film grain. The Backyard is a bright biome: golden key light
    /// (#F4C95D), grass bounce (#7CB342), hard cast shadows.
    ///
    /// It's a plain struct rather than a ScriptableObject asset so a fresh clone needs no
    /// authored asset to render correctly, per the code-driven rule — but every number a
    /// human would want to push is named and reachable here.
    /// </summary>
    [System.Serializable]
    public struct BackyardLook
    {
        // --- key light (the sun) ---
        public Color KeyColor;
        public float KeyIntensity;
        public Vector3 KeyEuler;
        public float ShadowStrength;      // 1 = pitch black shadows; below that lets the fill in

        // --- fill (cool sky bounce, opposite the key) ---
        public Color FillColor;
        public float FillIntensity;
        public Vector3 FillEuler;

        // --- rim / back light (the thing that separates Max from the ground) ---
        public Color RimColor;
        public float RimIntensity;
        public Vector3 RimEuler;

        // --- ambient (a gradient, not flat grey: warm sky above, grass bounce below) ---
        public Color AmbientSky;
        public Color AmbientEquator;
        public Color AmbientGround;

        // --- atmosphere ---
        public Color FogColor;
        public float FogDensity;

        // --- grade ---
        public float PostExposure;
        public float Contrast;
        public float Saturation;
        public Color ColorFilter;
        public Color ShadowTint;          // cool shadows + warm highlights = the split-tone
        public Color HighlightTint;

        public float BloomThreshold;
        public float BloomIntensity;
        public float BloomScatter;
        public Color BloomTint;

        public float VignetteIntensity;
        public float VignetteSmoothness;
        public float FilmGrain;

        /// <summary>The shipped Backyard look.</summary>
        public static BackyardLook Default => new BackyardLook
        {
            // Late-afternoon sun, raked across the arena so everything casts a long, readable
            // shadow — depth at a fixed top-down angle has to come from the shadows.
            // Warm, but not orange. A heavily-tinted key over neutral greybox sepia-tints the
            // whole arena into one flat brown — the mood has to come from warm light against
            // COOL shadow, not from dunking everything in amber.
            KeyColor = new Color(1f, 0.95f, 0.85f),
            KeyIntensity = 2.2f,
            KeyEuler = new Vector3(46f, -38f, 0f),
            // Deep, not black. The Backyard is a bright biome: at 0.78 the shadow side and the
            // Hutch's dark face crushed to near-black and the whole arena read as a sepia cave.
            ShadowStrength = 0.6f,

            // Cool sky bounce keeps the shadow side from going dead grey.
            FillColor = new Color(0.58f, 0.71f, 0.92f),
            FillIntensity = 0.6f,
            FillEuler = new Vector3(28f, 152f, 0f),

            // The rim: low and behind, warm and bright. This is what stops Max reading as a
            // grey capsule sitting on a grey floor.
            RimColor = new Color(1f, 0.83f, 0.6f),
            RimIntensity = 1.15f,
            RimEuler = new Vector3(14f, 196f, 0f),

            // Distinctly cool skylight: this is what fills the shadow side, so it is the whole
            // source of the warm/cool contrast. Grass bounces green back up from below.
            AmbientSky = new Color(0.47f, 0.58f, 0.80f),
            AmbientEquator = new Color(0.46f, 0.47f, 0.46f),
            AmbientGround = new Color(0.31f, 0.39f, 0.20f),

            // Light haze for depth across a ~20x-viewport arena. Anything denser and the far
            // side of the Backyard turns to soup.
            FogColor = new Color(0.60f, 0.63f, 0.62f),
            FogDensity = 0.005f,

            PostExposure = 0.3f,
            Contrast = 12f,
            Saturation = 16f,
            ColorFilter = new Color(1f, 0.97f, 0.92f),
            ShadowTint = new Color(0.30f, 0.44f, 0.72f),      // shadows lean firmly blue
            HighlightTint = new Color(1f, 0.88f, 0.68f),      // highlights lean gold

            BloomThreshold = 0.92f,
            BloomIntensity = 0.55f,
            BloomScatter = 0.62f,
            BloomTint = new Color(1f, 0.94f, 0.84f),

            // Enough to pull the eye to Max, not enough to darken the corners of the arena —
            // the player has to be able to see enemies coming in from the edge of frame.
            VignetteIntensity = 0.17f,
            VignetteSmoothness = 0.5f,
            FilmGrain = 0.12f,
        };
    }
}
