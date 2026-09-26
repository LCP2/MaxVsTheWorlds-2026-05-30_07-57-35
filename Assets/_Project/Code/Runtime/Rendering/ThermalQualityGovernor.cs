using UnityEngine;
using UnityEngine.Rendering.Universal;
using MaxWorlds.Core;

namespace MaxWorlds.Rendering
{
    /// <summary>The three render-load tiers <see cref="ThermalQualityGovernor"/> steps between.
    /// Ordered so plain relational comparison (<c>desired &gt;= AppliedTier</c>) tells "hotter" from
    /// "cooler" without a separate severity table.</summary>
    public enum ThermalTier
    {
        Nominal = 0,
        Serious = 1,
        Critical = 2,
    }

    /// <summary>
    /// MV-958: once iOS's own NSProcessInfo thermal state crosses into .serious, iOS throttles the
    /// CPU/GPU itself — every frame the game asks for then costs MORE, heating continues, and the game
    /// spirals (Lee's TestFlight 0.9.10 measurement: "thermal serious", 7.7-8.8 fps, gpu up to 43.2 ms/
    /// frame in combat). This steps the render load DOWN in response, through the same live on-device
    /// knobs MV-662 already exposes (<see cref="MobileRenderTuning"/>) — never by rewriting
    /// <c>Assets/Settings/Mobile_RPAsset.asset</c>.
    ///
    /// Pure C# with the thermal reading and clock both passed into <see cref="Tick"/> explicitly — same
    /// "unit-testable with no game running" shape as <see cref="FpsMeter"/>/<see cref="FrameTimingProbe"/>
    /// — so an EditMode test can drive Fair -> Serious -> Critical -> Fair with no native iOS bridge.
    /// The live game drives one instance from <see cref="ThermalQualityGovernorRunner"/>, iOS player
    /// only; every other platform never even compiles that runner in.
    ///
    /// A hotter reading is applied immediately: reacting a poll cycle late is exactly what lets the
    /// spiral compound. A cooler reading must instead be SUSTAINED for <see cref="StepUpHysteresisSeconds"/>
    /// before it restores anything — a reading that flickers across the Serious/Fair boundary must not
    /// thrash the render settings (and re-trigger the very spiral this exists to stop) every couple of
    /// seconds.
    /// </summary>
    public sealed class ThermalQualityGovernor
    {
        /// <summary>How often a live poll actually samples the thermal reading; extra <see cref="Tick"/>
        /// calls inside this window are free no-ops.</summary>
        public const float PollIntervalSeconds = 2f;

        /// <summary>Seconds a cooler reading must hold before the governor steps back toward Nominal.</summary>
        public const float StepUpHysteresisSeconds = 20f;

        public const float SeriousRenderScale = 0.7f;
        public const float SeriousShadowDistance = 25f;
        public const float CriticalRenderScale = 0.55f;

        /// <summary>Shared by both throttled tiers — Serious already halves the idle rate's worth of
        /// GPU/CPU headroom, and Critical never needs to go lower than what Serious already asks for.</summary>
        public const int ThrottledTargetFrameRate = 30;

        private float _lastPollAt = float.NegativeInfinity;
        private bool _hasPendingCooler;
        private ThermalTier _pendingCoolerTier;
        private float _pendingCoolerSince;

        /// <summary>The tier actually applied to the live render settings right now.</summary>
        public ThermalTier AppliedTier { get; private set; } = ThermalTier.Nominal;

        /// <summary>The live instance the iOS player is ticking, so the MV-910 overlay can read
        /// <see cref="AppliedTier"/> without the Rendering assembly depending on Gameplay to wire it in
        /// — same "Active" pattern as <c>Bootstrap.ActiveMeter</c>/<c>ActiveTimingProbe</c>. Null off-iOS,
        /// where no runner ever installs one.</summary>
        public static ThermalQualityGovernor Active { get; set; }

        /// <summary>Maps <see cref="IosDeviceStateProbe.ThermalStateName"/>'s own vocabulary directly, so
        /// the live runner needs no separate translation table. Anything not recognised (including the
        /// off-iOS "n/a") reads as Nominal — the safe, no-action default.</summary>
        private static ThermalTier TierFor(string thermalStateName) => thermalStateName switch
        {
            "serious" => ThermalTier.Serious,
            "critical" => ThermalTier.Critical,
            _ => ThermalTier.Nominal,
        };

        /// <summary>One-letter tag for the MV-910 overlay's "tier S"/"tier C" line — only ever read
        /// there when the thermal reading itself has a value, i.e. on-device.</summary>
        public static string TierTag(ThermalTier tier) => tier switch
        {
            ThermalTier.Serious => "S",
            ThermalTier.Critical => "C",
            _ => "N",
        };

