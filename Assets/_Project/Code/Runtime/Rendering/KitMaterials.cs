using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// Marks a set-dressing object (and its children) as carrying its own materials (YT-75).
    ///
    /// The material directors sweep every renderer in the scene and repaint anything they don't
    /// recognise — that's what keeps runtime-spawned greybox off the magenta error shader. A kit
    /// prop is the one thing that must NOT be repainted: its whole point is that a tree has bark
    /// AND leaves, and a single flat "prop" colour would collapse it back into a lump. So dressing
    /// says so, once, and both directors leave it alone.
    ///
    /// <see cref="Sways"/> is the other half: foliage should move in the wind, a fence post should
    /// not. The ambience layer reads it instead of guessing from the shape.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DressingSkin : MonoBehaviour
    {
        [Tooltip("Foliage: the ambience layer is allowed to sway this. Fences, pots and stone are not.")]
        public bool Sways;
    }

    /// <summary>
    /// The Backyard's set-dressing materials (YT-75) — one per material name in the imported kit.
    ///
    /// The kit's models arrive with named materials (wood, leafsGreen, dirt, stone…) and the kit
    /// author's own colours. We keep the NAMES and throw away the colours: the names are a free,
    /// stable classification of every surface in the kit, and the colours belong to somebody else's
    /// game. Recolouring through this table is what stops a hundred imported props from reading as
    /// a foreign asset pack dropped into our yard, and it's the hook YT-77's surface pass hangs on.
    ///
    /// Built in code and cached, like every other material in the project — nothing to hand-wire,
    /// and a fresh clone renders identically (docs/CODE_DRIVEN_SCENES.md).
    /// </summary>
    public static class KitMaterials
    {
        /// <summary>
        /// Colour per kit material name. A name missing from here is a prop we imported without
        /// deciding what it's made of, so it falls back to the biome's prop grey.
        ///
        /// THESE LOOK TOO DARK IN A COLOUR PICKER, AND THAT IS THE POINT. The yard is lit by a
        /// 2.2-intensity key (BackyardLook), and URP/Lit multiplies albedo by that: paint a fence
        /// the honest pine colour you'd pick on a swatch (~0.62) and every sunlit panel clips to
        /// cream, which is precisely what the first pass at this did. The ceiling is roughly 0.6 per
        /// channel, and the tones below sit under it — the same restraint, for the same reason, that
        /// <see cref="BiomePalette.Backyard"/> already applies to the lawn. Tests hold the line.
        /// </summary>
        private static readonly Dictionary<string, Color> Paint = new Dictionary<string, Color>
        {
            // Timber — the fence, the planters, the shed. Warm, sun-faded, never orange.
            ["wood"] = new Color(0.34f, 0.24f, 0.15f),
            ["woodDark"] = new Color(0.24f, 0.17f, 0.11f),
            ["woodBark"] = new Color(0.22f, 0.16f, 0.12f),
            ["woodBarkDark"] = new Color(0.16f, 0.12f, 0.09f),
            ["woodInner"] = new Color(0.42f, 0.33f, 0.22f),   // a fresh-cut log face: the pale one

            // Foliage. Deliberately a shade deeper and cooler than the lawn underneath it
            // (GroundAccent 0.32/0.50/0.18): a bush the same green as the grass it stands on is a
            // silhouette with nothing to separate it from the floor at a 72° camera.
            ["leafsGreen"] = new Color(0.17f, 0.30f, 0.12f),
            ["grass"] = new Color(0.21f, 0.34f, 0.14f),

            ["dirt"] = new Color(0.26f, 0.19f, 0.12f),
            ["dirtDark"] = new Color(0.18f, 0.13f, 0.09f),

            ["stone"] = new Color(0.36f, 0.35f, 0.33f),
            ["stoneDark"] = new Color(0.26f, 0.25f, 0.24f),

            // Flowers. The only saturated colours in the yard — and they are TINY, which is the
            // deal: Max (orange) and the robots must still be the loudest things on screen.
            ["colorRed"] = new Color(0.42f, 0.13f, 0.14f),
            ["colorYellow"] = new Color(0.52f, 0.42f, 0.12f),
            ["colorPurple"] = new Color(0.30f, 0.18f, 0.42f),

            // The kit's own leftover default, on the odd fitting: a sign's bracket, a pot's rim, the
            // edging of a paving stone. Painted rather than left to fall through: it's on four of
            // our props, and "fell through to the fallback" is not a colour anybody chose.
            ["_defaultMat"] = new Color(0.30f, 0.29f, 0.27f),
        };

        /// <summary>The brightest albedo the Backyard's key light can take without the surface
        /// clipping to white. Anything above this reads as cream, whatever colour it claims to
        /// be — see the note on <see cref="Paint"/>.</summary>
        public const float SunlitAlbedoCeiling = 0.6f;

        private static readonly Dictionary<string, Material> s_cache = new Dictionary<string, Material>();

        /// <summary>Every material name the imported kit is allowed to use. Tests pin the two
        /// halves together, so importing a prop that's made of something we never chose a colour
        /// for is a red test, not a grey prop somebody notices on the WebGL link.</summary>
        public static IEnumerable<string> Names => Paint.Keys;

        public static bool Knows(string materialName) =>
            !string.IsNullOrEmpty(materialName) && Paint.ContainsKey(materialName);

        /// <summary>The colour we paint a kit material name, or the biome's prop colour if the kit
        /// used a name we never classified.</summary>
        public static Color ColorOf(string materialName)
        {
            if (materialName != null && Paint.TryGetValue(materialName, out var c)) return c;
            return MaterialLibrary.Palette.ColorFor(SurfaceKind.Prop);
        }

        /// <summary>The material for a kit material name. Shared and instanced: the yard has ~100
        /// fence panels and they must not cost ~100 draw calls.</summary>
        public static Material For(string materialName)
        {
            string key = Knows(materialName) ? materialName : "_fallback";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var m = new Material(shader)
            {
                name = $"Kit_{key}",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };

            Color c = ColorOf(materialName) * MaterialLibrary.Palette.Tint;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.05f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);

            s_cache[key] = m;
            return m;
        }

        /// <summary>Drop the cache — the palette's tint feeds these, so a biome change must rebuild
        /// them just like it rebuilds the greybox surfaces.</summary>
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
    }
}
