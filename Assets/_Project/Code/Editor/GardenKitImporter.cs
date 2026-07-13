using UnityEditor;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Import rules for the set-dressing kit (YT-75).
    ///
    /// The kit is a folder of static props, not a folder of characters, and the importer's defaults
    /// are aimed at characters — a rig, an avatar and an animation clip per prop, on a hundred
    /// fence panels. So the settings are pinned here rather than clicked into an inspector: a fresh
    /// clone and the CI machine import the kit exactly as this machine did, which is the whole point
    /// of the code-driven rule (docs/CODE_DRIVEN_SCENES.md).
    ///
    /// The one setting that matters at runtime is the material mode. The kit's meshes are split by
    /// material — bark and leaves, timber and stone — and those material NAMES are the only thing
    /// telling us which is which. Importing them (embedded, so no loose .mat files land in the
    /// project) keeps the names; <see cref="MaxWorlds.Rendering.KitMaterials"/> then throws the kit's
    /// colours away and paints our own.
    ///
    /// Colliders are off: set-dressing must never obstruct movement, and the runtime strips any
    /// collider that slips through anyway.
    /// </summary>
    public sealed class GardenKitImporter : AssetPostprocessor
    {
        public const string KitDir = "Assets/_Project/Resources/GardenKit/";

        public static bool IsKitModel(string path) =>
            !string.IsNullOrEmpty(path) && path.Replace('\\', '/').StartsWith(KitDir);

        private void OnPreprocessModel()
        {
            if (!IsKitModel(assetPath)) return;
            Configure((ModelImporter)assetImporter);
        }

        /// <summary>The house rules for a kit prop. Public so the tests can hold the importer to
        /// them without having to re-import an asset to find out.</summary>
        public static void Configure(ModelImporter importer)
        {
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;

            importer.addCollider = false;

            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;   // flat-shaded; nothing normal-maps a fence
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.weldVertices = true;
            importer.isReadable = false;

            // Keep the kit's material names (we need them) and none of its .mat files (we don't).
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }
    }
}
