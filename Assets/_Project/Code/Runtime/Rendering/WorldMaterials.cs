using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// Puts the stylised materials onto the greybox (YT-50). Self-installing, so the scene file
    /// stays untouched and CI/WebGL get the same surfaces as the editor.
    ///
    /// It deliberately only dresses *world* surfaces — ground, walls, props. Anything that can be
    /// damaged (Max, enemies, the boss, the Mower Hutch) is left alone: those renderers are tinted
    /// at runtime by gameplay to show hit flashes, tells and damage state, and re-skinning them
    /// here would mean this system and the gameplay code fighting over the same colour every frame.
    /// They keep their existing look until they get real models.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldMaterials : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<WorldMaterials>() != null) return;
            new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
        }

        [SerializeField] private BiomePalette palette = BiomePalette.Backyard;

        private void Awake() => Apply(palette);

        /// <summary>Dress every world surface in the biome. Returns how many renderers it touched.</summary>
        public int Apply(BiomePalette p)
        {
            palette = p;
            MaterialLibrary.Palette = p;

            int dressed = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!IsWorldSurface(r)) continue;

                var mat = MaterialLibrary.Surface(KindOf(r));
                if (mat == null) continue;

                r.sharedMaterial = mat;
                dressed++;
            }
            return dressed;
        }

        /// <summary>World surfaces are the things gameplay never recolours, and that don't already
        /// have a look of their own. Anything damageable is owned by gameplay's tint logic (see the
        /// class summary); anything marked <see cref="KeepsOwnMaterial"/> is imported art that
        /// arrived with its materials attached, and repainting a tree in flat lawn-green is the
        /// opposite of what the art pass is for.</summary>
        public static bool IsWorldSurface(Renderer r)
        {
            if (r == null) return false;
            if (r.GetComponentInParent<KeepsOwnMaterial>() != null) return false;
            return r.GetComponentInParent<IDamageable>() == null;
        }

        /// <summary>Classify by shape rather than by name: a flat plane is ground, a tall box is a
        /// wall, anything else is a prop. Name-matching would break the moment the arena is
        /// generated rather than scaffolded.
        ///
        /// The height check alone stopped being reliable the moment a world authored walls shorter
        /// than its own cover (MV-742: World 2's wallHeight is 1.5 m, some of its cover is 1.6 m) — no
        /// single threshold can separate the two in either direction for that data. A box built as a
        /// boundary wall carries <see cref="StructuralWall"/>, put there by the one place that knows
        /// for certain what it is (<c>MapRuntime.Build</c>), so that marker is checked before shape
        /// ever has to guess.</summary>
        public static SurfaceKind KindOf(Renderer r)
        {
            if (r.GetComponentInParent<StructuralWall>() != null) return SurfaceKind.Wall;

            var filter = r.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null && mesh.name.StartsWith("Plane")) return SurfaceKind.Ground;

            Vector3 size = r.bounds.size;
            bool flat = size.y < 0.25f && (size.x > 4f || size.z > 4f);
            if (flat) return SurfaceKind.Ground;

            return size.y >= 2f ? SurfaceKind.Wall : SurfaceKind.Prop;
        }

        // --- MV-713: World 3 Reef ship kit — eight fixed-hex named materials. ---
        //
        // These sit ALONGSIDE the biome sweep above rather than inside it: BiomePalette.Reef feeds the
        // same eight tones into the shape-classified Ground/Wall/Prop/Metal/Foliage sweep every world
        // already gets, but the ticket's AC1 asks for the exact hex values to be readable back as named
        // materials in their own right — the guard against a typo'd hex surviving unnoticed inside a
        // recoloured/contrast-adjusted procedural variant. Built via the same shader-resolution chain
        // MaterialLibrary already exposes (MaterialLibrary.SurfaceShader), cached the same way, and
        // GPU-instanced per the ticket's own "URP emissive, GPU-instanced" instruction — unlike the
        // procedural world surfaces, none of these are static-batched with anything, so there is no
        // batching/instancing conflict to avoid (see MaterialLibrary.Build's own note on that).
        private static readonly Dictionary<string, Material> s_reefCache = new Dictionary<string, Material>();

        private static Color HexColor(uint rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        public static readonly Color ReefShipFloor = HexColor(0x132234);
        public static readonly Color ReefShipWall = HexColor(0x1E3247);
        public static readonly Color ReefCircuitCyan = HexColor(0x3CDCF2);
        public static readonly Color ReefCircuitPurple = HexColor(0xC455E8);
        public static readonly Color ReefBioGlow = HexColor(0x5CF2A4);
        public static readonly Color ReefHazard = HexColor(0xFF8B2E);
        public static readonly Color ReefMetalDark = HexColor(0x0C1622);

        /// <summary>The glass gradient's NEAR (top) tone — brighter, closer to the surface.</summary>
        public static readonly Color ReefGlassOceanNear = HexColor(0x0E6FA8);

        /// <summary>The glass gradient's FAR (bottom) tone — darker, deeper water.</summary>
        public static readonly Color ReefGlassOceanFar = HexColor(0x052B49);

        public static Material M_ShipFloor => ReefMaterial("M_ShipFloor", ReefShipFloor);
        public static Material M_ShipWall => ReefMaterial("M_ShipWall", ReefShipWall);
        public static Material M_Circuit_Cyan => ReefMaterial("M_Circuit_Cyan", ReefCircuitCyan, emissive: true);
        public static Material M_Circuit_Purple => ReefMaterial("M_Circuit_Purple", ReefCircuitPurple, emissive: true);
        public static Material M_BioGlow => ReefMaterial("M_BioGlow", ReefBioGlow, emissive: true);
        public static Material M_Hazard => ReefMaterial("M_Hazard", ReefHazard, emissive: true);
        public static Material M_MetalDark => ReefMaterial("M_MetalDark", ReefMetalDark);

        /// <summary>The observation-window glass — a two-stop vertical gradient (near/far) rather than a
        /// flat colour, built the same way <see cref="MaterialLibrary"/> bakes its own two-tone surfaces
        /// (<c>StylizedTextures.Blend</c>): a small vertical texture is cheaper than a second shader, and
        /// keeps this material readable through the same <c>_BaseMap</c>/<c>_BaseColor</c> pair every
        /// other surface in the game already uses.</summary>
        public static Material M_GlassOcean => ReefGlassMaterial();

        private static Material ReefMaterial(string key, Color color, bool emissive = false)
        {
            if (s_reefCache.TryGetValue(key, out var cached) && cached != null) return cached;

            Shader shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var m = new Material(shader)
            {
                name = key,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);

            if (emissive)
            {
                // Dense cyan/purple circuitry and the hazard tell both read as LIT, not just coloured
                // (ticket: "dense cyan emissive circuitry", "hazard orange used only for danger").
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", color);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            s_reefCache[key] = m;
            return m;
        }

        private static Material ReefGlassMaterial()
        {
            const string key = "M_GlassOcean";
            if (s_reefCache.TryGetValue(key, out var cached) && cached != null) return cached;

            Shader shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var m = new Material(shader)
            {
                name = key,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };

            var tex = new Texture2D(1, 2, TextureFormat.RGBA32, mipChain: false)
            {
                name = "ReefGlassOceanGradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            // Row 0 = near (top of the window), row 1 = far (bottom) — Texture2D rows run bottom-to-top,
            // so the FAR tone goes in row 0 and the NEAR tone in row 1 to read top-lit, bottom-dark.
            tex.SetPixel(0, 0, ReefGlassOceanFar);
            tex.SetPixel(0, 1, ReefGlassOceanNear);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            // The mean of the gradient, so a shader that ignores the texture (the URP/Lit fallback with
            // no UVs, same degrade path MaterialLibrary.Build documents) still renders a plausible glass
            // tone rather than default white.
            Color mean = Color.Lerp(ReefGlassOceanNear, ReefGlassOceanFar, 0.5f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", mean);
            if (m.HasProperty("_Color")) m.SetColor("_Color", mean);

            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.6f);   // wet glass, not matte hull

            s_reefCache[key] = m;
            return m;
        }
    }
}
