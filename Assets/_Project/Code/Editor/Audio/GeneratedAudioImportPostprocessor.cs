using UnityEditor;
using UnityEngine;

namespace MaxWorlds.Editor.Audio
{
    /// <summary>
    /// MV-1134: every clip <see cref="ElevenLabsAudioGenerator"/> writes under
    /// <see cref="SfxFolder"/> gets the same import settings — Force To Mono (belt-and-suspenders;
    /// the generator already writes mono WAVs), Decompress On Load (these are short one-shots played
    /// through a 16-voice pool, not streamed), ADPCM (small on disk without MP3's startup silence), and
    /// Preload Audio Data (so the first play of a cue isn't the one that pays a load stall).
    ///
    /// MV-1137: every clip under <see cref="MusicFolder"/> gets a different profile instead — these
    /// are 60-120 s generated tracks, not one-shots, so Streaming (never fully decoded into memory),
    /// Vorbis at quality 50 (music tolerates compression artefacts one-shots don't), Force To Mono off
    /// (the generated clip is already a single ElevenLabs render; forcing mono would just downmix it
    /// again for no gain) and Preload Audio Data off (nothing to gain preloading a streamed clip).
    /// </summary>
    internal sealed class GeneratedAudioImportPostprocessor : AssetPostprocessor
    {
        private const string SfxFolder = "Assets/_Project/Resources/Audio/Sfx/";
        private const string MusicFolder = "Assets/_Project/Resources/Audio/Music/";

        private void OnPreprocessAudio()
        {
            string path = assetPath.Replace('\\', '/');
            var importer = (AudioImporter)assetImporter;

            if (path.StartsWith(SfxFolder))
            {
                importer.forceToMono = true;

                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.ADPCM;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
            }
            else if (path.StartsWith(MusicFolder))
            {
                importer.forceToMono = false;

                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.5f;
                settings.preloadAudioData = false;
                importer.defaultSampleSettings = settings;
            }
        }
    }
}
