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

        // --- sky (YT-76) ---
        public Color SkyZenith;
        public Color SkyHorizon;
        public Color SkyGroundHaze;    // the half of the dome the 72° camera actually sees
        public Color SkySun;
        public Color SkyCloud;
        public float SkySunGlow;       // tightness of the glow (bigger = tighter)
        public float SkySunIntensity;
        public float SkyCloudAmount;
        public float SkyCloudScale;

        // --- ambient occlusion (YT-76) ---
        public float AoIntensity;
        public float AoRadius;

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
            // Down from 2.2 (YT-76). At 2.2 the lawn's own sunlit tone (GroundAccent, 0.50 green)
            // multiplied out past 1.0 and CLIPPED: every patch of grass the fence didn't shade came
            // out a flat, neon slab with no shading left in it. BiomePalette's own note warned about
            // exactly this — "a vivid albedo doesn't stay vivid, it goes neon" — and dropping the sun
            // to 34° made it obvious by putting bright, unshaded grass right next to shadowed grass.
            // 1.8 keeps the brightest surface in the yard just under the ceiling, where the
            // tonemapper can still roll it off instead of the framebuffer hard-clamping it.
            KeyIntensity = 1.8f,
            // YT-76: the sun drops from 46° to 40° above the horizon. Mid-afternoon becomes late
            // afternoon, and everything in the yard throws a longer shadow — at a fixed top-down
            // camera, cast shadows are most of what tells you a fence has a height and a tree stands
            // off the ground. It stopped at 40° rather than going lower because the yard is walled:
            // at 34° a 3.5 m fence threw a 5 m shadow and half the patio was a dark room, which is
            // atmosphere bought at the cost of seeing the fight.
            KeyEuler = new Vector3(40f, -38f, 0f),
            // Deep, not black. The Backyard is a bright biome: at 0.78 the shadow side and the
            // Hutch's dark face crushed to near-black and the whole arena read as a sepia cave.
            // Softened again for YT-76, because a longer shadow covers more of the yard: the same
            // darkness that read as "contrast" at 46° reads as "half the lawn is a hole" at 40°.
            // Max is orange and the robots are hazard-coloured — but only if there's light on them.
            ShadowStrength = 0.45f,

            // Cool sky bounce keeps the shadow side from going dead grey. Lifted for YT-76: the
            // kit's props are opaque timber and stone standing vertically, and vertical surfaces
            // catch almost nothing from a sun this low. Without this, every fence panel facing away
            // from the sun was a black silhouette.
            FillColor = new Color(0.58f, 0.71f, 0.92f),
            FillIntensity = 0.85f,
            FillEuler = new Vector3(28f, 152f, 0f),

            // The rim: low and behind, warm and bright. This is what stops Max reading as a
            // grey capsule sitting on a grey floor.
            RimColor = new Color(1f, 0.83f, 0.6f),
            RimIntensity = 1.15f,
            RimEuler = new Vector3(14f, 196f, 0f),

            // Distinctly cool skylight: this is what fills the shadow side, so it is the whole
            // source of the warm/cool contrast. Grass bounces green back up from below.
            //
            // The equator band is the one that matters now the yard is full of standing props: it
            // is the only light a vertical surface facing away from the sun receives. Raised and
            // warmed a little for YT-76 — flat grey ambient is what made the fence read as
            // cardboard on its shadow side.
            AmbientSky = new Color(0.47f, 0.58f, 0.80f),
            AmbientEquator = new Color(0.55f, 0.55f, 0.53f),
            AmbientGround = new Color(0.31f, 0.39f, 0.20f),

            // Light haze for depth across a ~20x-viewport arena. Anything denser and the far
            // side of the Backyard turns to soup. Warmed toward the sky's ground haze (YT-76) so
            // the far end of the yard fades INTO the sky rather than against it.
            FogColor = new Color(0.66f, 0.67f, 0.62f),
            FogDensity = 0.0055f,

            // --- the sky ---
            // Late-afternoon: a deep blue overhead thinning to a warm, hazy horizon. The camera
            // never sees any of this except the haze (see StylizedSky.shader) — which is exactly
            // why the haze is tuned to the fog and the grass, and the blue is just honest.
            SkyZenith = new Color(0.24f, 0.42f, 0.72f),
            SkyHorizon = new Color(0.78f, 0.80f, 0.76f),
            SkyGroundHaze = new Color(0.52f, 0.55f, 0.46f),   // hazy distance, faintly green: this is a garden
            SkySun = new Color(1f, 0.90f, 0.70f),
            SkyCloud = new Color(1f, 0.97f, 0.92f),
            SkySunGlow = 38f,
            SkySunIntensity = 1.05f,
            SkyCloudAmount = 0.35f,
            SkyCloudScale = 1.6f,

            // --- contact shadows ---
            // Ambient occlusion is the other half of the trade YT-76 makes: the fill and the ambient
            // go UP so nothing is a silhouette, and the crevices go back DOWN so nothing floats. A
            // fence post with a dark line where it meets the lawn is standing in the lawn; the same
            // post without one is a sticker on it. Small radius on purpose — this is for contact,
            // not for a dirty-corners look.
            AoIntensity = 0.75f,
            AoRadius = 0.25f,

            // A touch more exposure to pay back the dimmer key, and less saturation on top of it:
            // +16 was pushing an already-clipped green further into acid. The colour in the yard now
            // comes from the paint and the light, not from the grade shouting at both.
            PostExposure = 0.35f,
            Contrast = 12f,
            Saturation = 10f,
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
