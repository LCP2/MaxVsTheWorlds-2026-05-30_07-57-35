using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-931 — the performance-stats overlay's choice (Settings panel switch + Bootstrap's
    /// F1/top-strip toggle, both of which read and write <see cref="PerfOverlaySettings.CurrentMode"/>)
    /// must survive a relaunch, per device. Sets Full first and checks it survives a simulated reload
    /// too — otherwise a broken persistence path (that never writes PlayerPrefs at all) would still
    /// pass the Off-only assertion by coincidence, since Off is also the unsaved default.
    ///
    /// MV-933 widened the persisted value from a bool to <see cref="PerfOverlaySettings.Mode"/> (three
    /// states) — this test was updated to the new API rather than being a new test (Testing policy
    /// MV-465, Rule 1: at most one new test per ticket, and MV-933's is the readout-rebuild-cadence
    /// test in Mv933ReadoutThrottleTests).
    /// </summary>
    public sealed class Mv931PerfOverlaySettingsTests
    {
        private const string Key = "PerfOverlay.Mode";

        [SetUp]
        [TearDown]
        public void ClearPrefs()
        {
            PlayerPrefs.DeleteKey(Key);
            PerfOverlaySettings.ReloadForTest();
        }

        [Test]
        public void TurningOffPersistsAndTheFlagReadsOffAfterAReload()
        {
            PerfOverlaySettings.CurrentMode = PerfOverlaySettings.Mode.Full;
            PerfOverlaySettings.ReloadForTest();
            Assert.That(PerfOverlaySettings.CurrentMode, Is.EqualTo(PerfOverlaySettings.Mode.Full),
                "precondition: Full must survive a reload before Off can be trusted to");

            PerfOverlaySettings.CurrentMode = PerfOverlaySettings.Mode.Off;
            PerfOverlaySettings.ReloadForTest();
            Assert.That(PerfOverlaySettings.CurrentMode, Is.EqualTo(PerfOverlaySettings.Mode.Off),
                "the overlay mode must read Off after a simulated reload");
        }
    }
}
