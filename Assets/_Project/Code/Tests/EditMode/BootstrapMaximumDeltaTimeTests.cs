using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-883 — a slow rendered frame used to let Unity's fixed-timestep catch-up run unbounded
    /// (measured at 8.5 physics steps for a single 189 ms frame), which is a positive feedback loop:
    /// more physics work makes the next frame slower still. <see cref="Bootstrap"/> now clamps the
    /// catch-up window on boot. This asserts the RESOLVED value read off <see cref="Time"/> after
    /// <c>Awake</c> runs, not an authored constant in the test (Rule 2 / MV-465).
    /// </summary>
    public sealed class BootstrapMaximumDeltaTimeTests
    {
        [Test]
        public void Awake_ClampsMaximumDeltaTimeToOneTenthSecond()
        {
            float originalMaxDeltaTime = Time.maximumDeltaTime;
            float originalFixedDeltaTime = Time.fixedDeltaTime;
            var go = new GameObject("MV883 Bootstrap Probe");
            try
            {
                var bootstrap = go.AddComponent<Bootstrap>();
                // The EditMode test runner never dispatches Awake on its own — invoke it directly,
                // exactly as Unity would, matching the idiom other Bootstrap-adjacent tests use
                // (e.g. AreaGateTests.InvokeAwake, BackyardEntryDoorTests).
                typeof(Bootstrap).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(bootstrap, null);

                Assert.AreEqual(0.1f, Time.maximumDeltaTime,
                    "Bootstrap must clamp Time.maximumDeltaTime to 0.1s to cap physics catch-up");

                // MV-883 AC3: the fixed tick rate (50 Hz) must be untouched by this change.
                Assert.AreEqual(originalFixedDeltaTime, Time.fixedDeltaTime,
                    "Bootstrap must not change Time.fixedDeltaTime");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Time.maximumDeltaTime = originalMaxDeltaTime;
            }
        }
    }
}
