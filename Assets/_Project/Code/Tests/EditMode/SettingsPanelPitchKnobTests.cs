using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-450 AC3: the dev-only Camera pitch knob must be provably absent from a non-dev session —
    /// unlike every other Settings-panel knob, which YT-120 deliberately made always-compiled-in. The
    /// gate lives in <see cref="SettingsPanel.ShouldShowPitchKnob"/>, a standalone predicate tested
    /// directly here because building the panel's actual uGUI only ever runs in Play mode.
    /// </summary>
    public sealed class SettingsPanelPitchKnobTests
    {
        [SetUp]
        [TearDown]
        public void ClearState()
        {
            DevMode.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void PitchKnobIsAbsentWithoutDevMode()
        {
            Assert.That(DevMode.Enabled, Is.False, "precondition: not in dev mode");
            Assert.That(SettingsPanel.ShouldShowPitchKnob(), Is.False,
                "the pitch knob must be unreachable outside dev mode");
        }

        [Test]
        public void PitchKnobAppearsOnceDevModeIsOn()
        {
            DevMode.Enabled = true;
            Assert.That(SettingsPanel.ShouldShowPitchKnob(), Is.True);
        }

        [Test]
        public void ResetToDefaultsClearsTheCameraPitchOverrideToo()
        {
            DevTuning.CameraPitch = 60f;
            Assert.That(DevTuning.AnyOverride, Is.True);

            DevTuning.Reset();
            Assert.That(DevTuning.CameraPitch, Is.Null,
                "the panel's Reset button must clear the pitch override same as every other knob");
            Assert.That(DevTuning.AnyOverride, Is.False);
        }
    }
}
