using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;

namespace MaxWorlds.Tests
{
    /// <summary>
    /// MV-1134: covers the generated-audio contract end to end — every file under the generated SFX
    /// folder is named after a real cue, the imported clip (not the file on disk) resolves to a mono
    /// buffer with sane length/level, and <see cref="SfxCueLibrary.ResolveClip"/> prefers a generated
    /// clip when one exists and still falls back to the synthesised one for every other cue.
    /// </summary>
    public class GeneratedAudioTests
    {
        private const string SfxFolder = "Assets/_Project/Resources/Audio/Sfx";

        [Test]
        public void GeneratedClipsAndResolveClipMatchTheGeneratedAudioContract()
        {
            string[] files = Directory.Exists(SfxFolder) ? Directory.GetFiles(SfxFolder, "*.wav") : Array.Empty<string>();
            Assert.Greater(files.Length, 0, "expected at least one generated .wav file (UiClick)");

            foreach (string path in files)
            {
                string id = Path.GetFileNameWithoutExtension(path);
                Assert.IsTrue(Enum.TryParse(typeof(SfxCueLibrary.Cue), id, out _),
                    $"{id}.wav is not named after a SfxCueLibrary.Cue member");

                var clip = Resources.Load<AudioClip>("Audio/Sfx/" + id);
                Assert.IsNotNull(clip, $"Resources.Load found no imported clip for {id}");
                Assert.AreEqual(1, clip.channels, $"{id} is not mono");
                Assert.GreaterOrEqual(clip.length, 0.05f, $"{id} is shorter than 0.05s");
                Assert.LessOrEqual(clip.length, 31f, $"{id} is longer than 31s");

                var samples = new float[clip.samples * clip.channels];
                clip.GetData(samples, 0);
                float peak = 0f, sumSq = 0f;
                foreach (float s in samples)
                {
                    peak = Mathf.Max(peak, Mathf.Abs(s));
                    sumSq += s * s;
                }
                float rms = Mathf.Sqrt(sumSq / samples.Length);
                Assert.LessOrEqual(peak, 1.0f, $"{id} peak exceeds 1.0");
                Assert.Greater(rms, 0.01f, $"{id} RMS is at or below 0.01");
            }

            var uiClick = SfxCueLibrary.ResolveClip(SfxCueLibrary.Cue.UiClick);
            Assert.IsNotNull(uiClick, "ResolveClip(UiClick) returned null");
            Assert.AreEqual("UiClick", uiClick.name, "ResolveClip(UiClick) did not return the clip named UiClick");

            foreach (var cue in SfxCueLibrary.AllCues)
            {
                Assert.IsNotNull(SfxCueLibrary.ResolveClip(cue), $"ResolveClip({cue}) returned null");
            }
        }
    }
}
