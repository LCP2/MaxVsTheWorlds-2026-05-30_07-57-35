using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1073: Lee, on his phone — shed corner turrets are "extremely weak" and their "fire rate is
    /// too slow". <see cref="ShedFitting.StatsFor"/>'s cadence is halved (and HP tripled, not tested
    /// here — Tier 1 authored constants are asserted by <c>MV547ShedFittingTests</c> instead) per kind.
    /// This is the Rule-1 EditMode proof for the cadence half: it asserts RESOLVED firing behaviour
    /// over a fixed 6-simulated-second window — how many bolts a Spiker fitting actually fires — rather
    /// than reading the authored <c>Cadence</c> constant, which would pass even if
    /// <see cref="ShedFitting.Tick"/> never actually fired on that schedule. On base (cc6ecb2, 2.5s
    /// cadence) a Spiker held in range and sight for 6.0s fires 3 times; after the fix (1.2s cadence)
    /// it fires 5.
    /// </summary>
    public sealed class MV1073ShedFittingCadenceTests
    {
        [Test]
        public void Spiker_TargetHeldInRangeAndSight_TickedForSixSeconds_FiresAtLeastFiveShots()
        {
            // Delta against whatever BolterBolt count already exists, rather than an absolute count —
            // robust against stray bolts left by other tests sharing the same batch-mode domain.
            int strayBoltsBefore = Object.FindObjectsByType<BolterBolt>(FindObjectsSortMode.None).Length;

            var fittingGo = new GameObject("ShedFitting Probe");
            var targetGo = new GameObject("Target Probe");
            try
            {
                targetGo.transform.position = new Vector3(5f, 0f, 0f); // inside the Spiker's 8m range, nothing on the Cover layer between

                var fitting = fittingGo.AddComponent<ShedFitting>();
                fitting.Bind(null, ShedFittingKind.Spiker);
                fitting.SetTarget(targetGo.transform);

                const float dt = 0.1f;
                const int steps = 60; // 6.0 simulated seconds
                for (int i = 0; i < steps; i++) fitting.Tick(dt);

                int shotsFired = Object.FindObjectsByType<BolterBolt>(FindObjectsSortMode.None).Length - strayBoltsBefore;

                Assert.That(shotsFired, Is.GreaterThanOrEqualTo(5),
                    "a Spiker fitting held in range and sight for 6.0s must fire at least 5 shots at the " +
                    $"halved cadence (1.2s) — fired {shotsFired}");
            }
            finally
            {
                foreach (var bolt in Object.FindObjectsByType<BolterBolt>(FindObjectsSortMode.None))
                    Object.DestroyImmediate(bolt.gameObject);
                Object.DestroyImmediate(fittingGo);
                Object.DestroyImmediate(targetGo);
            }
        }
    }
}
