using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Particle materials + their textures, built in code and cached by key — the
    /// <see cref="MaxWorlds.UI.HudTextures"/> idiom applied to VFX, per the
    /// code-driven / no-committed-art rule.
    ///
    /// This exists because a ParticleSystem created with AddComponent gets
    /// <c>sharedMaterial == null</c> (Unity only assigns the default material when a
    /// component is added through the Inspector). Every effect must therefore assign
    /// its own material or it renders as nothing. The shader is resolved by name at
    /// runtime, so the URP particle shader is kept in Graphics Settings' Always
    /// Included Shaders (see MaxWorlds.Editor.VfxShaderInclude) — otherwise the
    /// player build strips it and Shader.Find returns null.
    /// </summary>
    public static class VfxMaterials
    {
        /// <summary>Shader used for every particle material, resolved once. Falls back down a
        /// chain so a stripped/renamed URP shader degrades to something visible rather than
        /// to the magenta error shader.</summary>
        private static readonly string[] ShaderChain =
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Sprites/Default",
            "Unlit/Transparent",
        };

        private static readonly Dictionary<string, Material> s_materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Texture2D> s_textures = new Dictionary<string, Texture2D>();
        private static Shader s_shader;

        /// <summary>The particle shader in use. Null only if every fallback is missing.</summary>
        public static Shader ParticleShader
        {
            get
            {
                if (s_shader != null) return s_shader;
                foreach (var name in ShaderChain)
                {
                    var sh = Shader.Find(name);
                    if (sh != null && sh.isSupported) { s_shader = sh; break; }
                }
                // Logged once: this is the line that tells you, from a player log alone,
                // whether the build kept the particle shader or stripped it.
                Debug.Log($"[VfxMaterials] particle shader: {(s_shader == null ? "NONE (VFX will not render)" : s_shader.name)}");
                return s_shader;
            }
        }

        /// <summary>Additive, soft-edged — glows, foam, the muzzle flash. Reads bright against
        /// the Backyard's golden/grass palette.</summary>
        public static Material Additive(Texture2D tex) => Get("add:" + tex.name, tex, additive: true);

        /// <summary>Alpha-blended — droplets and spray that should read as water volume, not light.</summary>
        public static Material AlphaBlend(Texture2D tex) => Get("alpha:" + tex.name, tex, additive: false);

        // --- textures ---

        /// <summary>Round droplet: opaque core, soft falloff. The workhorse water particle.</summary>
        public static Texture2D Droplet(int size = 64)
        {
            return Tex($"droplet{size}", size, (nx, ny) =>
            {
                float d = Mathf.Sqrt(nx * nx + ny * ny) * 2f;   // 0 centre -> 1 edge
                if (d >= 1f) return 0f;
                // Solid core out to 55%, then a smooth shoulder — keeps droplets legible
                // when they are only a few pixels across at the ~72° camera distance.
                return d <= 0.55f ? 1f : Mathf.SmoothStep(1f, 0f, (d - 0.55f) / 0.45f);
            });
        }

        /// <summary>Soft radial glow, no hard core — additive flashes and the muzzle bloom.</summary>
        public static Texture2D Glow(int size = 64)
        {
            return Tex($"glow{size}", size, (nx, ny) =>
            {
                float d = Mathf.Clamp01(Mathf.Sqrt(nx * nx + ny * ny) * 2f);
                float a = 1f - d;
                return a * a;                                   // quadratic falloff
            });
        }

        // --- internals ---

        private static Material Get(string key, Texture2D tex, bool additive)
        {
            if (s_materials.TryGetValue(key, out var m) && m != null) return m;

            var shader = ParticleShader;
            if (shader == null)
            {
                Debug.LogWarning("[VfxMaterials] no usable particle shader found; VFX will not render.");
                return null;
            }

            m = new Material(shader)
            {
                name = key,
                hideFlags = HideFlags.HideAndDontSave,
                // Transparent, never depth-writing: particles must not occlude each other
                // or punch holes in the scene.
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent,
            };

            // URP's particle shader is configured through properties + keywords rather
            // than through a preset blend mode, so set both or the surface stays opaque.
            SetTexture(m, tex);
            m.SetColor("_BaseColor", Color.white);   // white base: startColor drives the tint
            m.SetColor("_Color", Color.white);       // legacy fallback shaders
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);          // 0 opaque, 1 transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", additive ? 2f : 0f); // 2 additive, 0 alpha
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            if (m.HasProperty("_SrcBlend"))
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend"))
                m.SetFloat("_DstBlend", additive
                    ? (float)UnityEngine.Rendering.BlendMode.One
                    : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType", "Transparent");

            s_materials[key] = m;
            return m;
        }

        private static void SetTexture(Material m, Texture2D tex)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);   // URP
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);   // built-in fallbacks
        }

        private delegate float Falloff(float nx, float ny);

        /// <summary>Draws a white texture whose alpha is <paramref name="f"/> evaluated over
        /// normalised centred coords (-0.5..0.5). Cached; never written to disk.</summary>
        private static Texture2D Tex(string key, int size, Falloff f)
        {
            if (s_textures.TryGetValue(key, out var t) && t != null) return t;

            t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = key,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size - 0.5f;
                float ny = (y + 0.5f) / size - 0.5f;
                float a = Mathf.Clamp01(f(nx, ny));
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            t.SetPixels32(px);
            t.Apply();
            s_textures[key] = t;
            return t;
        }
    }
}
