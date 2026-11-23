using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// MV-1423: a plain, untextured material that carries its own colour. The stylised surface materials
    /// (<see cref="MaterialLibrary.Tinted"/>) bake a grain texture around the tone and multiply it by
    /// <c>_BaseColor</c>, and a <see cref="MaterialPropertyBlock"/> colour on that shader renders wrong in the
    /// live world (see CharacterSkin.IsMachine's note) - so a look that must read as one exact bright colour
    /// (the canister's red drum, the barricade's orange and white) wears one of these instead, and a flash
    /// swaps the material rather than pushing a block. Cached per quantised colour, so the set stays small.
    /// </summary>
    public static class FlatColorMaterial
    {
        private static readonly Dictionary<int, Material> Cache = new Dictionary<int, Material>(32);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>The material for <paramref name="color"/> (alpha ignored), or null if no shader survived.</summary>
        public static Material Get(Color color)
        {
            int r = Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f);
            int g = Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f);
            int b = Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f);
            int key = (r << 16) | (g << 8) | b;
            if (Cache.TryGetValue(key, out Material cached) && cached != null) return cached;

            // The hand-written surface shader is always in the player build (Always Included Shaders), and with its
            // default white albedo and flat normal it shows exactly _BaseColor - no grain, no brick.
            Shader shader = MaterialLibrary.StylizedSurfaceShader ?? MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            Color c = new Color(r / 255f, g / 255f, b / 255f, 1f);
            var m = new Material(shader) { name = $"Flat_{key:X6}", hideFlags = HideFlags.HideAndDontSave };
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
            m.enableInstancing = false;
            Cache[key] = m;
            return m;
        }

        /// <summary>The colour <paramref name="r"/> renders with: its material's own colour. Zero-alpha clear when it has none.</summary>
        public static Color ResolvedColor(Renderer r)
        {
            if (r == null || r.sharedMaterial == null) return Color.clear;
            Material m = r.sharedMaterial;
            if (m.HasProperty(BaseColorId)) return m.GetColor(BaseColorId);
            if (m.HasProperty(ColorId)) return m.GetColor(ColorId);
            return Color.clear;
        }
    }
}
