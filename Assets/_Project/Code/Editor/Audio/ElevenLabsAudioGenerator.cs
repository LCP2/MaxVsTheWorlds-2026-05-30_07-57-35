using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using UnityEditor;
using UnityEngine;
using MaxWorlds.Audio;

namespace MaxWorlds.Editor.Audio
{
    [Serializable]
    internal class AudioManifest
    {
        public SfxManifestEntry[] sfx;
        public MusicManifestEntry[] music;
    }

    [Serializable]
    internal class SfxManifestEntry
    {
        public string id;
        public string prompt;
        public float durationSeconds;
        public float promptInfluence;
        public bool loop;
    }

    [Serializable]
    internal class MusicManifestEntry
    {
        public string id;
        public string prompt;
        public int lengthMs;
    }

    /// <summary>
    /// MV-1134: calls ElevenLabs' sound-generation endpoint for any manifest entry whose output file
    /// doesn't exist yet, post-processes the result with <see cref="GeneratedAudioDsp"/>, and writes a
    /// 16-bit mono WAV into <see cref="OutputDir"/>. Re-running costs nothing for an entry already on
    /// disk; <c>-regen &lt;id&gt;</c> forces one specific entry. Never logs or writes the API key — see
    /// the ticket's "The key is a secret" section.
    /// </summary>
    public static class ElevenLabsAudioGenerator
    {
        private const string ManifestPath = "Assets/_Project/Audio/audio_manifest.json";
        private const string OutputDir = "Assets/_Project/Resources/Audio/Sfx";
        private const string MusicOutputDir = "Assets/_Project/Resources/Audio/Music";
        private const string LogPath = "Logs/elevenlabs_generate.log";
        private const string ApiUrl = "https://api.elevenlabs.io/v1/sound-generation";
        private const string MusicApiUrl = "https://api.elevenlabs.io/v1/music";
        private const string KeyFilePath = @"C:\Dev\MAx CCs\secrets\elevenlabs_api_key.txt";
        private const int SampleRate44100 = 44100;
        private const int SampleRate24000 = 24000;
        private static readonly TimeSpan MusicRequestTimeout = TimeSpan.FromMinutes(5);

        [MenuItem("MAX/Audio/Generate missing sounds")]
        public static void GenerateMissingSoundsMenuItem() => Generate(null);

        public static void GenerateFromCommandLine()
        {
            try
            {
                Generate(GetArg("-regen"));
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                // Deliberately e.Message only, never the response body or key — see "The key is a
                // secret" in the ticket. Debug.LogException would also dump a stack trace containing
                // nothing secret, but keep this path minimal and predictable.
                Debug.LogError($"[ElevenLabsAudioGenerator] {e.Message}");
                EditorApplication.Exit(1);
            }
        }

        private static void Generate(string regenId)
        {
            string manifestJson = File.ReadAllText(ManifestPath);
            var manifest = JsonUtility.FromJson<AudioManifest>(manifestJson);
            Directory.CreateDirectory(OutputDir);

            string apiKey = ResolveApiKey();

            foreach (var entry in manifest.sfx)
            {
                string outputPath = $"{OutputDir}/{entry.id}.wav";
                bool forced = entry.id == regenId;
                if (File.Exists(outputPath) && !forced) continue;

                if (!Enum.TryParse(typeof(SfxCueLibrary.Cue), entry.id, out var cueObj))
                {
                    throw new InvalidOperationException($"manifest id '{entry.id}' is not a SfxCueLibrary.Cue member");
                }
                var cue = (SfxCueLibrary.Cue)cueObj;

                GenerateOne(entry, cue, outputPath, apiKey);
            }

            if (manifest.music != null)
            {
                Directory.CreateDirectory(MusicOutputDir);
                foreach (var entry in manifest.music)
                {
                    string outputPath = $"{MusicOutputDir}/{entry.id}.mp3";
                    bool forced = entry.id == regenId;
                    if (File.Exists(outputPath) && !forced) continue;

                    GenerateMusicOne(entry, outputPath, apiKey);
                }
            }

            AssetDatabase.Refresh();
        }

