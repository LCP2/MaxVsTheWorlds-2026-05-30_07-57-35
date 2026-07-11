using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Procedurally-generated sprites for the YT-30 HUD — bars, discs, the concentric
    /// "tech-ring" joystick base, direction arrow, crosshair, and rounded icon boxes.
    /// Everything is drawn in code (no committed art), per the greybox / code-driven-scene
    /// rule, so a fresh clone builds an identical HUD headlessly. Sprites are cached by key.
    /// </summary>
    public static class HudTextures
    {
        private static readonly Dictionary<string, Sprite> s_cache = new Dictionary<string, Sprite>();

        /// <summary>1×1 white sprite. Tint via Image.color — used for every bar/panel fill.</summary>
        public static Sprite Solid()
        {
            if (s_cache.TryGetValue("solid", out var s)) return s;
            var tex = NewTex(4, 4);
            Fill(tex, Color.white);
            return Cache("solid", tex, 100f);
        }

        /// <summary>Filled anti-aliased disc (radial-fill overlays, joystick knob).</summary>
        public static Sprite Disc(int size = 128)
        {
            string key = $"disc{size}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(size, size);
            float r = size * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - r + 0.5f) * (x - r + 0.5f) + (y - r + 0.5f) * (y - r + 0.5f));
                float a = Mathf.Clamp01(r - d);           // 1px feathered edge
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache(key, tex, 100f);
        }

        /// <summary>Concentric glowing rings — the joystick base. Tint via Image.color.</summary>
        public static Sprite TechRings(int size = 160, int rings = 3)
        {
            string key = $"rings{size}_{rings}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(size, size);
            float cx = size * 0.5f, cy = size * 0.5f, outer = size * 0.5f - 1f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - cx + 0.5f) * (x - cx + 0.5f) + (y - cy + 0.5f) * (y - cy + 0.5f));
                float a = 0f;
                // A faint filled core plus `rings` bright concentric bands.
                if (d <= outer) a = 0.12f;
                for (int i = 1; i <= rings; i++)
                {
                    float rr = outer * i / rings;
                    float band = 1f - Mathf.Clamp01(Mathf.Abs(d - rr) / 2.2f); // ~2px bands
                    a = Mathf.Max(a, band);
                }
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache(key, tex, 100f);
        }

        /// <summary>Upward-pointing triangle (movement direction overlay).</summary>
        public static Sprite Arrow(int size = 64)
        {
            if (s_cache.TryGetValue("arrow", out var s)) return s;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float ny = (float)y / size;                 // 0 bottom -> 1 top
                float halfWidth = (1f - ny) * 0.5f * size;  // widens toward the base
                float cx = size * 0.5f;
                bool inside = ny > 0.15f && Mathf.Abs(x - cx) <= halfWidth;
                px[y * size + x] = new Color(1, 1, 1, inside ? 1f : 0f);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache("arrow", tex, 100f);
        }

        /// <summary>Thin crosshair glyph (aim-joystick centre).</summary>
        public static Sprite Crosshair(int size = 96)
        {
            if (s_cache.TryGetValue("cross", out var s)) return s;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float c = size * 0.5f, thick = Mathf.Max(1.5f, size * 0.04f), gap = size * 0.14f, len = size * 0.42f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - c + 0.5f), dy = Mathf.Abs(y - c + 0.5f);
                bool horiz = dy <= thick && dx >= gap && dx <= len;
                bool vert = dx <= thick && dy >= gap && dy <= len;
                px[y * size + x] = new Color(1, 1, 1, (horiz || vert) ? 1f : 0f);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache("cross", tex, 100f);
        }

        /// <summary>Rounded-corner box (icon buttons, ability slots, name card). 9-sliced.</summary>
        public static Sprite RoundedBox(int size = 64, float cornerFraction = 0.28f)
        {
            string key = $"rbox{size}_{Mathf.RoundToInt(cornerFraction * 100)}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float radius = size * cornerFraction;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float a = RoundedAlpha(x, y, size, size, radius);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px); tex.Apply();
            float b = radius; // 9-slice border so scaling keeps corners crisp
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            sprite.name = key;
            s_cache[key] = sprite;
            return sprite;
        }

        // --- helpers ---

        private static float RoundedAlpha(int x, int y, int w, int h, float radius)
        {
            float px = x + 0.5f, py = y + 0.5f;
            float cx = Mathf.Clamp(px, radius, w - radius);
            float cy = Mathf.Clamp(py, radius, h - radius);
            float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            return Mathf.Clamp01(radius - d + 0.5f);
        }

        private static Texture2D NewTex(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            return tex;
        }

        private static void Fill(Texture2D tex, Color c)
        {
            var px = new Color32[tex.width * tex.height];
            var c32 = (Color32)c;
            for (int i = 0; i < px.Length; i++) px[i] = c32;
            tex.SetPixels32(px); tex.Apply();
        }

        private static Sprite Cache(string key, Texture2D tex, float ppu)
        {
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), ppu);
            sprite.name = key;
            s_cache[key] = sprite;
            return sprite;
        }
    }
}
