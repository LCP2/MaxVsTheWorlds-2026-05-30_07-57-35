using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-957 AC1 — Lee's release-build TestFlight overlay could attribute well under 10ms/frame of the
    /// observed 58-90ms of main-thread time; the rest was invisible because ProfilerRecorder's physics/
    /// script/GC/batch markers only report in a Development Build. This proves the new release-safe
    /// census line (<see cref="FrameCost.FormatSystemCensusLine"/>, folded into
    /// <see cref="FrameCost.FormatLine"/>) reports RESOLVED counts read live from the scene — not authored
    /// constants — for three of the component types item 1 lists: <c>CharacterController</c>,
    /// <c>ParticleSystem</c> (playing only) and point <c>Light</c>. Fails on the pre-fix commit: that
    /// FormatLine() had no census line at all, so none of "cc 3"/"ps 2"/"light point 1" appear anywhere
    /// in its output.
    /// </summary>
    public sealed class Mv957SystemCensusLineTests
    {
        [TearDown]
        public void TearDown()
        {
            FrameCost.Reset();
            FrameCost.UseClockForTest(null);
        }

        [Test]
        public void FormatLine_CensusLine_ReportsResolvedCharacterControllerParticleSystemAndPointLightCounts()
        {
            // EditMode tests all run in one shared domain across the whole suite (~2000 tests, no scene
            // reload between them), so GameObjects other tests left behind are still alive here — the
            // census formatter walks the whole domain by design (production only ever has one game scene
            // loaded, so that's correct there). Purging pre-existing instances of exactly the three types
            // this test asserts on makes it deterministic regardless of suite order; anything else
            // (colliders, renderers, animators, ...) is left alone since nothing here asserts on it.
            PurgeExisting<CharacterController>();
            PurgeExisting<ParticleSystem>();
            PurgeExisting<Light>();

            var spawned = new List<GameObject>();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var go = new GameObject($"cc{i}");
                    go.AddComponent<CharacterController>();
                    spawned.Add(go);
                }

                for (int i = 0; i < 2; i++)
                {
                    var go = new GameObject($"ps{i}");
                    var ps = go.AddComponent<ParticleSystem>();
                    ps.Play();
                    spawned.Add(go);
                }

                var lightGo = new GameObject("light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                spawned.Add(lightGo);

                FrameCost.Reset();
                string line = FrameCost.FormatLine();

                Assert.That(line, Does.Contain("cc 3"), line);
                Assert.That(line, Does.Contain("ps 2"), line);
                Assert.That(line, Does.Contain("light point 1"), line);
            }
            finally
            {
                foreach (var go in spawned) Object.DestroyImmediate(go);
            }
        }

        private static void PurgeExisting<T>() where T : Component
        {
            foreach (var component in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
                Object.DestroyImmediate(component.gameObject);
        }
    }
}
