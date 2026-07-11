using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using MaxWorlds.Models;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// The generated-model import pipeline (YT-51).
    ///
    /// Flow: drop a model in <see cref="IncomingDir"/>, run the tool, get a prefab in
    /// <see cref="PrefabDir"/> loadable by stable key via <see cref="ModelLibrary"/> — with no
    /// manual inspector wiring at any point. See docs/ASSET_PIPELINE.md.
    ///
    /// The tool is idempotent: re-running it re-imports and rebuilds every incoming model, so a
    /// regenerated asset can simply overwrite the old file and be re-processed. That matters for
    /// an AI-generation workflow, where models are re-rolled far more often than they're authored.
    ///
    /// FORMAT NOTE: Unity imports FBX/OBJ/DAE natively. It does NOT import .glb/.gltf without the
    /// glTFast package, which is not in this project's manifest — and adding a package is a
    /// guardrail on this stream, so it hasn't been added. Meshy and Tripo both export FBX, which
    /// works today. If GLB is wanted as the source format, that's a package decision for Lee; the
    /// only thing that changes here is the extension filter (see AcceptedExtensions).
    /// </summary>
    public static class ModelImportPipeline
    {
        /// <summary>Drop generated models here.</summary>
        public const string IncomingDir = "Assets/_Project/Art/Models/Incoming";

        /// <summary>Prefabs land here. Under Resources/ so they load by key at runtime.</summary>
        public const string PrefabDir = "Assets/_Project/Resources/Models";

        /// <summary>What Unity can import without adding a package. See the class note on GLB.</summary>
        public static readonly string[] AcceptedExtensions = { ".fbx", ".obj", ".dae" };

        [MenuItem("MaxWorlds/Art/Process Incoming Models (YT-51)")]
        public static void ProcessMenu()
        {
            int n = Process();
            EditorUtility.DisplayDialog("Model import",
                n == 0
                    ? $"No models found in {IncomingDir}.\n\nDrop an .fbx there and run this again."
                    : $"Processed {n} model(s) into {PrefabDir}.",
                "OK");
        }

        /// <summary>Headless entry point (-executeMethod MaxWorlds.Editor.ModelImportPipeline.Run).</summary>
        public static void Run()
        {
            int n = Process();
            Debug.Log($"[ModelImport] processed {n} model(s)");
            EditorApplication.Exit(0);
        }

        /// <summary>Import every model in the incoming folder and build a keyed prefab for each.
        /// Returns how many were processed.</summary>
        public static int Process()
        {
            EnsureDir(IncomingDir);
            EnsureDir(PrefabDir);

            var sources = FindIncomingModels();
            if (sources.Count == 0)
            {
                Debug.Log($"[ModelImport] nothing in {IncomingDir}");
                return 0;
            }

            int done = 0;
            foreach (var path in sources)
            {
                if (BuildPrefab(path) != null) done++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return done;
        }

        public static List<string> FindIncomingModels()
        {
            var found = new List<string>();
            if (!Directory.Exists(IncomingDir)) return found;

            foreach (var file in Directory.GetFiles(IncomingDir))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (System.Array.IndexOf(AcceptedExtensions, ext) < 0) continue;
                found.Add(file.Replace('\\', '/'));
            }
            return found;
        }

        /// <summary>
        /// The key a source file maps to: its filename, lowercased, non-alphanumerics collapsed to
        /// underscores. "Big Bermuda v3.fbx" -> "big_bermuda_v3". Deterministic, so re-importing a
        /// regenerated model overwrites the same prefab instead of quietly creating a second one.
        /// </summary>
        public static string KeyFor(string sourcePath)
        {
            string name = Path.GetFileNameWithoutExtension(sourcePath).ToLowerInvariant();
            var sb = new StringBuilder(name.Length);
            bool lastUnderscore = false;
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                    lastUnderscore = false;
                }
                else if (!lastUnderscore && sb.Length > 0)
                {
                    sb.Append('_');
                    lastUnderscore = true;
                }
            }
            return sb.ToString().Trim('_');
        }

        /// <summary>Apply the house import settings, then save a keyed prefab. Returns its path.</summary>
        public static string BuildPrefab(string sourcePath)
        {
            var importer = AssetImporter.GetAtPath(sourcePath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[ModelImport] {sourcePath} did not import as a model — skipped.");
                return null;
            }

            ConfigureImporter(importer);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (model == null)
            {
                Debug.LogError($"[ModelImport] could not load {sourcePath} after import.");
                return null;
            }

            string key = KeyFor(sourcePath);
            string prefabPath = $"{PrefabDir}/{key}.prefab";

            // Build the prefab under a clean root that carries the key, rather than saving the raw
            // model: gameplay and the swap system address the root, so the generated hierarchy
            // underneath can change completely on a re-roll without breaking anything.
            var root = new GameObject(key);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.transform.SetParent(root.transform, worldPositionStays: false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                var tag = root.AddComponent<GeneratedModel>();
                tag.Set(key, sourcePath);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"[ModelImport] {sourcePath} -> {prefabPath} (key '{key}')");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            return prefabPath;
        }

        /// <summary>The house rules for a generated model. Centralised so every asset in the game
        /// lands with the same settings, whoever generated it.</summary>
        public static void ConfigureImporter(ModelImporter importer)
        {
            // Rigged by default: Meshy/Tripo output is a generic skeleton, not a Unity humanoid.
            // Generic keeps the bones intact and animatable without forcing a humanoid remap that
            // these models will not satisfy.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;

            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;

            // Generated meshes come in dense and unoptimised. Colliders come from the greybox that
            // stays behind the swap, so the model itself never needs one.
            importer.addCollider = false;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.weldVertices = true;
            importer.isReadable = false;

            // Keep the generated materials/textures inside the asset rather than spraying loose
            // .mat files into the project.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

            importer.SaveAndReimport();
        }

        private static void EnsureDir(string dir)
        {
            if (Directory.Exists(dir)) return;
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }
    }
}