        private static void GenerateOne(SfxManifestEntry entry, SfxCueLibrary.Cue cue, string outputPath, string apiKey)
        {
            string format;
            int sampleRate;
            byte[] audioBytes;
            string credits;

            var (okHigh, bytesHigh, creditsHigh) = RequestSound(entry, apiKey, "pcm_44100");
            if (okHigh)
            {
                format = "pcm_44100";
                sampleRate = SampleRate44100;
                audioBytes = bytesHigh;
                credits = creditsHigh;
            }
            else
            {
                var (okLow, bytesLow, creditsLow) = RequestSound(entry, apiKey, "pcm_24000");
                if (!okLow)
                {
                    throw new InvalidOperationException($"ElevenLabs sound-generation failed for '{entry.id}' on both pcm_44100 and pcm_24000");
                }
                format = "pcm_24000";
                sampleRate = SampleRate24000;
                audioBytes = bytesLow;
                credits = creditsLow;
            }

            int channelCount = Mathf.Max(1, Mathf.RoundToInt(audioBytes.Length / (sampleRate * 2f * entry.durationSeconds)));
            float[] samples = PcmBytesToFloat(audioBytes, channelCount);
            if (channelCount > 1) samples = GeneratedAudioDsp.ToMono(samples, channelCount);

            if (!entry.loop)
            {
                samples = GeneratedAudioDsp.TrimLeadingSilence(samples, sampleRate);
                samples = GeneratedAudioDsp.TrimTrailingSilence(samples);
                GeneratedAudioDsp.FadeInOut(samples, sampleRate);
            }

            float targetPeak = SfxCueLibrary.Presets[cue][0].Volume;
            GeneratedAudioDsp.ScaleToPeak(samples, targetPeak);

            WriteWav(outputPath, samples, sampleRate);

            float seconds = samples.Length / (float)sampleRate;
            File.AppendAllText(LogPath,
                $"ELEVENLABS {entry.id} OK format={format} seconds={seconds.ToString("0.###", CultureInfo.InvariantCulture)} credits={credits}\n");
        }

        private static void GenerateMusicOne(MusicManifestEntry entry, string outputPath, string apiKey)
        {
            using var client = new HttpClient { Timeout = MusicRequestTimeout };
            client.DefaultRequestHeaders.Add("xi-api-key", apiKey);

            string json = "{\"prompt\":" + JsonStringLiteral(entry.prompt) +
                ",\"music_length_ms\":" + entry.lengthMs.ToString(CultureInfo.InvariantCulture) +
                ",\"force_instrumental\":true}";
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = client.PostAsync($"{MusicApiUrl}?output_format=mp3_44100_128", content).GetAwaiter().GetResult();
            byte[] bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                string snippet = Encoding.UTF8.GetString(bytes, 0, Mathf.Min(bytes.Length, 300));
                throw new InvalidOperationException(
                    $"ElevenLabs music failed for '{entry.id}': HTTP {(int)response.StatusCode} {snippet}");
            }

            File.WriteAllBytes(outputPath, bytes);

            File.AppendAllText(LogPath, $"ELEVENLABS {entry.id} OK format=mp3_44100_128 bytes={bytes.Length}\n");
        }

        private static (bool ok, byte[] bytes, string credits) RequestSound(SfxManifestEntry entry, string apiKey, string outputFormat)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("xi-api-key", apiKey);

            string json = "{\"text\":" + JsonStringLiteral(entry.prompt) +
                ",\"duration_seconds\":" + entry.durationSeconds.ToString(CultureInfo.InvariantCulture) +
                ",\"prompt_influence\":" + entry.promptInfluence.ToString(CultureInfo.InvariantCulture) +
                ",\"loop\":" + (entry.loop ? "true" : "false") + "}";
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = client.PostAsync($"{ApiUrl}?output_format={outputFormat}", content).GetAwaiter().GetResult();
            byte[] bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return (false, bytes, null);

            string credits = response.Headers.TryGetValues("character-cost", out var values)
                ? System.Linq.Enumerable.FirstOrDefault(values)
                : "?";
            return (true, bytes, credits);
        }

        private static string JsonStringLiteral(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static float[] PcmBytesToFloat(byte[] bytes, int channelCount)
        {
            int frameCount = bytes.Length / (2 * channelCount);
            var samples = new float[frameCount * channelCount];
            for (int i = 0; i < samples.Length; i++)
            {
                short raw = BitConverter.ToInt16(bytes, i * 2);
                samples[i] = raw / 32768f;
            }
            return samples;
        }

        private static void WriteWav(string path, float[] samples, int sampleRate)
        {
            const int bitsPerSample = 16;
            const int channels = 1;
            int byteRate = sampleRate * channels * bitsPerSample / 8;
            int dataSize = samples.Length * bitsPerSample / 8;

            using var stream = new FileStream(path, FileMode.Create);
            using var writer = new BinaryWriter(stream);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((short)(channels * bitsPerSample / 8)); // block align
            writer.Write((short)bitsPerSample);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);

            foreach (float s in samples)
            {
                short v = (short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), short.MinValue, short.MaxValue);
                writer.Write(v);
            }
        }

        private static string ResolveApiKey()
        {
            string fromEnv = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
            if (!string.IsNullOrEmpty(fromEnv)) return fromEnv.Trim();

            if (!File.Exists(KeyFilePath))
            {
                throw new InvalidOperationException($"no ELEVENLABS_API_KEY env var and key file not found at {KeyFilePath}");
            }
            return File.ReadAllText(KeyFilePath).Trim();
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
