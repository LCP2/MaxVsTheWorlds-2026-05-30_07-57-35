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

            // MV-350: this used to be 0.92, which sits BELOW the brightest an ordinary,
            // ceiling-compliant surface reaches under the key alone (SunlitAlbedo.Ceiling(0.6) x
            // KeyIntensity(1.8) = 1.08). That is not a highlight or a piece of VFX — it is the
            // routine lit peak of any archetype colour painted right up to the sunlit ceiling
            // (which is most of them; ActorReadabilityTests demands they be loud). So on the
            // unshaded side of the yard, ordinary robot bodies were crossing the threshold on
            // their own lighting alone and self-blooming, and Bloom's warm BloomTint blended back
            // over them in proportion to the overage — desaturating every archetype toward the
            // same pale tan/cream regardless of what hue it was painted, worst where nothing
            // shades the key (e.g. beside the Mower Hutch). Three tickets (MV-303, MV-328,
            // MV-348) chased this by repainting archetypes, and it kept coming back because
            // nothing about a per-archetype colour was ever the defect — every one of them was
            // already compliant with the ceiling. 1.35 clears the exact worst case (1.08) with
            // headroom for the fill/rim adding onto the same lit facet, so bloom is reserved for
            // genuine highlights (hit flashes, the Hutch's pulsing core, muzzle flare) instead of
            // firing on every correctly-exposed body and surface in the yard. See
            // SunlitAlbedo.ClipsBloomUnderKey and BackyardLightingTests for the proof.
            BloomThreshold = 1.35f,
            BloomIntensity = 0.55f,
            BloomScatter = 0.62f,
            BloomTint = new Color(1f, 0.94f, 0.84f),

            // Enough to pull the eye to Max, not enough to darken the corners of the arena —
            // the player has to be able to see enemies coming in from the edge of frame.
            VignetteIntensity = 0.17f,
            VignetteSmoothness = 0.5f,
            FilmGrain = 0.12f,
        };

        /// <summary>
        /// Stormdrain — World 2, underground (MV-754).
        ///
        /// World 2 has been lit by <see cref="Default"/> since it shipped: a 1.8-intensity
        /// golden-hour key, a warm rim, a blue-sky ambient gradient and a live daylight sky dome. That
        /// is the single largest reason the drain "feels like a garden, just a dirty one" — the paint
        /// changed and the light never did.
        ///
        /// The direction here is the inverse of the Backyard's. The Backyard is a bright biome whose
        /// mood comes from warm light against cool shadow; the drain is a DARK biome whose mood comes
        /// from small pools of warm light against a lot of nothing. So the key drops to a fraction of
        /// the yard's and turns cold (a shaft through a grate, not a sun), the fill turns green
        /// (bounce off the sludge, the only bright thing down here), the rim stays warm and is the
        /// ONLY warm light in the scene — which is what keeps Max and the robots legible against a
        /// dark floor — and the fog goes up an order of magnitude, because the far end of a gallery
        /// falling away into dark is what makes a tunnel a tunnel.
        ///
        /// The sky fields are still filled in even though <c>ApplySky</c> nulls the skybox for this
        /// biome: a half-filled struct is a trap for the next person who reads it, and the values cost
        /// nothing.
        /// </summary>
        public static BackyardLook Stormdrain => new BackyardLook
        {
            // A shaft of daylight down a grate, not a sun. Cold and dim: at the yard's 1.8 the wet
            // concrete lifts to a mid grey and the whole world reads as an overcast car park.
            KeyColor = new Color(0.74f, 0.83f, 0.88f),
            KeyIntensity = 0.62f,
            // Steeper than the yard's 40 degrees. Light gets into a drain from directly above or not
            // at all, and a steep key throws short, hard shadows that read as "under something".
            KeyEuler = new Vector3(62f, -30f, 0f),
            // Harder than the Backyard's 0.45. Deep shadow is the point down here; the rim and the
            // emissive lamps are what stop it becoming unreadable, not a lifted shadow.
            ShadowStrength = 0.72f,

            // Bounce off the sludge — the drain's own light source, and the reason the shadow side is
            // green rather than blue. This is the single cue that says "there is something glowing on
            // the floor" even in a frame with no sludge in it.
            FillColor = new Color(0.34f, 0.46f, 0.26f),
            FillIntensity = 0.42f,
            FillEuler = new Vector3(20f, 140f, 0f),

            // The only warm light in the world, and the whole reason a robot has a readable edge
            // against a dark wall. Brighter than the yard's relative to the key on purpose.
            RimColor = new Color(1f, 0.74f, 0.46f),
            RimIntensity = 1.05f,
            RimEuler = new Vector3(10f, 200f, 0f),

            // No sky above, so the ambient sky term is what leaks down the grates: dim and cold. The
            // ground term is sludge green, bouncing up.
            AmbientSky = new Color(0.16f, 0.20f, 0.24f),
            AmbientEquator = new Color(0.15f, 0.16f, 0.15f),
            AmbientGround = new Color(0.17f, 0.22f, 0.10f),

            // MV-782: was 12x the yard's density (0.065) and pushed the camera's own 26 m sightline to
            // 94% fogged — past the point BiomePalette.Stormdrain's own separated value tiers (MV-777)
            // ever reach the eye, because fog composites every surface toward FogColor's own luminance
            // the further it sits from the camera. 0.018 keeps the drain closed-in (~21% fog at 26 m,
            // ~3% at 10 m) while leaving the tiers intact at gameplay range — some falloff is still
            // correct here, just not enough to erase the palette underneath it.
            //
            // FogColor is pushed BELOW the floor's own luminance (was 0.113, sitting between the floor's
            // 0.062 and the wall's 0.204 — exactly the value that drags every surface toward one flat
            // mid-tone as fog increases with distance). Fog darker than everything deepens a frame; fog
            // that sits mid-range flattens it.
            FogColor = new Color(0.035f, 0.045f, 0.042f),
            FogDensity = 0.018f,

            // Unused — ApplySky nulls the skybox for this biome — but filled so the struct is honest.
            SkyZenith = new Color(0.05f, 0.07f, 0.08f),
            SkyHorizon = new Color(0.09f, 0.12f, 0.11f),
            SkyGroundHaze = new Color(0.07f, 0.09f, 0.08f),
            SkySun = new Color(0.4f, 0.45f, 0.5f),
            SkyCloud = new Color(0.1f, 0.12f, 0.12f),
            SkySunGlow = 120f,
            SkySunIntensity = 0.2f,
            SkyCloudAmount = 0f,
            SkyCloudScale = 1f,

            // Stronger contact darkening than the yard: everything in a drain sits IN water or silt,
            // and an AO that grips is what sells a pipe touching a wall.
            AoIntensity = 0.85f,
            AoRadius = 0.35f,

            PostExposure = 0.15f,

            // MV-754 capture check: every other grade field this struct leaves unset safely
            // neutralises on its own (ShadowTint/HighlightTint go through BackyardLighting's
            // ToTrackball, which maps a zero colour to (1,1,1,0); Contrast/Saturation are 0 =
            // neutral in URP's -100..100 range; Bloom/Vignette/FilmGrain at intensity 0 add
            // nothing). ColorFilter is the one exception — it has no such fallback and is applied
            // to the frame verbatim, so leaving it at its Color default (0,0,0,0) multiplies every
            // pixel by black. The first mv-w2-light capture came back solid black; this is why.
            ColorFilter = Color.white,
        };

        /// <summary>The look for a loaded world, mirroring <see cref="BiomePalette.ForWorld"/>'s own
        /// index rule. World 3 keeps <see cref="Default"/> for now — MV-745 gave the Reef its own
        /// materials and killed its skybox, but never its lighting, and re-lighting it is that
        /// world's own ticket, not this one's.</summary>
        public static BackyardLook ForWorld(int worldIndex)
            => worldIndex == 1 ? Stormdrain : Default;
    }
}
