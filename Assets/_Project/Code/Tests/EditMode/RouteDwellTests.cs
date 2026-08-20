using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-477: a hedge has a collider (drives WallLatch) but sits deliberately off the Cover layer
    /// (MV-400), so the sight ray passes straight through it and Perception.HasSight flips every frame
    /// the ray happens to clear a gap — flipping TickChase's goal, and the steering direction with it,
    /// between two near-opposite headings several times a second, with no net displacement either way.
    /// <see cref="RouteDwell"/> is the pulled-out, pure decision this regresses against directly — same
    /// idiom as <see cref="WallLatchTests"/>/<see cref="ZoneHysteresisTests"/>.
    /// </summary>
    public sealed class RouteDwellTests
    {
        [Test]
        public void OscillatingCandidate_HeadingReversalsNeverLandCloserTogetherThanMinDwell()
        {
            // Worst-case repro of the hedge bug: the candidate direction flips between two headings
            // more than 90 degrees apart on EVERY tick, for a full simulated 10 seconds at 60fps —
            // exactly what a flickering sight-line through a hedge gap hands TickChase. AC1 requires
            // the OUTPUT to change by more than 90 degrees at most once per 0.75s regardless of how
            // often the upstream input itself flips.
            var dwell = new RouteDwell();
            Vector3 sideA = new Vector3(1f, 0f, 0f);
            Vector3 sideB = new Vector3(-1f, 0f, 0.05f).normalized; // ~178 degrees from sideA

            const float dt = 1f / 60f;
            const float durationSeconds = 10f;
            int ticks = Mathf.RoundToInt(durationSeconds / dt);

            Vector3? previousOutput = null;
            float timeSinceLastReversal = float.MaxValue;
            int reversalCount = 0;

            for (int i = 0; i < ticks; i++)
            {
                Vector3 candidate = (i % 2 == 0) ? sideA : sideB;
                Vector3 output = dwell.Resolve(candidate, dt);

                if (previousOutput.HasValue)
                {
                    float angle = Vector3.Angle(previousOutput.Value, output);
                    if (angle > RouteDwell.ReversalThresholdDegrees)
                    {
                        Assert.GreaterOrEqual(timeSinceLastReversal, RouteDwell.MinDwell - 0.001f,
                            $"tick {i}: heading reversed by {angle:F1} degrees only " +
                            $"{timeSinceLastReversal:F3}s after the previous reversal — the hedge-flicker " +
                            "repro must not be able to flip the output faster than the dwell allows");
                        reversalCount++;
                        timeSinceLastReversal = 0f;
                    }
                }

                timeSinceLastReversal += dt;
                previousOutput = output;
            }

            Assert.Greater(reversalCount, 0,
                "sanity: the alternating input must still produce at least one accepted reversal once " +
                "the dwell has been served, or this isn't exercising the bound at all");
        }
    }
}
