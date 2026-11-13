using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// MV-1351 (Reef relook 3/4): the Reef walls. Everything is generated at boot — no image files, no
    /// Unity primitives. One shared plating atlas texture (four riveted plate bands, seams, rust
    /// streaks, base barnacles), one shared material per surface type, flat collider-free pieces.
    ///
    /// Assembly note: lives in Rendering (mesh/material/texture only). WHICH walls get a face, where
    /// the gates and covers are and which dressing an area asks for is
    /// <c>MaxWorlds.Arena.ReefAreaKit</c>'s job; it hands this class plain positions and spans.
    ///
    /// Frame: a wall face is described by <see cref="Face.origin"/> (a point ON the room-facing face
    /// at along = 0, y = 0), <see cref="Face.inward"/> (unit, away from the wall into the room) and
    /// <see cref="Face.along"/> (unit, the run direction). A point at run-coordinate t is
    /// <c>origin + along * t</c>. Quads follow Unity's clockwise-front winding (the MV-1268 lesson):
    /// bottom-left, top-left, top-right, bottom-right as seen from the room.
    /// </summary>
    public static class ReefWallKit
    {
        public const float PlateTileMetres = 2f;       // the atlas tile covers 2 m of run and the full wall height
        public const float RibPitchMax = 2.0f;         // ribs are never further apart than this (AC: at most 2.2 m)
        public const float FaceOffset = 0.012f;        // plating floats this far off the wall face (no z-fight)
        public const float RibWidth = 0.22f, RibDepth = 0.14f;
        public const float TrayHeight = 0.16f, TrayDepth = 0.5f;
        public const float PipeRadius = 0.065f, FlangeEveryMetres = 3f;
        public const float PortholeDiameter = 0.9f, PortholeHeight = 1.7f;
        public const float PoolY = 0.03f;              // above the deck (0.015) and wear (0.02) layers

        public struct Face
        {
            public string areaId, wallName;
            public Vector3 origin, inward, along;
            public float spanMin, spanMax, height;
            /// <summary>Run ranges with no gate in them — plated and ribbed. (min, max) in run coordinates.</summary>
            public List<Vector2> segments;
            /// <summary>The config's window ranges on this wall — portholes go here. (min, max).</summary>
            public List<Vector2> windows;
        }

        private static readonly Dictionary<string, Material> s_solid = new Dictionary<string, Material>();
        private static Texture2D s_plateTex, s_poolTex;
        private static Material s_plateMat;
        private static readonly Dictionary<Color, Material> s_pool = new Dictionary<Color, Material>();

        // ---- hashing ----------------------------------------------------------------------------

        public static uint Hash(string s)
        {
            uint h = 2166136261u;
            if (s != null) foreach (char c in s) { h ^= c; h *= 16777619u; }
            return h;
        }

        public static float Hash01(uint seed, int salt)
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

        private static Color C(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);

        // ---- plating atlas ----------------------------------------------------------------------

        private const int TexW = 256, TexH = 384;   // 2 m x 3 m at 128 px/m
        // Rivet rows: just above and below each of the three inner seams, plus the floor and top edges.
        private static readonly float[] RivetRows = { 0.032f, 0.218f, 0.282f, 0.468f, 0.532f, 0.718f, 0.782f, 0.968f };
        private const int RivetsPerTile = 8;

        public static Texture2D PlateTexture()
        {
            if (s_plateTex != null) return s_plateTex;
            var t = new Texture2D(TexW, TexH, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = "ReefWallPlateAtlas",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[TexW * TexH];
            for (int y = 0; y < TexH; y++)
                for (int x = 0; x < TexW; x++)
                    px[y * TexW + x] = PaintPlate(x, y);
            t.SetPixels32(px);
            t.Apply(updateMipmaps: true);
            s_plateTex = t;
            return t;
        }

        private static Color32 PaintPlate(int px, int py)
        {
            float u = (px + 0.5f) / TexW, v = (py + 0.5f) / TexH;
            int band = Mathf.Min(3, Mathf.FloorToInt(v * 4f));
            float bv = v * 4f - band;   // 0 at the band's lower seam, 1 at its upper

            // Plate bands: #2E4650 at the top down to #1E3038 at the base.
            Color c = Color.Lerp(C(0x1E, 0x30, 0x38), C(0x2E, 0x46, 0x50), (band + bv) / 4f);
            c *= 0.965f + 0.07f * Noise2(px / 3, py / 3, band + 3);
            c *= 0.97f + 0.06f * Hash01(1000u + (uint)band, 1);
            c.a = 1f;

            // Dark seam below each band, a faint lit lip just above it.
            if (bv < 0.035f || bv > 0.985f) c = C(0x0A, 0x13, 0x18);
            else if (bv < 0.062f) c = Color.Lerp(c, C(0x6E, 0x90, 0x98), 0.16f);

            // Long rust streaks running down the wall.
            for (int i = 0; i < 6; i++)
            {
                float su = Hash01(77, i * 3), top = 0.35f + 0.65f * Hash01(77, i * 3 + 1), len = 0.25f + 0.3f * Hash01(77, i * 3 + 2);
                if (Mathf.Abs(u - su) * TexW < 1.6f && v < top && top - v < len)
                    c = Color.Lerp(c, C(0x6B, 0x3E, 0x22), 0.22f * (1f - (top - v) / len));
            }

            // Rivet rows along each seam, each with a rust drip on about half of them.
            int ri = Mathf.Min(RivetsPerTile - 1, Mathf.FloorToInt(u * RivetsPerTile));
            float cu = (ri + 0.5f) / RivetsPerTile;
            for (int r = 0; r < RivetRows.Length; r++)
            {
                float rv = RivetRows[r];
                float dx = (u - cu) * TexW, dy = (v - rv) * TexH;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < 2.7f) c = Color.Lerp(c, C(0x84, 0xA2, 0xA8), 0.72f);
                else if (d < 3.7f && dy < 0f) c *= 0.68f;

                float h = Hash01(500u + (uint)r, ri);
                if (h > 0.55f && v < rv && rv - v < 0.1f + 0.08f * h && Mathf.Abs(dx) < 1.5f)
                    c = Color.Lerp(c, C(0x7A, 0x44, 0x26), 0.42f * (1f - (rv - v) / (0.18f + 0.08f * h)));
            }

            // Pale barnacle specks near the base.
            if (v < 0.2f)
            {
                int cx = px / 5, cy = py / 5;
                if (Noise2(cx, cy, 9) < 0.05f * (1f - v / 0.2f) + 0.012f)
                {
                    float bx = cx * 5 + 2.5f + 1.5f * (Noise2(cx, cy, 10) - 0.5f);
                    float by = cy * 5 + 2.5f + 1.5f * (Noise2(cx, cy, 11) - 0.5f);
                    float dd = Mathf.Sqrt((px - bx) * (px - bx) + (py - by) * (py - by));
                    if (dd < 1.5f) c = Color.Lerp(c, C(0xC9, 0xD3, 0xCB), 0.85f);
                }
            }

            c.a = 1f;
            return c;
        }

        // ---- materials --------------------------------------------------------------------------

        public static Material PlateMaterial()
        {
            if (s_plateMat != null) return s_plateMat;
            Shader shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;
            var m = new Material(shader) { name = "M_ReefWallPlate", hideFlags = HideFlags.HideAndDontSave };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", PlateTexture());
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", PlateTexture());
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            s_plateMat = m;
            return m;
        }

        /// <summary>One shared plain material per surface type (cached by key).</summary>
        public static Material Solid(string key, Color color, float smoothness = 0.35f, float metallic = 0f)
        {
            if (s_solid.TryGetValue(key, out Material hit) && hit != null) return hit;
            Shader shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;
            var m = new Material(shader) { name = key, hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            s_solid[key] = m;
            return m;
        }

        public static Material Rib => Solid("M_ReefWallRib", C(0x07, 0x0D, 0x12), 0.2f);
        public static Material Tray => Solid("M_ReefWallTray", C(0x0B, 0x15, 0x1C), 0.3f, 0.3f);
        public static Material Copper => Solid("M_ReefWallCopper", WorldMaterials.ReefCopper, 0.5f, 0.55f);
        public static Material Steel => Solid("M_ReefWallSteel", C(0x6E, 0x87, 0x94), 0.5f, 0.6f);
        public static Material Brass => Solid("M_ReefWallBrass", WorldMaterials.ReefBrass, 0.55f, 0.6f);
        public static Material Glass => Solid("M_ReefWallGlass", C(0x0A, 0x3E, 0x66), 0.85f);
        public static Material Silhouette => Solid("M_ReefWallFish", C(0x03, 0x10, 0x1A), 0.1f);
        public static Material ValveRed => Solid("M_ReefWallValveRed", C(0xC0, 0x32, 0x2B), 0.4f, 0.2f);

        /// <summary>A soft additive floor glow (radial gradient quad). ONE material per tone, fixed render
        /// queue below the current lanes and characters (MV-1348 ordering).</summary>
        public static Material PoolMaterial(Color tone)
        {
            if (s_pool.TryGetValue(tone, out Material hit) && hit != null) return hit;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null || !shader.isSupported) return null;
            var m = new Material(shader) { name = "M_ReefWallPool", hideFlags = HideFlags.HideAndDontSave };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", PoolTexture());
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tone);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = ReefCurrentLaneKit.CausticsRenderQueue + 1;
            s_pool[tone] = m;
            return m;
        }

        private static Texture2D PoolTexture()
        {
            if (s_poolTex != null) return s_poolTex;
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false, false)
            {
                name = "ReefWallPoolGradient", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float f = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    byte b = (byte)Mathf.RoundToInt(255f * f * f);
                    px[y * n + x] = new Color32(b, b, b, 255);
                }
            t.SetPixels32(px);
            t.Apply(false);
            s_poolTex = t;
            return t;
        }

        // ---- object helpers ---------------------------------------------------------------------

        private static GameObject Obj(Transform parent, string name, Mesh mesh, Material mat, Vector3 localPos,
            Quaternion? rot = null, Vector3? scale = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale ?? Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<KeepsOwnMaterial>();
            go.isStatic = true;
            return go;
        }

        private static Mesh Box(Vector3 size) => CharacterMeshes.Bevelled(size, Mathf.Min(0.02f, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.3f));

        private static Mesh PoolQuad(float radius)
        {
            var m = new Mesh { name = "ReefWallPoolQuad", hideFlags = HideFlags.HideAndDontSave };
            m.SetVertices(new[] { new Vector3(-radius, 0f, -radius), new Vector3(-radius, 0f, radius), new Vector3(radius, 0f, radius), new Vector3(radius, 0f, -radius) });
            m.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            m.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A faint additive floor pool in the parent's local space (flat, no collider).</summary>
        public static GameObject BuildPool(Transform parent, string name, Vector3 localCentre, float radius, Color tone)
        {
            Material mat = PoolMaterial(tone);
            if (mat == null) return null;
            return Obj(parent, name, PoolQuad(radius), mat, new Vector3(localCentre.x, PoolY, localCentre.z));
        }

        private static Vector3 P(Face f, float t) => f.along * t;

        // ---- the wall face ----------------------------------------------------------------------

        /// <summary>Builds one room-facing wall: plating, rib frames, top cable tray with copper and steel
        /// pipes and portholes. Parented under <paramref name="parent"/> as "Reef Wall {area} {wall}"; its
        /// local origin IS <see cref="Face.origin"/>. Collider-free.</summary>
        public static GameObject BuildFace(Transform parent, Face f)
        {
            Material plate = PlateMaterial();
            var root = new GameObject($"Reef Wall {f.areaId} {f.wallName}");
            root.transform.SetParent(parent, false);
            root.transform.position = f.origin;
            Transform rt = root.transform;
            float len = f.spanMax - f.spanMin;
            if (len <= 0.01f || plate == null) return root;
            Vector3 up = Vector3.up;

            // Plating: one mesh per wall, one quad per gate-free segment.
            {
                var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var norms = new List<Vector3>(); var tris = new List<int>();
                Vector3 right = Vector3.Cross(up, -f.inward);
                float sign = Vector3.Dot(right, f.along) > 0f ? 1f : -1f;
                foreach (Vector2 seg in f.segments)
                {
                    float left = sign > 0f ? seg.x : seg.y, rightT = sign > 0f ? seg.y : seg.x;
                    Vector3 bl = P(f, left) + f.inward * FaceOffset, br = P(f, rightT) + f.inward * FaceOffset;
                    float u0 = Hash01(Hash(f.areaId + f.wallName), Mathf.RoundToInt(seg.x * 10f)), u1 = u0 + (seg.y - seg.x) / PlateTileMetres;
                    int i = verts.Count;
                    verts.Add(bl); verts.Add(bl + up * f.height); verts.Add(br + up * f.height); verts.Add(br);
                    uvs.Add(new Vector2(u0, 0f)); uvs.Add(new Vector2(u0, 1f)); uvs.Add(new Vector2(u1, 1f)); uvs.Add(new Vector2(u1, 0f));
                    for (int k = 0; k < 4; k++) norms.Add(f.inward);
                    tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
                }
                if (verts.Count > 0)
                {
                    var mesh = new Mesh { name = $"ReefWallPlating_{f.areaId}_{f.wallName}", hideFlags = HideFlags.HideAndDontSave };
                    mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetNormals(norms); mesh.SetTriangles(tris, 0);
                    mesh.RecalculateBounds();
                    Obj(rt, "Wall Plating", mesh, plate, Vector3.zero);
                }
            }

            // Rib frames: near-black vertical ribs, at most RibPitchMax apart inside each gate-free segment.
            var bayMids = new List<float>();
            Mesh ribMesh = Box(new Vector3(RibWidth, f.height - 0.05f, RibDepth));
            foreach (Vector2 seg in f.segments)
            {
                float segLen = seg.y - seg.x;
                if (segLen < 0.6f) continue;
                float lo = seg.x + 0.15f, hi = seg.y - 0.15f;
                int n = Mathf.Max(1, Mathf.CeilToInt((hi - lo) / RibPitchMax));
                var ribs = new List<float>();
                for (int i = 0; i <= n; i++) ribs.Add(lo + (hi - lo) * i / n);
                foreach (float t in ribs)
                {
                    Vector3 pos = P(f, t) + f.inward * (FaceOffset + RibDepth * 0.5f) + up * ((f.height - 0.05f) * 0.5f);
                    Obj(rt, "Wall Rib", ribMesh, Rib, pos, Quaternion.LookRotation(f.inward, up));
                }
                for (int i = 0; i < n; i++) bayMids.Add((ribs[i] + ribs[i + 1]) * 0.5f);
            }

            // Top: a dark cable tray along the FULL top, two pipes (copper, steel), flanges every 3 m.
            {
                float mid = (f.spanMin + f.spanMax) * 0.5f;
                Mesh trayMesh = Box(new Vector3(len, TrayHeight, TrayDepth));
                Vector3 tpos = P(f, mid) + f.inward * (TrayDepth * 0.5f) + up * (f.height - TrayHeight * 0.5f);
                Obj(rt, "Wall Tray", trayMesh, Tray, tpos, Quaternion.LookRotation(f.inward, up) * Quaternion.identity, null);
                // The tray mesh is built along local X; LookRotation(inward) puts local X on the lateral axis,
                // which IS the run direction for a wall whose inward is +/-Z; for +/-X inward it is the Z run.
                // Either way local X = cross(up, inward), parallel to f.along. Verified by the MV-1351 test.

                Mesh pipe = CharacterMeshes.Beam(len, PipeRadius, PipeRadius, 10);
                Quaternion along = Quaternion.FromToRotation(Vector3.up, f.along);
                Obj(rt, "Wall Pipe Copper", pipe, Copper, P(f, mid) + f.inward * (FaceOffset + PipeRadius + 0.03f) + up * (f.height - TrayHeight - 0.18f), along);
                Obj(rt, "Wall Pipe Steel", pipe, Steel, P(f, mid) + f.inward * (FaceOffset + PipeRadius + 0.03f) + up * (f.height - TrayHeight - 0.42f), along);
                Mesh flange = CharacterMeshes.Ring(PipeRadius * 0.9f, PipeRadius * 1.9f, 0.05f, 10);
                for (float t = f.spanMin + FlangeEveryMetres * 0.5f; t < f.spanMax; t += FlangeEveryMetres)
                {
                    Obj(rt, "Wall Pipe Flange", flange, Copper, P(f, t) + f.inward * (FaceOffset + PipeRadius + 0.03f) + up * (f.height - TrayHeight - 0.18f), along);
                    Obj(rt, "Wall Pipe Flange", flange, Steel, P(f, t) + f.inward * (FaceOffset + PipeRadius + 0.03f) + up * (f.height - TrayHeight - 0.42f), along);
                }
            }

            // Portholes on the config's window ranges.
            if (f.windows != null)
                foreach (Vector2 win in f.windows)
                {
                    float wlo = Mathf.Min(win.x, win.y), whi = Mathf.Max(win.x, win.y);
                    var placed = new List<float>();
                    foreach (float m in bayMids)
                        if (m - PortholeDiameter * 0.5f >= wlo && m + PortholeDiameter * 0.5f <= whi) placed.Add(m);
                    if (placed.Count == 0) placed.Add((wlo + whi) * 0.5f);
                    foreach (float t in placed) BuildPorthole(rt, f, t);
                }

            return root;
        }

        private static void BuildPorthole(Transform wall, Face f, float t)
        {
            var ph = new GameObject("Porthole");
            ph.transform.SetParent(wall, false);
            ph.transform.localPosition = P(f, t) + f.inward * (FaceOffset + 0.02f) + Vector3.up * PortholeHeight;
            ph.transform.localRotation = Quaternion.FromToRotation(Vector3.up, f.inward);
            ph.AddComponent<KeepsOwnMaterial>();
            Transform pt = ph.transform;
            float r = PortholeDiameter * 0.5f;

            // Local frame: +Y is out of the wall; local X/Z lie in the wall plane (Y-axis-up rotation of
            // the flat Ring mesh). Brass ring + deep-blue glass disc + one pale shaft + two fish.
            Obj(pt, "Porthole Ring", CharacterMeshes.Ring(r * 0.82f, r, 0.07f, 20), Brass, Vector3.zero);
            Obj(pt, "Porthole Glass", CharacterMeshes.Prism(20, r * 0.84f, r * 0.84f, 0.02f, 0.05f), Glass, new Vector3(0f, -0.005f, 0f));
            Material shaft = StormdrainLightKit.AdditiveUnlit(WorldMaterials.ReefAccentCyan * 0.16f, "PortholeShaft");
            if (shaft != null)
                Obj(pt, "Porthole Shaft", Box(new Vector3(0.13f, 0.01f, 0.62f)), shaft, new Vector3(0.12f, 0.02f, 0f), Quaternion.Euler(0f, 24f, 0f));
            uint seed = Hash($"{f.areaId}{f.wallName}") + (uint)Mathf.RoundToInt(t * 10f);
            for (int i = 0; i < 2; i++)
            {
                float fx = (Hash01(seed, i * 3) - 0.5f) * r * 0.9f, fz = (Hash01(seed, i * 3 + 1) - 0.5f) * r * 0.9f;
                Obj(pt, "Porthole Fish", CharacterMeshes.Sphere(8), Silhouette, new Vector3(fx, 0.03f, fz),
                    Quaternion.Euler(0f, 360f * Hash01(seed, i * 3 + 2), 0f), new Vector3(0.12f, 0.012f, 0.045f));
            }

            // A faint cyan pool where the light falls on the deck, ~1 m in from the wall.
            BuildPool(wall, "Porthole Pool", P(f, t) + f.inward * 1.1f, 0.95f, WorldMaterials.ReefAccentCyan * 0.2f);
        }

        // ---- wall dressing ----------------------------------------------------------------------
        // Each builder returns a root whose local +Z points out of the wall into the room, local +X is the
        // run direction, and whose origin sits on the wall face at the floor.

        private static GameObject DressRoot(Transform parent, string name, Vector3 ground, Vector3 inward)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = ground;
            root.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);
            root.AddComponent<KeepsOwnMaterial>();
            return root;
        }

        public const float RackWidth = 1.2f, PipeBundleWidth = 0.7f, TankWidth = 1.0f;

        /// <summary>racks: a two-shelf frame of 4 gas bottles (red, yellow, teal, grey) with steel caps.</summary>
        public static GameObject BuildRack(Transform parent, Vector3 ground, Vector3 inward)
        {
            GameObject root = DressRoot(parent, "Wall Rack", ground, inward);
            Transform t = root.transform;
            Mesh post = Box(new Vector3(0.06f, 1.5f, 0.06f));
            Mesh shelf = Box(new Vector3(RackWidth, 0.05f, 0.4f));
            Obj(t, "Rack Post", post, Steel, new Vector3(-0.57f, 0.75f, 0.22f));
            Obj(t, "Rack Post", post, Steel, new Vector3(0.57f, 0.75f, 0.22f));
            float[] shelfY = { 0.28f, 0.88f };
            Color[] tones = { C(0xC8, 0x32, 0x2C), C(0xD8, 0xB0, 0x30), C(0x2A, 0xA8, 0xA0), C(0x88, 0x96, 0xA0) };
            string[] names = { "Red", "Yellow", "Teal", "Grey" };
            Mesh cap = CharacterMeshes.Prism(6, 0.045f, 0.045f, 0.08f, 0.2f);
            for (int s = 0; s < 2; s++)
            {
                Obj(t, "Rack Shelf", shelf, Steel, new Vector3(0f, shelfY[s], 0.22f));
                for (int b = 0; b < 2; b++)
                {
                    int k = s * 2 + b;
                    float x = b == 0 ? -0.28f : 0.28f;
                    Obj(t, "Rack Bottle", CharacterMeshes.Prism(8, 0.11f, 0.095f, 0.55f, 0.2f),
                        Solid($"M_ReefWallBottle{names[k]}", tones[k], 0.55f, 0.2f), new Vector3(x, shelfY[s] + 0.025f + 0.275f, 0.22f));
                    Obj(t, "Rack Bottle Cap", cap, Steel, new Vector3(x, shelfY[s] + 0.025f + 0.55f + 0.03f, 0.22f));
                }
            }
            return root;
        }

        /// <summary>pipes: a bundle of 3 vertical pipes (copper, steel, copper) with flanges and a red valve wheel.</summary>
        public static GameObject BuildPipeBundle(Transform parent, Vector3 ground, Vector3 inward, float wallHeight)
        {
            GameObject root = DressRoot(parent, "Wall PipeBundle", ground, inward);
            Transform t = root.transform;
            float h = Mathf.Min(wallHeight - 0.7f, 2.3f);
            Mesh pipe = CharacterMeshes.Beam(h, 0.08f, 0.08f, 10);
            Mesh flange = CharacterMeshes.Ring(0.07f, 0.135f, 0.05f, 10);
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.24f;
                Material m = i == 1 ? Steel : Copper;
                Obj(t, "Bundle Pipe", pipe, m, new Vector3(x, h * 0.5f, 0.16f));
                Obj(t, "Bundle Flange", flange, m, new Vector3(x, 0.45f, 0.16f));
                Obj(t, "Bundle Flange", flange, m, new Vector3(x, Mathf.Min(1.75f, h - 0.2f), 0.16f));
            }
            // Red valve wheel on the middle pipe, facing out into the room.
            Quaternion face = Quaternion.Euler(90f, 0f, 0f);
            Obj(t, "Bundle Valve Stem", CharacterMeshes.Beam(0.14f, 0.025f, 0.025f, 6), ValveRed, new Vector3(0f, 1.1f, 0.3f), face);
            Obj(t, "Bundle Valve Wheel", CharacterMeshes.Ring(0.1f, 0.15f, 0.03f, 12), ValveRed, new Vector3(0f, 1.1f, 0.38f), face);
            Obj(t, "Bundle Valve Spoke", Box(new Vector3(0.28f, 0.03f, 0.03f)), ValveRed, new Vector3(0f, 1.1f, 0.38f));
            Obj(t, "Bundle Valve Spoke", Box(new Vector3(0.03f, 0.28f, 0.03f)), ValveRed, new Vector3(0f, 1.1f, 0.38f));
            return root;
        }

        /// <summary>planters: a glass coral tank (brass base) holding 7 glowing coral cones, with a faint cyan pool.</summary>
        public static GameObject BuildCoralTank(Transform parent, Vector3 ground, Vector3 inward)
        {
            GameObject root = DressRoot(parent, "Wall CoralTank", ground, inward);
            Transform t = root.transform;
            Obj(t, "Tank Base", Box(new Vector3(TankWidth, 0.2f, 0.5f)), Brass, new Vector3(0f, 0.1f, 0.28f));
            Obj(t, "Tank Glass Back", Box(new Vector3(TankWidth, 1.1f, 0.03f)), Glass, new Vector3(0f, 0.75f, 0.05f));
            Obj(t, "Tank Glass Side", Box(new Vector3(0.03f, 1.1f, 0.46f)), Glass, new Vector3(-TankWidth * 0.5f + 0.015f, 0.75f, 0.28f));
            Obj(t, "Tank Glass Side", Box(new Vector3(0.03f, 1.1f, 0.46f)), Glass, new Vector3(TankWidth * 0.5f - 0.015f, 0.75f, 0.28f));
            Obj(t, "Tank Cap", Box(new Vector3(TankWidth, 0.05f, 0.5f)), Brass, new Vector3(0f, 1.325f, 0.28f));

            Material[] glow = { WorldMaterials.M_Circuit_Cyan, WorldMaterials.M_Circuit_Purple, WorldMaterials.M_Hazard };
            uint seed = Hash($"tank{ground.x:F1}{ground.z:F1}");
            for (int i = 0; i < 7; i++)
            {
                float h = 0.25f + 0.35f * Hash01(seed, i);
                float x = -0.38f + 0.76f * i / 6f, z = 0.2f + 0.16f * Hash01(seed, i + 20);
                Obj(t, "Tank Coral", CharacterMeshes.Prism(5, 0.07f, 0.015f, h, 0.1f), glow[i % 3], new Vector3(x, 0.2f + h * 0.5f, z));
            }
            BuildPool(t, "Tank Pool", new Vector3(0f, 0f, 0.95f), 0.9f, WorldMaterials.ReefAccentCyan * 0.2f);
            return root;
        }

        // ---- wall base life ---------------------------------------------------------------------

        /// <summary>Clusters of 3-7 small glowing coral nubs (cyan, pink or amber) plus a low kelp frond at the
        /// given run coordinates. Combined into one mesh per material; every piece under 0.15 m tall.</summary>
        public static GameObject BuildBaseLife(Transform parent, Face f, List<float> clusterAt)
        {
            var root = new GameObject($"Reef Wall Base Life {f.areaId} {f.wallName}");
            root.transform.SetParent(parent, false);
            root.transform.position = f.origin;
            root.AddComponent<KeepsOwnMaterial>();
            if (clusterAt == null || clusterAt.Count == 0) return root;

            Material[] mats = { WorldMaterials.M_Circuit_Cyan, WorldMaterials.M_Circuit_Purple, WorldMaterials.M_Hazard, WorldMaterials.M_KelpGreen };
            var lists = new List<CombineInstance>[4];
            for (int i = 0; i < 4; i++) lists[i] = new List<CombineInstance>();
            uint seed = Hash($"base{f.areaId}{f.wallName}");
            Vector3 up = Vector3.up;

            for (int c = 0; c < clusterAt.Count; c++)
            {
                float t = clusterAt[c];
                int nubs = 3 + Mathf.FloorToInt(Hash01(seed, c * 31) * 5f) % 5;
                int tone = Mathf.FloorToInt(Hash01(seed, c * 31 + 1) * 3f) % 3;
                for (int n = 0; n < nubs; n++)
                {
                    int s = c * 31 + 2 + n * 4;
                    float h = 0.05f + 0.07f * Hash01(seed, s), rad = 0.03f + 0.025f * Hash01(seed, s + 1);
                    Vector3 pos = P(f, t + (Hash01(seed, s + 2) - 0.5f) * 0.7f) + f.inward * (0.12f + 0.45f * Hash01(seed, s + 3)) + up * (h * 0.5f + 0.015f);
                    int mi = (tone + (n == 0 ? 0 : Mathf.FloorToInt(Hash01(seed, s + 5) * 3f))) % 3;
                    lists[mi].Add(new CombineInstance { mesh = CharacterMeshes.Prism(5, rad, rad * 0.45f, h, 0.2f), transform = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one) });
                }
                // Kelp frond: a thin blade leaning 78 degrees off vertical so it stays under 0.15 m.
                Vector3 kp = P(f, t + (Hash01(seed, c * 31 + 28) - 0.5f) * 0.5f) + f.inward * 0.3f + up * 0.05f;
                Quaternion kr = Quaternion.AngleAxis(360f * Hash01(seed, c * 31 + 29), up) * Quaternion.AngleAxis(78f, Vector3.right);
                lists[3].Add(new CombineInstance { mesh = CharacterMeshes.Prism(4, 0.03f, 0.008f, 0.42f, 0.1f), transform = Matrix4x4.TRS(kp, kr, Vector3.one) });
            }

            string[] names = { "Cyan", "Pink", "Amber", "Kelp" };
            for (int i = 0; i < 4; i++)
            {
                if (lists[i].Count == 0 || mats[i] == null) continue;
                var mesh = new Mesh { name = $"ReefWallBaseLife_{names[i]}", hideFlags = HideFlags.HideAndDontSave };
                mesh.CombineMeshes(lists[i].ToArray(), true, true);
                mesh.RecalculateBounds();
                Obj(root.transform, $"Wall Base {names[i]}", mesh, mats[i], Vector3.zero);
            }
            return root;
        }
    }
}
