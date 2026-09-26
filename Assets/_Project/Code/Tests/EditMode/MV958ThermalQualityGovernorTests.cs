using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-958 — Lee's TestFlight overlay (0.9.10, World 2) measured "thermal serious" with 7.7-8.8 fps
    /// and gpu up to 43.2 ms/frame in combat: once iOS reports Serious it throttles the CPU/GPU itself,
    /// so every subsequent frame costs MORE, heating continues, and the game spirals. This pins
    /// <see cref="ThermalQualityGovernor"/>, which steps the render load down through the same live
    /// on-device knobs MV-662 already exposes (<see cref="MobileRenderTuning"/>) rather than by editing
    /// <c>Assets/Settings/Mobile_RPAsset.asset</c>.
    ///
    /// Drives the governor with an injected clock and thermal-state string (same "unit-testable with
    /// no game running" shape as FpsMeter/FrameTimingProbe) through Fair -> Serious -> Critical -> Fair,
    /// and asserts the RESOLVED URP <c>renderScale</c>/<c>shadowDistance</c> and the RESOLVED
    /// <see cref="Application.targetFrameRate"/> at each step (Tier 2 — resolved values, per MV-465) —
    /// never a literal read back off <see cref="ThermalQualityGovernor"/>'s own consts (Rule 2). Also
    /// proves stepping back to Fair only happens once the 20s hysteresis window has actually elapsed,
    /// not the instant the reading cools — the spiral this ticket exists to stop would otherwise just
    /// re-trigger on the first flicker back toward Serious.
    /// </summary>
    public sealed class MV958ThermalQualityGovernorTests
    {
        private float _origRenderScale;
        private float _origShadowDistance;
        private int _origTargetFrameRate;

        private static UniversalRenderPipelineAsset ActiveAsset()
        {
            var urp = UniversalRenderPipeline.asset;
            Assert.IsNotNull(urp, "no active UniversalRenderPipelineAsset — MobileRenderTuning applies to this, not the authored asset file directly");
            return urp;
        }

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            ModalFrameRateGate.ResetForTests();

            var urp = ActiveAsset();
            _origRenderScale = urp.renderScale;
            _origShadowDistance = urp.shadowDistance;
            _origTargetFrameRate = Application.targetFrameRate;

            // A clean, known baseline regardless of what an earlier test in this shared EditMode
            // domain left on the active asset — MobileRenderTuning's own consts mirror the authored file.
            MobileRenderTuning.ApplyRenderScale(MobileRenderTuning.DefaultRenderScale);
            MobileRenderTuning.ApplyShadowDistance(MobileRenderTuning.DefaultShadowDistance);
            Application.targetFrameRate = ModalFrameRateGate.ActiveFrameRate;
        }

        [TearDown]
        public void TearDown()
        {
            MobileRenderTuning.ApplyRenderScale(_origRenderScale);
            MobileRenderTuning.ApplyShadowDistance(_origShadowDistance);
            Application.targetFrameRate = _origTargetFrameRate;
            DevTuning.Reset();
            ModalFrameRateGate.ResetForTests();
        }

        [Test]
        public void Governor_StepsRenderLoadWithThermalTier_AndHoldsHysteresisBeforeRestoring()
        {
            var urp = ActiveAsset();
            var governor = new ThermalQualityGovernor();

            governor.Tick(0f, "fair");
            Assert.AreEqual(ThermalTier.Nominal, governor.AppliedTier, "fair must resolve to the Nominal tier");
            Assert.AreEqual(MobileRenderTuning.DefaultRenderScale, urp.renderScale,
                "Nominal must leave renderScale at the authored default");
            Assert.AreEqual(MobileRenderTuning.DefaultShadowDistance, urp.shadowDistance,
                "Nominal must leave shadowDistance at the authored default");
            Assert.AreEqual(ModalFrameRateGate.ActiveFrameRate, Application.targetFrameRate,
                "Nominal must leave targetFrameRate at the active 60fps");

            governor.Tick(2f, "serious");
            Assert.AreEqual(ThermalTier.Serious, governor.AppliedTier, "serious must step to the Serious tier immediately");
            Assert.AreEqual(ThermalQualityGovernor.SeriousRenderScale, urp.renderScale,
                "Serious must resolve renderScale to 0.7");
            Assert.AreEqual(ThermalQualityGovernor.SeriousShadowDistance, urp.shadowDistance,
                "Serious must resolve shadowDistance to 25");
            Assert.AreEqual(ThermalQualityGovernor.ThrottledTargetFrameRate, Application.targetFrameRate,
                "Serious must resolve targetFrameRate to 30");

            governor.Tick(4f, "critical");
            Assert.AreEqual(ThermalTier.Critical, governor.AppliedTier, "critical must step to the Critical tier immediately");
            Assert.AreEqual(ThermalQualityGovernor.CriticalRenderScale, urp.renderScale,
                "Critical must resolve renderScale to 0.55");
            Assert.AreEqual(ThermalQualityGovernor.ThrottledTargetFrameRate, Application.targetFrameRate,
                "Critical must resolve targetFrameRate to 30");

            // Cools straight back to fair — must NOT restore immediately, or the spiral this ticket
            // exists to stop just re-triggers on the first flicker.
            governor.Tick(6f, "fair");
            Assert.AreEqual(ThermalTier.Critical, governor.AppliedTier,
                "a cooler reading must not restore quality before the hysteresis window elapses");
            Assert.AreEqual(ThermalQualityGovernor.CriticalRenderScale, urp.renderScale,
                "renderScale must still read the Critical value mid-hysteresis");

            // Still short of the 20s hysteresis window (6 -> 24 is 18s).
            governor.Tick(24f, "fair");
            Assert.AreEqual(ThermalTier.Critical, governor.AppliedTier,
                "18s of a cooler reading must still be short of the 20s hysteresis window");

            // Now the window has actually elapsed (6 -> 26 is 20s).
            governor.Tick(26f, "fair");
            Assert.AreEqual(ThermalTier.Nominal, governor.AppliedTier,
                "20s of a sustained cooler reading must restore the Nominal tier");
            Assert.AreEqual(MobileRenderTuning.DefaultRenderScale, urp.renderScale,
                "restoring Nominal must resolve renderScale back to the authored default");
            Assert.AreEqual(MobileRenderTuning.DefaultShadowDistance, urp.shadowDistance,
                "restoring Nominal must resolve shadowDistance back to the authored default");
            Assert.AreEqual(ModalFrameRateGate.ActiveFrameRate, Application.targetFrameRate,
                "restoring Nominal must resolve targetFrameRate back to 60");
        }
    }
}
