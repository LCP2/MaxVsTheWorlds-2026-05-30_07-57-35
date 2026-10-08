using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1135 — proves every cue resolves to its named clip (the generated ElevenLabs file, not the
    /// synthesised fallback) and that HoseLoop no longer dips at the wrap point. The synthesised
    /// HoseLoop preset is one 0.32s noise burst with an attack and a decay, so its level near each edge
    /// sits far below its middle — that's the audible dip every time the loop wraps. The fix's
    /// generated clip is untrimmed and unfaded (loop:true skips both in the generator) and the prompt
    /// asked for "even level throughout", so its edges should sit much closer to its middle.
    ///
    /// Fail-first: on the commit before this ticket's 21 generated files land, ResolveClip(HoseLoop)
    /// returns that synthesised burst, whose edge-to-middle RMS ratio is far below the 0.5 floor this
    /// test asserts — see the fix comment for the quoted failure output. (A plain seam-sample jump,
    /// the AC's literal wording, was tried first and found to pass on that same base commit — both the
    /// synthesised clip's first and last samples sit at its envelope's zero-crossing, so the raw jump
    /// is tiny either way; the real defect is the level dip either side of the seam, not a
    /// discontinuity at it, so this test measures that instead.)
    /// </summary>
    public sealed class GeneratedAudioBatch2Tests
    {
        [Test]
        public void EveryCueResolvesToItsNamedClip_AndHoseLoopNoLongerDipsAtTheWrap()
        {
            foreach (var cue in SfxCueLibrary.AllCues)
            {
                var clip = SfxCueLibrary.ResolveClip(cue);
                Assert.AreEqual(cue.ToString(), clip.name,
                    $"ResolveClip({cue}) did not return a clip named '{cue}'");
            }

            var loop = SfxCueLibrary.ResolveClip(SfxCueLibrary.Cue.HoseLoop);
            var samples = new float[loop.samples * loop.channels];
            loop.GetData(samples, 0);

            int window = Mathf.Clamp(Mathf.RoundToInt(0.005f * loop.frequency), 1, samples.Length / 4);
            float edgeRms = (Rms(samples, 0, window) + Rms(samples, samples.Length - window, window)) / 2f;
            float midRms = Rms(samples, samples.Length / 2 - window / 2, window);

            Assert.GreaterOrEqual(edgeRms, midRms * 0.5f,
                $"HoseLoop's edge level ({edgeRms}) dips below half its middle level ({midRms}) - audible as a dip every time the loop wraps");
        }

        private static float Rms(float[] samples, int start, int length)
        {
            float sumSq = 0f;
            for (int i = start; i < start + length; i++) sumSq += samples[i] * samples[i];
            return Mathf.Sqrt(sumSq / length);
        }
    }
}
