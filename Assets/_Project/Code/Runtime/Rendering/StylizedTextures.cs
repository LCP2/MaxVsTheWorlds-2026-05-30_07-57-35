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

            t = NormalFromHeight(key, GroundHeight(size), size, strength);
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
        /// <param name="linear">
        /// False for an albedo — the tones are authored in sRGB and Unity's sRGB→linear conversion on
        /// sample is exactly right for them.
        ///
        /// TRUE when the result is a MULTIPLIER rather than a colour (YT-77's wear maps, which carry
        /// no colour of their own and are multiplied against a tint gameplay supplies). Getting this
        /// wrong is silent and expensive: a wear map averaging 0.84 in sRGB is 0.67 once the sampler
        /// has linearised it, so the Mower Hutch came out a third darker than the colour it had been
        /// given, went muddy, and stopped responding to its own tint — a hue change of 25% moved the
        /// rendered pixel by 2%.
        /// </param>
        public static Texture2D Blend(string key, Texture2D mask, Color a, Color b, bool linear = false)
        {
            if (s_cache.TryGetValue(key, out var t) && t != null) return t;

            int size = mask.width;
            t = NewTexture(key, size, linear: linear);

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

        // ------------------------------------------------------------------ MV-1053: the deck floor
        //
        // Two separate maps, not one, and deliberately not built on the shared Height()/Blend() path
        // above: the albedo is a pure MULTIPLIER (mean ~1 across the plate, dipping at the seam) so
        // WorldMaterials.M_ShipFloor's own _BaseColor keeps carrying the tone MV-1019 tuned — see that
        // material's own comment for why. The emission mask is the opposite: a real colour in its own
        // right, baked "linear: false" like every other albedo here.
        //
        // Both represent exactly ONE 2 m plate. ReefKit.DressHull is what makes that read as "2 m
        // plates" on the actual floor: it sets the material's own mainTextureScale from the floor's
        // resolved world size, so this image only ever has to BE one plate, never worry how many of
        // them the floor needs.

        private const float ReefDeckSeamFraction = 0.03f;    // ~6 cm seam / 2 m plate, ticket's own number
        private const float ReefDeckSeamMultiplier = 0.46f;  // how much darker the seam reads than the plate

        // MV-1265: the circuit-inlay trace was 5 texels wide (cellSize * 0.03, rounded) - ~7.5 screen px
        // at play distance, under DECISIONS.md §3's ~10 px legibility floor, and mip-averaging crushed it
        // further. Widened so the resolved bake was >=12 texels (13 at the shipped 256px/4-cell bake).
        //
        // MV-1334: the floor read as busier than the robots once every cell carried this width at full
        // brightness. Thinned to about two thirds of MV-1265's 0.1f (ticket, Change item 1) - the
        // resolved bake drops from 13 to 9 texels (~13.5 screen px at the same 1.5 px/texel ratio
        // MV-1265 measured), which still clears DECISIONS.md §3's ~10 px floor even though it falls
        // under MV-1265's own, stricter 12-texel test floor - see that test's updated comment.
        private const float ReefDeckTraceHalfWidthFraction = 0.1f * (2f / 3f);

        /// <summary>Which roll of <see cref="ReefDeckPatternIndexAt"/>'s existing seeded hash counts as
        /// "lit" (MV-1334 ticket, Change item 1: "draw an inlay in about 1 cell in 4 ... chosen by the
        /// existing seeded hash ... so it is stable"). Any other roll leaves the cell black. Picking a
        /// single fixed roll out of <see cref="WorldMaterials.ReefDeckInlayPatternCount"/> possible ones
        /// is what gives the ~1-in-4 odds the ticket asks for, for free, off a hash that already exists
        /// and already resolves 0..3 roughly uniformly (MV1256ReefDeckDetailTests pins all 4 values
        /// appearing across the real floor) - no new hash needed.</summary>
        private const int ReefDeckLitPatternIndex = 0;

        private const float ReefDeckRivetInset = 0.09f;      // how far a rivet sits from the plate's corner
        private const float ReefDeckRivetRadius = 0.045f;
        private const float ReefDeckRivetRingMultiplier = 0.7f;

        // A grate inset, centred and clear of both the seam band and the corner rivets — "occasional"
        // within the repeating tile rather than across separate plates, since one image here IS one
        // plate (see the section note above): there is nowhere else for "occasional" to live.
        private const float ReefDeckGrateMin = 0.37f, ReefDeckGrateMax = 0.63f;
        private const int ReefDeckGrateBars = 5;
        private const float ReefDeckGrateBarMultiplier = 0.50f;

        /// <summary>MV-1256: deterministic per-plate tint jitter ("about +/-6% value") so the deck's own
        /// supertile never repeats as a visually uniform grid once <see cref="ReefDeckAlbedo"/> is
        /// tiled across the floor — the fine FBM noise above varies WITHIN a plate; this varies plate
        /// to plate, which the old one-image-per-whole-floor bake had no cell to carry at all.</summary>
        private const float ReefDeckTintJitterMax = 0.06f;

        private static float ReefDeckTintJitterAt(int gx, int gy)
        {
            uint h = ReefHash($"decktint_{gx}_{gy}");
            float t01 = (h % 2000u) / 1999f;
            return (t01 * 2f - 1f) * ReefDeckTintJitterMax;
        }

        /// <summary>Which of <see cref="WorldMaterials.ReefDeckInlayPatternCount"/> circuit-inlay
        /// layouts cell (<paramref name="gx"/>, <paramref name="gy"/>) of the deck's own baked
        /// supertile resolves to (MV-1256, Change item 2: "at least 4 inlay patterns ... picked per
        /// plate from a seeded hash"). Pure and cheap enough that <see cref="ReefDeckEmission"/>'s own
        /// bake and <see cref="MaxWorlds.Rendering.ReefKit.ApplyDeckTiling"/>'s resolved-value
        /// <see cref="ReefDeckPlateField"/> both call it directly, rather than keeping two copies of
        /// the same hash in step by hand.</summary>
        public static int ReefDeckPatternIndexAt(int gx, int gy)
        {
            uint h = ReefHash($"deckpattern_{gx}_{gy}");
            return (int)(h % (uint)WorldMaterials.ReefDeckInlayPatternCount);
        }

        /// <summary>The deck's baked "supertile" — <see cref="WorldMaterials.ReefDeckGridCells"/> square
        /// plates, each its own greyscale MULTIPLIER against <c>M_ShipFloor</c>'s own _BaseColor: ~1
        /// across the face with a little tileable per-plate value noise (the ticket's "±5% per-plate
        /// value variation"), a dark border at the plate's own seam, a small full-value rivet disc
        /// ringed darker at each corner, one grate inset, and (MV-1256) a further deterministic tint
        /// jitter unique to THIS cell (<see cref="ReefDeckTintJitterAt"/>) — the ticket's own fix for
        /// MV-1053's "reads as a uniform tile grid" gap, since a single repeating plate image had no
        /// cell for plate-to-plate variation to live in at all.</summary>
        public static Texture2D ReefDeckAlbedo(int size = 256)
        {
            const string key = "reefDeckAlbedo";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var t = NewTexture(key, size, linear: true);
            var px = new Color32[size * size];

            int cells = WorldMaterials.ReefDeckGridCells;
            int cellSize = Mathf.Max(1, size / cells);

            for (int gy = 0; gy < cells; gy++)
            for (int gx = 0; gx < cells; gx++)
            {
                float jitterMul = 1f + ReefDeckTintJitterAt(gx, gy);
                int originX = gx * cellSize;
                int originY = gy * cellSize;

                for (int ly = 0; ly < cellSize; ly++)
                for (int lx = 0; lx < cellSize; lx++)
                {
                    int x = originX + lx;
                    int y = originY + ly;
                    if (x >= size || y >= size) continue;

                    float u = (lx + 0.5f) / cellSize;
                    float v = (ly + 0.5f) / cellSize;

                    float variation = (Fbm(u, v, baseFreq: 5, octaves: 2) - 0.5f) * 0.10f;
                    float m = 1f + variation;

                    float edge = Mathf.Max(Mathf.Abs(u - 0.5f) / 0.5f, Mathf.Abs(v - 0.5f) / 0.5f);
                    float seam = Curve(1f - ReefDeckSeamFraction, 1f, edge);
                    m = Mathf.Lerp(m, ReefDeckSeamMultiplier, seam);

                    float rivet = ReefDeckRivetMultiplier(u, v);
                    if (rivet >= 0f) m = rivet;

                    float grate = ReefDeckGrateMultiplier(u, v);
                    if (grate >= 0f) m = Mathf.Min(m, grate);

                    m *= jitterMul;

                    byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(m) * 255f);
                    px[y * size + x] = new Color32(g, g, g, 255);
                }
            }

            t.SetPixels32(px);
            t.Apply(updateMipmaps: true);
            s_cache[key] = t;
            return t;
        }

        /// <summary>-1 where no rivet reaches; otherwise the multiplier to use. Never above 1: a
        /// texture multiply can only darken relative to _BaseColor, never brighten past it, so the
        /// rivet's own disc reads at the plate's full (untouched) value rather than as a highlight.
        /// </summary>
        private static float ReefDeckRivetMultiplier(float u, float v)
        {
            float best = -1f;
            foreach (float cx in new[] { ReefDeckRivetInset, 1f - ReefDeckRivetInset })
            foreach (float cy in new[] { ReefDeckRivetInset, 1f - ReefDeckRivetInset })
            {
                float d = Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy));
                if (d <= ReefDeckRivetRadius) return 1f;
                if (d <= ReefDeckRivetRadius * 1.6f)
                {
                    float ring = 1f - Curve(ReefDeckRivetRadius, ReefDeckRivetRadius * 1.6f, d);
                    float m = Mathf.Lerp(1f, ReefDeckRivetRingMultiplier, ring);
                    if (best < 0f || m < best) best = m;
                }
            }
            return best;
        }

        private static float ReefDeckGrateMultiplier(float u, float v)
        {
            if (u < ReefDeckGrateMin || u > ReefDeckGrateMax || v < ReefDeckGrateMin || v > ReefDeckGrateMax)
                return -1f;

            float lu = (u - ReefDeckGrateMin) / (ReefDeckGrateMax - ReefDeckGrateMin);
            float lv = (v - ReefDeckGrateMin) / (ReefDeckGrateMax - ReefDeckGrateMin);
            float barU = Mathf.Abs(Frac(lu * ReefDeckGrateBars) - 0.5f) * 2f;
            float barV = Mathf.Abs(Frac(lv * ReefDeckGrateBars) - 0.5f) * 2f;
            float bar = Mathf.Max(Curve(0.7f, 1f, barU), Curve(0.7f, 1f, barV));
            return Mathf.Lerp(0.85f, ReefDeckGrateBarMultiplier, bar);
        }

        /// <summary>The deck's emissive mask (MV-1256 rework of MV-1053's own single-pattern bake, since
        /// thinned and sparsened by MV-1334): of the <see cref="WorldMaterials.ReefDeckGridCells"/>-square
        /// supertile's cells, only the ~1 in 4 <see cref="ReefDeckPatternIndexAt"/>'s own seeded hash
        /// marks <see cref="ReefDeckLitPatternIndex"/> draws an inlay at all (MV-1334 ticket, Change
        /// item 1) — every other cell stays plain black plate, seams only from the albedo. Cyan is the
        /// dominant colour; <see cref="DrawDeckInlayPattern"/>'s own per-element hash swaps roughly 1
        /// trace in 8 to violet or magenta (MV-1256 ticket: "about 1 in 8 traces is magenta or violet").
        /// Baked as a real colour, not a multiplier — <see cref="WorldMaterials.M_ShipFloor"/> leaves
        /// _EmissionColor white so this paints the floor's glow directly.</summary>
        public static Texture2D ReefDeckEmission(int size = 256)
        {
            const string key = "reefDeckEmission";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var t = NewTexture(key, size, linear: false);
            var px = new Color32[size * size];
            var black = new Color32(0, 0, 0, 255);
            for (int i = 0; i < px.Length; i++) px[i] = black;

            int cells = WorldMaterials.ReefDeckGridCells;
            int cellSize = Mathf.Max(1, size / cells);

            for (int gy = 0; gy < cells; gy++)
            for (int gx = 0; gx < cells; gx++)
            {
                // MV-1334: only the ~1-in-4 cells the hash marks "lit" carry an inlay now; every
                // other cell stays plain black plate (seams only, from the albedo).
                if (ReefDeckPatternIndexAt(gx, gy) != ReefDeckLitPatternIndex) continue;
                DrawDeckInlayPattern(px, size, gx * cellSize, gy * cellSize, cellSize, gx, gy);
            }

            t.SetPixels32(px);
            t.Apply(updateMipmaps: true);
            s_cache[key] = t;
            return t;
        }

        /// <summary>The one inlay layout a "lit" cell draws (MV-1334 ticket, Change item 1: "remove the
        /// round nodes/dots"). MV-1256's four node-bearing pattern variants are gone - <see cref="ReefDeckPatternIndexAt"/>'s
        /// roll now only decides whether a cell is lit at all (<see cref="ReefDeckEmission"/>'s own
        /// check), so there is nothing left to vary the layout BY once a cell is picked. Every trace
        /// still asks <paramref name="gx"/>/<paramref name="gy"/>'s own per-element hash for its colour,
        /// so the same cell always rolls the same accents (deterministic across Editor/WebGL/CI, same
        /// reasoning as every other seeded-hash pick in this project).</summary>
        private static void DrawDeckInlayPattern(Color32[] px, int texSize, int originX, int originY,
            int cellSize, int gx, int gy)
        {
            // MV-1265: this used to be ReefDeckSeamFraction * 0.5f (~0.015, right against the cell's own
            // edge) - fine while the edge trace was only a couple of texels thick, but the widened
            // stroke clips almost entirely off the cell at that inset. 0.13 leaves enough margin for the
            // stroke to sit fully inside the cell while still reading as edge-hugging.
            float half = Mathf.Max(ReefDeckSeamFraction * 0.5f, 0.13f);
            int elementIndex = 0;

            Color32 NextColor()
            {
                uint h = ReefHash($"deckaccent_{gx}_{gy}_{elementIndex}");
                elementIndex++;
                if ((h % 8u) != 0u) return (Color32)WorldMaterials.ReefFloorCircuitCyan;
                return (h % 16u) < 8u
                    ? (Color32)WorldMaterials.ReefFloorCircuitViolet
                    : (Color32)WorldMaterials.ReefKelpMagenta;
            }

            DrawCellTraceV(px, texSize, originX, originY, cellSize, half, NextColor());
            DrawCellTraceH(px, texSize, originX, originY, cellSize, half, NextColor());
        }

        /// <summary>A vertical trace confined to one cell's own sub-rectangle of the texture, at local
        /// U <paramref name="localU"/> (0..1 within the cell) — the supertile-scoped counterpart to
        /// <see cref="DrawTraceAlongV"/>, which draws across the WHOLE image and so can't be reused
        /// once a bake holds several independently-patterned cells.</summary>
        private static void DrawCellTraceV(Color32[] px, int texSize, int originX, int originY, int cellSize,
            float localU, Color32 c)
        {
            int x = originX + Mathf.Clamp(Mathf.RoundToInt(localU * cellSize), 0, cellSize - 1);
            int half = Mathf.Max(1, Mathf.RoundToInt(cellSize * ReefDeckTraceHalfWidthFraction));
            for (int ly = 0; ly < cellSize; ly++)
            {
                int y = originY + ly;
                if (y >= texSize) continue;
                for (int dx = -half; dx <= half; dx++)
                {
                    int xx = x + dx;
                    if (xx < originX || xx >= originX + cellSize || xx >= texSize) continue;
                    px[y * texSize + xx] = c;
                }
            }
        }

        private static void DrawCellTraceH(Color32[] px, int texSize, int originX, int originY, int cellSize,
            float localV, Color32 c)
        {
            int y = originY + Mathf.Clamp(Mathf.RoundToInt(localV * cellSize), 0, cellSize - 1);
            if (y >= texSize) return;
            int half = Mathf.Max(1, Mathf.RoundToInt(cellSize * ReefDeckTraceHalfWidthFraction));
            for (int lx = 0; lx < cellSize; lx++)
            {
                int x = originX + lx;
                if (x >= texSize) continue;
                for (int dy = -half; dy <= half; dy++)
                {
                    int yy = y + dy;
                    if (yy < originY || yy >= originY + cellSize || yy >= texSize) continue;
                    px[yy * texSize + x] = c;
                }
            }
        }

        private const float ReefFloodCausticAmplitude = 0.18f;   // MV-1256: "slow caustic shimmer" — a soft tileable multiplier, same mean-1 idiom as ReefDeckAlbedo

        /// <summary>The Reef's own flooded-deck water (MV-1256, Change item 5): a soft, tileable
        /// caustic-shimmer MULTIPLIER (mean ~1) against <see cref="WorldMaterials.M_FloodWater"/>'s own
        /// _BaseColor — never a colour in its own right, same "texture over shader" idiom
        /// <see cref="ReefDeckAlbedo"/> already uses for the deck. <c>MaxWorlds.Arena.ReefFloorDetail.DressFlood</c>
        /// scrolls it slowly via the tile's own existing <c>SludgeFlow</c>, which is what reads as the
        /// shimmer actually moving.</summary>
        public static Texture2D ReefFloodWaterAlbedo(int size = 128)
        {
            const string key = "reefFloodWaterAlbedo";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var t = NewTexture(key, size, linear: true);
            var px = new Color32[size * size];

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float v = (y + 0.5f) / size;
                float blotch = Fbm(u, v, baseFreq: 3, octaves: 3);
                float m = 1f + (blotch - 0.5f) * 2f * ReefFloodCausticAmplitude;
                byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(m) * 255f);
                px[y * size + x] = new Color32(g, g, g, 255);
            }

            t.SetPixels32(px);
            t.Apply(updateMipmaps: true);
            s_cache[key] = t;
            return t;
        }

        /// <summary>Stable, cross-platform string hash (FNV-1a) — same algorithm and "never
        /// <c>string.GetHashCode</c>, which .NET process-randomises" reasoning as
        /// <c>ReefKit.StableHash</c>, which this can't call directly (a private member of a different
        /// class in a different file); kept as its own small copy rather than widening that one's
        /// visibility for a single pure function.</summary>
        private static uint ReefHash(string s)
        {
            uint hash = 2166136261u;
            if (s != null)
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
            return hash;
        }

        /// <summary>World 3's ocean void backdrop (MV-1054, change item 1): a baked radial gradient —
        /// <see cref="WorldMaterials.ReefOceanVoidCenter"/> at the centre fading to
        /// <see cref="WorldMaterials.ReefOceanVoidEdge"/> at the edge — with soft diagonal light
        /// shafts, a scatter of pale fish specks, and two faint dark silhouettes (a manta, a wreck
        /// spar) standing in for distant shapes. Baked once: the plane never moves or relights, so
        /// there is nothing here a shader needs to recompute per frame (ticket's own "textures/shaders
        /// over meshes" instruction).</summary>
        public static Texture2D OceanVoidAlbedo(int size = 512)
        {
            const string key = "oceanVoidAlbedo";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var t = NewTexture(key, size, linear: false);
            var px = new Color32[size * size];

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float v = (y + 0.5f) / size;

                float d = Mathf.Clamp01(Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) / 0.7071068f);
                Color c = Color.Lerp(WorldMaterials.ReefOceanVoidCenter, WorldMaterials.ReefOceanVoidEdge,
                    Curve(0.15f, 0.9f, d));

                // Soft diagonal light shafts: a low-frequency band along one diagonal, faded out
                // toward the edge so a shaft never washes the gradient's own dark rim out to flat
                // colour.
                float diag = (u + v) * 0.5f;
                float shaft = Mathf.Max(0f, Mathf.Sin(diag * Mathf.PI * 6f)) * (1f - d);
                c = Color.Lerp(c, Color.white, shaft * 0.10f);

                // Fish specks: sparse bright pinpricks from a high-frequency hash, independent of the
                // gradient/shaft layers.
                float speck = Fbm(u, v, baseFreq: 60, octaves: 1);
                if (speck > 0.93f) c = Color.Lerp(c, Color.white, (speck - 0.93f) / 0.07f * 0.6f);

                c += OceanVoidSilhouette(u, v);

                px[y * size + x] = new Color32(
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f),
                    255);
            }

            t.SetPixels32(px);
            t.Apply(updateMipmaps: true);
            s_cache[key] = t;
            return t;
        }

        /// <summary>Darkens two fixed spots into faint silhouettes — a wide, flat ellipse standing in
        /// for a distant manta, and a thin vertical sliver for a wreck spar — subtracted from the
        /// gradient rather than painted as a flat shape, so each only ever darkens, never flattens, the
        /// gradient underneath.</summary>
        private static Color OceanVoidSilhouette(float u, float v)
        {
            Color delta = Color.black;

            float mu = (u - 0.28f) / 0.16f, mv = (v - 0.62f) / 0.05f;
            float manta = mu * mu + mv * mv;
            if (manta < 1f) delta -= new Color(0.10f, 0.10f, 0.10f) * (1f - manta);

            float su = (u - 0.74f) / 0.015f, sv = (v - 0.30f) / 0.22f;
            float spar = su * su + sv * sv;
            if (spar < 1f) delta -= new Color(0.07f, 0.07f, 0.07f) * (1f - spar);

            return delta;
        }

        // ------------------------------------------------------------------ YT-77: the materials
        //
        // Everything below follows the shape YT-69 proved on the lawn: build ONE height field, then
        // derive the albedo mask and the normal map from it. That order is the whole trick. A surface
        // whose paint and whose relief were generated independently has the light breaking in places
        // the colour says are flat, and the eye reads the result as noise printed on glass rather
        // than as timber. Sharing the field means a groove between two planks is dark BECAUSE it is
        // a groove.
        //
        // These are sampled triplanar in world space (see StylizedSurface.shader), so U runs along
        // one world axis and V along another. Grain authored to run along V therefore stands VERTICAL
        // on a vertical face — palings and shed planks stand up, which is how timber is built.

        /// <summary>Sawn timber: planks running along V, with the grain, the grooves between them, and
        /// the odd knot.</summary>
        private static float[] WoodHeight(int size)
        {
            const int Planks = 5;   // across the tile — a plank lands ~15 cm wide at the shipped scale

            return Height($"woodH{size}", size, (u, v) =>
            {
                // Grain: a lattice stretched hard along V. Sampling many cells across U and few along
                // it is what turns value noise into fibre running one way instead of blobs going
                // nowhere — the same stretched-lattice trick the lawn uses for blades.
                float grain = Noise(u * 44f, v * 5f, 44, 5) * 0.62f
                            + Noise(u * 96f, v * 11f, 96, 11) * 0.38f;

                // The groove between two boards. Narrow and hard: a plank edge is a shadow line, and
                // softening it just reads as a stain down the middle of one board.
                float acrossPlank = u * Planks;
                float edge = Mathf.Abs(Frac(acrossPlank) - 0.5f) * 2f;   // 0 mid-plank, 1 at the join
                float groove = Curve(0.84f, 1f, edge);

                // Each plank cut from its own board, so no two are quite the same tone. Without this
                // the fence reads as one milled sheet with lines scored across it.
                float board = Hash(Mathf.FloorToInt(acrossPlank), 0, Planks, 1);

                float knot = Curve(0.78f, 0.95f, Fbm(u, v, baseFreq: 3, octaves: 2));   // rare, dark

                float h = 0.30f + grain * 0.34f + board * 0.16f;
                h *= Mathf.Lerp(1f, 0.40f, groove);
                h *= Mathf.Lerp(1f, 0.62f, knot);
                return h;
            });
        }

        /// <summary>Cut stone: slabs laid in a bond, with chipped, worn faces.</summary>
        private static float[] StoneHeight(int size)
        {
            // EVEN, and that is load-bearing. The courses are offset half a slab on alternate rows, so
            // the parity of the row index has to survive the wrap at the tile's edge — with an odd
            // count, row 0 and row N would disagree about whether they are offset, and the paving would
            // grow a seam straight across the yard.
            const int Cells = 6;

            return Height($"stoneH{size}", size, (u, v) =>
            {
                int cy = Mathf.FloorToInt(v * Cells);

                // A running bond, like paving is actually laid. It is also what stops a lattice of
                // slabs reading as a chessboard.
                //
                // The first cut of this jittered each slab's edges instead, which looked better and
                // did not tile: the jitter is a function of the CELL, so the two cells that meet at
                // the tile's boundary jittered their shared edge in different directions, and the
                // texture no longer wrapped. A half-slab offset is irregular enough and wraps exactly.
                float bond = (cy & 1) * 0.5f;

                int cx = Mathf.FloorToInt(u * Cells + bond);
                float slab = Hash(cx, cy, Cells, Cells);                     // each slab its own tone

                // The chipped line between slabs.
                float ex = Mathf.Abs(Frac(u * Cells + bond) - 0.5f) * 2f;
                float ey = Mathf.Abs(Frac(v * Cells) - 0.5f) * 2f;
                float gap = Curve(0.76f, 1f, Mathf.Max(ex, ey));   // 1 only hard against a slab's edge

                // Pitting across the face — stone is not a polished plane.
                float pit = Fbm(u, v, baseFreq: 22, octaves: 3);

                float h = 0.42f + slab * 0.30f + (pit - 0.5f) * 0.24f;
                h *= Mathf.Lerp(1f, 0.45f, gap);
                return h;
            });
        }

        /// <summary>Turned soil and gravel: clods, with grit through them.</summary>
        private static float[] DirtHeight(int size)
        {
            return Height($"dirtH{size}", size, (u, v) =>
            {
                float clod = Fbm(u, v, baseFreq: 9, octaves: 3);
                float grit = Fbm(u, v, baseFreq: 38, octaves: 2);

                // Gravel: the top of the grit range pulled out into distinct stones. A flat FBM alone
                // reads as sandpaper; what makes it gravel is that a few grains sit proud of the rest.
                float stones = Curve(0.62f, 0.9f, grit);

                return 0.26f + clod * 0.40f + grit * 0.18f + stones * 0.22f;
            });
        }

        /// <summary>Worn painted metal: a machine's panels, its rivets, and the paint coming off it.
        ///
        /// This one is a GREYSCALE wear map and nothing more, because the only thing wearing it is
        /// the Mower Hutch — and gameplay owns the Hutch's colour (hazard-orange, damage tint, hit
        /// flash, all pushed through a MaterialPropertyBlock on _BaseColor). Carrying a colour here
        /// would multiply against that and fight it for the hue. So this decides where the paint is
        /// thin; gameplay decides what colour the paint is.</summary>
        private static float[] MetalHeight(int size)
        {
            const int Panels = 3;
            const int Rivets = 8;

            return Height($"metalH{size}", size, (u, v) =>
            {
                // Panel seams, running the other way to the timber's grain, so a metal box never reads
                // as a wooden one at a glance.
                float alongPanel = v * Panels;
                float edge = Mathf.Abs(Frac(alongPanel) - 0.5f) * 2f;
                float seam = Curve(0.88f, 1f, edge);

                // Rivets: a lattice of dots sitting proud of the plate, thickest along the seams.
                float du = Frac(u * Rivets) - 0.5f;
                float dv = Frac(alongPanel) - 0.5f;
                float d = Mathf.Sqrt(du * du + dv * dv) * 2f;
                float rivet = 1f - Curve(0.16f, 0.30f, d);

                // Paint wear: broad patches where the coat has gone thin, plus fine scratching.
                float wear = Fbm(u, v, baseFreq: 7, octaves: 3);
                float scratch = Noise(u * 70f, v * 9f, 70, 9);

                float h = 0.72f + (wear - 0.5f) * 0.34f + (scratch - 0.5f) * 0.12f;
                h *= Mathf.Lerp(1f, 0.66f, seam);
                h += rivet * 0.20f;
                return h;
            });
        }

        /// <summary>Leaf mass — soft, clumped, and shallow. Deliberately the gentlest of the set: a
        /// hedge wants to catch the light in clumps, not to look like it has been carved.</summary>
        private static float[] FoliageHeight(int size)
        {
            return Height($"foliageH{size}", size, (u, v) =>
            {
                float leaves = Fbm(u, v, baseFreq: 16, octaves: 3);
                float clump = Fbm(u, v, baseFreq: 6, octaves: 2);
                return 0.34f + leaves * 0.42f + clump * 0.24f;
            });
        }

        /// <summary>MV-1205: the City road's asphalt — fine grain plus a scatter of faint repair
        /// patches 1-3 m across within the tile (the tile itself is ~4 m, per
        /// <see cref="MaterialLibrary"/>'s own Asphalt <c>DetailScale</c>). Deliberately NOT built
        /// the lawn's way (<see cref="GroundHeight"/>): that generator suppresses low frequencies on
        /// purpose, because a tiled BLOTCH is what makes grass look like wallpaper. A repair patch is
        /// the opposite case — the ticket asks for it to be SEEN, not hidden — so this keeps the
        /// patch layer un-averaged (a single thresholded noise, not an FBM blur) to hold the contrast
        /// a flattened sum would otherwise wash out, and that is also what pushes AC1's std-dev floor
        /// past the grain alone.</summary>
        private static float[] AsphaltHeight(int size)
        {
            return Height($"asphaltH{size}", size, (u, v) =>
            {
                // A single un-averaged noise layer, not FBM: averaging octaves is exactly what tames
                // variance toward the centre, and a flattened grain is the pre-fix defect this ticket
                // exists to undo (AC1's own std-dev floor is measuring precisely this).
                float grain = Noise(u * 30f, v * 30f, 30, 30);

                // baseFreq 3 over the kind's own ~4 m tile puts a patch cell at ~1.3 m — inside the
                // ticket's 1-3 m range. A single un-averaged octave plus a NARROW threshold band
                // carves distinct, mostly-binary patches rather than a soft gradient — bimodal mass
                // at the extremes is what a bounded 0..1 range needs to carry real variance.
                float patchRaw = Fbm(u, v, baseFreq: 3, octaves: 1);
                float patch = Curve(0.50f, 0.62f, patchRaw);

                return grain * 0.35f + patch * 0.65f;
            });
        }

        /// <summary>MV-1205: the City's paving slabs — a running bond of 1.0 x 0.6 m slabs (6 x 10
        /// cells across the Pavement kind's own 6 m tile, see <see cref="MaterialLibrary"/>'s
        /// Pavement <c>DetailScale</c>), 3 cm darker joints, each slab its own tone. The row count
        /// (10) is EVEN for the same reason <see cref="StoneHeight"/>'s own <c>Cells</c> is: the
        /// half-slab offset alternates by row parity, and an odd row count disagrees with itself
        /// about that parity across the tile's own wrap.</summary>
        private static float[] PavingSlabHeight(int size)
        {
            const int CellsU = 6;          // 1.0 m slabs at a 6 m tile
            const int CellsV = 10;         // 0.6 m slabs — EVEN, see summary
            const float JointFracU = 0.03f;  // 3 cm joint / 1.0 m slab width
            const float JointFracV = 0.05f;  // 3 cm joint / 0.6 m slab height

            return Height($"pavingH{size}", size, (u, v) =>
            {
                int cy = Mathf.FloorToInt(v * CellsV);
                float bond = (cy & 1) * 0.5f;          // running bond: alternate rows offset half a slab

                int cx = Mathf.FloorToInt(u * CellsU + bond);
                float slab = Hash(cx, cy, CellsU, CellsV);

                float ex = Mathf.Abs(Frac(u * CellsU + bond) - 0.5f) * 2f;
                float ey = Mathf.Abs(Frac(v * CellsV) - 0.5f) * 2f;
                float gapU = Curve(1f - JointFracU, 1f, ex);
                float gapV = Curve(1f - JointFracV, 1f, ey);
                float gap = Mathf.Max(gapU, gapV);

                float pit = Fbm(u, v, baseFreq: 18, octaves: 2);

                float h = 0.5f + slab * 0.26f + (pit - 0.5f) * 0.10f;
                h *= Mathf.Lerp(1f, 0.30f, gap);       // the joint reads darker than the slab it cuts
                return h;
            });
        }

        /// <summary>The greyscale mask for a surface — a *mask*, selecting between two tones (see
        /// <see cref="Blend"/>), not an albedo. Null for kinds with no material of their own.</summary>
        public static Texture2D MaskFor(SurfaceKind kind, int size = 256)
        {
            var h = HeightFor(kind, size);
            if (h == null) return Surface();

            string key = $"mask:{kind}:{size}";
            if (s_cache.TryGetValue(key, out var t) && t != null) return t;

            t = NewTexture(key, size, linear: false);

            var px = new Color32[h.Length];
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

        /// <summary>The normal map for a surface, derived from the same height field its mask is
        /// painted from. This is the half that makes the light break — without it a fence panel is a
        /// flat plane wearing a picture of wood, which is precisely the state YT-69 found the lawn in.
        /// </summary>
        public static Texture2D NormalFor(SurfaceKind kind, int size = 256, float strength = 2.2f)
        {
            var h = HeightFor(kind, size);
            if (h == null) return null;

            string key = $"normal:{kind}:{size}";
            if (s_cache.TryGetValue(key, out var t) && t != null) return t;

            t = NormalFromHeight(key, h, size, strength);
            s_cache[key] = t;
            return t;
        }

        private static float[] HeightFor(SurfaceKind kind, int size)
        {
            switch (kind)
            {
                case SurfaceKind.Wood:
                case SurfaceKind.Wall:      // the yard's walls ARE its fences and shed — see BiomePalette
                    return WoodHeight(size);
                case SurfaceKind.Stone:
                    return StoneHeight(size);
                case SurfaceKind.Dirt:
                    return DirtHeight(size);
                case SurfaceKind.Metal:
                    return MetalHeight(size);
                case SurfaceKind.Foliage:
                    return FoliageHeight(size);
                case SurfaceKind.Asphalt:
                    return AsphaltHeight(size);
                case SurfaceKind.Pavement:
                    return PavingSlabHeight(size);
                default:
                    return null;            // Ground has its own; Prop keeps the plain grain
            }
        }

        /// <summary>
        /// Build (or fetch) a height field, STRETCHED to fill 0..1.
        ///
        /// The normalisation is not tidiness, it is the whole reason these surfaces read. A generator
        /// that happens to land in, say, 0.25..0.55 is only using a third of the range — and since the
        /// albedo is a lerp across that mask and the normal map is its gradient, a third of the range
        /// means a third of the contrast and a third of the relief. The first cut of the timber did
        /// exactly that and came out as a faint stripe on a flat brown plank.
        ///
        /// Stretching here means each generator only has to get the SHAPE of its surface right, and
        /// how hard that surface is felt is set in one place — the material's Contrast and its normal
        /// strength — instead of being an accident of the constants inside the generator.
        /// </summary>
        private static float[] Height(string key, int size, System.Func<float, float, float> f)
        {
            if (s_heights.TryGetValue(key, out var cached)) return cached;

            var h = new float[size * size];
            float min = float.MaxValue, max = float.MinValue;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float v = f((x + 0.5f) / size, (y + 0.5f) / size);
                h[y * size + x] = v;
                if (v < min) min = v;
                if (v > max) max = v;
            }

            float span = max - min;
            if (span > 1e-5f)
            {
                float inv = 1f / span;
                for (int i = 0; i < h.Length; i++) h[i] = (h[i] - min) * inv;
            }

            s_heights[key] = h;
            return h;
        }

        private static float Frac(float v) => v - Mathf.Floor(v);

        /// <summary>
        /// HLSL's <c>smoothstep(edge0, edge1, x)</c>: 0 below <paramref name="edge0"/>, 1 above
        /// <paramref name="edge1"/>, eased between.
        ///
        /// NOT <see cref="Mathf.SmoothStep"/>, which is a different function wearing the same name —
        /// it interpolates BETWEEN its first two arguments, so <c>Mathf.SmoothStep(0f, 0.16f, x)</c>
        /// returns a number in 0..0.16 rather than a 0..1 mask. Reaching for it here is how the first
        /// pass at the timber ended up dimming every plank by half instead of cutting a groove between
        /// them: the fence had no seams and nobody could see why.
        /// </summary>
        private static float Curve(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Normal map from a height field by central differences, taps wrapped so the normal
        /// map has no seam where the albedo doesn't. Plain linear RGB — NOT DXT5nm-swizzled; the
        /// shaders that read these unpack them by hand to match.</summary>
        private static Texture2D NormalFromHeight(string key, float[] h, int size, float strength)
        {
            var t = NewTexture(key, size, linear: true);

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
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
            return t;
        }

        /// <summary>Drop every generated texture the palette actually made stale.
        ///
        /// The <c>"albedo:tint:"</c> entries are exempt (MV-738), matching <see cref="MaterialLibrary.Clear"/>'s
        /// own exemption for the <see cref="MaterialLibrary.Tinted"/> materials that reference them —
        /// destroying the texture out from under a material this project deliberately kept alive would
        /// just trade one orphaned reference (magenta) for another (a Tinted surface gone flat white,
        /// since <c>_BaseColor</c> alone carries no tone for these — the albedo texture is the tone).
        ///
        /// <c>"reefDeck"</c> (MV-1053) is deliberately NOT exempt the same way, even though
        /// <see cref="WorldMaterials.M_ShipFloor"/> is ALSO a permanently-cached material that
        /// references one of these: unlike a Tinted albedo, a deck multiplier/emission mask carries
        /// no biome colour of its own, so there is no equivalent reason for it to outlive every other
        /// generated texture in the project. <see cref="WorldMaterials"/>'s own cache instead
        /// self-heals — it notices a destroyed base map on next access and re-points the (still-live)
        /// material at a freshly rebuilt one — so this Clear() can sweep these two exactly like every
        /// other entry here without leaving anything dangling.</summary>
        /// <summary>MV-1103 test seam: when set, <see cref="Clear"/> hands a swept texture here
        /// instead of destroying it, so an EditMode test can simulate a running player's deferred
        /// <c>Object.Destroy</c> — the destroy actually lands later, under the test's own control,
        /// rather than synchronously inside this call the way <c>DestroyImmediate</c> always does in
        /// the editor. Nothing in the game sets this.</summary>
        public static System.Action<Texture2D> DestroyOverrideForTests;

        public static void Clear()
        {
            var keys = new List<string>(s_cache.Keys);
            foreach (var key in keys)
            {
                if (key.StartsWith("albedo:tint:", System.StringComparison.Ordinal)) continue;

                var t = s_cache[key];
                s_cache.Remove(key);
                if (t == null) continue;
                if (DestroyOverrideForTests != null) DestroyOverrideForTests(t);
                else if (Application.isPlaying) Object.Destroy(t);
                else Object.DestroyImmediate(t);
            }
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
