using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Audio;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1160: the end of a generated track was overlapping the start of its next pass for the full
    /// 3s <c>WrapLeadSeconds</c> crossfade window, because the hand-off point was the clip's raw end
    /// rather than a beat-aligned loop point. This drives <see cref="MusicDirector.AdvanceSeam"/> —
    /// the pure state machine <see cref="MusicDirector"/>'s private <c>SeamLoopPlayer</c> ticks every
    /// frame — at 60fps with a known loop end, and asserts both halves of the fix: the crossfade
    /// window is <= 0.15s (not the old 3s), and the outgoing pass's reconstructed play position never
    /// exceeds <c>loopEndSeconds + 0.075</c>.
    ///
    /// The crossfade window handed to <see cref="MusicDirector.AdvanceSeam"/> is derived from
    /// <see cref="MusicDirector.SeamCrossfadeHalfSeconds"/> — the same constant
    /// <c>SeamLoopPlayer.Tick</c> itself reads — rather than a test-local literal, while the pass/fail
    /// thresholds below stay the ticket's fixed 0.15s/0.075s numbers. So if that constant ever
    /// regresses back toward the old 3s overlap (e.g. someone restores the pre-fix 1.5s half-window),
    /// this test drives <c>AdvanceSeam</c> with the regressed window and the fixed thresholds catch it
    /// — unlike a version of this test that invents its own 0.075s and never reads production's.
    ///
    /// Fail-first: on the base commit <see cref="MusicDirector.AdvanceSeam"/> doesn't exist, so this
    /// file fails to compile and every test in the assembly reports as an error, e.g.:
    /// "error CS0117: 'MusicDirector' does not contain a definition for 'AdvanceSeam'".
    /// </summary>
    public sealed class MV1160SeamLoopTests
    {
        private const float Dt = 1f / 60f;

        // The ticket's own AC numbers (MV-1160 Acceptance #1) — fixed, independent of whatever
        // MusicDirector.SeamCrossfadeHalfSeconds currently resolves to, so a regression in that
        // constant is caught here rather than silently shifting the pass bound with it.
        private const float AcOverlapCeilingSeconds = 0.15f;
        private const float AcOvershootCeilingSeconds = 0.075f;

        [Test]
        public void AdvanceSeam_CrossfadeWindowIsShort_AndOutgoingPositionNeverExceedsLoopEndPlusHalfWindow()
        {
            const float loopEndSeconds = 8f;
            float crossfadeHalfSeconds = MusicDirector.SeamCrossfadeHalfSeconds;
            float wrapAt = loopEndSeconds - crossfadeHalfSeconds;
            float window = crossfadeHalfSeconds * 2f;

            bool crossfading = false;
            float crossfadeT = 0f;
            float activeTime = 0f;

            float crossfadeStartedAt = -1f;
            float crossfadeEndedAt = -1f;
            float maxActiveTime = 0f;
            bool swapped = false;

            for (int frame = 0; frame < 1200 && !swapped; frame++)
            {
                bool wasCrossfading = crossfading;
                var step = MusicDirector.AdvanceSeam(crossfading, crossfadeT, activeTime, Dt, wrapAt, window);

                if (!wasCrossfading && step.crossfading)
                {
                    crossfadeStartedAt = activeTime + Dt;
                }

                crossfading = step.crossfading;
                crossfadeT = step.crossfadeT;
                activeTime = step.activeTime;
                maxActiveTime = Mathf.Max(maxActiveTime, activeTime);

                if (step.swapped)
                {
                    crossfadeEndedAt = activeTime;
                    swapped = true;
                }
            }

            Assert.IsTrue(swapped, "the seam never completed its crossfade within 20 simulated seconds");
            Assert.LessOrEqual(crossfadeEndedAt - crossfadeStartedAt, AcOverlapCeilingSeconds + Dt + 1e-4f,
                $"crossfade ran {crossfadeEndedAt - crossfadeStartedAt}s, longer than AC1's {AcOverlapCeilingSeconds}s ceiling " +
                $"(fed window was {window}s, derived from MusicDirector.SeamCrossfadeHalfSeconds={crossfadeHalfSeconds})");
            Assert.LessOrEqual(maxActiveTime, loopEndSeconds + AcOvershootCeilingSeconds + 1e-4f,
                $"outgoing pass reached {maxActiveTime}s, past loopEndSeconds ({loopEndSeconds}) + AC1's {AcOvershootCeilingSeconds}s ceiling " +
                $"(derived crossfadeHalfSeconds was {crossfadeHalfSeconds})");
        }
    }
}
