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

        /// <summary>
        /// City — World 3's Market Street night look (MV-1175, Phase 1 Slice A). A steep, dim, cool key
        /// (daylight from directly above or not at all, same reasoning <see cref="Stormdrain"/>'s own
        /// doc gives for its key angle) and a warm rim are the whole read: the street itself stays dark
        /// and cool so every window pool, lamp pool and neon sign — none of which this struct touches,
        /// they come from <see cref="CityKit"/>'s own unlit/additive materials — is the only saturated,
        /// bright thing in frame.
        /// </summary>
        public static BackyardLook City => new BackyardLook
        {
            KeyColor = new Color(0.70f, 0.76f, 0.95f),
            // MV-1207: Lee, 17 Oct — "too much of a dark shade of colour; structures getting lost."
            // +25% on the moon (was 0.70). Still a night key, just one that lets the structures the
            // street is full of actually read.
            KeyIntensity = 0.70f * 1.25f,
            KeyEuler = new Vector3(62f, -30f, 0f),
            ShadowStrength = 0.6f,

            FillColor = new Color(0.36f, 0.38f, 0.58f),
            FillIntensity = 0.45f,
            FillEuler = new Vector3(20f, 140f, 0f),

            RimColor = new Color(1.0f, 0.80f, 0.58f),
            RimIntensity = 0.9f,
            RimEuler = new Vector3(12f, 200f, 0f),

            // MV-1207: +35% ambient. A uniform RGB scale moves V without touching H/S (max and min
            // both scale by the same factor, so (max-min)/max — the S term — cancels), so this is
            // "lift the ambient" in exactly the sense the HSV-preserving palette lift below is.
            AmbientSky = new Color(0.20f, 0.22f, 0.32f) * 1.35f,
            AmbientEquator = new Color(0.17f, 0.17f, 0.21f) * 1.35f,
            AmbientGround = new Color(0.12f, 0.12f, 0.15f) * 1.35f,

            // MV-1207: fog lifted 20% in value. Street-lamp pools are CityKit's own unlit/additive
            // materials, untouched by this struct, so they keep reading as lamps against the lifted
            // street.
            FogColor = new Color(0.05f, 0.055f, 0.08f) * 1.2f,
            FogDensity = 0.010f,

            // Sky fields copied from Stormdrain — unused, ApplySky nulls the skybox for this biome
            // (BiomePalette.City.RemovesSkybox = true), but filled so the struct is honest (same
            // reasoning Stormdrain's own doc gives for its own sky fields).
            SkyZenith = new Color(0.05f, 0.07f, 0.08f),
            SkyHorizon = new Color(0.09f, 0.12f, 0.11f),
            SkyGroundHaze = new Color(0.07f, 0.09f, 0.08f),
            SkySun = new Color(0.4f, 0.45f, 0.5f),
            SkyCloud = new Color(0.1f, 0.12f, 0.12f),
            SkySunGlow = 120f,
            SkySunIntensity = 0.2f,
            SkyCloudAmount = 0f,
            SkyCloudScale = 1f,

            AoIntensity = 0.8f,
            AoRadius = 0.3f,

            PostExposure = 0.2f,
            Contrast = 8f,
            Saturation = 6f,
            ColorFilter = Color.white,
            ShadowTint = new Color(0.40f, 0.45f, 0.70f),
            HighlightTint = new Color(1.0f, 0.92f, 0.80f),

            BloomThreshold = 1.0f,
            BloomIntensity = 0.6f,
            BloomScatter = 0.6f,
            BloomTint = Color.white,

            VignetteIntensity = 0.18f,
            VignetteSmoothness = 0.5f,
            FilmGrain = 0.08f,
        };

        /// <summary>
        /// Reef — World 4's flooded ship interior, its own dark look (MV-1250). World 3/4 has shipped
        /// lit by <see cref="Default"/> since MV-713 (MV-745/MV-1141's own "a different ticket's, not
        /// this one's" deferral) — the Backyard's 1.8-intensity golden key and warm ambient gradient
        /// over the Reef's own navy hull palette (<see cref="MaxWorlds.Rendering.BiomePalette.Reef"/>)
        /// measured median luminance 74/255, flat lavender-grey (#656c81 etc) on a live TestFlight
        /// screenshot — against the design frames' 27-36/255 dark navy. Same "daylight from directly
        /// above or not at all" steep key angle every sealed-hull interior above uses, since nothing
        /// down here has a sky.
        ///
        /// The whole room stays cool and dark on purpose: <see cref="KeyColor"/>/<see cref="FillColor"/>/
        /// <see cref="AmbientSky"/>/<see cref="AmbientEquator"/>/<see cref="AmbientGround"/>/
        /// <see cref="FogColor"/> are all blue, none warm. The one warm light left is
        /// <see cref="RimColor"/> — hazard orange, the ticket's own exception ("no warm light anywhere,
        /// except hazard orange where it already exists for danger or interaction") and also the exact
        /// hue Max and the Reef's own robots already wear, so the rim reads as the readability tool it
        /// is rather than a second light source warming the room. The cyan/magenta strips, window glass
        /// and bio-glow growth are <see cref="MaxWorlds.Rendering.WorldMaterials"/>'s own emissive
        /// materials, untouched by this struct — <see cref="BloomThreshold"/> stays low enough (0.9,
        /// matching <see cref="City"/>/<see cref="ShipBase"/>) that they bloom against the dark room
        /// instead of needing this grade to brighten anything.
        ///
        /// Lee, 29 Sep 2026: keep the Reef robots' own colouring; if contrast is a problem, change the
        /// floor and the light, not the robots (this ticket's own "Do not re-raise") — the readability
        /// floor route if a capture ever comes back with Max or a robot lost in the dark is a brighter
        /// <see cref="RimIntensity"/>, never a brighter room.
        /// </summary>
        public static BackyardLook Reef => new BackyardLook
        {
            // Cool cyan-blue, not neutral white — a shaft through flooded machinery, not a sun.
            // Dimmer than Stormdrain's 0.62: the Reef's own palette (GroundBase #1D2C3B) already sits
            // brighter than Stormdrain's near-black concrete, so matching Stormdrain's key would
            // re-create the same "washed out" complaint one step down instead of fixing it.
            KeyColor = new Color(0.45f, 0.68f, 0.85f),
            // MV-1275: x2.14 (the +1.1 stops this ticket moves out of PostExposure) off the MV-1250
            // value — see PostExposure's own comment below for why the lift moved here instead.
            KeyIntensity = 0.90f,
            KeyEuler = new Vector3(62f, -30f, 0f),
            ShadowStrength = 0.68f,

            // Teal bounce off the hull plating, not Stormdrain's sludge-green and not Default's sky-blue
            // — this is what keeps the shadow side of a pipe or bulkhead from crushing to pure black.
            FillColor = new Color(0.14f, 0.28f, 0.36f),
            FillIntensity = 0.77f,  // MV-1275: x2.14, same as KeyIntensity above
            FillEuler = new Vector3(20f, 140f, 0f),

            // The only warm light anywhere (see class doc) — hazard orange, matching Max's own body
            // colour, so it reads as "the thing that separates Max from the floor" rather than mood
            // lighting. Brighter than the key/fill on purpose: a dark biome needs ITS rim doing more of
            // the readability work than a bright one does.
            RimColor = new Color(1f, 0.56f, 0.26f),
            RimIntensity = 2.25f,  // MV-1275: x2.14, same as KeyIntensity above
            RimEuler = new Vector3(12f, 200f, 0f),

            // Near-black navy, every term — sky, equator AND ground — per the ticket's own "a near-black
            // navy ambient gradient" line. No green/sludge equator band like Stormdrain's: the Reef has
            // no sludge, only hull plate and bio-glow. MV-1275: all three x2.14, same factor as the
            // lights above, to hold the same dark-navy ratios the MV-1250 values established.
            AmbientSky = new Color(0.096f, 0.160f, 0.246f),
            AmbientEquator = new Color(0.075f, 0.128f, 0.203f),
            AmbientGround = new Color(0.047f, 0.086f, 0.150f),

            // Dense dark-teal fog — thicker than Stormdrain's 0.018 and City's 0.010, per the ticket's
            // own "dense dark-teal fog so far walls fall off into the deep": a flooded hull's far walls
            // should vanish into the dark, not just dim.
            FogColor = new Color(0.025f, 0.06f, 0.08f),
            FogDensity = 0.022f,

            // Unused — ApplySky nulls the skybox for this biome (BiomePalette.Reef.RemovesSkybox =
            // true) — but filled so the struct is honest (same reasoning Stormdrain's own doc gives).
            SkyZenith = new Color(0.05f, 0.07f, 0.08f),
            SkyHorizon = new Color(0.09f, 0.12f, 0.11f),
            SkyGroundHaze = new Color(0.07f, 0.09f, 0.08f),
            SkySun = new Color(0.4f, 0.45f, 0.5f),
            SkyCloud = new Color(0.1f, 0.12f, 0.12f),
            SkySunGlow = 120f,
            SkySunIntensity = 0.2f,
            SkyCloudAmount = 0f,
            SkyCloudScale = 1f,

            // Firmer contact darkening than the yard — everything down here sits in a flooded,
            // riveted hull, same reasoning Stormdrain's own doc gives for its own AO.
            AoIntensity = 0.85f,
            AoRadius = 0.32f,

            // MV-1275: PostExposure back to 0.15f, Stormdrain's value — the +1.1 stops MV-1250 put here
            // read mauve on GLES/Metal (WebGL Mobile tier, iPhone) while reading navy on D3D12, because
            // a +1.25-stop lift on a near-black LDR buffer (Mobile tier has no HDR) hits the two APIs'
            // post colour paths differently. Every other biome lights the SCENE and takes little or no
            // post lift; the fix is to do the same here instead of depending on a lift with a platform-
            // dependent result. The 2.14x this removes from post is now baked into KeyIntensity/
            // FillIntensity/RimIntensity/AmbientSky/AmbientEquator/AmbientGround above instead.
            PostExposure = 0.15f,
            Contrast = 10f,
            Saturation = 4f,     // low — the only saturated colour left is the kit's own cyan/magenta emissives
            ColorFilter = Color.white,
            ShadowTint = new Color(0.30f, 0.46f, 0.66f),    // cool shadows lean navy
            HighlightTint = new Color(0.55f, 0.85f, 0.9f),  // cool highlights too — no warm grade anywhere

            // Bloom threshold low enough that the kit's own cyan/magenta strips and the bio-glow growth
            // read as glowing against this dark room, same idiom City/ShipBase already use.
            BloomThreshold = 0.9f,
            BloomIntensity = 0.6f,
            BloomScatter = 0.6f,
            BloomTint = new Color(0.6f, 0.9f, 1f),

            VignetteIntensity = 0.2f,
            VignetteSmoothness = 0.5f,
            FilmGrain = 0.1f,
        };

        /// <summary>Fields every Mothership deck preset below shares (MV-1249): a steep, dim key — the
        /// same "daylight from directly above or not at all" reasoning <see cref="Stormdrain"/>'s own
        /// doc gives its key angle, since a sealed hull interior has no other source — soft AO, a
        /// neutral grade and a light vignette. Only <see cref="KeyColor"/>/<see cref="KeyIntensity"/>/
        /// <see cref="FillColor"/>/<see cref="FillIntensity"/>/<see cref="RimColor"/>/
        /// <see cref="RimIntensity"/>/<see cref="AmbientSky"/>/<see cref="AmbientEquator"/>/
        /// <see cref="AmbientGround"/>/<see cref="FogColor"/>/<see cref="BloomTint"/> vary per deck —
        /// see each preset's own doc for why its own numbers differ. Every deck's <see cref="AmbientSky"/>
        /// stays under 0.15 Rec.709 luminance (MV-1249 AC1): "dark ship, not lavender" (ticket's own "do
        /// not re-raise") — see <see cref="MaxWorlds.Arena.MothershipDecks"/> for the area-index ->
        /// look lookup this feeds.</summary>
        private static BackyardLook ShipBase => new BackyardLook
        {
            KeyEuler = new Vector3(62f, -30f, 0f),
            ShadowStrength = 0.62f,

            FillEuler = new Vector3(20f, 140f, 0f),

            RimEuler = new Vector3(12f, 200f, 0f),

            FogDensity = 0.012f,   // "light" fog — between Stormdrain's 0.018 and City's 0.010

            // Sky fields copied from Stormdrain/City — unused, ApplySky nulls the skybox for this
            // biome (BiomePalette.Ship.RemovesSkybox = true), but filled so the struct is honest.
            SkyZenith = new Color(0.05f, 0.07f, 0.08f),
            SkyHorizon = new Color(0.09f, 0.12f, 0.11f),
            SkyGroundHaze = new Color(0.07f, 0.09f, 0.08f),
            SkySun = new Color(0.4f, 0.45f, 0.5f),
            SkyCloud = new Color(0.1f, 0.12f, 0.12f),
            SkySunGlow = 120f,
            SkySunIntensity = 0.2f,
            SkyCloudAmount = 0f,
            SkyCloudScale = 1f,

            AoIntensity = 0.8f,
            AoRadius = 0.3f,

            // MV-1249 AC3's single adjustment pass: the first capture (PostExposure 0.2, the same
            // value every other biome uses) measured every hero frame roughly half its target median
            // (Hangar/Assembly/Vault/Bridge all ~13-15/255 against 28-55 targets) -- the whole-frame
            // median is dominated by the still-undressed void beyond this ticket's lit deck platforms
            // (no floor plating, no bulkheads yet -- MV-1259/MV-1260's own job), not by an authoring
            // mistake in the per-deck colours themselves. A one-stop lift here (2^1.1 =~ 2.14x) raises
            // every dark frame roughly proportionally without touching the AmbientSky values the AC1
            // EditMode test already asserts stay under 0.15, or the per-deck hue balance.
            PostExposure = 1.1f,
            Contrast = 8f,
            Saturation = 6f,
            ColorFilter = Color.white,
            ShadowTint = new Color(0.35f, 0.42f, 0.68f),
            HighlightTint = new Color(1f, 0.9f, 0.75f),

            // Ticket's own "bloom on (threshold about 0.9) so emissive strips and lamps glow".
            BloomThreshold = 0.9f,
            BloomIntensity = 0.6f,
            BloomScatter = 0.6f,

            VignetteIntensity = 0.14f,     // "vignette light"
            VignetteSmoothness = 0.5f,
            FilmGrain = 0.08f,
        };

        /// <summary>Hangar deck (areas 1-5) — cold blue with a hazard-yellow rim, the ticket's own
        /// deck identity. Target capture median luminance 31/255 (MV-1249 AC3).</summary>
        public static BackyardLook ShipHangar
        {
            get
            {
                BackyardLook l = ShipBase;
                l.KeyColor = new Color(0.62f, 0.78f, 0.98f);
                l.KeyIntensity = 0.55f;
                l.FillColor = new Color(0.30f, 0.42f, 0.58f);
                l.FillIntensity = 0.40f;
                l.RimColor = new Color(1.0f, 0.85f, 0.25f);    // hazard yellow
                l.RimIntensity = 1.0f;
                l.AmbientSky = new Color(0.10f, 0.13f, 0.19f);        // luminance ~0.128
                l.AmbientEquator = new Color(0.08f, 0.10f, 0.14f);
                l.AmbientGround = new Color(0.05f, 0.07f, 0.10f);
                l.FogColor = new Color(0.05f, 0.07f, 0.10f);
                l.BloomTint = new Color(0.85f, 0.92f, 1.0f);
                return l;
            }
        }

        /// <summary>Assembly deck (areas 6-12) — amber work light, the ticket's own single-colour deck
        /// identity (no named second accent, unlike the other four decks). Target capture median
        /// luminance 28/255 (MV-1249 AC3).</summary>
        public static BackyardLook ShipAssembly
        {
            get
            {
                BackyardLook l = ShipBase;
                l.KeyColor = new Color(0.95f, 0.80f, 0.55f);
                l.KeyIntensity = 0.60f;
                l.FillColor = new Color(0.42f, 0.33f, 0.22f);
                l.FillIntensity = 0.40f;
                l.RimColor = new Color(1.0f, 0.78f, 0.42f);    // the work light itself
                l.RimIntensity = 0.95f;
                l.AmbientSky = new Color(0.16f, 0.12f, 0.08f);        // luminance ~0.126
                l.AmbientEquator = new Color(0.13f, 0.10f, 0.07f);
                l.AmbientGround = new Color(0.08f, 0.06f, 0.04f);
                l.FogColor = new Color(0.08f, 0.06f, 0.045f);
                l.BloomTint = new Color(1.0f, 0.85f, 0.55f);
                return l;
            }
        }

        /// <summary>Holds deck (areas 13-18) — teal with a gold rim, the ticket's own deck identity.
        /// Target capture median luminance 29/255 at the Specimen Vault (MV-1249 AC3).</summary>
        public static BackyardLook ShipHolds
        {
            get
            {
                BackyardLook l = ShipBase;
                l.KeyColor = new Color(0.55f, 0.85f, 0.88f);
                l.KeyIntensity = 0.50f;
                l.FillColor = new Color(0.22f, 0.42f, 0.44f);
                l.FillIntensity = 0.38f;
                l.RimColor = new Color(1.0f, 0.87f, 0.55f);    // gold
                l.RimIntensity = 0.9f;
                l.AmbientSky = new Color(0.07f, 0.13f, 0.14f);        // luminance ~0.118
                l.AmbientEquator = new Color(0.06f, 0.11f, 0.12f);
                l.AmbientGround = new Color(0.04f, 0.07f, 0.08f);
                l.FogColor = new Color(0.045f, 0.08f, 0.085f);
                l.BloomTint = new Color(0.75f, 0.95f, 0.85f);
                return l;
            }
        }

        /// <summary>Engineering deck (areas 19-24) — heat orange with a cyan rim, the ticket's own deck
        /// identity. Target capture median luminance 87/255 at the Reactor Ring (MV-1249 AC3) — far
        /// brighter than every other hero frame because the Reactor's own emissive core (not this
        /// struct) fills most of that frame; this look stays dark like every other deck's.</summary>
        public static BackyardLook ShipEngineering
        {
            get
            {
                BackyardLook l = ShipBase;
                l.KeyColor = new Color(0.98f, 0.68f, 0.45f);
                l.KeyIntensity = 0.62f;
                l.FillColor = new Color(0.46f, 0.28f, 0.18f);
                l.FillIntensity = 0.42f;
                l.RimColor = new Color(0.40f, 0.90f, 0.95f);   // cyan
                l.RimIntensity = 1.05f;
                l.AmbientSky = new Color(0.17f, 0.11f, 0.07f);        // luminance ~0.120
                l.AmbientEquator = new Color(0.14f, 0.09f, 0.06f);
                l.AmbientGround = new Color(0.09f, 0.055f, 0.035f);
                l.FogColor = new Color(0.09f, 0.055f, 0.035f);
                l.BloomTint = new Color(1.0f, 0.75f, 0.55f);
                return l;
            }
        }

        /// <summary>Command deck (areas 25-30) — gold with a deep-blue rim, the ticket's own deck
        /// identity. Target capture median luminance 55/255 at The Bridge (MV-1249 AC3).</summary>
        public static BackyardLook ShipCommand
        {
            get
            {
                BackyardLook l = ShipBase;
                l.KeyColor = new Color(0.95f, 0.82f, 0.55f);
                l.KeyIntensity = 0.58f;
                l.FillColor = new Color(0.40f, 0.33f, 0.22f);
                l.FillIntensity = 0.38f;
                l.RimColor = new Color(0.25f, 0.35f, 0.75f);   // deep blue
                l.RimIntensity = 0.9f;
                l.AmbientSky = new Color(0.15f, 0.12f, 0.09f);        // luminance ~0.124
                l.AmbientEquator = new Color(0.12f, 0.10f, 0.075f);
                l.AmbientGround = new Color(0.08f, 0.065f, 0.045f);
                l.FogColor = new Color(0.08f, 0.065f, 0.045f);
                l.BloomTint = new Color(1.0f, 0.88f, 0.6f);
                return l;
            }
        }

        /// <summary>Blend every field of two looks (MV-849 — the World 1 -> World 2 joining
        /// corridor). Every <c>Color</c>/<c>float</c>/<c>Vector3</c> field gets a plain
        /// linear interpolation; nothing here is hand-picked, because a field this struct grows
        /// later should blend for free rather than silently staying at <paramref name="a"/>'s
        /// value until someone remembers to add it here too.</summary>
        public static BackyardLook Lerp(BackyardLook a, BackyardLook b, float t) => new BackyardLook
        {
            KeyColor = Color.Lerp(a.KeyColor, b.KeyColor, t),
            KeyIntensity = Mathf.Lerp(a.KeyIntensity, b.KeyIntensity, t),
            KeyEuler = Vector3.Lerp(a.KeyEuler, b.KeyEuler, t),
            ShadowStrength = Mathf.Lerp(a.ShadowStrength, b.ShadowStrength, t),

            FillColor = Color.Lerp(a.FillColor, b.FillColor, t),
            FillIntensity = Mathf.Lerp(a.FillIntensity, b.FillIntensity, t),
            FillEuler = Vector3.Lerp(a.FillEuler, b.FillEuler, t),

            RimColor = Color.Lerp(a.RimColor, b.RimColor, t),
            RimIntensity = Mathf.Lerp(a.RimIntensity, b.RimIntensity, t),
            RimEuler = Vector3.Lerp(a.RimEuler, b.RimEuler, t),

            AmbientSky = Color.Lerp(a.AmbientSky, b.AmbientSky, t),
            AmbientEquator = Color.Lerp(a.AmbientEquator, b.AmbientEquator, t),
            AmbientGround = Color.Lerp(a.AmbientGround, b.AmbientGround, t),

            FogColor = Color.Lerp(a.FogColor, b.FogColor, t),
            FogDensity = Mathf.Lerp(a.FogDensity, b.FogDensity, t),

            SkyZenith = Color.Lerp(a.SkyZenith, b.SkyZenith, t),
            SkyHorizon = Color.Lerp(a.SkyHorizon, b.SkyHorizon, t),
            SkyGroundHaze = Color.Lerp(a.SkyGroundHaze, b.SkyGroundHaze, t),
            SkySun = Color.Lerp(a.SkySun, b.SkySun, t),
            SkyCloud = Color.Lerp(a.SkyCloud, b.SkyCloud, t),
            SkySunGlow = Mathf.Lerp(a.SkySunGlow, b.SkySunGlow, t),
            SkySunIntensity = Mathf.Lerp(a.SkySunIntensity, b.SkySunIntensity, t),
            SkyCloudAmount = Mathf.Lerp(a.SkyCloudAmount, b.SkyCloudAmount, t),
            SkyCloudScale = Mathf.Lerp(a.SkyCloudScale, b.SkyCloudScale, t),

            AoIntensity = Mathf.Lerp(a.AoIntensity, b.AoIntensity, t),
            AoRadius = Mathf.Lerp(a.AoRadius, b.AoRadius, t),

            PostExposure = Mathf.Lerp(a.PostExposure, b.PostExposure, t),
            Contrast = Mathf.Lerp(a.Contrast, b.Contrast, t),
            Saturation = Mathf.Lerp(a.Saturation, b.Saturation, t),
            ColorFilter = Color.Lerp(a.ColorFilter, b.ColorFilter, t),
            ShadowTint = Color.Lerp(a.ShadowTint, b.ShadowTint, t),
            HighlightTint = Color.Lerp(a.HighlightTint, b.HighlightTint, t),

            BloomThreshold = Mathf.Lerp(a.BloomThreshold, b.BloomThreshold, t),
            BloomIntensity = Mathf.Lerp(a.BloomIntensity, b.BloomIntensity, t),
            BloomScatter = Mathf.Lerp(a.BloomScatter, b.BloomScatter, t),
            BloomTint = Color.Lerp(a.BloomTint, b.BloomTint, t),

            VignetteIntensity = Mathf.Lerp(a.VignetteIntensity, b.VignetteIntensity, t),
            VignetteSmoothness = Mathf.Lerp(a.VignetteSmoothness, b.VignetteSmoothness, t),
            FilmGrain = Mathf.Lerp(a.FilmGrain, b.FilmGrain, t),
        };
    }
}
