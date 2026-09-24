using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-926: a live 93-robot World 2 scene went from a 6.5 ms robot bucket (16 robots awake, 0.4 ms
    /// each) to 188.4 ms (93 awake, 2 ms each) after a single camera zoom-out — cost per robot GREW with
    /// the count, which a linear per-robot cost can't produce. This ticket's own diagnosis names
    /// <see cref="CharacterControllerMotion.SafeMove"/> as the likely superlinear driver: every oversized
    /// single-frame <c>Move()</c> (any moving robot, once a stalling frame inflates <c>speed * dt</c>
    /// past <see cref="CharacterControllerMotion.MaxSafeStep"/>) used to call <c>Debug.LogWarning</c>
    /// unconditionally AND split into as many sub-steps as the raw distance implied, with no ceiling on
    /// either. WebGL's console logging is expensive, so more robots stalling -> more warnings -> a
    /// slower frame -> a bigger stall next frame -> more warnings still: the feedback loop this ticket
    /// exists to break.
    ///
    /// Must FAIL on base commit 8433925 (HEAD before this ticket): <see cref="CharacterControllerMotion.HasWarnedOversizedMove"/>
    /// does not exist there at all — the field, the suppression it gates, and the fixed
    /// <see cref="CharacterControllerMotion.MaxSubSteps"/> budget are all part of this ticket's own fix.
    /// Proven by hand: with <c>SafeMove</c>'s suppression check temporarily reverted to base commit
    /// 8433925's unconditional <c>Debug.LogWarning</c> call (fields/instrumentation left in place so the
    /// test still compiles), this exact test fails with:
    /// "Unhandled log message: '[Warning] [CharacterControllerMotion] MV-926 SafeMove budget probe:
    /// oversized single-frame Move (0.50 m) split into 3 steps'. Use UnityEngine.TestTools.LogAssert.Expect"
    /// — i.e. exactly the per-call warning this ticket removes.
    ///
    /// This is an EditMode test, not a live 93-robot capture — CC_AUTONOMY.md forbids PlayMode/driving a
    /// live build here, and <see cref="CharacterControllerMotion.SafeMove"/> needs no player loop to
    /// probe: it's a single deliberate oversized call.
    /// </summary>
    public sealed class Mv926SafeMoveWarningBudgetTests
    {
        [TearDown]
        public void TearDown()
        {
            CharacterControllerMotion.HasWarnedOversizedMove = false;
        }

        [Test]
        public void SafeMove_PastFirstWarning_LogsNothingAndBoundsSweepCountRegardlessOfDistance()
        {
            var go = new GameObject("MV-926 SafeMove budget probe", typeof(CharacterController));
            var cc = go.GetComponent<CharacterController>();
            try
            {
                // Simulate the steady state a real stall reaches almost immediately: SOME robot already
                // tripped the one-time warning this session, so every other robot's own oversized move
                // this same frame must log nothing at all -- the "at most one warning per session" the
                // ticket asks for, not "one warning per call".
                CharacterControllerMotion.HasWarnedOversizedMove = true;
                CharacterControllerMotion.MoveSweepCount = 0;

                CharacterControllerMotion.SafeMove(cc, Vector3.forward * 0.5f);

                LogAssert.NoUnexpectedReceived();
                Assert.LessOrEqual(CharacterControllerMotion.MoveSweepCount, CharacterControllerMotion.MaxSubSteps,
                    "MV-926: SafeMove's sub-step count must come from a fixed budget (MaxSubSteps), not " +
                    $"scale unboundedly with move length -- got {CharacterControllerMotion.MoveSweepCount} " +
                    $"physics sweeps for one call");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
