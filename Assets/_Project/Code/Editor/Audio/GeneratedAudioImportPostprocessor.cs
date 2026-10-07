using UnityEditor;
using UnityEngine;

namespace MaxWorlds.Editor.Audio
{
    /// <summary>
    /// MV-1134: every clip <see cref="ElevenLabsAudioGenerator"/> writes under
    /// <see cref="TargetFolder"/> gets the same import settings — Force To Mono (belt-and-suspenders;
    /// the generator already writes mono WAVs), Decompress On Load (these are short one-shots played
    /// through a 16-voice pool, not streamed), ADPCM (small on disk without MP3's startup silence), and
    /// Preload Audio Data (so the first play of a cue isn't the one that pays a load stall).
    /// </summary>
    internal sealed class GeneratedAudioImportPostprocessor : AssetPostprocessor
    {
        private const string TargetFolder = "Assets/_Project/Resources/Audio/Sfx/";

        private void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(TargetFolder)) return;

            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = true;

            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.ADPCM;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
        }
    }
}