        /// <summary>Samples at most once per <see cref="PollIntervalSeconds"/> — call every frame, same
        /// idiom as <see cref="FrameTimingProbe.Tick"/>. Returns the tier now applied.</summary>
        public ThermalTier Tick(float now, string thermalStateName)
        {
            if (now - _lastPollAt < PollIntervalSeconds) return AppliedTier;
            _lastPollAt = now;

            ThermalTier desired = TierFor(thermalStateName);

            if (desired >= AppliedTier)
            {
                // A hotter (or unchanged) reading cancels any pending step-up and applies immediately.
                _hasPendingCooler = false;
                if (desired != AppliedTier)
                {
                    AppliedTier = desired;
                    Apply(desired);
                }
                return AppliedTier;
            }

            // Cooler than what's currently applied — hold it for the hysteresis window first.
            if (!_hasPendingCooler || _pendingCoolerTier != desired)
            {
                _hasPendingCooler = true;
                _pendingCoolerTier = desired;
                _pendingCoolerSince = now;
                return AppliedTier;
            }

            if (now - _pendingCoolerSince >= StepUpHysteresisSeconds)
            {
                _hasPendingCooler = false;
                AppliedTier = desired;
                Apply(desired);
            }

            return AppliedTier;
        }

        /// <summary>Applies one tier's render settings. Each case sets every knob it cares about outright
        /// (never an incremental delta off the previous tier), so this is correct regardless of which
        /// tier was applied before it — including a governor that starts life already at Serious/Critical.
        /// Skips any <see cref="MobileRenderTuning"/> knob the Settings panel has already overridden via
        /// <see cref="DevTuning"/> (DevTuning wins), and never forces <see cref="ModalFrameRateGate.ActiveFrameRate"/>
        /// back on top of an open modal's idle rate.</summary>
        private static void Apply(ThermalTier tier)
        {
            switch (tier)
            {
                case ThermalTier.Nominal:
                    if (!DevTuning.MobileRenderScale.HasValue)
                        MobileRenderTuning.ApplyRenderScale(MobileRenderTuning.DefaultRenderScale);
                    if (!DevTuning.MobileShadowDistance.HasValue)
                        MobileRenderTuning.ApplyShadowDistance(MobileRenderTuning.DefaultShadowDistance);
                    if (!DevTuning.MobileSoftShadows.HasValue)
                        MobileRenderTuning.ApplySoftShadows(MobileRenderTuning.DefaultSoftShadowsSupported);
                    MobileRenderTuning.ApplyMainLightShadows(MobileRenderTuning.DefaultMainLightShadowsSupported);
                    MobileRenderTuning.ApplyAdditionalLightsRenderingMode(MobileRenderTuning.DefaultAdditionalLightsRenderingMode);
                    if (ModalFrameRateGate.OpenCount == 0)
                        Application.targetFrameRate = ModalFrameRateGate.ActiveFrameRate;
                    break;

                case ThermalTier.Serious:
                    if (!DevTuning.MobileRenderScale.HasValue)
                        MobileRenderTuning.ApplyRenderScale(SeriousRenderScale);
                    if (!DevTuning.MobileShadowDistance.HasValue)
                        MobileRenderTuning.ApplyShadowDistance(SeriousShadowDistance);
                    if (!DevTuning.MobileSoftShadows.HasValue)
                        MobileRenderTuning.ApplySoftShadows(false);
                    MobileRenderTuning.ApplyMainLightShadows(true);
                    MobileRenderTuning.ApplyAdditionalLightsRenderingMode(LightRenderingMode.PerVertex);
                    // Serious/Critical both pin 30 outright rather than deferring to ModalFrameRateGate —
                    // 30 already equals the gate's own IdleFrameRate, so there is nothing to conflict with.
                    Application.targetFrameRate = ThrottledTargetFrameRate;
                    break;

                case ThermalTier.Critical:
                    if (!DevTuning.MobileRenderScale.HasValue)
                        MobileRenderTuning.ApplyRenderScale(CriticalRenderScale);
                    if (!DevTuning.MobileShadowDistance.HasValue)
                        MobileRenderTuning.ApplyShadowDistance(SeriousShadowDistance);
                    if (!DevTuning.MobileSoftShadows.HasValue)
                        MobileRenderTuning.ApplySoftShadows(false);
                    MobileRenderTuning.ApplyMainLightShadows(false);
                    MobileRenderTuning.ApplyAdditionalLightsRenderingMode(LightRenderingMode.Disabled);
                    Application.targetFrameRate = ThrottledTargetFrameRate;
                    break;
            }
        }
    }
}
