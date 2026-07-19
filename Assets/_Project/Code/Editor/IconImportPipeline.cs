using UnityEditor;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Import settings for app-icon source art (YT-104). Code-driven per
    /// <c>docs/CODE_DRIVEN_SCENES.md</c>: a fresh clone imports the icon identically without anyone
    /// touching the inspector, and the settings can't drift out of the repo into someone's .meta.
    ///
    /// The alpha rules matter — Apple rejects an App Store icon that carries an alpha channel, even
    /// when every pixel is opaque.
    /// </summary>
    public sealed class IconImportPipeline : AssetPostprocessor
    {
        private const string IconFolder = "/_Project/Art/Icons/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains(IconFolder)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.sRGBTexture = true;
            // Uncompressed at full size: this is the source Unity downscales every icon slot from,
            // so compression artefacts here would show up on every icon in the catalog.
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = true;
        }
    }
}
