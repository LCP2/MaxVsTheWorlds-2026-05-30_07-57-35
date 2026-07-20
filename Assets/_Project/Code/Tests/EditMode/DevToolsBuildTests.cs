using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The dev tuning panel has to be present in a TestFlight build and absent from an App Store
    /// one (YT-118). Neither half of that is observable from inside a normal test run — the editor
    /// compiles without the define — so what these pin is the part that CAN go wrong silently: the
    /// safe default staying safe, and the CI plumbing that adds the define still existing.
    /// </summary>
    public sealed class DevToolsBuildTests
    {
        private const string Define = "MAXWORLDS_DEV_TOOLS";

        private static string RepoRoot => Directory.GetParent(Application.dataPath).FullName;

        [SetUp]
        [TearDown]
        public void ResetDevMode() => DevMode.Reset();

        // ---------------------------------------------------------------- the safe default

        /// <summary>
        /// The committed settings must NOT carry the define. This is the direction the whole design
        /// leans on: a build ships without the panel unless something deliberately opts in, so
        /// forgetting a step can only ever cost Lee his sliders — never leak dev tools to the
        /// App Store.
        /// </summary>
        [Test]
        public void TheCommittedProjectSettingsDoNotCarryTheDevToolsDefine()
        {
            string settings = Path.Combine(RepoRoot, "ProjectSettings", "ProjectSettings.asset");
            Assert.That(File.Exists(settings), Is.True, $"missing {settings}");

            Assert.That(File.ReadAllText(settings), Does.Not.Contain(Define),
                        $"{Define} is committed to ProjectSettings — every iOS build would carry " +
                        "the dev panel, including an App Store submission. CI adds it per build.");
        }

        [Test]
        public void AnOrdinaryBuildCompilesWithoutTheDevTools()
        {
            Assert.That(DevMode.DevToolsBuild, Is.False,
                        "this test run was compiled WITH the dev-tools define, so it cannot speak " +
                        "for a release build");
        }

        // ---------------------------------------------------------------- the CI plumbing exists

        /// <summary>
        /// The define is added by a workflow step, and a workflow step that quietly stops running is
        /// invisible: the build succeeds, TestFlight gets a build with no sliders, and the only
        /// symptom is Lee not finding a button. So the wiring is asserted rather than assumed.
        /// </summary>
        [Test]
        public void TheTestFlightWorkflowStillAsksForTheDevToolsDefine()
        {
            string script = Path.Combine(RepoRoot, "scripts", "enable-dev-tools-define.py");
            Assert.That(File.Exists(script), Is.True,
                        "the script that adds the dev-tools define is gone; TestFlight builds would " +
                        "silently ship without the tuning panel");

            string workflow = Path.Combine(RepoRoot, ".github", "workflows", "ios-testflight.yml");
            Assert.That(File.Exists(workflow), Is.True, $"missing {workflow}");

            string yaml = File.ReadAllText(workflow);
            Assert.That(yaml, Does.Contain("enable-dev-tools-define.py"),
                        "the iOS workflow no longer runs the dev-tools step");
            Assert.That(yaml.IndexOf("enable-dev-tools-define.py", System.StringComparison.Ordinal),
                        Is.LessThan(yaml.IndexOf("game-ci/unity-builder", System.StringComparison.Ordinal)),
                        "the define has to be added BEFORE Unity builds, or it does nothing at all");
        }

        // ---------------------------------------------------------------- tools vs cheats

        /// <summary>
        /// The separation YT-118 turns on. A beta build gets the panel; it must not get invincibility
        /// and an infinite tank, or Lee would be tuning "life" and "water rates" in a build where
        /// neither can run out — the sliders would move and nothing he was trying to feel would change.
        /// </summary>
        [Test]
        public void HavingTheToolsIsNotTheSameAsHavingTheCheats()
        {
            Assert.That(DevMode.IsInvincible, Is.False);
            Assert.That(DevMode.IsInfiniteEnergy, Is.False);

            // The cheats read Enabled, which a dev-tools build never sets on its own.
            Assert.That(DevMode.Enabled, Is.False,
                        "compiling the tools in must not switch the cheats on");
        }

        [Test]
        public void WithoutTheToolsGameplayGetsTheAuthoredNumber()
        {
            DevTuning.PlayerMoveSpeed = 99f;

            Assert.That(DevMode.ToolsAvailable, Is.False);
            Assert.That(DevTuning.Or(DevTuning.PlayerMoveSpeed, 6f), Is.EqualTo(6f),
                        "a release session must ignore any override left lying around");
        }

        [Test]
        public void WithTheToolsAvailableTheSliderActuallyMovesTheNumber()
        {
            DevMode.Enabled = true;   // the editor/WebGL route to the same ToolsAvailable state
            DevTuning.PlayerMoveSpeed = 99f;

            Assert.That(DevMode.ToolsAvailable, Is.True);
            Assert.That(DevTuning.Or(DevTuning.PlayerMoveSpeed, 6f), Is.EqualTo(99f),
                        "a slider that moves without changing the game is worse than no slider");
        }

        [Test]
        public void AnUntouchedKnobStillGivesTheAuthoredNumber()
        {
            DevMode.Enabled = true;

            Assert.That(DevTuning.Or(null, 6f), Is.EqualTo(6f),
                        "merely having the panel must not change how the game plays");
        }
    }
}
