using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Models;
using MaxWorlds.Rendering;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Turns the free garden kit into props the game can load by key (YT-75).
    ///
    /// Source: Kenney's Nature Kit, CC0 — see the License.txt beside the models. The kit's .fbx files
    /// are committed; this tool imports them and writes one prefab per model into a Resources folder,
    /// so <see cref="ModelLibrary"/> can fetch a fence panel with a string and nothing is ever dragged
    /// into an inspector slot. Run once after adding or replacing a model; the prefabs it produces are
    /// committed, so CI and a fresh clone just load them.
    ///
    /// It does two things that are not obvious, and both are load-bearing:
    ///
    ///   * IT REBUILDS THE MATERIALS. The kit's models are flat-coloured — no textures, one colour per
    ///     material. Rather than trust whatever shader the FBX importer picks (and find out on the
    ///     deployed link that it picked one URP strips, which is exactly how the enemies went magenta
    ///     — YT-58), every material is re-made here on the same URP shader the rest of the game uses,
    ///     carrying the colour the kit author chose. The result is a committed .mat asset whose shader
    ///     the build can see, so there is nothing left to strip.
    ///
    ///   * IT NORMALISES THE PIVOT AND THE SCALE. Kenney models are authored on a grid — a fence panel
    ///     sits on the EDGE of its cell, not the middle — and FBX unit conventions are a coin flip.
    ///     Placement code should not have to know any of that. So each prefab is re-centred on its own
    ///     bounds with its base on y=0, and scaled until it measures exactly what
    ///     <see cref="PropCatalog"/> says it measures. After that, "place a 3 m tree at (4, 9)" means
    ///     what it says.
    /// </summary>
    public static class KitImport
    {
        public const string KitDir = "Assets/_Project/Art/Kits/KenneyNatureKit";
        public const string MaterialDir = KitDir + "/Materials";
        public const string PrefabDir = "Assets/_Project/Resources/Models/props";

        [MenuItem("MaxWorlds/Art/Import Garden Kit (YT-75)")]
        public static void ImportMenu()
        {
            int n = Import();
            EditorUtility.DisplayDialog("Garden kit",
                n == 0 ? $"No .fbx found in {KitDir}." : $"Built {n} prop prefab(s) in {PrefabDir}.",
                "OK");
        }

        /// <summary>Headless entry point (-executeMethod MaxWorlds.Editor.KitImport.Run).</summary>
        public static void Run()
        {
            int n = Import();
            Debug.Log($"[KitImport] built {n} prop prefab(s)");
            EditorApplication.Exit(n > 0 ? 0 : 1);
        }

        /// <summary>Import every kit model and build its prefab. Returns how many were built.</summary>
        public static int Import()
        {
            if (!Directory.Exists(KitDir))
            {
                Debug.LogError($"[KitImport] no kit at {KitDir}");
                return 0;
            }

            EnsureDir(MaterialDir);
            EnsureDir(PrefabDir);

            var materials = new Dictionary<string, Material>();
            int built = 0;

            foreach (string source in Directory.GetFiles(KitDir, "*.fbx"))
            {
                string path = source.Replace('\\', '/');
                string key = Path.GetFileNameWithoutExtension(path);

                if (!PropCatalog.Has(key))
                {
                    // A model nothing can place is dead weight in the build. Say so rather than
                    // silently shipping it.
                    Debug.LogWarning($"[KitImport] {key} is not in PropCatalog — skipped.");
                    continue;
                }

                if (BuildPrefab(path, key, materials)) built++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return built;
        }

        private static bool BuildPrefab(string source, string key, Dictionary<string, Material> materials)
        {
            var importer = AssetImporter.GetAtPath(source) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[KitImport] {source} did not import as a model.");
                return false;
            }

            Configure(importer);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (model == null)
            {
                Debug.LogError($"[KitImport] could not load {source} after import.");
                return false;
            }

            var root = new GameObject(key);
            try
            {
                var instance = Object.Instantiate(model);
                instance.name = "model";
                instance.transform.SetParent(root.transform, worldPositionStays: false);

                Remap(instance, materials);

                if (!Normalise(instance, key))
                {
                    Debug.LogError($"[KitImport] {key} has no renderable geometry.");
                    return false;
                }

                root.AddComponent<KeepsOwnMaterial>();
                root.AddComponent<GeneratedModel>().Set(PropCatalog.ResourceKey(key), source);

                PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{key}.prefab");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>The house rules for a kit prop. No animation, no collider (dressing is scenery),
        /// and readable meshes — runtime static batching combines them, and it can only combine a mesh
        /// it is allowed to read.</summary>
        public static void Configure(ModelImporter importer)
        {
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;

            importer.addCollider = false;
            importer.isReadable = true;
            importer.weldVertices = true;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.meshCompression = ModelImporterMeshCompression.Medium;

            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;   // no normal maps on flat colour

            // Import the kit's materials only so their colours can be read back — Remap replaces them.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

            importer.SaveAndReimport();
        }

        /// <summary>Swap every imported material for one of ours in the same colour, built on the
        /// shader the rest of the game renders with. See the class summary for why.</summary>
        private static void Remap(GameObject instance, Dictionary<string, Material> cache)
        {
            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] source = renderer.sharedMaterials;
                var swapped = new Material[source.Length];

                for (int i = 0; i < source.Length; i++)
                {
                    swapped[i] = source[i] == null
                        ? Solid("kit_default", Color.grey, cache)
                        : Solid(source[i].name, source[i].color, cache);
                }

                renderer.sharedMaterials = swapped;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        /// <summary>
        /// The brightest albedo the Backyard's sun can take. Above this, a surface stops being a
        /// colour and becomes a highlight.
        ///
        /// The key light is <see cref="BackyardLook.KeyIntensity"/> = 2.2 and URP/Lit multiplies
        /// albedo by it, so anything much past 0.6 per channel is clipped to white before the
        /// tonemapper ever sees it. Kenney paints his kit in bright pastels for an unlit look —
        /// <c>wood</c> is (1.00, 0.56, 0.38) — and under this sun every fence panel facing the light
        /// came out bleached cream while the same panel in shadow stayed brown. Same material, two
        /// completely different fences.
        /// </summary>
        public const float SunlitAlbedoCeiling = 0.6f;

        /// <summary>
        /// The kit's colours, pulled onto the Backyard's palette AND under its sun.
        ///
        /// Two separate problems, one table:
        ///
        ///  * HUE. Kenney's Nature Kit paints all its foliage a mint turquoise (#29C9AB / #2CD8B8).
        ///    It is a lovely palette and it is not a back garden — dropped on this lawn it reads as
        ///    an alien planet. Green channel clearly dominant, red kept well below it: the line
        ///    YT-69 drew when the lawn came out mustard, which foliage standing on that lawn has to
        ///    hold too.
        ///
        ///  * BRIGHTNESS. The rest of the kit's colours are hue-correct and far too bright to
        ///    survive a 2.2× key (see <see cref="SunlitAlbedoCeiling"/>). Timber, stone, soil and
        ///    flowers are all brought under the ceiling here. They look too dark in a colour picker.
        ///    That is the point: the sun supplies the rest, and <see cref="BiomePalette.Backyard"/>
        ///    already holds the lawn to exactly the same restraint, for exactly the same reason.
        /// </summary>
        private static readonly Dictionary<string, Color> Recolour = new Dictionary<string, Color>
        {
            { "leafsGreen", new Color(0.24f, 0.45f, 0.17f) },   // tree canopy: deeper than the turf
            { "grass", new Color(0.35f, 0.56f, 0.21f) },        // shrubs, tufts, stems: lusher than it

            // Timber: the fence line, the shed's walls, the planters. The most of the yard by area,
            // and the worst of the blow-out — kit wood is (1.00, 0.56, 0.38).
            { "wood", new Color(0.34f, 0.24f, 0.15f) },
            { "woodDark", new Color(0.24f, 0.17f, 0.11f) },
            { "woodBark", new Color(0.22f, 0.16f, 0.12f) },
            { "woodBarkDark", new Color(0.16f, 0.12f, 0.09f) },
            { "woodInner", new Color(0.42f, 0.33f, 0.22f) },    // a cut log's pale face — the light one

            { "dirt", new Color(0.26f, 0.19f, 0.12f) },
            { "dirtDark", new Color(0.18f, 0.13f, 0.09f) },

            // Kit stone is a pale blue-white (0.72, 0.89, 0.91) — it read as snow on the lawn.
            { "stone", new Color(0.36f, 0.35f, 0.33f) },
            { "stoneDark", new Color(0.26f, 0.25f, 0.24f) },

            // Flowers keep their saturation and lose their glare: they are the only strong colours in
            // the yard, and they are tiny — Max (orange) and the robots still have to be the loudest
            // things on the screen.
            { "colorRed", new Color(0.42f, 0.13f, 0.14f) },
            { "colorYellow", new Color(0.52f, 0.42f, 0.12f) },
            { "colorPurple", new Color(0.30f, 0.18f, 0.42f) },
            { "colorWhite", new Color(0.55f, 0.54f, 0.52f) },

            { "_defaultMat", new Color(0.30f, 0.29f, 0.27f) },  // the odd bracket, rim and edging
        };

        /// <summary>The colour a kit material is painted: ours if we chose one, otherwise the kit's
        /// own — dimmed if it would clip. The dimming is a backstop, not a policy: a prop whose
        /// material we never classified should look dull, not blown out, and a new kit dropped in
        /// tomorrow can't paint a white fence across the yard before anyone notices.</summary>
        private static Color Recoloured(string material, Color kit) =>
            Recolour.TryGetValue(material, out Color c) ? c : UnderTheSun(kit);

        /// <summary>Scale a colour down until nothing in it clips, keeping its hue and its internal
        /// contrast — dividing by the peak, rather than clamping each channel, is what stops a
        /// too-bright orange from turning into a washed-out yellow on the way.</summary>
        public static Color UnderTheSun(Color c)
        {
            float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (peak <= SunlitAlbedoCeiling) return c;

            float k = SunlitAlbedoCeiling / peak;
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }

        private static Material Solid(string name, Color color, Dictionary<string, Material> cache)
        {
            string clean = name.Replace(" (Instance)", string.Empty).Trim();
            if (cache.TryGetValue(clean, out var cached)) return cached;

            string path = $"{MaterialDir}/kit_{clean}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null)
            {
                mat = new Material(MaterialLibrary.SurfaceShader) { name = $"kit_{clean}" };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = MaterialLibrary.SurfaceShader;
            }

            Color c = Recoloured(clean, color);

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 0f);
            mat.enableInstancing = true;

            EditorUtility.SetDirty(mat);
            cache[clean] = mat;
            return mat;
        }

        /// <summary>Re-centre the model on its own bounds with its base on the ground, and scale it
        /// until it is exactly the size the catalog promises. Returns false if there was nothing to
        /// measure.</summary>
        private static bool Normalise(GameObject instance, string key)
        {
            var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) return false;

            // Measure with the model's own position zeroed, because the correction REPLACES that
            // position. Measuring with it still applied reads bounds the result no longer has —
            // which is how a fence panel ended up hovering 5 cm above the lawn.
            Transform t = instance.transform;
            Vector3 rotation = t.localEulerAngles;   // the FBX's axis conversion: keep it
            Vector3 scale = t.localScale;
            t.localPosition = Vector3.zero;

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            if (b.size.sqrMagnitude <= 0f) return false;

            // One factor, from the axis with the most to measure: a per-axis fit would distort a model
            // whose bounds differ from the catalog by a rounding error on some thin axis.
            Vector3 want = PropCatalog.Size(key);
            int axis = want.x >= want.y && want.x >= want.z ? 0 : (want.y >= want.z ? 1 : 2);
            float have = b.size[axis];
            float fit = have > 1e-5f ? want[axis] / have : 1f;

            t.localEulerAngles = rotation;
            t.localScale = scale * fit;
            t.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z) * fit;
            return true;
        }

        private static void EnsureDir(string dir)
        {
            if (Directory.Exists(dir)) return;
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }
    }
}
