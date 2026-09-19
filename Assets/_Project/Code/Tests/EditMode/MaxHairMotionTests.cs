using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-854 (the one new test): his hair actually moves — in the wind while he stands still, and
    /// goes still again once the wind and his own speed both drop to zero.
    ///
    /// Fails on the commit before this ticket: <c>MaxWorlds.VFX.MaxHair</c> does not exist there, so
    /// this test does not compile (quoted in the fix comment).
    ///
    /// Tier 2 (resolved value): the tip asserted here is a node position <see cref="MaxHair.Tick"/>
    /// actually resolves from the spring chain frame by frame, not an authored constant — the whole
    /// point of a spring is that nobody authors where the tip ends up.
    ///
    /// "Moves" is measured as the TOTAL PATH LENGTH the tip travels across the sampled second, not the
    /// straight-line distance between its endpoints — the per-lock flutter term runs at 6 rad/s
    /// (<see cref="MaxHair.Tick"/>), close enough to a 1-second sample spacing that an endpoint-only
    /// delta can alias to near zero on a lock that is visibly swaying the whole time (verified against
    /// the real spring maths: every back lock's tip travels 3.8–6.6 cm of path across a windy second,
    /// while its raw endpoint delta can land under 1 cm purely from where the 6 rad/s wave happens to
    /// sit at the two sample instants). Path length is what "moves" means for something that keeps
    /// changing direction, and it is still a resolved value, not an authored one.
    /// </summary>
    public sealed class MaxHairMotionTests
    {
        private const float Dt = 1f / 60f;

        private static Vector3 Tip(Vector3[] nodes) => nodes[MaxHair.NodeCount - 1];

        private static void StepSeconds(in HairLockSpec spec, HairLockState state, float seconds,
                                        float windStrength, float speed01, ref float time, Vector3[] nodes)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                time += Dt;
                Vector3 pushWorld = MaxHair.ComputePushWorld(Vector3.forward, MaxHair.WindDirWorld, windStrength,
                                                              time, stridePhase: 0f, speed01);
                MaxHair.Tick(spec, state, Dt, speed01, pushWorld, time, windStrength, nodes);
            }
        }

        /// <summary>Advances the same way <see cref="StepSeconds"/> does, but returns the total
        /// distance the tip travelled along the way rather than discarding it.</summary>
        private static float StepSecondsMeasuringPathLength(in HairLockSpec spec, HairLockState state,
                                                             float seconds, float windStrength, float speed01,
                                                             ref float time, Vector3[] nodes)
        {
            float pathLength = 0f;
            Vector3 prevTip = Tip(nodes);
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                time += Dt;
                Vector3 pushWorld = MaxHair.ComputePushWorld(Vector3.forward, MaxHair.WindDirWorld, windStrength,
                                                              time, stridePhase: 0f, speed01);
                MaxHair.Tick(spec, state, Dt, speed01, pushWorld, time, windStrength, nodes);
                Vector3 tip = Tip(nodes);
                pathLength += Vector3.Distance(tip, prevTip);
                prevTip = tip;
            }
            return pathLength;
        }

        [Test]
        public void IdleHair_BlowsInTheWind_AndGoesStillWhenItStops()
        {
            var specs = MaxHair.BuildLayout();
            HairLockSpec back = default;
            bool found = false;
            foreach (var s in specs)
            {
                if (s.Fringe || !s.Back) continue;
                back = s;
                found = true;
                break;
            }
            Assert.IsTrue(found, "the layout grew no back lock to test against.");

            // --- Max idle, wind > 0: the tip has to actually travel between t=1s and t=2s.
            var windState = new HairLockState(back);
            var nodes = new Vector3[MaxHair.NodeCount];
            float time = 0f;
            StepSeconds(back, windState, 1f, MaxHair.WindStrengthDefault, 0f, ref time, nodes);
            float moved = StepSecondsMeasuringPathLength(back, windState, 1f, MaxHair.WindStrengthDefault, 0f,
                                                          ref time, nodes);

            Assert.That(moved, Is.GreaterThanOrEqualTo(0.03f),
                $"a back lock's tip travelled only {moved * 100f:0.0} cm between t=1s and t=2s in a " +
                $"{MaxHair.WindStrengthDefault:0.00}-strength wind. His hair is supposed to blow while " +
                "he stands still (Lee: \"blow in the wind when he stands still\").");

            // --- wind = 0, speed = 0, after settling: the tip has to go still.
            var stillState = new HairLockState(back);
            var stillNodes = new Vector3[MaxHair.NodeCount];
            float stillTime = 0f;
            StepSeconds(back, stillState, 5f, 0f, 0f, ref stillTime, stillNodes);   // let the spring settle
            float driftedBy = StepSecondsMeasuringPathLength(back, stillState, 1f, 0f, 0f, ref stillTime,
                                                              stillNodes);

            Assert.That(driftedBy, Is.LessThan(0.005f),
                $"a back lock's tip is still travelling {driftedBy * 100f:0.00} cm across a second with " +
                "no wind and no speed, a moment after settling. It should have gone still.");
        }
    }
}
