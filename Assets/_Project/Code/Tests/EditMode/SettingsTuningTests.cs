using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The tuning layer behind the always-compiled Settings panel (YT-120), and a guard that the
    /// build-time define mechanism that broke the iOS build (YT-118 → YT-119) is really gone and
    /// cannot creep back.
    /// </summary>
    public sealed class SettingsTuningTests
    {
        private static string RepoRoot => Directory.GetParent(Application.dataPath).FullName;

        [SetUp]
        [TearDown]
        public void ClearOverrides() => DevTuning.Reset();

        // ---------------------------------------------------------------- the tuning applies flag-free

        [Test]
        public void AnUntouchedKnobGivesTheAuthoredNumber()
        {
            Assert.That(DevTuning.Or(null, 6f), Is.EqualTo(6f),
                "a fresh session, no slider moved, plays exactly as authored");
        }

        [Test]
        public void AMovedSliderChangesTheNumberWithNoDevMode()
        {
            Assert.That(DevMode.Enabled, Is.False, "precondition: not in dev mode");
            DevTuning.PlayerMoveSpeed = 11f;

            Assert.That(DevTuning.Or(DevTuning.PlayerMoveSpeed, 6f), Is.EqualTo(11f),
                "the Settings panel is a real feature now — a moved slider must apply, dev mode or not");
        }

        [Test]
        public void ResettingDevModeDoesNotWipeTheTuning()
        {
            DevTuning.PlayerMoveSpeed = 9f;
            DevMode.Reset();

            Assert.That(DevTuning.PlayerMoveSpeed, Is.EqualTo(9f),
                "tuning is a settings feature now, decoupled from dev mode — toggling dev mode off " +
                "must not silently discard a value the player dialled in");
        }

        [Test]
        public void AnOverrideOfZeroIsHonoured_NotTreatedAsUnset()
        {
            // Zero drain is a legitimate thing to try (infinite fire); a `!= 0` style guard would
            // silently ignore it. float? exists precisely so 0 and "unset" differ.
            DevTuning.BlasterDrainPerSecond = 0f;
            Assert.That(DevTuning.Or(DevTuning.BlasterDrainPerSecond, 10f), Is.EqualTo(0f));
        }

        [Test]
        public void ResetToDefaultsClearsEveryKnob()
        {
            DevTuning.CameraDistance = 40f;
            DevTuning.PlayerMoveSpeed = 9f;
            Assert.That(DevTuning.AnyOverride, Is.True);

            DevTuning.Reset();
            Assert.That(DevTuning.AnyOverride, Is.False, "the panel's Reset button must clear everything");
        }

        // ---------------------------------------------------------------- the fragile mechanism is gone

        /// <summary>
        /// The whole reason for YT-120: the old step injected this define by editing
        /// ProjectSettings.asset mid-CI, which dirtied the tree and failed the build (YT-119).
        /// Nothing should ever put it back.
        /// </summary>
        [Test]
        public void NoBuildTimeDevToolsDefineExistsAnywhere()
        {
            string settings = Path.Combine(RepoRoot, "ProjectSettings", "ProjectSettings.asset");
            Assert.That(File.ReadAllText(settings), Does.Not.Contain("MAXWORLDS_DEV_TOOLS"),
                "the dev-tools scripting define is back in ProjectSettings — this is exactly what " +
                "dirtied the tree and broke the iOS build");

            string script = Path.Combine(RepoRoot, "scripts", "enable-dev-tools-define.py");
            Assert.That(File.Exists(script), Is.False,
                "the define-injection script is back; it is the thing that edits ProjectSettings mid-CI");
        }

        [Test]
        public void TheTestFlightWorkflowHasNoDevToolsToggle()
        {
            string workflow = Path.Combine(RepoRoot, ".github", "workflows", "ios-testflight.yml");
            string yaml = File.ReadAllText(workflow);

            Assert.That(yaml, Does.Not.Contain("dev_tools"),
                "the workflow still exposes the dev-tools toggle that turned the build red when on");
            Assert.That(yaml, Does.Not.Contain("enable-dev-tools-define"),
                "the workflow still runs the define-injection step");
        }
    }
}
