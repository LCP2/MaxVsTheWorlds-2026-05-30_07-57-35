using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// Procedural, seamlessly-tiling surface textures (YT-50), generated in code so a fresh
    /// clone builds them identically with no authored asset.
    ///
    /// The point of these is not detail — it's *variation*. A single flat albedo over a big
    /// plane reads as dead grey (or, once lit warm, as dead brown) no matter how good the
    /// lighting is, because there is nothing on the surface for the light to differentiate.
    /// Low-frequency mottling gives the ground something to shade.
    ///
    /// The noise is lattice-based with a wrapping hash, so the result tiles seamlessly and can
    /// be repeated across the arena without a visible seam.
    /// </summary>
    public static class StylizedTextures
    {
        private static readonly Dictionary<string, Texture2D> s_cache = new Dictionary<string, Texture2D>();

        /// <summary>Organic mottling mask for the ground — broad patches with a fine speckle on
        /// top, so it reads as turf/dirt rather than a gradient. Greyscale: it is a *mask* that
        /// selects between two colours (see <see cref="Blend"/>), not an albedo.</summary>
        public static Texture2D Ground(int size = 512)
        {
            return Cache($"ground{size}", size, (u, v) =>
            {
                // Enough high-frequency detail to read as turf up close. At low frequencies the
                // mottling turns into big soft blobs and the ground looks like blurred camouflage.
                float broad = Fbm(u, v, baseFreq: 6, octaves: 4);      // patches
                float fine = Fbm(u, v, baseFreq: 24, octaves: 2);      // blades/speckle
                return Mathf.Clamp01(broad * 0.68f + fine * 0.32f);
            });
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
            t = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = key,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.HideAndDontSave,
            };

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
            return Cache($"surface{size}", size, (u, v) =>
            {
                float n = Fbm(u, v, baseFreq: 5, octaves: 3);
                return Mathf.Lerp(0.62f, 1f, n);                       // shallow range = subtle
            });
        }

        // --- tileable value noise ---

        private static float Fbm(float u, float v, int baseFreq, int octaves)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int freq = baseFreq;
            for (int i = 0; i < octaves; i++)
            {
                sum += Noise(u * freq, v * freq, freq) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>Value noise on a lattice of <paramref name="period"/> cells, wrapping at the
        /// edges — this wrap is what makes the texture tile with no seam.</summary>
        private static float Noise(float x, float y, int period)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;

            float a = Hash(xi, yi, period);
            float b = Hash(xi + 1, yi, period);
            float c = Hash(xi, yi + 1, period);
            float d = Hash(xi + 1, yi + 1, period);

            float sx = xf * xf * (3f - 2f * xf);   // smoothstep — kills the lattice grid look
            float sy = yf * yf * (3f - 2f * yf);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        private static float Hash(int x, int y, int period)
        {
            // Wrap the lattice coords into the period so opposite edges sample the same corners.
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }

        private delegate float Sampler(float u, float v);

        private static Texture2D Cache(string key, int size, Sampler f)
        {
            if (s_cache.TryGetValue(key, out var t) && t != null) return t;

            t = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = key,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float g = Mathf.Clamp01(f((x + 0.5f) / size, (y + 0.5f) / size));
                var c = new Color(g, g, g, 1f);   // greyscale: the tint comes from _BaseColor
                px[y * size + x] = c;
            }
            t.SetPixels32(px);
            t.Apply(updateMipmaps: true);
            s_cache[key] = t;
            return t;
        }
    }
}
