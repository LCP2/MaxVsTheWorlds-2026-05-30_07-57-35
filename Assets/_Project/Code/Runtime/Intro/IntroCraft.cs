using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Intro
{
    /// <summary>
    /// The opening cinematic's palette (YT-156).
    ///
    /// The story is a ROBOT INVASION: the comet is the invasion's ARRIVAL, and the pieces that split
    /// off it and land on Earth are the alien invaders touching down (Lee's fiction note on YT-154).
    /// So the comet's pods are painted from the SAME cold-chitin / hot-core language as the Backyard
    /// boss — the BROOD-HULK (YT-150) — so the thing that lands in the intro is visibly the thing Max
    /// ends up fighting. These constants are copied to the digit from <see cref="MaxWorlds.VFX"/>'s
    /// Brood-Hulk rig on purpose: one look for the invaders, set in the intro, paid off in the fight.
    /// </summary>
    public static class IntroPalette
    {
        // ---- space -------------------------------------------------------------------------------
        /// <summary>Deep space. Not pure black — a trace of cold blue so the black reads as a sky and
        /// not as a dead framebuffer, and so the stars have something to be brighter than.</summary>
        public static readonly Color Space = new Color(0.015f, 0.017f, 0.035f);
        public static readonly Color Star = new Color(0.85f, 0.90f, 1f);
        public static readonly Color StarWarm = new Color(1f, 0.92f, 0.80f);
        public static readonly Color SunGlow = new Color(1f, 0.95f, 0.82f);

        // ---- Earth from orbit --------------------------------------------------------------------
        public static readonly Color Ocean = new Color(0.10f, 0.26f, 0.52f);
        public static readonly Color Land = new Color(0.28f, 0.46f, 0.22f);
        public static readonly Color LandDry = new Color(0.55f, 0.47f, 0.28f);
        public static readonly Color Ice = new Color(0.90f, 0.94f, 0.98f);
        /// <summary>The thin day-lit rim of atmosphere on the globe's edge.</summary>
        public static readonly Color Atmosphere = new Color(0.45f, 0.72f, 1f);

        // ---- the comet ---------------------------------------------------------------------------
        /// <summary>The head: a hot, near-white core. It is the brightest thing in the frame.</summary>
        public static readonly Color CometCore = new Color(1f, 0.93f, 0.72f);
        /// <summary>The scorch trail cools from the core out to a deep ember.</summary>
        public static readonly Color CometTrail = new Color(1f, 0.48f, 0.14f);
        public static readonly Color CometEmber = new Color(0.95f, 0.24f, 0.06f);

        // ---- the invaders (Brood-Hulk language, YT-150) ------------------------------------------
        public static readonly Color Chitin = new Color(0.10f, 0.078f, 0.157f);   // void chitin shell
        public static readonly Color ChitinPlate = new Color(0.165f, 0.129f, 0.251f); // aubergine plate
        public static readonly Color XenoTeal = new Color(0.33f, 0.88f, 0.88f);   // the brood-glow
        public static readonly Color EyeAmber = new Color(1f, 0.66f, 0.12f);      // the ocular core

        // ---- Earth surface / the town ------------------------------------------------------------
        public static readonly Color Grass = new Color(0.34f, 0.52f, 0.24f);
        public static readonly Color Road = new Color(0.22f, 0.22f, 0.25f);
        public static readonly Color RoofTile = new Color(0.42f, 0.24f, 0.19f);
        public static readonly Color HouseWall = new Color(0.62f, 0.58f, 0.50f);
        public static readonly Color HouseWallB = new Color(0.55f, 0.48f, 0.42f);

        // ---- Max's shed --------------------------------------------------------------------------
        public static readonly Color ShedPlank = new Color(0.40f, 0.30f, 0.20f);
        public static readonly Color ShedPlankDark = new Color(0.28f, 0.21f, 0.14f);
        public static readonly Color ShedFloor = new Color(0.30f, 0.25f, 0.20f);
        public static readonly Color Workbench = new Color(0.36f, 0.27f, 0.18f);
        public static readonly Color Bulb = new Color(1f, 0.86f, 0.55f);
        /// <summary>The daylight past the door — the world Max is about to step into.</summary>
        public static readonly Color Daylight = new Color(1f, 0.97f, 0.86f);

        // ---- Max (from MaxRig's palette) ---------------------------------------------------------
        public static readonly Color Hoodie = new Color(1f, 0.35f, 0.12f);       // hot orange-red
        public static readonly Color HoodieShade = new Color(0.80f, 0.28f, 0.10f);
        public static readonly Color Skin = new Color(0.87f, 0.63f, 0.46f);
        public static readonly Color Hair = new Color(0.33f, 0.20f, 0.12f);
        public static readonly Color Trousers = new Color(0.20f, 0.21f, 0.25f);
        public static readonly Color Steel = new Color(0.58f, 0.64f, 0.72f);
        public static readonly Color HoseGreen = new Color(0.24f, 0.55f, 0.30f);
        public static readonly Color TankWater = new Color(0.31f, 0.76f, 0.97f);
    }

    /// <summary>
    /// The shared "build a body out of primitives" toolkit for the intro (YT-156), the same idiom the
    /// character rigs use (<see cref="MaxWorlds.VFX.MaxRig"/>, the Brood-Hulk): compose Unity
    /// primitives into a hierarchy, STRIP their colliders, and hand every one a real material — a
    /// primitive's default material has no URP subshader and ships MAGENTA in a player build (YT-58).
    ///
    /// Intro materials are process-lifetime and cached by key, exactly as
    /// <see cref="MaxWorlds.Arena.BackyardBackdrop"/> caches its backdrop paints: the cinematic plays
    /// once and is torn down, and a dozen cached materials outliving it is cheaper and safer than
    /// destroying materials that a torn-down renderer might still reference for a frame.
    /// </summary>
    public static class IntroBuild
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private static readonly Dictionary<string, Material> s_lit = new Dictionary<string, Material>();

        /// <summary>A plain lit surface in a flat colour, cached. Duller/matte by default — the intro's
        /// contrast budget goes on the glowing comet and the invaders, not on the set.</summary>
        public static Material Lit(string key, Color color, Color? emission = null)
        {
            string k = emission.HasValue ? $"{key}|e" : key;
            if (s_lit.TryGetValue(k, out var cached) && cached != null) return cached;

            var shader = MaterialLibrary.SurfaceShader;   // URP/Lit down a fallback chain, never magenta
            if (shader == null) return null;

            var m = new Material(shader) { name = "Intro_" + key, hideFlags = HideFlags.HideAndDontSave };
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.06f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (emission.HasValue && m.HasProperty(EmissionId))
            {
                m.SetColor(EmissionId, emission.Value);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            s_lit[k] = m;
            return m;
        }

        /// <summary>One solid part: a primitive, collider stripped, given a real material.</summary>
        public static Transform Part(Transform parent, string name, PrimitiveType shape, Vector3 at,
                                     Vector3 scale, Material mat, Quaternion? rot = null,
                                     bool castShadows = true)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Strip(go);

            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = at;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            if (!castShadows) r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>An empty pivot to hang parts off and animate as one.</summary>
        public static Transform Pivot(Transform parent, string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = at;
            return go.transform;
        }

        /// <summary>
        /// A glowing "light" — an additive, unlit sphere that CATCHES no shadow and IS the light. The
        /// comet core, an invader's eye, a star, the daylight past the door: all the same trick the
        /// boss's lamps and Max's goggles use — one shared VFX material, a property block per renderer,
        /// so each glows its own colour without minting a material each.
        /// </summary>
        public static MeshRenderer Glow(Transform parent, string name, Vector3 at, float size, Color color,
                                        float flatten = 1f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Strip(go);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = at;
            go.transform.localScale = new Vector3(size, size, size * flatten);

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            SetGlow(r, color);
            return r;
        }

        private static MaterialPropertyBlock s_mpb;

        /// <summary>Re-tint a glow renderer (drives the tells: an eye lighting, the daylight brightening).</summary>
        public static void SetGlow(Renderer r, Color color)
        {
            if (r == null) return;
            s_mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(s_mpb);
            s_mpb.SetColor(BaseColorId, color);
            r.SetPropertyBlock(s_mpb);
        }

        /// <summary>
        /// GLSL-style smoothstep: 0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>,
        /// smoothly ramped between — a value that remaps <paramref name="x"/> into 0..1.
        ///
        /// NOT <see cref="Mathf.SmoothStep"/>, which interpolates BETWEEN its first two arguments and
        /// returns a value in [from, to] — the exact confusion <see cref="MaxWorlds.VFX.VfxMaterials"/>'s
        /// Edge() warns about, and the one that made the shed door report "55% open" while shut.
        /// </summary>
        public static float Ramp(float edge0, float edge1, float x)
        {
            if (edge1 <= edge0) return x >= edge1 ? 1f : 0f;
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Nothing in the cinematic collides with anything: it is a picture, not a place.</summary>
        public static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }
        }
    }
}
