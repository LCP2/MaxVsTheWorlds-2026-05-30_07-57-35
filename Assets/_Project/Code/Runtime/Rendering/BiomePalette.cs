using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// The surfaces a biome's material set covers.
    ///
    /// The first three are classified by SHAPE (see <see cref="WorldMaterials.KindOf"/>) — that is
    /// all the greybox can tell you about itself. The rest (YT-77) are classified by what a thing is
    /// actually MADE of, which is knowledge only the thing's source has: the garden kit names its own
    /// materials (`kit_wood`, `kit_stone`, …) and the factory is a machine. Shape can't distinguish a
    /// timber paling from a stone paver — both are just "a box" — so material identity has to come
    /// from somewhere shape isn't.
    /// </summary>
    public enum SurfaceKind
    {
        Ground,
        Wall,
        Prop,

        // --- YT-77: what a surface is made of ---
        Wood,
        Stone,
        Dirt,
        Metal,
        Foliage,
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

        // --- YT-77: the biome's material tones ---
        public Color Wood;
        public Color Stone;
        public Color Dirt;
        public Color Metal;
        public Color Foliage;

        /// <summary>The third ground tone: sun-bleached, drier turf. The macro pass drifts toward
        /// this across the yard, so the variation is a HUE shift as well as a value one — a lawn
        /// that only varies in brightness still reads as one material under a coloured light.</summary>
        public Color GroundDry;

        /// <summary>Grass texture repeats per METRE of world (YT-69), not per UV. The floor is two
        /// objects — a 30 m scene plane and a 34 m box spawned by BackyardPath — whose UVs are
        /// scaled differently, so a UV-space tiling factor makes the grass change size across the
        /// seam between them. Scaling off world position is immune to that, and to gameplay
        /// reshaping the arena (YT-68).</summary>
        public float GroundDetailScale;

        /// <summary>Wavelength of the across-the-yard variation, in cycles per metre. Low: this is
        /// the "not one uniform slab" knob, and it wants to be measured in metres, not centimetres.</summary>
        public float GroundMacroScale;

        /// <summary>How far the dry patches drift toward <see cref="GroundDry"/> (0 = a uniform
        /// lawn again).</summary>
        public float GroundMacroStrength;

        /// <summary>How deeply the lush patches sit below the base tone. Together with the dry drift
        /// this is the whole macro range: lush/shaded at one end, dry/bleached at the other.</summary>
        public float GroundLushShade;

        /// <summary>How hard the light breaks across the grass relief. 0 = back to a flat fill.</summary>
        public float GroundNormalStrength;

        /// <summary>Size of the turf clumps, in cycles per metre. Generated in world space rather
        /// than baked into the tiling texture, because clump scale is precisely the scale at which a
        /// repeat becomes visible — a metre-wide blotch appearing on a grid is what makes a floor
        /// read as wallpaper.</summary>
        public float GroundClumpScale;

        /// <summary>How pronounced the clumping is.</summary>
        public float GroundClumpDepth;

        /// <summary>How far the blades LEAN, in metres, at the peak of a gust (YT-78). The lawn is a
        /// flat plane and cannot sway, so the wind moves where its grass texture is sampled instead —
        /// which leans every blade in the yard. Zero for a biome with no weather in it.</summary>
        public float GroundWindLean;

        /// <summary>How fast a gust crosses the yard.</summary>
        public float GroundWindSpeed;

        /// <summary>How much the grass lightens and darkens as it leans. A blade turning its edge to
        /// the sun is darker than one turning its face; without this the lawn slides rather than
        /// moves.</summary>
        public float GroundWindShimmer;

        /// <summary>How many times the ground texture repeats across the plane. Only used by the
        /// URP/Lit fallback path, for when the stylised ground shader is unavailable.</summary>
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
        ///
        /// YT-69, second pass: the colour was right and the floor still read as artificial, because
        /// colour was ALL it had. A flat plane wearing a picture of grass has no light response, so
        /// it looks like a fill. The tones below are unchanged — the surface underneath them is not.
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
            // Sun-bleached turf. Warmer and paler than the accent, but still green-dominant — the
            // "vomit" was made of exactly this kind of drift, so the dry tone is allowed to go
            // strawy WITHOUT being allowed to go mustard. The tests hold that line.
            GroundDry = new Color(0.36f, 0.50f, 0.20f),
            Wall = new Color(0.36f, 0.27f, 0.18f),           // fence/soil browns
            Prop = new Color(0.46f, 0.44f, 0.40f),

            // YT-77. Tones for the greybox's own materials, sitting deliberately alongside the garden
            // kit's (KitImport's colour table) rather than beside them: a greybox wall and a kit fence
            // panel stand in the same fence line, and two different browns in one fence is worse than
            // no wood grain at all. Every one of these is under the 0.6 sunlit ceiling — at a 1.8×
            // key, anything past that stops being a colour and becomes a highlight.
            Wood = new Color(0.34f, 0.24f, 0.15f),           // == the kit's `wood`
            Stone = new Color(0.36f, 0.35f, 0.33f),          // == the kit's `stone`
            Dirt = new Color(0.26f, 0.19f, 0.12f),           // == the kit's `dirt`
            // Weathered galvanised steel. Only ever reached by something NOT tinted by gameplay — the
            // Mower Hutch takes the wear pattern without the colour (MaterialLibrary.Wear), because
            // its hazard-orange is gameplay's to own.
            Metal = new Color(0.40f, 0.41f, 0.43f),
            Foliage = new Color(0.24f, 0.45f, 0.17f),        // == the kit's `leafsGreen`

            // One tile per ~1.8 m. Fine enough that a blade-scale streak is a few centimetres, coarse
            // enough that the mip chain still has something to average at the far end of the yard.
            GroundDetailScale = 0.55f,
            // ~17 m per cycle: one or two big patches across the arena, which is the scale the eye
            // reads as "a lawn with history" rather than "a texture".
            GroundMacroScale = 0.06f,
            GroundMacroStrength = 0.35f,
            GroundLushShade = 0.78f,
            GroundNormalStrength = 0.85f,
            GroundClumpScale = 0.65f,       // tufts about 1.5 m across
            GroundClumpDepth = 0.12f,

            // A breeze, not a storm. 6 cm of lean is about a blade's width at this scale — enough that
            // the lawn is visibly moving from thirty metres up, nowhere near enough to pull the eye off
            // a telegraph. The gust rolls across the yard roughly every eight seconds.
            GroundWindLean = 0.06f,
            GroundWindSpeed = 0.9f,
            GroundWindShimmer = 0.045f,

            GroundTiling = 5f,                               // fallback path only
            Smoothness = 0.06f,
        };

        public Color ColorFor(SurfaceKind kind)
        {
            Color c;
            switch (kind)
            {
                case SurfaceKind.Ground: c = GroundBase; break;
                case SurfaceKind.Wall: c = Wall; break;
                case SurfaceKind.Wood: c = Wood; break;
                case SurfaceKind.Stone: c = Stone; break;
                case SurfaceKind.Dirt: c = Dirt; break;
                case SurfaceKind.Metal: c = Metal; break;
                case SurfaceKind.Foliage: c = Foliage; break;
                default: c = Prop; break;
            }
            return c * Tint;
        }
    }
}
