using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1007 — proves the procedural SFX synth actually produces real, distinct sound (not silence,
    /// not near-silence, not the same buffer reused under a different name) and that SfxDirector's
    /// per-cue rate limit holds under a burst, both WITHOUT any real audio hardware: EditMode reads a
    /// rendered sample buffer straight back, a resolved value (Testing policy v2 Tier 2) rather than a
    /// rendered pixel/played sound (Tier 3, the conformance harness's job).
    ///
    /// Fail-first: neither <see cref="ProcSfx"/> nor <see cref="SfxDirector"/> existed on 8c86feb —
    /// the game had no audio at all (no AudioSource/AudioClip anywhere under Code/Runtime).
    /// </summary>
    public sealed class ProcSfxTests
    {
        [TearDown]
        public void TearDown()
        {
            foreach (var d in Object.FindObjectsByType<SfxDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
        }

        [Test]
        public void EveryPreset_RendersDistinctAudibleSamples_AndEnemyKilledRateLimitHoldsUnderABurst()
        {
            var renderedBuffers = new List<float[]>();

            foreach (var cue in SfxCueLibrary.AllCues)
            {
                float[] samples = ProcSfx.Render(SfxCueLibrary.Presets[cue]);

                Assert.GreaterOrEqual(samples.Length, 2000,
                    $"{cue}: too short to read as a sound ({samples.Length} samples)");

                float peak = 0f;
                bool nonZero = false;
                foreach (float s in samples)
                {
                    peak = Mathf.Max(peak, Mathf.Abs(s));
                    if (s != 0f) nonZero = true;
                }

                Assert.IsTrue(nonZero, $"{cue}: rendered an all-zero buffer");
                Assert.GreaterOrEqual(peak, 0.2f, $"{cue}: peak {peak} below the audible floor");
                Assert.LessOrEqual(peak, 1.0f, $"{cue}: peak {peak} clips");

                foreach (var earlier in renderedBuffers)
                    CollectionAssert.AreNotEqual(earlier, samples,
                        $"{cue}: produced the exact same sample buffer as an earlier preset");
                renderedBuffers.Add(samples);
            }

            // --- SfxDirector's per-cue rate limit (the table's EnemyKilled row: <= 8/s) ---
            var director = new GameObject("SfxDirector(Test)").AddComponent<SfxDirector>();

            for (int i = 0; i < 20; i++) HudSignals.EmitEnemyKilled(Vector3.zero);

            Assert.LessOrEqual(director.VoiceStartsThisWindow(SfxCueLibrary.Cue.EnemyKilled), 8,
                "20 EnemyKilled events raised in one frame must not start more than the table's 8/s cap");
        }
    }
}
