using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1459: a current lane's chevrons point downstream but scrolled UPSTREAM, because
    /// CurrentLaneBand.shader sampled <c>along = uv.x + _ScrollOffset</c> (a growing offset moves the
    /// painted pattern toward -uv). One test, both reversal phases. A CPU mirror of the shader's own
    /// <c>along</c> mapping (sign read from the shipped shader source, so the mirror cannot drift from the
    /// shader) locates the chevron apex along the flow axis before and after 0.5 s of
    /// <see cref="MapCurrentLaneVisual.Tick"/>; the apex must move with the lane's LIVE flow direction by
    /// SpeedMps x 0.5 (+-10%).
    /// </summary>
    public sealed class MV1459CurrentChevronScrollDirectionTests
    {
        private const string ShaderPath = "Assets/_Project/Art/Shaders/CurrentLaneBand.shader";

        // Apex position (metres, authored-axis uv.x) of one chevron for a given _ScrollOffset, using the
        // shader's mapping along = uv.x + k * offset  ->  apex where along = apexNorm * period.
        private static float ApexU(float k, float offset, float apexNorm, float period)
            => apexNorm * period - k * offset;

        [Test]
        public void ChevronApexScrollsWithLiveFlowDirection_InBothReversalPhases()
        {
            string src = File.ReadAllText(Path.Combine(Application.dataPath, "..", ShaderPath));
            Match m = Regex.Match(src, @"float\s+along\s*=\s*IN\.uv\.x\s*([+-])\s*_ScrollOffset");
            Assert.IsTrue(m.Success, "could not find the shader's 'along' mapping line");
            float k = m.Groups[1].Value == "+" ? 1f : -1f;

            Shader shader = Shader.Find("MaxWorlds/CurrentLaneBand");
            Assert.IsNotNull(shader, "MaxWorlds/CurrentLaneBand must resolve");

            var runnerGo = new GameObject("MV1459-runner");
            var host = new GameObject("MV1459-lane");
            var mat = new Material(shader);
            try
            {
                var runner = runnerGo.AddComponent<MapCurrentReversalRunner>();
                runner.TickCurrentReversal(0f);

                var lane = host.AddComponent<MapCurrentLaneVisual>();
                const float speed = 2f;
                lane.Configure("mv1459", new Rect(0, 0, 4, 20), Vector3.back, speed, 10f, mat);

                const float period = 1f, apexNorm = 0.75f;
                for (int phase = 0; phase < 2; phase++)
                {
                    if (phase == 1) runner.TickCurrentReversal(12f); // past the first flip at t=10

                    float before = mat.GetFloat("_ScrollOffset");
                    lane.Tick(0.5f);
                    float after = mat.GetFloat("_ScrollOffset");

                    float apexMove = ApexU(k, after, apexNorm, period) - ApexU(k, before, apexNorm, period);
                    // uv.x runs along AuthoredDir; project onto the live flow direction.
                    float liveSign = Vector3.Dot(lane.ResolvedFlowDirection, lane.AuthoredDir) >= 0f ? 1f : -1f;
                    float downstream = apexMove * liveSign;

                    Assert.AreEqual(speed * 0.5f, downstream, speed * 0.5f * 0.1f,
                        $"phase {phase}: chevron apex must move DOWNSTREAM (with the live flow) by SpeedMps*0.5");
                }
            }
            finally
            {
                Object.DestroyImmediate(mat);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(runnerGo);
            }
        }
    }
}
