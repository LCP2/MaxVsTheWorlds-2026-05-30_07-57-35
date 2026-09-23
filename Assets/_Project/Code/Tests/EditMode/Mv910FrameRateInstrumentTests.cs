using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Dev;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-910 — MV-908 found a steady 30fps on device with no <see cref="ModalFrameRateGate"/> call
    /// site's modal visibly open, and the project had no way to tell "our own code requested 30" from
    /// "iOS overrode our 60fps request" apart from a screenshot. This pins the new overlay instrument
    /// (Tier 2 — resolved values, not authored constants, per MV-465): the RESOLVED
    /// <see cref="Application.targetFrameRate"/> readback and <see cref="ModalFrameRateGate.OpenCount"/>
    /// against known state, and that the native-only thermal reading renders "n/a" off-iOS (every
    /// EditMode run) rather than a false reading.
    /// </summary>
    public sealed class Mv910FrameRateInstrumentTests
    {
        [Test]
        public void FrameRateSnapshot_ResolvesLiveRateAndGateCount_AndThermalIsNaOffIos()
        {
            int originalRate = Application.targetFrameRate;
            ModalFrameRateGate.ResetForTests();
            try
            {
                // Hypothesis 1 settled directly: the gate reads 0 open with the rate still at 60 —
                // nothing leaked it upward.
                Application.targetFrameRate = 60;
                var idle = Mv503DiagnosticOverlay.BuildFrameRateSnapshot();
                Assert.AreEqual(60, idle.ResolvedTargetFrameRate,
                    "must read back the resolved Application.targetFrameRate, not an authored constant");
                Assert.AreEqual(0, idle.ModalGateOpenCount, "no modal open — count must read 0");

                // A real Enter() call raises the count and (as MV-574 already proved) idles the rate —
                // the overlay must show BOTH moving together, which is what turns "no modal looks open"
                // into a number instead of a screenshot judgement call.
                ModalFrameRateGate.Enter();
                var opened = Mv503DiagnosticOverlay.BuildFrameRateSnapshot();
                Assert.AreEqual(ModalFrameRateGate.IdleFrameRate, opened.ResolvedTargetFrameRate,
                    "opening a modal must resolve the live rate to the idle constant");
                Assert.AreEqual(1, opened.ModalGateOpenCount, "one open modal — count must read 1");

                // Thermal state is native-only (NSProcessInfo via a Plugins/iOS bridge) and cannot be
                // driven under -batchmode -nographics; every EditMode run is off-iOS, so this must
                // never print a confident reading it did not take.
                Assert.IsFalse(opened.ThermalHasReading,
                    "thermal reading must be unavailable off-iOS (EditMode always runs off-device)");
                Assert.AreEqual("n/a", opened.ThermalStateName,
                    "an unavailable thermal reading must render 'n/a', never a false state name");
                Assert.IsFalse(opened.IsLowPowerModeEnabled,
                    "an unavailable reading must never assert Low Power Mode true");

                ModalFrameRateGate.Exit();
                var closed = Mv503DiagnosticOverlay.BuildFrameRateSnapshot();
                Assert.AreEqual(60, closed.ResolvedTargetFrameRate, "closing the last modal must restore 60");
                Assert.AreEqual(0, closed.ModalGateOpenCount, "closing the last modal must read 0 open");
            }
            finally
            {
                ModalFrameRateGate.ResetForTests();
                Application.targetFrameRate = originalRate;
            }
        }
    }
}
