using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-931 — the performance-stats overlay's ON/OFF choice (Settings panel switch + Bootstrap's
    /// F1/top-strip toggle, both of which read and write <see cref="PerfOverlaySettings.Visible"/>)
    /// must survive a relaunch, per device. Sets ON first and checks it survives a simulated reload
    /// too — otherwise a broken persistence path (that never writes PlayerPrefs at all) would still
    /// pass the OFF-only assertion by coincidence, since OFF is also the unsaved default.
    /// </summary>
    public sealed class Mv931PerfOverlaySettingsTests
    {
        private const string Key = "PerfOverlay.Visible";

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
            PerfOverlaySettings.Visible = true;
            PerfOverlaySettings.ReloadForTest();
            Assert.That(PerfOverlaySettings.Visible, Is.True,
                "precondition: ON must survive a reload before OFF can be trusted to");

            PerfOverlaySettings.Visible = false;
            PerfOverlaySettings.ReloadForTest();
            Assert.That(PerfOverlaySettings.Visible, Is.False,
                "the overlay-visible flag must read OFF after a simulated reload");
        }
    }
}
