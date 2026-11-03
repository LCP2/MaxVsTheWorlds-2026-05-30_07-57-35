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
    /// no game running" shape as FpsMeter/FrameTimingProbe) through Fair -> Serious -> Critical -> Fair
    /// -> Nominal, and asserts the RESOLVED URP <c>renderScale</c>/<c>shadowDistance</c> and the
    /// RESOLVED <see cref="Application.targetFrameRate"/> at each step (Tier 2 — resolved values, per
    /// MV-465) — never a literal read back off <see cref="ThermalQualityGovernor"/>'s own consts (Rule
    /// 2). Also proves stepping back down only happens once the 20s hysteresis window has actually
    /// elapsed, not the instant the reading cools — the spiral this ticket exists to stop would
    /// otherwise just re-trigger on the first flicker back toward Serious.
    ///
    /// MV-1343 gave "fair" its own Fair tier instead of folding it into Nominal (Lee's TestFlight
    /// 0.25.0 World 1 report: iOS already reports "fair" with nothing throttled) — this test's own
    /// "fair resolves to Nominal" premise is exactly what that ticket changed, so the sequence below
    /// now asserts Fair's own resolved values and only reaches Nominal via an explicit "nominal" tick.
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
            Assert.AreEqual(ThermalTier.Fair, governor.AppliedTier,
                "fair must resolve to its own Fair tier (MV-1343) — it must act immediately, same as any other hotter-or-equal reading");
            Assert.AreEqual(ThermalQualityGovernor.FairRenderScale, urp.renderScale,
                "Fair must resolve renderScale to 0.75");
            Assert.AreEqual(ThermalQualityGovernor.FairShadowDistance, urp.shadowDistance,
                "Fair must resolve shadowDistance to 18");
            Assert.AreEqual(ModalFrameRateGate.ActiveFrameRate, Application.targetFrameRate,
                "Fair must leave targetFrameRate at the active 60fps — only Serious/Critical throttle that");

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

            // Now the window has actually elapsed (6 -> 26 is 20s) — steps down to Fair, the tier the
            // "fair" reading itself resolves to (MV-1343), not all the way to Nominal.
            governor.Tick(26f, "fair");
            Assert.AreEqual(ThermalTier.Fair, governor.AppliedTier,
                "20s of a sustained cooler \"fair\" reading must restore the Fair tier, not skip past it to Nominal");
            Assert.AreEqual(ThermalQualityGovernor.FairRenderScale, urp.renderScale,
                "restoring Fair must resolve renderScale back to 0.75");
            Assert.AreEqual(ThermalQualityGovernor.FairShadowDistance, urp.shadowDistance,
                "restoring Fair must resolve shadowDistance back to 18");

            // Cools further to nominal — again held for the hysteresis window before restoring.
            governor.Tick(28f, "nominal");
            Assert.AreEqual(ThermalTier.Fair, governor.AppliedTier,
                "a cooler reading must not restore quality before the hysteresis window elapses, even from Fair");

            governor.Tick(48f, "nominal");
            Assert.AreEqual(ThermalTier.Nominal, governor.AppliedTier,
                "20s of a sustained \"nominal\" reading must restore the Nominal tier");
            Assert.AreEqual(MobileRenderTuning.DefaultRenderScale, urp.renderScale,
                "restoring Nominal must resolve renderScale back to the authored default");
            Assert.AreEqual(MobileRenderTuning.DefaultShadowDistance, urp.shadowDistance,
                "restoring Nominal must resolve shadowDistance back to the authored default");
            Assert.AreEqual(ModalFrameRateGate.ActiveFrameRate, Application.targetFrameRate,
                "restoring Nominal must resolve targetFrameRate back to 60");
        }
    }
}
