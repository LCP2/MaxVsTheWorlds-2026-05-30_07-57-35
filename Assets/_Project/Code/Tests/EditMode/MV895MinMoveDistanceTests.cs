using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-895: a live iOS TestFlight readout (build 0.9.7) caught Max's own <c>[MV-503]</c> stuck
    /// diagnostic firing with <c>displacement</c> non-zero but <c>actualDelta</c> exactly zero, at a
    /// residual (non-normalised, analogue-stick) <c>moveDir</c> that resolves to roughly 0.00077 m of
    /// commanded horizontal motion per frame. <see cref="CharacterController.minMoveDistance"/> defaults
    /// to 0.001 m and is set nowhere in this codebase, so Max's controller carries that default and
    /// <see cref="CharacterController.Move"/> silently discards any commanded motion below it -- the
    /// "invisible barrier" reported at both a11's and a13's west-gate corners is this discard, not a
    /// solid: pressing into a wall at a corner cancels most of <c>moveDir</c>, leaving a residual that
    /// only drops below threshold there (and worse still under MV-883's low-timeScale clamp, which
    /// shrinks <c>dt</c> and therefore the commanded displacement further).
    /// </summary>
    public sealed class MV895MinMoveDistanceTests
    {
        // The exact order of magnitude of the live readout's residual displacement (walkSpeed 3.860557
        // x moveDir magnitude 0.153 x dt 0.0013 ~= 0.00077 m) -- comfortably under CharacterController's
        // default 0.001 m minMoveDistance threshold, so it reproduces the discard on the base commit.
        private const float SubThresholdDisplacement = 0.0008f;

        private GameObject _go;
        private CharacterController _cc;
        private PlayerController _player;

        // Awake isn't reliably invoked for AddComponent/GameObject-constructor components outside Play
        // mode (same empirical finding MV503StuckDiagnosticTests already works around) -- without this,
        // PlayerController never touches its CharacterController at all.
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("MV-895 MinMoveDistance Probe", typeof(CharacterController), typeof(PlayerController));
            _cc = _go.GetComponent<CharacterController>();
            _player = _go.GetComponent<PlayerController>();
            InvokeAwake(_player);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void MinMoveDistanceResolvesToZero_SoASubThresholdDisplacementActuallyMovesMax()
        {
            // AC1 comes first and is asserted on its own, before AC2 -- so its own failure output (on
            // the base commit) proves THIS claim specifically, rather than being masked by an earlier
            // assertion aborting the method before this one is ever reached.
            Vector3 before = _go.transform.position;
            _cc.Move(Vector3.forward * SubThresholdDisplacement);
            Vector3 actualDelta = _go.transform.position - before;

            Assert.AreNotEqual(0f, actualDelta.sqrMagnitude,
                $"a {SubThresholdDisplacement:0.####} m Move() was silently discarded -- this is the exact " +
                "MV-895 mechanism: CharacterController.minMoveDistance throwing away a commanded motion " +
                "smaller than itself, which is what a residual, non-normalised analogue moveDir at a " +
                "corner produces once MV-883's low-timeScale clamp shrinks dt far enough.");

            // AC2: the resolved value on the instantiated player, not a prefab/constant.
            Assert.AreEqual(0f, _cc.minMoveDistance,
                "Max's instantiated CharacterController must resolve minMoveDistance to 0 -- Unity's " +
                "default of 0.001 m is what silently discards a sub-threshold Move() (MV-895).");
        }
    }
}
