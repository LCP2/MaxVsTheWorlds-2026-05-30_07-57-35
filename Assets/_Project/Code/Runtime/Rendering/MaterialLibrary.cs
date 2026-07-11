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

        /// <summary>
        /// The same surface, recoloured toward an element — the hook that makes future
        /// enemy/world recolours data-driven instead of hand-made. See docs/ELEMENTAL_VARIANTS.md.
        /// </summary>
        public static Material Variant(SurfaceKind kind, Element element)
        {
            string key = $"{kind}:{element}";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var shader = SurfaceShader;
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

            bool isGround = kind == SurfaceKind.Ground;

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

            float tiling = isGround ? Mathf.Max(1f, s_palette.GroundTiling) : 1f;
            m.mainTextureScale = new Vector2(tiling, tiling);

            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", s_palette.Smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", s_palette.Smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);

            s_cache[key] = m;
            return m;
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
