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

        private static readonly Color DeniedRed = new Color(0.95f, 0.18f, 0.15f, 1f);

        /// <summary>The <see cref="PowerCell"/> battery, dimmed, with a red prohibition ring and
        /// diagonal slash struck through it (MV-407) — the "can't afford this" read for a deploy
        /// control gated on cell cost, built as its own cached sprite so callers just swap the icon
        /// rather than tint/overlay two sprites at runtime.</summary>
        public static Sprite PowerCellDenied(int size = 64)
        {
            const string key = "powercelldenied";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = NewTex(size, size);
            var px = new Color32[size * size];   // starts fully transparent

            float w = size * 0.5f, h = size * 0.66f;
            float cx = size * 0.5f, cy = size * 0.46f;
            float left = cx - w * 0.5f, right = cx + w * 0.5f;
            float bottom = cy - h * 0.5f, top = cy + h * 0.5f;
            float radius = size * 0.08f;
            float border = size * 0.09f;

            float nubW = w * 0.4f, nubH = size * 0.08f;
            float nubL = cx - nubW * 0.5f, nubR = cx + nubW * 0.5f;
            float nubB = top, nubT = top + nubH;

            Color dimCyan = Fade(CellCyan, 0.55f);
            Color dimDark = Fade(CellDark, 0.55f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    Color c = new Color(0, 0, 0, 0);

                    if (fx >= nubL && fx <= nubR && fy >= nubB && fy <= nubT)
                    {
                        c = dimCyan;   // the nub
                    }
                    else if (RoundedInside(fx, fy, left, right, bottom, top, radius))
                    {
                        bool onBorder = !RoundedInside(fx, fy, left + border, right - border,
                                                       bottom + border, top - border, radius * 0.5f);
                        c = onBorder ? dimCyan : dimDark;
                        if (!onBorder)
                        {
                            float rel = (fy - (bottom + border)) / (top - bottom - 2f * border);
                            float band = rel * 3f;
                            if (band - Mathf.Floor(band) < 0.66f) c = dimCyan;
                        }
                    }

                    if (c.a > 0f) px[y * size + x] = c;
                }
            }

            // The prohibition ring + a diagonal slash through it, on top of the dimmed cell — the
            // same "can't do this" shape used everywhere outside the game too.
            float ringR = size * 0.46f;
            float ringThick = size * 0.07f;
            float slashHalfThick = size * 0.045f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f - cx, fy = y + 0.5f - cy;
                    float dist = Mathf.Sqrt(fx * fx + fy * fy);

                    bool onRing = dist <= ringR && dist >= ringR - ringThick;

                    // Diagonal from bottom-left to top-right, thickness measured perpendicular to it.
                    float perpDist = Mathf.Abs(fx + fy) * 0.70710678f;
                    bool onSlash = perpDist <= slashHalfThick && dist <= ringR;

                    if (onRing || onSlash) px[y * size + x] = DeniedRed;
                }
            }

            tex.SetPixels32(px);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = key;
            s_cache[key] = sprite;
            return sprite;
        }

        /// <summary>MV-520: the "open this" glyph for an unlock cost tag — a keyhole (circle over a
        /// tapering triangle), the universal lock ideogram, distinct at a glance from
        /// <see cref="UpgradeGlyph"/>'s raise-arrow without reading the number. Drawn solid white so the
        /// caller's own <c>Image.color</c> tint (module cyan, dimmed or not) is the only colour source —
        /// same idiom every other node-shell graphic on the board uses.</summary>
        public static Sprite UnlockGlyph(int size = 32)
        {
            const string key = "unlockglyph";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = NewTex(size, size);
            var px = new Color32[size * size];

            // MV-520 fix: Texture2D pixel row 0 is the BOTTOM of the rendered sprite (same
            // bottom-left-origin convention PowerCell's own "top = cy + h*0.5" relies on) — so the
            // circle (the keyhole's ring) sits at the LARGER y, the tapering triangle hangs below it
            // toward the SMALLER y, matching a real keyhole rather than rendering upside down.
            float cx = size * 0.5f;
            float holeR = size * 0.20f;
            float holeCy = size * 0.68f;
            float triTop = holeCy - holeR * 0.35f;
            float triBottom = size * 0.18f;
            float triHalfWidthTop = holeR * 0.75f;
            float triHalfWidthBottom = size * 0.10f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    bool onCircle = (fx - cx) * (fx - cx) + (fy - holeCy) * (fy - holeCy) <= holeR * holeR;

                    bool onTriangle = false;
                    if (fy >= triBottom && fy <= triTop)
                    {
                        float t = (fy - triBottom) / (triTop - triBottom);
                        float halfW = Mathf.Lerp(triHalfWidthBottom, triHalfWidthTop, t);
                        onTriangle = Mathf.Abs(fx - cx) <= halfW;
                    }

                    if (onCircle || onTriangle) px[y * size + x] = Color.white;
                }
            }

            tex.SetPixels32(px);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = key;
            s_cache[key] = sprite;
            return sprite;
        }

        /// <summary>MV-520: the "raise this" glyph for an upgrade cost tag — an upward chevron over a
        /// stem, reading as an ascend arrow. Distinct at a glance from <see cref="UnlockGlyph"/>'s
        /// keyhole. Solid white, tinted by the caller exactly as <see cref="UnlockGlyph"/> is.</summary>
        public static Sprite UpgradeGlyph(int size = 32)
        {
            const string key = "upgradeglyph";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = NewTex(size, size);
            var px = new Color32[size * size];

            // MV-520 fix: same bottom-left-origin correction as UnlockGlyph — the apex (the point of
            // the "raise" arrow) sits at the LARGER y (the rendered top), the stem hangs below it
            // toward the SMALLER y (the rendered bottom), so the arrow actually points up.
            float cx = size * 0.5f;
            float apexY = size * 0.84f, capBaseY = size * 0.44f;
            float capHalfWidthBase = size * 0.30f;
            float stemTop = capBaseY, stemBottom = size * 0.16f;
            float stemHalfWidth = size * 0.11f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;

                    bool onCap = false;
                    if (fy >= capBaseY && fy <= apexY)
                    {
                        float t = (fy - capBaseY) / (apexY - capBaseY);
                        float halfW = Mathf.Lerp(capHalfWidthBase, 0f, t);
                        onCap = Mathf.Abs(fx - cx) <= halfW;
                    }
                    bool onStem = fy >= stemBottom && fy <= stemTop && Mathf.Abs(fx - cx) <= stemHalfWidth;

                    if (onCap || onStem) px[y * size + x] = Color.white;
                }
            }

            tex.SetPixels32(px);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = key;
            s_cache[key] = sprite;
            return sprite;
        }

        private static Color Fade(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

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
