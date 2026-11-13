using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// MV-1350 (Reef relook 2/4): the Reef deck. One boot-generated atlas (no image files) of 2 m
    /// riveted hull plates, one per-area mesh of 2 m quads picking cells from it, one batched
    /// alpha-blended wear mesh per area (stains, silt drifts, walkway dashes, stencilled deck number)
    /// and a few flat, collider-free floor props in the 2.5 m band along the walls.
    ///
    /// Assembly note: lives in Rendering (mesh/material/texture only). WHICH areas get a deck is
    /// <c>MaxWorlds.Arena.ReefFloorDetail.PlaceDeck</c>'s job.
    ///
    /// Winding: Unity is clockwise-front (the MV-1268 lesson). Every floor quad here is emitted
    /// bottom-left, top-left, top-right, bottom-right as seen from above (x right, z up), which gives
    /// <c>cross(v1 - v0, v2 - v0).y &gt; 0</c> — an up-facing face.
    /// </summary>
    public static class ReefDeckKit
    {
        // ---- layout ---------------------------------------------------------------------------

        /// <summary>Deck atlas grid: 3 x 3 cells, one 2 m plate variant per cell.</summary>
        public const int DeckCols = 3, DeckRows = 3;
        public const int CellPlain0 = 0, CellPlain1 = 1, CellPlain2 = 2, CellTread = 3, CellVent = 4,
            CellHatch = 5, CellManhole = 6, CellDrain = 7, CellScratched = 8;

        /// <summary>Wear atlas grid: 6 x 4 cells.</summary>
        public const int WearCols = 6, WearRows = 4;
        private const int WearRust = 0, WearWet = 1, WearOil = 2, WearSilt = 3, WearDash = 4, WearGlyphBase = 5;
        private const string GlyphChars = "DECK0123456789-+";   // '+' is the mid-dot

        public const float PlateMetres = 2f;
        /// <summary>The deck sits this far above the map's own floor slab (rule: overlays at least 0.01 m).</summary>
        public const float DeckY = 0.015f;
        public const float WearY = 0.02f;
        public const float PropBandMetres = 2.5f;
        public const float SiltWidthMetres = 1.5f;
        public const float WalkwayInsetMetres = 2.2f;

        private const int DeckCellPx = 128;
        private const int WearCellPx = 48;
        private const float CellInset = 0.004f;

        private static Texture2D s_deckAtlas, s_wearAtlas;
        private static Material s_deckMat, s_wearMat, s_propMat;

        // ---- hashing --------------------------------------------------------------------------

        internal static uint Hash(string s)
        {
            uint h = 2166136261u;
            if (s != null) foreach (char c in s) { h ^= c; h *= 16777619u; }
            return h;
        }

        private static float Hash01(uint seed, int salt)
        {
            uint h = seed ^ (uint)(salt * 374761393);
            h = (h ^ (h >> 15)) * 2246822519u;
            h = (h ^ (h >> 13)) * 3266489917u;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / (float)0x1000000;
        }

        private static float Noise2(int x, int y, int salt)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        private static float SmoothNoise(float x, float y, int salt)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Noise2(xi, yi, salt), b = Noise2(xi + 1, yi, salt);
            float c = Noise2(xi, yi + 1, salt), d = Noise2(xi + 1, yi + 1, salt);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        // ---- variant choice -------------------------------------------------------------------

        /// <summary>Plate variant for one plate of one area, seeded by the area id. About 55% plain
        /// (three tones), 15% tread, 8% vent, 4% hatch, 4% manhole, 4% drain, 10% scratched.</summary>
        public static int PickCell(string areaId, int gx, int gz)
        {
            float r = Hash01(Hash($"deck_{areaId}_{gx}_{gz}"), 7);
            if (r < 0.55f) return CellPlain0 + Mathf.Min(2, Mathf.FloorToInt(r / 0.55f * 3f));
            if (r < 0.70f) return CellTread;
            if (r < 0.78f) return CellVent;
            if (r < 0.82f) return CellHatch;
            if (r < 0.86f) return CellManhole;
            if (r < 0.90f) return CellDrain;
            return CellScratched;
        }

        // ---- atlas: deck ----------------------------------------------------------------------

        private static Color C(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);

        public static Texture2D DeckAtlas()
        {
            if (s_deckAtlas != null) return s_deckAtlas;

            int w = DeckCols * DeckCellPx, h = DeckRows * DeckCellPx;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = "ReefDeckAtlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[w * h];
            for (int cell = 0; cell < DeckCols * DeckRows; cell++)
            {
                int cx = cell % DeckCols, cy = cell / DeckCols;
                for (int y = 0; y < DeckCellPx; y++)
                for (int x = 0; x < DeckCellPx; x++)
                {
                    float u = (x + 0.5f) / DeckCellPx, v = (y + 0.5f) / DeckCellPx;
                    px[(cy * DeckCellPx + y) * w + cx * DeckCellPx + x] = PaintDeck(cell, u, v);
                }
            }
            t.SetPixels32(px);
            t.Apply(updateMipmaps: false);
            s_deckAtlas = t;
            return t;
        }

        private static Color32 PaintDeck(int cell, float u, float v)
        {
            Color c;
            switch (cell)
            {
                case CellPlain0: c = C(0x3A, 0x66, 0x70); break;
                case CellPlain1: c = C(0x35, 0x5F, 0x69); break;
                case CellPlain2: c = C(0x40, 0x6D, 0x77); break;
                default: c = C(0x38, 0x62, 0x6C); break;
            }
            c *= 0.97f + 0.06f * Noise2(Mathf.FloorToInt(u * 24f), Mathf.FloorToInt(v * 24f), cell);
            c.a = 1f;

            float du = u - 0.5f, dv = v - 0.5f;
            float r = Mathf.Sqrt(du * du + dv * dv);

            switch (cell)
            {
                case CellTread:
                    if (u > 0.1f && u < 0.9f && v > 0.1f && v < 0.9f)
                    {
                        bool a = Mathf.Abs(Mathf.Repeat((u + v) * 12f, 1f) - 0.5f) < 0.10f;
                        bool b = Mathf.Abs(Mathf.Repeat((u - v) * 12f, 1f) - 0.5f) < 0.10f;
                        if (a || b) c = Color.Lerp(c, C(0x5A, 0x8C, 0x94), 0.55f);
                        else c *= 0.82f;
                    }
                    break;
                case CellVent:
                    if (u > 0.28f && u < 0.72f && v > 0.22f && v < 0.78f)
                    {
                        float s = Mathf.Repeat(v * 11f, 1f);
                        c = s < 0.55f ? C(0x0C, 0x1E, 0x24) : C(0x2C, 0x50, 0x58);
                    }
                    break;
                case CellHatch:
                {
                    float m = 0.15f, l = 0.17f, th = 0.014f;
                    bool mark = false;
                    foreach (float sx in new[] { 0f, 1f })
                    foreach (float sz in new[] { 0f, 1f })
                    {
                        float cu = sx < 0.5f ? m : 1f - m, cv = sz < 0.5f ? m : 1f - m;
                        float dirU = sx < 0.5f ? 1f : -1f, dirV = sz < 0.5f ? 1f : -1f;
                        float lu = (u - cu) * dirU, lv = (v - cv) * dirV;
                        if (lu > -th && lu < l && lv > -th && lv < th) mark = true;
                        if (lv > -th && lv < l && lu > -th && lu < th) mark = true;
                    }
                    if (mark) c = C(0xC8, 0xA8, 0x30);
                    else if (r < 0.05f) c = C(0x14, 0x28, 0x2E);
                    break;
                }
                case CellManhole:
                    if (r < 0.34f)
                    {
                        c = r > 0.30f ? C(0x14, 0x2A, 0x30) : C(0x30, 0x58, 0x60);
                        for (int i = 0; i < 8; i++)
                        {
                            float ang = i * Mathf.PI * 0.25f;
                            float bx = Mathf.Cos(ang) * 0.265f, bz = Mathf.Sin(ang) * 0.265f;
                            float d = Mathf.Sqrt((du - bx) * (du - bx) + (dv - bz) * (dv - bz));
                            if (d < 0.022f) c = C(0x7C, 0xA8, 0xAC);
                        }
                    }
                    break;
                case CellDrain:
                    if (r < 0.24f) c = r > 0.20f ? C(0x58, 0x80, 0x88) : (Mathf.Abs(Mathf.Repeat(u * 9f, 1f) - 0.5f) < 0.12f ? C(0x30, 0x50, 0x58) : C(0x04, 0x0C, 0x10));
                    break;
                case CellScratched:
                    for (int i = 0; i < 6; i++)
                    {
                        float cx = 0.15f + 0.7f * Hash01(11, i * 5), cy = 0.15f + 0.7f * Hash01(11, i * 5 + 1);
                        float ang = Hash01(11, i * 5 + 2) * Mathf.PI, len = 0.10f + 0.16f * Hash01(11, i * 5 + 3);
                        float dx = Mathf.Cos(ang), dy = Mathf.Sin(ang);
                        float along = (u - cx) * dx + (v - cy) * dy, across = -(u - cx) * dy + (v - cy) * dx;
                        if (Mathf.Abs(along) < len && Mathf.Abs(across) < 0.006f) c = Color.Lerp(c, C(0x7C, 0xA4, 0xA8), 0.6f);
                    }
                    break;
            }

            // Inner panel line (rect inset 0.1).
            float line = Mathf.Min(Mathf.Min(Mathf.Abs(u - 0.1f), Mathf.Abs(u - 0.9f)), Mathf.Min(Mathf.Abs(v - 0.1f), Mathf.Abs(v - 0.9f)));
            if (u > 0.094f && u < 0.906f && v > 0.094f && v < 0.906f && line < 0.006f) c *= 0.78f;

            // Riveted border: 6 rivets per side.
            if (u < 0.09f || u > 0.91f || v < 0.09f || v > 0.91f)
            {
                for (int i = 0; i < 6; i++)
                {
                    float t = (i + 0.5f) / 6f;
                    for (int s = 0; s < 4; s++)
                    {
                        float rx = s < 2 ? t : (s == 2 ? 0.05f : 0.95f);
                        float rz = s < 2 ? (s == 0 ? 0.05f : 0.95f) : t;
                        float d = Mathf.Sqrt((u - rx) * (u - rx) + (v - rz) * (v - rz));
                        if (d < 0.017f) c = Color.Lerp(c, C(0x8C, 0xB4, 0xB8), 0.65f);
                        else if (d < 0.025f && v < rz) c *= 0.70f;
                    }
                }
            }

            // Plate edge seam, top bevel highlight, bottom shadow.
            float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            if (edge < 0.012f) c *= 0.55f;
            else if (v > 0.968f) c = Color.Lerp(c, C(0x9C, 0xC4, 0xC8), 0.20f);
            else if (v < 0.04f) c *= 0.75f;

            // Static caustic dapple (about 2%), baked in — MV-1348's moving caustics flickered on device.
            float dap = Mathf.Max(0f, Mathf.Sin(u * 9.4f + cell) * Mathf.Sin(v * 9.4f + cell * 1.7f));
            c = Color.Lerp(c, C(0xA0, 0xE8, 0xE4), 0.02f * dap);
            c.a = 1f;
            return c;
        }

        // ---- atlas: wear ----------------------------------------------------------------------

        private static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            ['D'] = new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" },
            ['E'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
            ['C'] = new[] { "01110", "10001", "10000", "10000", "10000", "10001", "01110" },
            ['K'] = new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" },
            ['0'] = new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
            ['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
            ['2'] = new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
            ['3'] = new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" },
            ['4'] = new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
            ['5'] = new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
            ['6'] = new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" },
            ['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
            ['8'] = new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
            ['9'] = new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" },
            ['-'] = new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" },
            ['+'] = new[] { "00000", "00000", "00000", "00100", "00000", "00000", "00000" },
        };

        public static Texture2D WearAtlas()
        {
            if (s_wearAtlas != null) return s_wearAtlas;

            int w = WearCols * WearCellPx, h = WearRows * WearCellPx;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = "ReefWearAtlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[w * h];
            for (int cell = 0; cell < WearCols * WearRows; cell++)
            {
                int cx = cell % WearCols, cy = cell / WearCols;
                for (int y = 0; y < WearCellPx; y++)
                for (int x = 0; x < WearCellPx; x++)
                {
                    float u = (x + 0.5f) / WearCellPx, v = (y + 0.5f) / WearCellPx;
                    px[(cy * WearCellPx + y) * w + cx * WearCellPx + x] = PaintWear(cell, u, v);
                }
            }
            t.SetPixels32(px);
            t.Apply(updateMipmaps: false);
            s_wearAtlas = t;
            return t;
        }

        private static Color32 PaintWear(int cell, float u, float v)
        {
            Color c = new Color(0f, 0f, 0f, 0f);
            float du = u - 0.5f, dv = v - 0.5f;

            if (cell == WearRust || cell == WearWet || cell == WearOil)
            {
                float sx = cell == WearOil ? 1.5f : 1f;
                float r = Mathf.Sqrt(du * du * sx * sx + dv * dv) * 2f;
                float n = SmoothNoise(u * 5f, v * 5f, cell + 3);
                float a = Mathf.SmoothStep(1f, 0.25f, r + (n - 0.5f) * 0.7f);
                if (cell == WearRust) { c = C(0x7A, 0x42, 0x22); a *= 0.38f * (0.7f + 0.6f * Noise2(Mathf.FloorToInt(u * 16f), Mathf.FloorToInt(v * 16f), 5)); }
                else if (cell == WearWet) { c = C(0x04, 0x10, 0x16); a *= 0.34f; }
                else { c = C(0x02, 0x05, 0x08); a *= 0.50f; }
                c.a = Mathf.Clamp01(a);
            }
            else if (cell == WearSilt)
            {
                float a = Mathf.Pow(1f - v, 1.6f) * 0.32f * (0.85f + 0.15f * SmoothNoise(u * 40f, v * 3f, 9));
                c = C(0x86, 0xA4, 0x8C); c.a = a;
            }
            else if (cell == WearDash)
            {
                float a = Mathf.SmoothStep(0.36f, 0.42f, v) * (1f - Mathf.SmoothStep(0.58f, 0.64f, v)) *
                          Mathf.SmoothStep(0.03f, 0.10f, u) * (1f - Mathf.SmoothStep(0.90f, 0.97f, u));
                c = C(0xC0, 0xA4, 0x30); c.a = a * 0.30f;
            }
            else if (cell >= WearGlyphBase && cell < WearGlyphBase + GlyphChars.Length)
            {
                string[] rows = Font[GlyphChars[cell - WearGlyphBase]];
                float gu = (u - 0.12f) / 0.76f, gv = (v - 0.08f) / 0.84f;
                if (gu >= 0f && gu < 1f && gv >= 0f && gv < 1f)
                {
                    int col = Mathf.FloorToInt(gu * 5f), row = 6 - Mathf.FloorToInt(gv * 7f);
                    if (rows[row][col] == '1') { c = C(0xD0, 0xE0, 0xD8); c.a = 0.22f; }
                }
            }
            return c;
        }

        // ---- materials ------------------------------------------------------------------------

        public static Material DeckMaterial()
        {
            if (s_deckMat != null) return s_deckMat;
            Shader shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;
            var m = new Material(shader) { name = "M_ReefDeckAtlas", hideFlags = HideFlags.HideAndDontSave };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", DeckAtlas());
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", DeckAtlas());
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.28f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            s_deckMat = m;
            return m;
        }

        public static Material WearMaterial()
        {
            if (s_wearMat != null) return s_wearMat;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null || !shader.isSupported) return null;
            var m = new Material(shader) { name = "M_ReefWearAtlas", hideFlags = HideFlags.HideAndDontSave };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", WearAtlas());
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = ReefCurrentLaneKit.CausticsRenderQueue;   // below the current lanes and the marine snow (MV-1348)
            s_wearMat = m;
            return m;
        }

        // ---- mesh helpers ---------------------------------------------------------------------

        private sealed class QuadBuffer
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<int> T = new List<int>();

            /// <summary>Corners in BL, TL, TR, BR order as seen from above (clockwise).</summary>
            public void Add(Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, Vector2 uvBl, Vector2 uvTl, Vector2 uvTr, Vector2 uvBr)
            {
                int i = V.Count;
                V.Add(bl); V.Add(tl); V.Add(tr); V.Add(br);
                UV.Add(uvBl); UV.Add(uvTl); UV.Add(uvTr); UV.Add(uvBr);
                for (int k = 0; k < 4; k++) N.Add(Vector3.up);
                T.Add(i); T.Add(i + 1); T.Add(i + 2);
                T.Add(i); T.Add(i + 2); T.Add(i + 3);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
                if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V); m.SetUVs(0, UV); m.SetNormals(N); m.SetTriangles(T, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        private static Vector2 CellUv(int cell, int cols, int rows, float u, float v)
        {
            int cx = cell % cols, cy = cell / cols;
            return new Vector2((cx + CellInset + u * (1f - 2f * CellInset)) / cols,
                               (cy + CellInset + v * (1f - 2f * CellInset)) / rows);
        }

        private static GameObject NewMeshObject(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            go.AddComponent<KeepsOwnMaterial>();
            go.isStatic = true;
            return go;
        }

        // ---- deck -----------------------------------------------------------------------------

        /// <summary>One mesh of 2 m quads covering the whole area footprint (edge plates are cropped,
        /// not stretched), variants seeded by <paramref name="areaId"/>.</summary>
        public static GameObject BuildDeck(Transform host, string areaId, float xMin, float zMin, float xMax, float zMax)
        {
            if (host == null || xMax - xMin <= 0f || zMax - zMin <= 0f) return null;

            var q = new QuadBuffer();
            int gz = 0;
            for (float z = zMin; z < zMax - 0.0001f; z += PlateMetres, gz++)
            {
                float z1 = Mathf.Min(z + PlateMetres, zMax);
                float fv = (z1 - z) / PlateMetres;
                int gx = 0;
                for (float x = xMin; x < xMax - 0.0001f; x += PlateMetres, gx++)
                {
                    float x1 = Mathf.Min(x + PlateMetres, xMax);
                    float fu = (x1 - x) / PlateMetres;
                    int cell = PickCell(areaId, gx, gz);
                    // Crop from the far edge so a full plate's riveted border always sits at the area's own origin corner.
                    q.Add(new Vector3(x, DeckY, z), new Vector3(x, DeckY, z1), new Vector3(x1, DeckY, z1), new Vector3(x1, DeckY, z),
                        CellUv(cell, DeckCols, DeckRows, 0f, 0f), CellUv(cell, DeckCols, DeckRows, 0f, fv),
                        CellUv(cell, DeckCols, DeckRows, fu, fv), CellUv(cell, DeckCols, DeckRows, fu, 0f));
                }
            }
            return NewMeshObject(host, $"Reef Deck {areaId}", q.ToMesh($"ReefDeck_{areaId}"), DeckMaterial());
        }

        /// <summary>Which atlas cell a deck quad samples, decoded from its UV centre.</summary>
        public static int CellOfUv(Vector2 uv) => Mathf.Clamp(Mathf.FloorToInt(uv.x * DeckCols), 0, DeckCols - 1)
                                                  + DeckCols * Mathf.Clamp(Mathf.FloorToInt(uv.y * DeckRows), 0, DeckRows - 1);

        // ---- wear -----------------------------------------------------------------------------

        private static void AddRotated(QuadBuffer q, int cell, Vector2 centre, float w, float d, float yawDeg)
        {
            float s = Mathf.Sin(yawDeg * Mathf.Deg2Rad), c = Mathf.Cos(yawDeg * Mathf.Deg2Rad);
            Vector3 P(float lx, float lz) => new Vector3(centre.x + lx * c + lz * s, WearY, centre.y - lx * s + lz * c);
            float hw = w * 0.5f, hd = d * 0.5f;
            q.Add(P(-hw, -hd), P(-hw, hd), P(hw, hd), P(hw, -hd),
                CellUv(cell, WearCols, WearRows, 0f, 0f), CellUv(cell, WearCols, WearRows, 0f, 1f),
                CellUv(cell, WearCols, WearRows, 1f, 1f), CellUv(cell, WearCols, WearRows, 1f, 0f));
        }

        /// <summary>The label stencilled near one corner, e.g. "DECK 3 + C-1" with the mid-dot.</summary>
        public static string DeckLabel(string areaId)
        {
            string digits = "";
            foreach (char ch in areaId ?? "") if (char.IsDigit(ch)) digits += ch;
            if (digits.Length == 0) digits = "0";
            return $"DECK 3 + C-{digits}";
        }

        /// <summary>One alpha-blended mesh per area: stains, silt drifts, walkway dashes, deck number.</summary>
        public static GameObject BuildWear(Transform host, string areaId, float xMin, float zMin, float xMax, float zMax)
        {
            if (host == null) return null;
            Material mat = WearMaterial();
            if (mat == null) return null;

            uint seed = Hash($"wear_{areaId}");
            float w = xMax - xMin, d = zMax - zMin;
            var q = new QuadBuffer();

            // Stains (rust / wet / oil), about one per 40 m2.
            int stains = Mathf.Max(4, Mathf.RoundToInt(w * d / 40f));
            for (int i = 0; i < stains; i++)
            {
                int cell = WearRust + Mathf.FloorToInt(Hash01(seed, i * 7) * 3f) % 3;
                float size = 1.0f + 1.4f * Hash01(seed, i * 7 + 1);
                var centre = new Vector2(xMin + 1f + (w - 2f) * Hash01(seed, i * 7 + 2), zMin + 1f + (d - 2f) * Hash01(seed, i * 7 + 3));
                AddRotated(q, cell, centre, size * (0.8f + 0.5f * Hash01(seed, i * 7 + 4)), size, 360f * Hash01(seed, i * 7 + 5));
            }

            // Silt drifts along every wall base (v = 0 at the wall).
            float sw = Mathf.Min(SiltWidthMetres, Mathf.Min(w, d) * 0.5f);
            Vector2 a0 = CellUv(WearSilt, WearCols, WearRows, 0f, 0f), a1 = CellUv(WearSilt, WearCols, WearRows, 1f, 0f);
            Vector2 b0 = CellUv(WearSilt, WearCols, WearRows, 0f, 1f), b1 = CellUv(WearSilt, WearCols, WearRows, 1f, 1f);
            // south: wall at zMin
            q.Add(new Vector3(xMin, WearY, zMin), new Vector3(xMin, WearY, zMin + sw), new Vector3(xMax, WearY, zMin + sw), new Vector3(xMax, WearY, zMin), a0, b0, b1, a1);
            // north: wall at zMax
            q.Add(new Vector3(xMin, WearY, zMax - sw), new Vector3(xMin, WearY, zMax), new Vector3(xMax, WearY, zMax), new Vector3(xMax, WearY, zMax - sw), b0, a0, a1, b1);
            // west: wall at xMin
            q.Add(new Vector3(xMin, WearY, zMin), new Vector3(xMin, WearY, zMax), new Vector3(xMin + sw, WearY, zMax), new Vector3(xMin + sw, WearY, zMin), a0, a1, b1, b0);
            // east: wall at xMax
            q.Add(new Vector3(xMax - sw, WearY, zMin), new Vector3(xMax - sw, WearY, zMax), new Vector3(xMax, WearY, zMax), new Vector3(xMax, WearY, zMin), b0, b1, a1, a0);

            // Faded yellow dashed walkway line, 2.2 m inside the walls.
            float lx0 = xMin + WalkwayInsetMetres, lx1 = xMax - WalkwayInsetMetres;
            float lz0 = zMin + WalkwayInsetMetres, lz1 = zMax - WalkwayInsetMetres;
            if (lx1 - lx0 > 2f && lz1 - lz0 > 2f)
            {
                const float pitch = 1.6f, dash = 1.0f, thick = 0.14f;
                for (float x = lx0; x + dash <= lx1; x += pitch)
                {
                    AddRotated(q, WearDash, new Vector2(x + dash * 0.5f, lz0), dash, thick, 0f);
                    AddRotated(q, WearDash, new Vector2(x + dash * 0.5f, lz1), dash, thick, 0f);
                }
                for (float z = lz0; z + dash <= lz1; z += pitch)
                {
                    AddRotated(q, WearDash, new Vector2(lx0, z + dash * 0.5f), thick, dash, 0f);
                    AddRotated(q, WearDash, new Vector2(lx1, z + dash * 0.5f), thick, dash, 0f);
                }
            }

            // Stencilled deck number near the south-west corner.
            const float gw = 0.55f, gh = 0.8f;
            float tx = xMin + 3.2f, tz = zMin + 3.4f;
            foreach (char ch in DeckLabel(areaId))
            {
                if (ch == ' ') { tx += gw * 0.7f; continue; }
                int idx = GlyphChars.IndexOf(ch);
                if (idx < 0) continue;
                AddRotated(q, WearGlyphBase + idx, new Vector2(tx + gw * 0.5f, tz), gw, gh, 0f);
                tx += gw;
            }

            return NewMeshObject(host, $"Reef Wear {areaId}", q.ToMesh($"ReefWear_{areaId}"), mat);
        }

        // ---- props ----------------------------------------------------------------------------

        public const int PropMin = 3, PropMax = 5;

        private static Material PropMaterial() => s_propMat != null ? s_propMat : (s_propMat = WorldMaterials.M_MetalDark);

        /// <summary>3-5 seeded floor props within <see cref="PropBandMetres"/> of the area's walls:
        /// a short chain (9 links), a coiled hose, loose bolts, a fallen deck panel. Visual only: no
        /// collider, every one under 0.15 m tall.</summary>
        public static GameObject BuildProps(Transform host, string areaId, float xMin, float zMin, float xMax, float zMax)
        {
            if (host == null) return null;
            var root = new GameObject($"Reef Deck Props {areaId}");
            root.transform.SetParent(host, false);
            root.AddComponent<KeepsOwnMaterial>();

            uint seed = Hash($"props_{areaId}");
            int count = PropMin + Mathf.FloorToInt(Hash01(seed, 1) * (PropMax - PropMin + 1)) % (PropMax - PropMin + 1);
            float w = xMax - xMin, d = zMax - zMin;
            float band = Mathf.Min(PropBandMetres - 0.3f, Mathf.Min(w, d) * 0.5f - 0.1f);

            for (int i = 0; i < count; i++)
            {
                int side = Mathf.FloorToInt(Hash01(seed, i * 5 + 2) * 4f) % 4;
                float along = Hash01(seed, i * 5 + 3), depth = 0.7f + (band - 0.7f) * Hash01(seed, i * 5 + 4);
                Vector3 p;
                switch (side)
                {
                    case 0: p = new Vector3(Mathf.Lerp(xMin + 1f, xMax - 1f, along), 0f, zMin + depth); break;
                    case 1: p = new Vector3(Mathf.Lerp(xMin + 1f, xMax - 1f, along), 0f, zMax - depth); break;
                    case 2: p = new Vector3(xMin + depth, 0f, Mathf.Lerp(zMin + 1f, zMax - 1f, along)); break;
                    default: p = new Vector3(xMax - depth, 0f, Mathf.Lerp(zMin + 1f, zMax - 1f, along)); break;
                }
                int kind = Mathf.FloorToInt(Hash01(seed, i * 5 + 5) * 4f) % 4;
                float yaw = 360f * Hash01(seed, i * 5 + 6);
                BuildProp(root.transform, kind, p, yaw, seed + (uint)i);
            }

            if (Application.isPlaying) StaticBatchingUtility.Combine(root);
            return root;
        }

        private static void BuildProp(Transform parent, int kind, Vector3 pos, float yaw, uint seed)
        {
            var combine = new List<CombineInstance>();
            string name;
            switch (kind)
            {
                case 0:   // chain: 9 flat links
                {
                    name = "chain";
                    Mesh link = CharacterMeshes.Ring(0.03f, 0.065f, 0.025f, 8);
                    for (int i = 0; i < 9; i++)
                        combine.Add(Part(link, new Vector3((i - 4f) * 0.095f, 0.0125f, 0.012f * ((i & 1) == 0 ? 1f : -1f)), Quaternion.Euler(0f, (i & 1) == 0 ? 0f : 90f, 0f), Vector3.one));
                    break;
                }
                case 1:   // coiled hose
                {
                    name = "hose";
                    combine.Add(Part(CharacterMeshes.Ring(0.24f, 0.33f, 0.05f, 16), new Vector3(0f, 0.025f, 0f), Quaternion.identity, Vector3.one));
                    combine.Add(Part(CharacterMeshes.Ring(0.13f, 0.22f, 0.05f, 16), new Vector3(0f, 0.075f, 0f), Quaternion.identity, Vector3.one));
                    break;
                }
                case 2:   // loose bolts
                {
                    name = "bolts";
                    Mesh bolt = CharacterMeshes.Prism(6, 0.022f, 0.022f, 0.04f, 0.004f);
                    for (int i = 0; i < 6; i++)
                        combine.Add(Part(bolt, new Vector3((Hash01(seed, i * 3) - 0.5f) * 0.6f, 0.02f, (Hash01(seed, i * 3 + 1) - 0.5f) * 0.4f),
                            Quaternion.Euler(0f, 360f * Hash01(seed, i * 3 + 2), 0f), Vector3.one));
                    break;
                }
                default:  // fallen deck panel
                {
                    name = "panel";
                    Vector3 size = new Vector3(0.9f, 0.04f, 0.6f);
                    combine.Add(Part(CharacterMeshes.Bevelled(size, 0.01f), new Vector3(0f, 0.02f, 0f), Quaternion.identity, Vector3.one));
                    break;
                }
            }

            var mesh = new Mesh { name = $"ReefDeckProp_{name}", hideFlags = HideFlags.HideAndDontSave };
            mesh.CombineMeshes(combine.ToArray(), mergeSubMeshes: true, useMatrices: true);
            mesh.RecalculateBounds();

            var go = NewMeshObject(parent, $"ReefDeckProp_{name}", mesh, PropMaterial());
            go.transform.position = new Vector3(pos.x, DeckY, pos.z);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private static CombineInstance Part(Mesh m, Vector3 pos, Quaternion rot, Vector3 scale)
            => new CombineInstance { mesh = m, transform = Matrix4x4.TRS(pos, rot, scale) };
    }
}
