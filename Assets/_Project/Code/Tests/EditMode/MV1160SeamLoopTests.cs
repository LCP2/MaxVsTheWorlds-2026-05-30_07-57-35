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
    /// Fail-first: on the base commit <see cref="MusicDirector.AdvanceSeam"/> doesn't exist, so this
    /// file fails to compile and every test in the assembly reports as an error, e.g.:
    /// "error CS0117: 'MusicDirector' does not contain a definition for 'AdvanceSeam'".
    /// </summary>
    public sealed class MV1160SeamLoopTests
    {
        private const float Dt = 1f / 60f;

        [Test]
        public void AdvanceSeam_CrossfadeWindowIsShort_AndOutgoingPositionNeverExceedsLoopEndPlusHalfWindow()
        {
            const float loopEndSeconds = 8f;
            const float crossfadeHalfSeconds = 0.075f;
            float wrapAt = loopEndSeconds - crossfadeHalfSeconds;
            const float window = crossfadeHalfSeconds * 2f;

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
            Assert.LessOrEqual(crossfadeEndedAt - crossfadeStartedAt, window + Dt + 1e-4f,
                $"crossfade ran {crossfadeEndedAt - crossfadeStartedAt}s, longer than the {window}s window");
            Assert.LessOrEqual(maxActiveTime, loopEndSeconds + crossfadeHalfSeconds + 1e-4f,
                $"outgoing pass reached {maxActiveTime}s, past loopEndSeconds ({loopEndSeconds}) + {crossfadeHalfSeconds}s");
        }
    }
}
