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

        private const float ReefDeckRivetInset = 0.09f;      // how far a rivet sits from the plate's corner
        private const float ReefDeckRivetRadius = 0.045f;
        private const float ReefDeckRivetRingMultiplier = 0.7f;

        // A grate inset, centred and clear of both the seam band and the corner rivets — "occasional"
        // within the repeating tile rather than across separate plates, since one image here IS one
        // plate (see the section note above): there is nowhere else for "occasional" to live.
        private const float ReefDeckGrateMin = 0.37f, ReefDeckGrateMax = 0.63f;
        private const int ReefDeckGrateBars = 5;
        private const float ReefDeckGrateBarMultiplier = 0.50f;

        /// <summary>The deck's one 2 m plate, as a greyscale MULTIPLIER against <c>M_ShipFloor</c>'s
        /// own _BaseColor: ~1 across the face with a little tileable per-plate value noise (the
        /// ticket's "±5% per-plate value variation"), a dark border at the plate's own seam, a small
        /// full-value rivet disc ringed darker at each corner, and one grate inset.</summary>
        public static Texture2D ReefDeckAlbedo(int size = 256)
        {
            const string key = "reefDeckAlbedo";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var t = NewTexture(key, size, linear: true);
            var px = new Color32[size * size];

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float v = (y + 0.5f) / size;

                float variation = (Fbm(u, v, baseFreq: 5, octaves: 2) - 0.5f) * 0.10f;
                float m = 1f + variation;

                float edge = Mathf.Max(Mathf.Abs(u - 0.5f) / 0.5f, Mathf.Abs(v - 0.5f) / 0.5f);
                float seam = Curve(1f - ReefDeckSeamFraction, 1f, edge);
                m = Mathf.Lerp(m, ReefDeckSeamMultiplier, seam);

                float rivet = ReefDeckRivetMultiplier(u, v);
                if (rivet >= 0f) m = rivet;

                float grate = ReefDeckGrateMultiplier(u, v);
                if (grate >= 0f) m = Mathf.Min(m, grate);

                byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(m) * 255f);
                px[y * size + x] = new Color32(g, g, g, 255);
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

        /// <summary>The deck's emissive mask: sparse cyan circuit traces along two of the plate's
        /// four seams (never all four — the ticket's own "running along SOME seams"), small brighter
        /// nodes along them, and one violet node. Baked as a real colour, not a multiplier —
        /// <see cref="WorldMaterials.M_ShipFloor"/> leaves _EmissionColor white so this paints the
        /// floor's glow directly.</summary>
        public static Texture2D ReefDeckEmission(int size = 256)
        {
            const string key = "reefDeckEmission";
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var t = NewTexture(key, size, linear: false);
            var px = new Color32[size * size];
            var black = new Color32(0, 0, 0, 255);
            var cyan = (Color32)WorldMaterials.ReefFloorCircuitCyan;
            var violet = (Color32)WorldMaterials.ReefFloorCircuitViolet;

            for (int i = 0; i < px.Length; i++) px[i] = black;

            float traceHalfWidth = ReefDeckSeamFraction * 0.5f;
            DrawTraceAlongV(px, size, u: traceHalfWidth, cyan);
            DrawTraceAlongU(px, size, v: 1f - traceHalfWidth, cyan);

            PutNode(px, size, traceHalfWidth, 0.30f, cyan, radius: 0.02f);
            PutNode(px, size, traceHalfWidth, 0.70f, cyan, radius: 0.02f);
            PutNode(px, size, 0.40f, 1f - traceHalfWidth, cyan, radius: 0.02f);
            PutNode(px, size, 0.75f, 1f - traceHalfWidth, violet, radius: 0.022f);   // the odd violet node

            t.SetPixels32(px);
            t.Apply(updateMipmaps: true);
            s_cache[key] = t;
            return t;
        }

        private static void DrawTraceAlongV(Color32[] px, int size, float u, Color32 c)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(u * size), 0, size - 1);
            int half = Mathf.Max(1, Mathf.RoundToInt(size * 0.006f));
            for (int y = 0; y < size; y++)
            for (int dx = -half; dx <= half; dx++)
                px[y * size + Wrap(x + dx, size)] = c;
        }

        private static void DrawTraceAlongU(Color32[] px, int size, float v, Color32 c)
        {
            int y = Mathf.Clamp(Mathf.RoundToInt(v * size), 0, size - 1);
            int half = Mathf.Max(1, Mathf.RoundToInt(size * 0.006f));
            for (int x = 0; x < size; x++)
            for (int dy = -half; dy <= half; dy++)
                px[Wrap(y + dy, size) * size + x] = c;
        }

        private static void PutNode(Color32[] px, int size, float u, float v, Color32 c, float radius)
        {
            int cx = Mathf.RoundToInt(u * size);
            int cy = Mathf.RoundToInt(v * size);
            int r = Mathf.Max(1, Mathf.RoundToInt(radius * size));
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > r * r) continue;
                px[Wrap(cy + dy, size) * size + Wrap(cx + dx, size)] = c;
            }
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
        public static void Clear()
        {
            var keys = new List<string>(s_cache.Keys);
            foreach (var key in keys)
            {
                if (key.StartsWith("albedo:tint:", System.StringComparison.Ordinal)) continue;

                var t = s_cache[key];
                s_cache.Remove(key);
                if (t == null) continue;
                if (Application.isPlaying) Object.Destroy(t);
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
