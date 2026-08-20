using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-504: the existing MV-503 <c>[MV-503] handoff:</c>/<c>[MV-503] stuck:</c> lines let a live build
    /// be read as "dt is zero" but not *why* — this proves <see cref="Time.timeScale"/> now rides along on
    /// the handoff line so a device log states the strongest MV-504 candidate (timeScale left at 0)
    /// directly instead of by inference. Drives <see cref="PlayerController.LogHandoffDiagnostic"/>
    /// directly (it is public, same test-facing idiom as <see cref="MV503StuckDiagnosticTests"/>'s
    /// reflection-driven <c>EvaluateStuckDiagnostic</c>), with <see cref="Time.timeScale"/> pinned to a
    /// value only this fix would print.
    /// </summary>
    public sealed class MV504HandoffDiagnosticTimeScaleTests
    {
        private GameObject _go;
        private PlayerController _player;
        private float _originalTimeScale;

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            _go = new GameObject("MV-504 Handoff Probe", typeof(CharacterController), typeof(PlayerController));
            _player = _go.GetComponent<PlayerController>();
            InvokeAwake(_player);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _originalTimeScale;
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void HandoffLineNamesTheLiveTimeScale()
        {
            // 0.437 is not a value any real pause/resume path in this codebase would coincidentally
            // produce (they all restore to 1, or freeze to 0) — its presence in the log line can only
            // come from actually reading Time.timeScale at the point of the call.
            Time.timeScale = 0.437f;

            LogAssert.Expect(LogType.Log, new Regex(@"^\[MV-503\] handoff:.*timeScale=0\.437 unscaledDt=[\d.]+ .*displacement="));
            _player.LogHandoffDiagnostic();
            LogAssert.NoUnexpectedReceived();
        }
    }
}
