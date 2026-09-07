using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using MaxWorlds.Feel;
using MaxWorlds.VFX;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-684 — AnimSequence is the shared time-evaluator substrate for scripted VFX; these tests pin
    /// its resolved-value contract (Tier 2 per MV-465) with no scene and no rendered pixel involved.
    /// </summary>
    public sealed class AnimSequenceTests
    {
        [Test]
        public void Progress_IsExactlyZeroBeforeDelay_AndExactlyOneAtAndAfterDelayPlusDuration()
        {
            // Multiples of 0.125 are exactly representable in float, so every Tick() sum below lands
            // on its target boundary bit-for-bit — no epsilon needed to prove "exactly 0" / "exactly 1".
            var steps = new[]
            {
                new AnimStep(0.125f, 0.25f, AnimEase.Linear),  // ends 0.375
                new AnimStep(0.5f, 0.25f, AnimEase.Linear),    // ends 0.75
                new AnimStep(0.875f, 0.125f, AnimEase.Linear), // ends 1.0
            };
            var sequence = new AnimSequence(steps);

            Assert.AreEqual(0f, sequence.Progress(0), "step 0 must read 0 before any time has passed");
            Assert.AreEqual(0f, sequence.Progress(1), "step 1 must read 0 before any time has passed");
            Assert.AreEqual(0f, sequence.Progress(2), "step 2 must read 0 before any time has passed");

            sequence.Tick(0.375f); // total 0.375 — step 0's Delay + Duration exactly
            Assert.AreEqual(1f, sequence.Progress(0), "step 0 must read exactly 1 at Delay + Duration");
            Assert.AreEqual(0f, sequence.Progress(1), "step 1's delay (0.5) has not elapsed yet");
            Assert.AreEqual(0f, sequence.Progress(2), "step 2's delay (0.875) has not elapsed yet");

            sequence.Tick(0.375f); // total 0.75 — step 1's Delay + Duration exactly
            Assert.AreEqual(1f, sequence.Progress(0), "step 0 must stay complete once past its own end");
            Assert.AreEqual(1f, sequence.Progress(1), "step 1 must read exactly 1 at Delay + Duration");
            Assert.AreEqual(0f, sequence.Progress(2), "step 2's delay (0.875) has still not elapsed");

            sequence.Tick(0.25f); // total 1.0 — step 2's Delay + Duration exactly
            Assert.AreEqual(1f, sequence.Progress(2), "step 2 must read exactly 1 at Delay + Duration");
            Assert.IsTrue(sequence.IsComplete,
                "IsComplete must be true once time reaches the max of Delay + Duration across all steps");
        }

        [Test]
        public void EveryEase_SatisfiesFZeroIsZero_AndFOneIsOne()
        {
            foreach (AnimEase ease in Enum.GetValues(typeof(AnimEase)))
            {
                var sequence = new AnimSequence(new[] { new AnimStep(0f, 1f, ease) });

                Assert.AreEqual(0f, sequence.Progress(0), 1e-5f, $"{ease}: f(0) must be 0");

                sequence.Tick(1f);
                Assert.AreEqual(1f, sequence.Progress(0), 1e-5f, $"{ease}: f(1) must be 1");
            }
        }

        [TestCase(AnimEase.Linear)]
        [TestCase(AnimEase.SmoothStep)]
        [TestCase(AnimEase.InQuad)]
        [TestCase(AnimEase.OutQuad)]
        [TestCase(AnimEase.InOutQuad)]
        public void MonotonicEases_NeverDecreaseAcross100Samples(AnimEase ease)
        {
            var sequence = new AnimSequence(new[] { new AnimStep(0f, 1f, ease) });

            float previous = sequence.Progress(0);
            for (int i = 1; i <= 100; i++)
            {
                sequence.Tick(0.01f);
                float current = sequence.Progress(0);
                Assert.That(current, Is.GreaterThanOrEqualTo(previous - 1e-6f),
                    $"{ease} must be monotonically non-decreasing (sample {i}: {previous} -> {current})");
                previous = current;
            }
        }

        // ---------------------------------------------------------------------------- AC5

        /// <summary>Same mechanism as <c>LegGaitDriverTests.Tick_AllocatesNoGcMemoryPerCall</c> — the
        /// established zero-allocation guard already in this codebase for exactly this shape of
        /// "Tick a driver, read a resolved value" API — extended to 1000 calls as the ticket specifies.
        /// (MV527AllocationGuardTests.cs, the file this AC names, asserts allocation-freedom by a
        /// different proxy — buffer-reference identity across reflected calls — because its subjects
        /// are invoked via <c>MethodInfo.Invoke</c>, whose own allocation profile would otherwise
        /// contaminate the measurement. AnimSequence has no such constraint: Tick/Progress are called
        /// directly, so <c>Is.Not.AllocatingGCMemory()</c> — NUnit's direct byte-count constraint, and
        /// the literal ask in this AC — applies cleanly and is preferred over reinventing that proxy.)</summary>
        [Test]
        public void TickAndProgress_AllocateNoGcMemory_Across1000Calls()
        {
            var sequence = new AnimSequence(new[] { new AnimStep(0f, 1000f, AnimEase.OutElastic) });

            // Warm up first — the very first call may lazily touch something the constraint would
            // otherwise (mis)attribute to the method itself.
            sequence.Tick(0.001f);
            sequence.Progress(0);

            Assert.That(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    sequence.Tick(0.001f);
                    sequence.Progress(0);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        // ---------------------------------------------------------------------------- AC6

        [Test]
        public void TeleportGhostCollapse_ReproducesTheOldLinearCollapseCurve()
        {
            var go = new GameObject("ghost");
            go.transform.localScale = Vector3.one;
            var ghost = go.AddComponent<TeleportGhostCollapse>();

            try
            {
                ghost.Begin(0.5f);

                MethodInfo advance = typeof(TeleportGhostCollapse).GetMethod("Advance",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(advance, "TeleportGhostCollapse.Advance went missing (MV-684)");

                float[] sampleTimes = { 0f, 0.125f, 0.125f, 0.125f, 0.125f }; // cumulative 0,.125,.25,.375,.5
                float elapsed = 0f;
                foreach (float dt in sampleTimes)
                {
                    if (dt > 0f)
                    {
                        // The final sample completes the sequence, so Advance calls Destroy(gameObject).
                        // Destroy() logs an edit-mode-only error and is deferred rather than immediate
                        // (that's WHY the finally block below still needs DestroyImmediate) — expected
                        // noise, not a real failure, so it's suppressed the same way MV578/MV584's
                        // reflection-driven component tests already suppress their own Awake/OnEnable
                        // edit-mode warnings.
                        LogAssert.ignoreFailingMessages = true;
                        try { advance.Invoke(ghost, new object[] { dt }); }
                        finally { LogAssert.ignoreFailingMessages = false; }
                    }
                    elapsed += dt;

                    float expectedFactor = 1f - elapsed / 0.5f;
                    float actualFactor = go.transform.localScale.x;
                    Assert.AreEqual(expectedFactor, actualFactor, 1e-4f,
                        $"scale factor at t={elapsed} must match the old curve 1 - t/duration");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
