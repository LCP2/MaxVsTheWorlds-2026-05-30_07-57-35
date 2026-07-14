using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// Builds and caches the stylised URP surface materials (YT-50).
    ///
    /// Materials are created in code from <see cref="BiomePalette"/> + <see cref="StylizedTextures"/>
    /// rather than committed as .mat assets, so a fresh clone and CI produce identical surfaces
    /// with nothing to hand-wire. Everything is URP/Lit, which matters: gameplay tints renderers
    /// with a MaterialPropertyBlock on <c>_BaseColor</c> (hit flashes, factory damage state), and
    /// that keeps working on these materials exactly as it did on the default ones.
    ///
    /// Variants are cached per (kind, element), so recolouring is a lookup, not an allocation.
    /// </summary>
    public static class MaterialLibrary
    {
        private static readonly string[] ShaderChain =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Standard",
        };

        private static readonly Dictionary<string, Material> s_cache = new Dictionary<string, Material>();
        private static Shader s_shader;
        private static Shader s_groundShader;
        private static bool s_groundShaderResolved;
        private static Shader s_stylizedSurface;
        private static bool s_surfaceShaderResolved;
        private static BiomePalette s_palette = BiomePalette.Backyard;

        /// <summary>
        /// The biome currently in force. Changing it clears the cache, so the single
        /// <see cref="BiomePalette.Tint"/> tunable re-colours the whole arena on the next build.
        ///
        /// Setting it to the palette it ALREADY holds is not a change, and deliberately does nothing.
        /// That is not an optimisation — it is a correctness fix (YT-77). Clear() destroys every
        /// cached material, and a material that is destroyed while a renderer is still pointing at it
        /// renders MAGENTA. Both <see cref="WorldMaterials"/> and the yard's kit dressing install
        /// themselves at AfterSceneLoad, in an order neither controls; whichever ran second used to
        /// re-assert the same Backyard palette, wipe the cache, and take the other one's materials
        /// down with it. The kit props lost every material they had just been given and the whole
        /// garden came up magenta in the player while looking perfectly correct in the editor.
        /// </summary>
        public static BiomePalette Palette
        {
            get => s_palette;
            set
            {
                // Bitwise struct equality: BiomePalette is all floats and Colors, and both sides of
                // this comparison come from the same static preset, so the bits match exactly.
                if (s_palette.Equals(value)) return;

                s_palette = value;
                Clear();
            }
        }

        public static Shader SurfaceShader
        {
            get
            {
                if (s_shader != null) return s_shader;
                foreach (var name in ShaderChain)
                {
                    var sh = Shader.Find(name);
                    if (sh != null && sh.isSupported) { s_shader = sh; break; }
                }
                Debug.Log($"[MaterialLibrary] surface shader: {(s_shader == null ? "NONE" : s_shader.name)}");
                return s_shader;
            }
        }

        /// <summary>The stylised material for a surface, in the current biome.</summary>
        public static Material Surface(SurfaceKind kind) => Variant(kind, Element.Neutral);

        /// <summary>Name of the hand-written character shader (YT-57). Kept in Graphics Settings'
        /// Always Included Shaders, since nothing but Shader.Find references it.</summary>
        public const string CharacterShaderName = "MaxWorlds/StylizedCharacter";

        /// <summary>Name of the hand-written ground shader (YT-69) — grass relief plus the
        /// across-the-yard macro variation. Same deal: nothing but Shader.Find references it, so it
        /// lives in Always Included Shaders or the build strips it.</summary>
        public const string GroundShaderName = "MaxWorlds/StylizedGround";

        /// <summary>Name of the hand-written surface shader (YT-77) — world-space triplanar wood,
        /// stone, dirt and painted metal. Also Shader.Find-only, so also in Always Included Shaders.
        /// </summary>
        public const string StylizedSurfaceShaderName = "MaxWorlds/StylizedSurface";

        /// <summary>
        /// The stylised character material — outline, rim light, dissolve (YT-57).
        ///
        /// Shared by every character. Per-body state (the dissolve amount, hit-flash tints) is set
        /// through MaterialPropertyBlocks, so one material serves the whole roster without an
        /// instance per enemy.
        /// </summary>
        public static Material Character()
        {
            const string key = "character";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var shader = Shader.Find(CharacterShaderName);
            if (shader == null || !shader.isSupported)
            {
                // Degrade to the plain lit look rather than to the magenta error shader: losing the
                // outline is a cosmetic regression, rendering nothing is a broken build.
                Debug.LogWarning($"[MaterialLibrary] '{CharacterShaderName}' unavailable; characters keep the plain lit material.");
                return null;
            }

            var m = new Material(shader)
            {
                name = "Stylized_Character",
                hideFlags = HideFlags.HideAndDontSave,
            };
            m.SetColor("_BaseColor", Color.white);   // gameplay's MaterialPropertyBlock tints drive this

            // THE EDGE (YT-86). Set here rather than left to the shader's defaults, because this is
            // the one place a human or an AI would come looking to turn the actors up, and a number
            // that only exists inside an HLSL Properties block is a number nobody will ever find.
            //
            // The rim is the readability workhorse at a fixed top-down camera: a character's SILHOUETTE
            // is most of what you can see of it, so lighting the edge is what separates it from the
            // ground. It was warm and gentle, which is the one thing it must not be in this yard — the
            // whole scene is lit by a warm key, so a warm rim on a small body just reads as more
            // sunlight. It is now bright and slightly cool, and it is loud.
            m.SetColor("_RimColor", RimColor);
            m.SetFloat("_RimStrength", RimStrength);
            m.SetFloat("_RimPower", RimPower);

            // The outline is a screen-space extrusion, so it holds the same PIXEL width however far
            // away the camera is — which is exactly what the ticket asks for ("actors stay crisp when
            // the camera is zoomed out", YT-82). It only has to be thick enough to survive a phone.
            m.SetColor("_OutlineColor", OutlineColor);
            m.SetFloat("_OutlineWidth", OutlineWidth);

            s_cache[key] = m;
            return m;
        }

        /// <summary>Bright and slightly COOL. The Backyard's key light is warm, so a warm rim on a
        /// 20-pixel body reads as sunlight rather than as an edge; a cool one reads as an edge.</summary>
        private static readonly Color RimColor = new Color(0.86f, 0.94f, 1f);

        /// <summary>Loud. It was 0.55 and the actors read as flat blobs at gameplay zoom.</summary>
        private const float RimStrength = 1.25f;

        /// <summary>
        /// How TIGHT the rim is to the silhouette. Higher is narrower.
        ///
        /// This is the number that has to be right, and the first cut got it badly wrong: at 2.2 the
        /// rim was a wide wash rather than an edge, and on a body seen from almost directly above it
        /// spilled across most of the visible surface. Max stopped being orange and became a pale
        /// smear with an orange middle — the rim ate the very colour it exists to frame.
        ///
        /// An edge is an EDGE. Keep it hard against the silhouette, and let the albedo own the body.
        /// </summary>
        private const float RimPower = 4.6f;

        /// <summary>Near-black, and not pure black: a true black edge against a dark bruiser is no edge
        /// at all, and the whole point of the line is that it works on ANY background.</summary>
        private static readonly Color OutlineColor = new Color(0.05f, 0.05f, 0.07f);

        /// <summary>Screen-space, so it is a constant number of pixels at any zoom. Chunky on purpose
        /// — this is the Brawl Stars idea and Brawl Stars does not draw hairlines.</summary>
        private const float OutlineWidth = 0.013f;

        /// <summary>
        /// The same surface, recoloured toward an element — the hook that makes future
        /// enemy/world recolours data-driven instead of hand-made. See docs/ELEMENTAL_VARIANTS.md.
        /// </summary>
        public static Material Variant(SurfaceKind kind, Element element)
        {
            string key = $"{kind}:{element}";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var m = Build(key, kind, ElementPalette.Recolor(s_palette.ColorFor(kind), element), element);
            if (m == null) return null;

            s_cache[key] = m;
            return m;
        }

        /// <summary>
        /// A surface of <paramref name="kind"/> carrying someone else's colour (YT-77).
        ///
        /// This is how the garden kit gets its grain. The kit's own materials already hold the tones
        /// YT-75 chose for it — timber, soil, stone, painted flowers, the mint-turquoise foliage that
        /// had to be pulled back to green — and those decisions are not this ticket's to revisit. So
        /// the tone comes in from the kit and only the SURFACE is ours: the same wood that the yard's
        /// walls are made of, wearing the kit's brown.
        /// </summary>
        public static Material Tinted(SurfaceKind kind, Color tone)
        {
            // Quantised into the key, or a stray float in a kit colour would mint a new material —
            // and a new material per prop is a new draw call per prop. 217 of them share ~16 tones.
            string key = $"tint:{kind}:{Mathf.RoundToInt(tone.r * 255f):X2}" +
                         $"{Mathf.RoundToInt(tone.g * 255f):X2}{Mathf.RoundToInt(tone.b * 255f):X2}";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var m = Build(key, kind, tone, Element.Neutral);
            if (m == null) return null;

            s_cache[key] = m;
            return m;
        }

        private static Material Build(string key, SurfaceKind kind, Color tone, Element element)
        {
            bool isGround = kind == SurfaceKind.Ground;

            // Three shaders, in order of how much they know about the surface. The ground gets the one
            // that can make grass; everything else gets the triplanar one that can make timber and
            // stone; if neither survived into the build, plain URP/Lit still renders a correctly
            // coloured — if flat — yard. A look regression, never a magenta one (YT-58).
            var shader = isGround ? GroundShader ?? SurfaceShader
                                  : StylizedSurfaceShader ?? SurfaceShader;
            if (shader == null)
            {
                Debug.LogWarning("[MaterialLibrary] no usable surface shader; greybox keeps its default material.");
                return null;
            }

            var m = new Material(shader)
            {
                name = $"Stylized_{key}",
                hideFlags = HideFlags.HideAndDontSave,
            };

            SurfaceProfile p = ProfileFor(kind);

            // The surface's two tones. Spread SYMMETRICALLY around the tone it was given, so the mean
            // albedo still IS that tone: a kit colour that survives this pass renders at the value
            // YT-75 picked for it, with grain through it, rather than being quietly brightened by the
            // act of texturing it. Both ends recoloured toward the element, so a variant keeps its
            // internal contrast instead of collapsing to one flat colour.
            Color lo, hi;
            if (isGround)
            {
                lo = ElementPalette.Recolor(s_palette.ColorFor(kind), element);
                hi = ElementPalette.Recolor(s_palette.GroundAccent * s_palette.Tint, element);
            }
            else
            {
                lo = ElementPalette.Recolor(tone * (1f - p.Contrast), element);
                // Under a 1.8x key, an albedo past the ceiling stops being a colour and becomes a
                // highlight — the bug YT-75's follow-up found painting the fence cream. The bright end
                // of a grain is the first thing to cross it, so it is clamped HERE, at the point the
                // texture is baked, and not left to a reviewer's eye.
                hi = ElementPalette.Recolor(SunlitAlbedo.Clamp(tone * (1f + p.Contrast)), element);
            }

            var mask = isGround ? StylizedTextures.Ground() : StylizedTextures.MaskFor(kind);
            var tex = StylizedTextures.Blend($"albedo:{key}", mask, lo, hi);
            SetTexture(m, tex);

            // The albedo carries the colour, so _BaseColor is just the biome tint (white by
            // default) — a coloured _BaseColor here would multiply the tones a second time.
            SetColor(m, s_palette.Tint);

            if (isGround && shader == GroundShader)
            {
                // World-space scaled: the ground shader doesn't read mesh UVs at all.
                DressGround(m, element);
            }
            else if (shader == StylizedSurfaceShader)
            {
                DressSurface(m, kind, p);
            }
            else
            {
                // URP/Lit fallback. Mesh UVs are all it can read, so the grain lands wherever the kit's
                // atlas UVs happen to point — wrong, but lit and correctly coloured.
                float tiling = isGround ? Mathf.Max(1f, s_palette.GroundTiling) : 1f;
                m.mainTextureScale = new Vector2(tiling, tiling);
                var normal = StylizedTextures.NormalFor(kind);
                if (normal != null && m.HasProperty("_BumpMap"))
                {
                    m.SetTexture("_BumpMap", normal);
                    m.EnableKeyword("_NORMALMAP");   // URP/Lit ignores the map without it
                }
            }

            float smoothness = isGround ? s_palette.Smoothness : p.Smoothness;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);

            // No GPU instancing. It buys nothing here: the kit props are static-batched
            // (BackyardDressing), and static batching and instancing are mutually exclusive, so the
            // flag was doing no work on the only 217 objects in the yard that could have used it.
            //
            // It is explicitly NOT off because of the Mower Hutch bug. Instancing was my first suspect
            // when a property-blocked renderer on this shader came out four times too dark, and
            // turning it off changed precisely nothing — the renderer was still wrong. Whatever that
            // interaction is, it is between the property block and this shader, and it is written up
            // where it is actually handled, in CharacterSkin.IsMachine. A wrong cause left in a comment
            // is worse than no comment: the next person to hit this would spend their day here.
            m.enableInstancing = false;
            return m;
        }

        /// <summary>Hand the triplanar surface shader its grain and its relief.</summary>
        private static void DressSurface(Material m, SurfaceKind kind, SurfaceProfile p)
        {
            var normal = StylizedTextures.NormalFor(kind);
            if (normal != null) m.SetTexture("_BumpMap", normal);

            m.SetFloat("_DetailScale", p.DetailScale);
            m.SetFloat("_NormalStrength", p.NormalStrength);

            // Wind (YT-78). Zero on everything that isn't a plant — the yard's WALLS wear this shader,
            // and a fence that breathes is a bug, not ambience.
            m.SetFloat("_WindStrength", p.Wind);

            // How tall a plant has to be to bend all the way. Set here rather than left to the
            // shader's default, and it is half the reason the first cut was invisible: at 2.5 m the
            // curve was scaled for a tree, and the yard is mostly shrubs and tufts a knee high. They
            // reached a fifth of the sway they were given. This is the height of the plants that are
            // actually in the yard, so the plants that are actually in the yard actually bend.
            m.SetFloat("_WindHeight", FoliageBendHeight);
        }

        /// <summary>Metres of height over which a plant works up to its full bend (YT-78). The yard's
        /// greenery is shrubs, tufts and flower beds, not a forest — a bend curve scaled for a 2.5 m
        /// tree left all of them nearly still.</summary>
        public const float FoliageBendHeight = 1.4f;

        /// <summary>What a material is physically like, as opposed to what colour the biome painted
        /// it. Timber and stone are the same brown-ish family in this yard and are told apart by the
        /// size of their grain and the way they take the light, not by hue.</summary>
        private readonly struct SurfaceProfile
        {
            public readonly float DetailScale;      // tiles per metre of world
            public readonly float NormalStrength;
            public readonly float Smoothness;
            public readonly float Contrast;         // how far the grain swings either side of the tone
            public readonly float Wind;             // metres of sway at full height; 0 = it doesn't move

            public SurfaceProfile(float detailScale, float normalStrength, float smoothness,
                                  float contrast, float wind = 0f)
            {
                DetailScale = detailScale;
                NormalStrength = normalStrength;
                Smoothness = smoothness;
                Contrast = contrast;
                Wind = wind;
            }
        }

        private static SurfaceProfile ProfileFor(SurfaceKind kind)
        {
            switch (kind)
            {
                // Timber. ~14 cm palings at this scale (the mask cuts 5 planks per tile), and matte:
                // a shiny fence is a plastic fence.
                case SurfaceKind.Wood:
                case SurfaceKind.Wall:
                    return new SurfaceProfile(1.3f, 1.0f, 0.08f, 0.22f);

                // Slabs about 35 cm across. The one surface allowed any sheen — wet-ish stone is what
                // stone looks like, and the stepping stones sit flat under the sun where it reads.
                case SurfaceKind.Stone:
                    return new SurfaceProfile(0.4f, 1.1f, 0.16f, 0.20f);

                // Soil is the roughest thing in the yard, so it gets the deepest relief.
                case SurfaceKind.Dirt:
                    return new SurfaceProfile(1.2f, 1.25f, 0.02f, 0.26f);

                // Panels about a metre across, to match the Hutch's 3 m body. Weathered paint, so a
                // little more sheen than timber and nowhere near a mirror.
                case SurfaceKind.Metal:
                    return new SurfaceProfile(0.45f, 0.8f, 0.28f, 0.20f);

                // Leaves: soft and round. Gentle — a carved-looking bush is worse than a flat one.
                //
                // The one surface in the yard that MOVES (YT-78), and the number is now argued in the
                // only unit that decides whether a thing moves: PIXELS ON LEE'S SCREEN.
                //
                // At the rig the game is played from — 25.1 m back, 40 deg vertical FOV — a metre of
                // lawn is about 59 pixels tall on a 1080p frame. The sway that shipped was 11 cm at
                // FULL bend, and the bend was normalised over 2.5 m of height, so an ordinary knee-high
                // shrub reached about a fifth of it: two centimetres, one pixel and a half, a whole
                // wind you could not see. I shipped that and called it done because I had measured
                // "pixels that changed at all" rather than "pixels that changed enough to notice".
                //
                // 26 cm, normalised over 1.4 m — the height of the yard's actual shrubs rather than of
                // an imaginary tree — puts the top of a typical bush through about 13 px of travel and
                // a tree canopy through about 20. That is a plant in a breeze. It is still nowhere near
                // a telegraph, which is a hard ring that appears; this is a slow lean that never
                // resolves into an edge. WindTests holds both ends of that in pixels, not in metres.
                case SurfaceKind.Foliage:
                    return new SurfaceProfile(2.0f, 0.65f, 0.05f, 0.14f, wind: 0.26f);

                default:
                    return new SurfaceProfile(1.0f, 0.5f, 0.06f, 0.10f);
            }
        }

        /// <summary>The hand-written ground shader, or null if it isn't in this build. Resolved once
        /// and remembered — a failed Shader.Find is remembered too, via the sentinel.</summary>
        public static Shader GroundShader
        {
            get
            {
                if (s_groundShaderResolved) return s_groundShader;
                s_groundShaderResolved = true;

                var sh = Shader.Find(GroundShaderName);
                if (sh == null || !sh.isSupported)
                {
                    // Degrade to the flat-but-correct URP/Lit ground rather than to magenta (YT-58).
                    // The floor loses its relief; it does not lose its colour or disappear.
                    Debug.LogWarning($"[MaterialLibrary] '{GroundShaderName}' unavailable; " +
                                     "the ground falls back to plain lit (no grass relief).");
                    return null;
                }

                Debug.Log($"[MaterialLibrary] ground shader: {sh.name}");
                s_groundShader = sh;
                return s_groundShader;
            }
        }

        /// <summary>The hand-written triplanar surface shader (YT-77), or null if it isn't in this
        /// build. Resolved once and remembered — a failed Shader.Find is remembered too, via the
        /// sentinel.</summary>
        public static Shader StylizedSurfaceShader
        {
            get
            {
                if (s_surfaceShaderResolved) return s_stylizedSurface;
                s_surfaceShaderResolved = true;

                var sh = Shader.Find(StylizedSurfaceShaderName);
                if (sh == null || !sh.isSupported)
                {
                    // Degrade to flat-but-correct URP/Lit rather than to magenta (YT-58). The yard
                    // loses its grain; it does not lose its colour or disappear.
                    Debug.LogWarning($"[MaterialLibrary] '{StylizedSurfaceShaderName}' unavailable; " +
                                     "surfaces fall back to plain lit (no wood/stone grain).");
                    return null;
                }

                Debug.Log($"[MaterialLibrary] stylized surface shader: {sh.name}");
                s_stylizedSurface = sh;
                return s_stylizedSurface;
            }
        }

        /// <summary>Hand the ground shader the grass relief and the macro-variation knobs. The
        /// albedo and the normal map come from one shared height field, so the light breaks where
        /// the paint is dark (see <see cref="StylizedTextures"/>).</summary>
        private static void DressGround(Material m, Element element)
        {
            m.SetTexture("_BumpMap", StylizedTextures.GroundNormal());

            m.SetFloat("_DetailScale", Mathf.Max(0.01f, s_palette.GroundDetailScale));
            m.SetFloat("_NormalStrength", s_palette.GroundNormalStrength);
            m.SetFloat("_MacroScale", s_palette.GroundMacroScale);
            m.SetFloat("_MacroStrength", s_palette.GroundMacroStrength);
            m.SetFloat("_LushShade", s_palette.GroundLushShade);
            m.SetFloat("_ClumpScale", s_palette.GroundClumpScale);
            m.SetFloat("_ClumpDepth", s_palette.GroundClumpDepth);

            // Wind across the lawn (YT-78). The lawn is most of what is on the screen, so this is most
            // of what makes the yard stop looking like a photograph.
            m.SetFloat("_WindStrength", s_palette.GroundWindLean);
            m.SetFloat("_WindSpeed", s_palette.GroundWindSpeed);
            m.SetFloat("_WindShimmer", s_palette.GroundWindShimmer);

            // The dry tone follows the element too, or an ice-variant lawn would drift toward a
            // strawy green in its dry patches and the variant would read as two materials.
            m.SetColor("_DryColor", ElementPalette.Recolor(s_palette.GroundDry * s_palette.Tint, element));
        }

        /// <summary>Drop every cached material — call after changing the palette.</summary>
        public static void Clear()
        {
            foreach (var m in s_cache.Values)
            {
                if (m == null) continue;
                if (Application.isPlaying) Object.Destroy(m);
                else Object.DestroyImmediate(m);
            }
            s_cache.Clear();

            // The albedos are baked FROM the palette's colours, so a stale texture cache would hand
            // the new materials the old lawn back. Materials first, then the textures they held.
            StylizedTextures.Clear();
        }

        private static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);   // URP
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);           // built-in fallback
        }

        private static void SetTexture(Material m, Texture2D tex)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        }
    }
}
