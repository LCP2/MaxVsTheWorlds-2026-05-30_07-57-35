using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-503: "Max rotates but never translates on a fresh run" has no known fix yet — this ticket adds
    /// only the diagnostic that will identify the cause from a live build, not a fix. This proves the
    /// instrument itself is correct before anyone relies on its output: it must fire when a move input
    /// produces no real displacement (a disabled <see cref="CharacterController"/> is one of the two
    /// mechanisms the ticket's own investigation names as a live candidate — <c>Move()</c> on one is a
    /// silent no-op), and it must stay silent once a move actually lands, so the instrument reports the
    /// symptom rather than merely "input is present".
    ///
    /// Drives <see cref="PlayerController"/>'s private <c>EvaluateStuckDiagnostic</c> directly via
    /// reflection (same idiom as <c>GunnerSentinelBeamTests</c>/<c>AreaAccumulationDirectorGarrisonAndPlacementTests</c>),
    /// with the moveDir/displacement/actualDelta vectors supplied explicitly — <c>Update()</c> reads
    /// them off the New Input System, which an EditMode test cannot drive without a running player loop.
    /// </summary>
    public sealed class MV503StuckDiagnosticTests
    {
        private GameObject _go;
        private CharacterController _cc;
        private PlayerController _player;
        private MethodInfo _evaluate;

        // Awake isn't reliably invoked for AddComponent/GameObject-constructor components outside
        // Play mode (same empirical finding EnemyNavigationGateTests/WaterBlasterGateDamageTests
        // already work around) — without this, PlayerController's private _cc field is never set and
        // EvaluateStuckDiagnostic NREs on it instead of exercising the diagnostic at all.
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("MV-503 Stuck Probe", typeof(CharacterController), typeof(PlayerController));
            _cc = _go.GetComponent<CharacterController>();
            _player = _go.GetComponent<PlayerController>();
            InvokeAwake(_player);
            _evaluate = typeof(PlayerController).GetMethod("EvaluateStuckDiagnostic",
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void FiresWhileHeldWithNoRealDisplacement_AndStaysSilentOnceTheMoveActuallyLands()
        {
            Vector3 moveDir = Vector3.forward;
            Vector3 commandedDisplacement = Vector3.forward * (3.01f * 0.016f);

            // A disabled CharacterController: Move() on it is a silent no-op, so a full move input and
            // a non-trivial commanded displacement still measure zero actual movement — exactly the
            // live symptom this ticket exists to pin down.
            _cc.enabled = false;
            LogAssert.Expect(LogType.Log,
                new Regex(@"^\[MV-503\] stuck: cc\.enabled=False .*actualDelta=\(0\.0000, 0\.0000, 0\.0000\)$"));
            _evaluate.Invoke(_player, new object[] { moveDir, commandedDisplacement, Vector3.zero, 0.016f });

            // Re-enable and this time supply the actual delta as the FULL commanded displacement — a
            // real move landed. Must not log at all: the instrument has to tell "stuck" from "moving",
            // not just "input held".
            _cc.enabled = true;
            _evaluate.Invoke(_player, new object[] { moveDir, commandedDisplacement, commandedDisplacement, 0.016f });
            LogAssert.NoUnexpectedReceived();
        }
    }
}
