using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Procedural HUD icons for the weapon system (YT-134). Right now just the power-cell counter
    /// icon, which YT-131 draws as a bare cyan disc — a disc reads as "a thing", not as "a battery".
    ///
    /// Procedural and cached, the same shape as <c>HudTextures</c>: no committed PNG, generated once,
    /// handed out as a shared <see cref="Sprite"/>. Self-contained so it has no dependency on the HUD
    /// code (which lives in the gameplay stream until it merges) — the counter just swaps
    /// <c>HudTextures.Disc(...)</c> for <see cref="PowerCell"/>.
    /// </summary>
    public static class WeaponHudIcons
    {
        private static readonly Dictionary<string, Sprite> s_cache = new Dictionary<string, Sprite>();

        private static readonly Color CellCyan = new Color(0.31f, 0.86f, 0.98f, 1f);
        private static readonly Color CellDark = new Color(0.06f, 0.20f, 0.26f, 1f);

        /// <summary>A little battery cell: a rounded casing, a terminal nub, and three charge segments.
        /// White-cored cyan so it stays legible at the ~40 px it renders at in the HUD pill.</summary>
        public static Sprite PowerCell(int size = 64)
        {
            const string key = "powercell";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = NewTex(size, size);
            var px = new Color32[size * size];   // starts fully transparent

            // Battery body: a rounded rectangle, taller than wide, centred, with a terminal nub on top.
            float w = size * 0.5f, h = size * 0.66f;
            float cx = size * 0.5f, cy = size * 0.46f;
            float left = cx - w * 0.5f, right = cx + w * 0.5f;
            float bottom = cy - h * 0.5f, top = cy + h * 0.5f;
            float radius = size * 0.08f;
            float border = size * 0.09f;

            // The terminal nub.
            float nubW = w * 0.4f, nubH = size * 0.08f;
            float nubL = cx - nubW * 0.5f, nubR = cx + nubW * 0.5f;
            float nubB = top, nubT = top + nubH;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    Color c = new Color(0, 0, 0, 0);

                    if (fx >= nubL && fx <= nubR && fy >= nubB && fy <= nubT)
                    {
                        c = CellCyan;   // the nub
                    }
                    else if (RoundedInside(fx, fy, left, right, bottom, top, radius))
                    {
                        // Border cyan, interior dark, with three bright charge segments.
                        bool onBorder = !RoundedInside(fx, fy, left + border, right - border,
                                                       bottom + border, top - border, radius * 0.5f);
                        if (onBorder)
                        {
                            c = CellCyan;
                        }
                        else
                        {
                            c = CellDark;
                            // Three horizontal charge bars stacked in the interior.
                            float rel = (fy - (bottom + border)) / (top - bottom - 2f * border);
                            float band = rel * 3f;
                            if (band - Mathf.Floor(band) < 0.66f) c = CellCyan;
                        }
                    }

                    if (c.a > 0f) px[y * size + x] = c;
                }
            }

            tex.SetPixels32(px);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = key;
            s_cache[key] = sprite;
            return sprite;
        }

        private static readonly Color PartBlack = new Color(0.05f, 0.05f, 0.06f, 1f);

        /// <summary>A salvage-part nut/bolt: a black hex-nut silhouette with a punched-out circular
        /// bore. BLACK, not the old plain-orange chip (YT-168) — a solid dark silhouette is the one
        /// shape that stays high-contrast against the chip's warm orange AND the lawn behind the part
        /// pickup, so "there's a part to deal with" reads before the eye resolves anything else.</summary>
        public static Sprite Part(int size = 64)
        {
            const string key = "part";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = NewTex(size, size);
            var px = new Color32[size * size];   // starts fully transparent

            float cx = size * 0.5f, cy = size * 0.5f;
            float outerR = size * 0.44f;
            float boreR = size * 0.17f;

            // Regular hexagon, flat-topped, as six half-plane tests against the outer radius.
            Vector2[] axes = new Vector2[3];
            for (int i = 0; i < 3; i++)
            {
                float a = (30f + i * 60f) * Mathf.Deg2Rad;
                axes[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
            float apothem = outerR * 0.866f;   // cos(30deg) — hexagon inradius from its circumradius

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f - cx, fy = y + 0.5f - cy;
                    bool inHex = true;
                    for (int i = 0; i < 3; i++)
                    {
                        if (Mathf.Abs(fx * axes[i].x + fy * axes[i].y) > apothem) { inHex = false; break; }
                    }
                    bool inBore = (fx * fx + fy * fy) <= boreR * boreR;

                    if (inHex && !inBore) px[y * size + x] = PartBlack;
                }
            }

            tex.SetPixels32(px);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = key;
            s_cache[key] = sprite;
            return sprite;
        }

        // --- helpers (inlined so this class stands alone) ---

        /// <summary>True if (x,y) is inside an axis-aligned rounded rectangle.</summary>
        private static bool RoundedInside(float x, float y, float l, float r, float b, float t, float rad)
        {
            if (x < l || x > r || y < b || y > t) return false;
            float cx = Mathf.Clamp(x, l + rad, r - rad);
            float cy = Mathf.Clamp(y, b + rad, t - rad);
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= rad * rad || (x >= l + rad && x <= r - rad) || (y >= b + rad && y <= t - rad);
        }

        private static Texture2D NewTex(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }
    }
}
