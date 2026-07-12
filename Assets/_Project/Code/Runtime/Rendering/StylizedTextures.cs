using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// Procedural, seamlessly-tiling surface textures (YT-50, reworked in YT-69), generated in code
    /// so a fresh clone builds them identically with no authored asset.
    ///
    /// The first pass aimed at *variation* — anything to stop a flat albedo reading as dead grey.
    /// It worked, and the floor still looked artificial, because variation in colour alone is not
    /// what makes a surface look like a surface. Two things were missing:
    ///
    ///   - RELIEF. The mottle was albedo-only, so the key light shaded every pixel of the lawn
    ///     identically. <see cref="GroundNormal"/> now derives a normal map from the same height
    ///     field the albedo is painted from, so light and colour agree and the light actually breaks
    ///     across the grass.
    ///
    ///   - GRASS-SHAPED DETAIL. The old mask was low-frequency FBM: big soft blobs that read as
    ///     blurred camouflage. Grass is fibrous, so the height field now carries two crossed
    ///     anisotropic layers (stretched noise = streaks) over the clumps.
    ///
    /// The noise is lattice-based with a wrapping hash, so the result tiles seamlessly.
    /// </summary>
    public static class StylizedTextures
    {
        private static readonly Dictionary<string, Texture2D> s_cache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, float[]> s_heights = new Dictionary<string, float[]>();

        /// <summary>
        /// The ground's height field — one shared source of truth for both the albedo mottle and the
        /// normal map. Generating them from the same field is the point: if the bright patches and
        /// the raised patches disagreed, the light would fight the paint and the surface would read
        /// as noise printed on glass.
        /// </summary>
        private static float[] GroundHeight(int size)
        {
            string key = $"groundH{size}";
            if (s_heights.TryGetValue(key, out var cached)) return cached;

            var h = new float[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float v = (y + 0.5f) / size;

                // Anisotropic layers: a lattice of tall thin cells interpolates into streaks rather
                // than blobs, which is what makes this read as fibre. Two of them, crossed, so the
                // lawn has a grain without every blade pointing the same way — one layer alone looks
                // like brushed carpet.
                float fibreA = Noise(u * 40f, v * 14f, 40, 14);
                float fibreB = Noise(u * 14f, v * 40f, 14, 40);
                float fibre = (fibreA + fibreB) * 0.5f;

                float speck = Fbm(u, v, baseFreq: 26, octaves: 2);

                // Deliberately little low-frequency content. LOW frequencies are what make a tiled
                // texture look tiled: the eye can't see a 4cm speckle repeat, but it picks up a
                // metre-wide blotch repeating on a grid instantly, and calls the whole lawn
                // wallpaper. So the tile carries the fine grain only, and the clump-and-patch scale
                // is generated in world space by the shader, where it never repeats at all.
                float clump = Fbm(u, v, baseFreq: 6, octaves: 3);

                h[y * size + x] = Mathf.Clamp01(fibre * 0.55f + speck * 0.27f + clump * 0.18f);
            }

            s_heights[key] = h;
            return h;
        }

        /// <summary>Organic mottling mask for the ground. Greyscale: it is a *mask* that selects
        /// between two colours (see <see cref="Blend"/>), not an albedo.</summary>
        public static Texture2D Ground(int size = 512)
        {
            string key = $"ground{size}";
            if (s_cache.TryGetValue(key, out var t) && t != null) return t;

            var h = GroundHeight(size);
            t = NewTexture(key, size, linear: false);

            var px = new Color32[size * size];
            for (int i = 0; i < h.Length; i++)
            {
                byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(h[i]) * 255f);
                px[i] = new Color32(g, g, g, 255);
            }
            t.SetPixels32(px);
            t.Apply(updateMipmaps: true);
            s_cache[key] = t;
            return t;
        }

        /// <summary>
        /// The ground's normal map, derived from <see cref="GroundHeight"/> by central differences.
        ///
        /// This is the piece that was missing. Without it the lawn is a perfectly flat plane wearing
        /// a picture of grass: correct colour, zero light response, and the eye calls it a fill.
        ///
        /// Written as plain linear RGB — NOT DXT5nm-swizzled — and the shader unpacks it by hand to
        /// match. Unity's UnpackNormalScale would read the wrong channels here.
        /// </summary>
        public static Texture2D GroundNormal(int size = 512, float strength = 2.4f)
        {
            string key = $"groundN{size}";
            if (s_cache.TryGetValue(key, out var t) && t != null) return t;

            var h = GroundHeight(size);
            t = NewTexture(key, size, linear: true);

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Wrap the taps, or the normal map seams exactly where the albedo doesn't.
                float hl = h[y * size + Wrap(x - 1, size)];
                float hr = h[y * size + Wrap(x + 1, size)];
                float hd = h[Wrap(y - 1, size) * size + x];
                float hu = h[Wrap(y + 1, size) * size + x];

                var n = new Vector3(-(hr - hl) * strength, -(hu - hd) * strength, 1f).normalized;

                px[y * size + x] = new Color32(
                    (byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f),
                    (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                    (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f),
                    255);
            }
            t.SetPixels32(px);
            t.Apply(updateMipmaps: true);
            s_cache[key] = t;
            return t;
        }

        /// <summary>
        /// Bake a mask into a two-colour albedo: <paramref name="a"/> where the mask is dark,
        /// <paramref name="b"/> where it is bright.
        ///
        /// This exists because URP/Lit has no two-colour blend of its own, and the obvious
        /// alternative — leaving the greyscale mask as the albedo map and letting it multiply
        /// _BaseColor — drives the dark half of the mask toward black. On a big ground plane that
        /// doesn't read as turf, it reads as high-contrast camouflage.
        /// </summary>
        public static Texture2D Blend(string key, Texture2D mask, Color a, Color b)
        {
            if (s_cache.TryGetValue(key, out var t) && t != null) return t;

            int size = mask.width;
            t = NewTexture(key, size, linear: false);

            var src = mask.GetPixels32();
            var px = new Color32[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                float m = src[i].r / 255f;
                m = m * m * (3f - 2f * m);                 // ease the midtones: fewer muddy in-betweens
                px[i] = Color.Lerp(a, b, m);
            }
            t.SetPixels32(px);
            t.Apply(updateMipmaps: true);
            s_cache[key] = t;
            return t;
        }

        /// <summary>Subtle grain for walls and props — enough to catch the light, not enough to
        /// look like a texture the art team would have to justify.</summary>
        public static Texture2D Surface(int size = 128)
        {
            string key = $"surface{size}";
            if (s_cache.TryGetValue(key, out var t) && t != null) return t;

            t = NewTexture(key, size, linear: false);

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = Fbm((x + 0.5f) / size, (y + 0.5f) / size, baseFreq: 5, octaves: 3);
                byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(Mathf.Lerp(0.62f, 1f, n)) * 255f);
                px[y * size + x] = new Color32(g, g, g, 255);   // shallow range = subtle
            }
            t.SetPixels32(px);
            t.Apply(updateMipmaps: true);
            s_cache[key] = t;
            return t;
        }

        /// <summary>Drop every generated texture — the palette changed, so the albedos are stale.</summary>
        public static void Clear()
        {
            foreach (var t in s_cache.Values)
            {
                if (t == null) continue;
                if (Application.isPlaying) Object.Destroy(t);
                else Object.DestroyImmediate(t);
            }
            s_cache.Clear();
            // The height field is palette-independent, so it survives: it is the expensive half.
        }

        // --- tileable value noise ---

        private static int Wrap(int i, int size) => ((i % size) + size) % size;

        private static float Fbm(float u, float v, int baseFreq, int octaves)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int freq = baseFreq;
            for (int i = 0; i < octaves; i++)
            {
                sum += Noise(u * freq, v * freq, freq, freq) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>Value noise on a lattice, wrapping at <paramref name="periodX"/> /
        /// <paramref name="periodY"/> cells — the wrap is what makes the texture tile with no seam.
        /// The two periods are separate so the lattice can be stretched into streaks (see the fibre
        /// layers in <see cref="GroundHeight"/>); a square lattice can only ever make blobs.</summary>
        private static float Noise(float x, float y, int periodX, int periodY)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;

            float a = Hash(xi, yi, periodX, periodY);
            float b = Hash(xi + 1, yi, periodX, periodY);
            float c = Hash(xi, yi + 1, periodX, periodY);
            float d = Hash(xi + 1, yi + 1, periodX, periodY);

            float sx = xf * xf * (3f - 2f * xf);   // smoothstep — kills the lattice grid look
            float sy = yf * yf * (3f - 2f * yf);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        private static float Hash(int x, int y, int periodX, int periodY)
        {
            // Wrap the lattice coords into the period so opposite edges sample the same corners.
            x = ((x % periodX) + periodX) % periodX;
            y = ((y % periodY) + periodY) % periodY;
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }

        private static Texture2D NewTexture(string key, int size, bool linear)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true, linear: linear)
            {
                name = key,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }
    }
}
