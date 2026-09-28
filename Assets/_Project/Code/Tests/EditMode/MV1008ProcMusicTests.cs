using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1008 — proves the procedural music synth actually renders a real, seamlessly-looping,
    /// per-world track, entirely on RESOLVED values read back from the rendered
    /// <see cref="AudioClip"/> (Testing policy v2 Tier 2), never an authored constant and never a
    /// rendered pixel:
    ///
    /// 1. each world's clip length is exactly 32 bars at that world's authored BPM, computed here
    ///    independently of <see cref="ProcMusic"/>'s own (private) bar-length math, not read off it;
    /// 2. the loop join — the sample the clip wraps from (its very last sample) to the sample it wraps
    ///    into (its very first) — has no discontinuity greater than 0.05 in amplitude, i.e. no audible
    ///    click at the loop point;
    /// 3. no two worlds render the same buffer.
    ///
    /// Fail-first: <see cref="ProcMusic"/> did not exist on <c>8c86feb</c> — the game had no music at
    /// all (no <c>AudioClip</c> anywhere under Code/Runtime/Audio besides <c>ProcSfx</c>'s one-shot
    /// cues, none of them looping).
    /// </summary>
    public sealed class MV1008ProcMusicTests
    {
        private const int SampleRate = ProcMusic.SampleRate;
        private const int BarsPerLoop = ProcMusic.BarsPerLoop;
        private const float MaxJoinDiscontinuity = 0.05f;

        private static readonly (MusicWorld world, float bpm)[] Worlds =
        {
            (MusicWorld.Backyard, 96f),     // World 1 — C major pentatonic, 96 BPM (ticket text)
            (MusicWorld.Stormdrain, 84f),   // World 2 — D minor, 84 BPM
            (MusicWorld.Reef, 70f),         // World 3 — A minor, 70 BPM
        };

        [Test]
        public void EveryWorldLoop_IsExactly32Bars_JoinsSeamlessly_AndDiffersFromTheOtherWorlds()
        {
            var renderedMono = new float[Worlds.Length][];

            for (int w = 0; w < Worlds.Length; w++)
            {
                var (world, bpm) = Worlds[w];
                AudioClip clip = ProcMusic.RenderLoopClip(world);

                // --- (1) exactly 32 bars at this world's authored BPM, ±1 sample for rounding ---
                float barSeconds = 4f * 60f / bpm;
                int expectedSamples = Mathf.RoundToInt(barSeconds * BarsPerLoop * SampleRate);
                Assert.AreEqual(expectedSamples, clip.samples, 1,
                    $"{world}: loop length {clip.samples} samples, expected {expectedSamples} (32 bars @ {bpm} BPM)");

                Assert.AreEqual(SampleRate, clip.frequency, $"{world}: not rendered at {SampleRate} Hz");
                Assert.AreEqual(2, clip.channels, $"{world}: not a stereo clip");

                var interleaved = new float[clip.samples * clip.channels];
                clip.GetData(interleaved, 0);

                var mono = new float[clip.samples];
                for (int i = 0; i < clip.samples; i++) mono[i] = interleaved[i * clip.channels];
                renderedMono[w] = mono;

                // --- (2) the loop join: last sample -> first sample, no audible click ---
                float discontinuity = Mathf.Abs(mono[mono.Length - 1] - mono[0]);
                Assert.LessOrEqual(discontinuity, MaxJoinDiscontinuity,
                    $"{world}: loop join discontinuity {discontinuity:F4} exceeds {MaxJoinDiscontinuity} " +
                    "(last sample vs first sample) — would click on every repeat");

                // Sanity: not a silent/near-silent render (a bug that would trivially "join seamlessly").
                float peak = 0f;
                foreach (float s in mono) peak = Mathf.Max(peak, Mathf.Abs(s));
                Assert.GreaterOrEqual(peak, 0.2f, $"{world}: rendered buffer peak {peak} below the audible floor");
            }

            // --- (3) every world differs from every other world ---
            for (int a = 0; a < renderedMono.Length; a++)
                for (int b = a + 1; b < renderedMono.Length; b++)
                    CollectionAssert.AreNotEqual(renderedMono[a], renderedMono[b],
                        $"{Worlds[a].world} and {Worlds[b].world} rendered the exact same buffer");
        }
    }
}
