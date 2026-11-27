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
        // MV-1438: takes per cue (<id>_1..n); 0/absent = 1. Loops are always 1.
        public int variants;
        // MV-1438: optional sub-folder under Resources/Audio (e.g. "Ambience"); empty = Sfx, id must be a Cue.
        public string dir;
        // MV-1438: level trim in dB relative to the -1 dBFS combat peak (UI cues -6).
        public float gainDb;
        // MV-1474: "file" = hand-supplied WAV already in the repo. The generator never touches it, even
        // with -regen (id or "all").
        public string source;
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
        private const string SoundModelId = "eleven_text_to_sound_v3"; // MV-1438 (v2 is deprecated)
        private const string MusicModelId = "music_v2_5";                // MV-1439: Eleven Music v2.5 (logged per track)
        private const int CreditBudget = 20000;                         // MV-1438: hard stop per run
        private const float CombatPeak = 0.891f;                        // -1 dBFS
        private const float AmbienceRmsTarget = 0.1259f;                // -18 dBFS RMS
        private const float LoopCrossfadeSeconds = 1.5f;
        private static int _creditsUsed;
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

        /// <summary>MV-1474: single entry-selection rule for sfx. A hand-supplied (<c>"source": "file"</c>)
        /// entry is never generated, forced or not; otherwise generate when the file is missing or forced.</summary>
        public static bool ShouldGenerateSfx(string id, bool outputExists, bool forced)
        {
            var manifest = JsonUtility.FromJson<AudioManifest>(File.ReadAllText(ManifestPath));
            var entry = Array.Find(manifest.sfx, e => e.id == id);
            return ShouldGenerate(entry, outputExists, forced);
        }

        internal static bool ShouldGenerate(SfxManifestEntry entry, bool outputExists, bool forced)
        {
            if (entry != null && entry.source == "file") return false;
            return !outputExists || forced;
        }

        private static void Generate(string regenId)
        {
            string manifestJson = File.ReadAllText(ManifestPath);
            var manifest = JsonUtility.FromJson<AudioManifest>(manifestJson);
            Directory.CreateDirectory(OutputDir);

            string apiKey = ResolveApiKey();

            _creditsUsed = 0;
            bool regenAll = regenId == "all";
            var regenSet = new System.Collections.Generic.HashSet<string>(
                (regenId ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));

            foreach (var entry in manifest.sfx)
            {
                bool forced = regenAll || regenSet.Contains(entry.id);
                bool isSfx = string.IsNullOrEmpty(entry.dir);
                SfxCueLibrary.Cue cue = default;
                if (isSfx)
                {
                    if (!Enum.TryParse(typeof(SfxCueLibrary.Cue), entry.id, out var cueObj))
                        throw new InvalidOperationException($"manifest id '{entry.id}' is not a SfxCueLibrary.Cue member");
                    cue = (SfxCueLibrary.Cue)cueObj;
                }
                string dir = isSfx ? OutputDir : $"Assets/_Project/Resources/Audio/{entry.dir}";
                Directory.CreateDirectory(dir);
                int takes = entry.loop ? 1 : Mathf.Max(1, entry.variants);
                for (int v = 1; v <= takes; v++)
                {
                    string outputPath = isSfx ? $"{dir}/{entry.id}_{v}.wav" : $"{dir}/{entry.id}.wav";
                    if (!ShouldGenerate(entry, File.Exists(outputPath), forced)) continue;
                    GenerateOne(entry, outputPath, apiKey, v);
                }
                // The old single-take file is replaced by the variants.
                if (isSfx && entry.source != "file")
                {
                    string legacy = $"{dir}/{entry.id}.wav";
                    if (File.Exists(legacy)) { File.Delete(legacy); if (File.Exists(legacy + ".meta")) File.Delete(legacy + ".meta"); }
                }
            }

            if (manifest.music != null)
            {
                Directory.CreateDirectory(MusicOutputDir);
                foreach (var entry in manifest.music)
                {
                    string outputPath = $"{MusicOutputDir}/{entry.id}.mp3";
                    bool forced = regenAll || regenSet.Contains(entry.id);
                    if (File.Exists(outputPath) && !forced) continue;

                    GenerateMusicOne(entry, outputPath, apiKey);
                }
            }

            AssetDatabase.Refresh();
        }

        private static void GenerateOne(SfxManifestEntry entry, string outputPath, string apiKey, int take)
        {
            if (_creditsUsed >= CreditBudget)
                throw new InvalidOperationException($"credit budget {CreditBudget} reached ({_creditsUsed}); stopping before '{entry.id}' take {take}");

            int sampleRate = SampleRate44100;
            string format = "pcm_44100";
            var (ok, audioBytes, credits) = RequestSound(entry, apiKey, format);
            if (!ok)
            {
                format = "pcm_24000"; sampleRate = SampleRate24000;
                (ok, audioBytes, credits) = RequestSound(entry, apiKey, format);
                if (!ok) throw new InvalidOperationException($"ElevenLabs sound-generation failed for '{entry.id}' on both pcm_44100 and pcm_24000");
            }
            int cost = int.TryParse(credits, out var c) ? c : 0;
            _creditsUsed += cost;

            // ElevenLabs PCM is mono (the old duration-ratio channel guess broke when a model returns
            // a different length than asked for).
            // v3 returns interleaved stereo PCM (measured: 30 s request -> 58.5 s read as mono), so infer
            // the channel count from byte length vs the requested duration and downmix.
            int channelCount = Mathf.Clamp(Mathf.RoundToInt(audioBytes.Length / (sampleRate * 2f * entry.durationSeconds)), 1, 2);
            float[] samples = PcmBytesToFloat(audioBytes, channelCount);
            if (channelCount > 1) samples = GeneratedAudioDsp.ToMono(samples, channelCount);

            if (entry.loop)
            {
                samples = MakeSeamless(samples, sampleRate);
                if (!string.IsNullOrEmpty(entry.dir))
                {
                    // Beds are written at 22.05 kHz (average-pair decimation) to keep Resources/Audio <= 25 MB.
                    var half = new float[samples.Length / 2];
                    for (int i = 0; i < half.Length; i++) half[i] = 0.5f * (samples[2 * i] + samples[2 * i + 1]);
                    samples = half; sampleRate /= 2;
                    ScaleToRms(samples, AmbienceRmsTarget);
                }
                else GeneratedAudioDsp.ScaleToPeak(samples, CombatPeak * Mathf.Pow(10f, entry.gainDb / 20f));
            }
            else
            {
                samples = GeneratedAudioDsp.TrimLeadingSilence(samples, sampleRate);
                samples = GeneratedAudioDsp.TrimTrailingSilence(samples);
                GeneratedAudioDsp.FadeInOut(samples, sampleRate);
                GeneratedAudioDsp.ScaleToPeak(samples, CombatPeak * Mathf.Pow(10f, entry.gainDb / 20f));
            }

            WriteWav(outputPath, samples, sampleRate);

            float seconds = samples.Length / (float)sampleRate;
            File.AppendAllText(LogPath,
                $"ELEVENLABS {entry.id} take={take} OK model={SoundModelId} format={format} seconds={seconds.ToString("0.###", CultureInfo.InvariantCulture)} credits={credits} total={_creditsUsed}/{CreditBudget}\n");
        }

        // Equal-power crossfade of the tail into the head so the loop point is click-free.
        private static float[] MakeSeamless(float[] x, int sampleRate)
        {
            int n = Mathf.Min((int)(LoopCrossfadeSeconds * sampleRate), x.Length / 3);
            var y = new float[x.Length - n];
            Array.Copy(x, y, y.Length);
            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) / n;
                float head = Mathf.Sin(t * Mathf.PI * 0.5f), tail = Mathf.Cos(t * Mathf.PI * 0.5f);
                y[i] = x[i] * head + x[y.Length + i] * tail;
            }
            return y;
        }

        private static void ScaleToRms(float[] x, float targetRms)
        {
            double sum = 0; foreach (var v in x) sum += v * v;
            float rms = (float)Math.Sqrt(sum / Math.Max(1, x.Length));
            if (rms < 1e-6f) return;
            float g = targetRms / rms;
            float peak = 0f; foreach (var v in x) peak = Mathf.Max(peak, Mathf.Abs(v));
            g = Mathf.Min(g, 0.98f / Mathf.Max(peak, 1e-6f));
            for (int i = 0; i < x.Length; i++) x[i] *= g;
        }

        private static void GenerateMusicOne(MusicManifestEntry entry, string outputPath, string apiKey)
        {
            using var client = new HttpClient { Timeout = MusicRequestTimeout };
            client.DefaultRequestHeaders.Add("xi-api-key", apiKey);

            string json = "{\"prompt\":" + JsonStringLiteral(entry.prompt) +
                ",\"music_length_ms\":" + entry.lengthMs.ToString(CultureInfo.InvariantCulture) +
                ",\"model_id\":\"" + MusicModelId + "\",\"force_instrumental\":true}";
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

            // MV-1439: /v1/music returns no cost header (key lacks user_read too): log model + seconds, credits n/a.
            File.AppendAllText(LogPath, $"ELEVENLABS {entry.id} OK model={MusicModelId} format=mp3_44100_128 bytes={bytes.Length} seconds={entry.lengthMs / 1000f} credits=n/a\n");
        }

        private static (bool ok, byte[] bytes, string credits) RequestSound(SfxManifestEntry entry, string apiKey, string outputFormat)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("xi-api-key", apiKey);

            string json = "{\"text\":" + JsonStringLiteral(entry.prompt) +
                ",\"duration_seconds\":" + entry.durationSeconds.ToString(CultureInfo.InvariantCulture) +
                ",\"prompt_influence\":" + entry.promptInfluence.ToString(CultureInfo.InvariantCulture) +
                ",\"loop\":" + (entry.loop ? "true" : "false") +
                ",\"model_id\":\"" + SoundModelId + "\"}";
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
