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
        private static BiomePalette s_palette = BiomePalette.Backyard;

        /// <summary>The biome currently in force. Setting it clears the cache, so the single
        /// <see cref="BiomePalette.Tint"/> tunable re-colours the whole arena on the next build.</summary>
        public static BiomePalette Palette
        {
            get => s_palette;
            set { s_palette = value; Clear(); }
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
            s_cache[key] = m;
            return m;
        }

        /// <summary>
        /// The same surface, recoloured toward an element — the hook that makes future
        /// enemy/world recolours data-driven instead of hand-made. See docs/ELEMENTAL_VARIANTS.md.
        /// </summary>
        public static Material Variant(SurfaceKind kind, Element element)
        {
            string key = $"{kind}:{element}";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            bool isGround = kind == SurfaceKind.Ground;

            // The ground gets the shader that can actually make it look like ground. Everything else
            // stays on plain URP/Lit: a wall doesn't need grass relief or a macro pass.
            var shader = isGround ? GroundShader ?? SurfaceShader : SurfaceShader;
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

            // The surface's two tones, both recoloured toward the element so a variant keeps its
            // internal contrast instead of collapsing to one flat colour.
            Color lo = ElementPalette.Recolor(s_palette.ColorFor(kind), element);
            Color hi = ElementPalette.Recolor(
                isGround ? s_palette.GroundAccent * s_palette.Tint : s_palette.ColorFor(kind) * 1.18f,
                element);

            var mask = isGround ? StylizedTextures.Ground() : StylizedTextures.Surface();
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
            else
            {
                float tiling = isGround ? Mathf.Max(1f, s_palette.GroundTiling) : 1f;
                m.mainTextureScale = new Vector2(tiling, tiling);
            }

            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", s_palette.Smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", s_palette.Smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);

            s_cache[key] = m;
            return m;
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

            // The set-dressing's materials are tinted by the same palette, so they go with it —
            // otherwise a biome change re-colours the arena and leaves the fence in the old one.
            KitMaterials.Clear();
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
