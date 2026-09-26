using System.Reflection;
using UnityEngine.Rendering.Universal;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// Live on-device GPU-bandwidth knobs for the Mobile URP tier (MV-662) — render scale, shadow
    /// distance, and soft shadows — applied straight onto whichever <see cref="UniversalRenderPipelineAsset"/>
    /// is currently active, so Lee can bisect the iOS thermal issue by sweeping these on-device
    /// without a rebuild. The three consts mirror <c>Assets/Settings/Mobile_RPAsset.asset</c>'s
    /// authored values; keep them in sync if that asset's numbers change.
    ///
    /// <see cref="UniversalRenderPipelineAsset.supportsSoftShadows"/> has a public getter but only an
    /// <c>internal</c> setter — there is no public runtime API to flip it on the active asset, so this
    /// reaches the backing field directly the same way Unity's own serializer does.
    /// </summary>
    public static class MobileRenderTuning
    {
        public const float DefaultRenderScale = 1f;
        // MV-969: mirrors Mobile_RPAsset.asset's own reduction from 55m to 25m — see this class's own
        // doc comment on why these consts have to track that file's authored values.
        public const float DefaultShadowDistance = 25f;
        public const bool DefaultSoftShadowsSupported = true;
        public const bool DefaultMainLightShadowsSupported = true;
        public const LightRenderingMode DefaultAdditionalLightsRenderingMode = LightRenderingMode.PerPixel;

        private static readonly FieldInfo SoftShadowsField = typeof(UniversalRenderPipelineAsset)
            .GetField("m_SoftShadowsSupported", BindingFlags.NonPublic | BindingFlags.Instance);

        // MV-958: same "internal setter, no public runtime API" story as m_SoftShadowsSupported above —
        // supportsMainLightShadows and additionalLightsRenderingMode both expose an internal-only set.
        private static readonly FieldInfo MainLightShadowsField = typeof(UniversalRenderPipelineAsset)
            .GetField("m_MainLightShadowsSupported", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo AdditionalLightsRenderingModeField = typeof(UniversalRenderPipelineAsset)
            .GetField("m_AdditionalLightsRenderingMode", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void ApplyRenderScale(float value)
        {
            var urp = UniversalRenderPipeline.asset;
            if (urp != null) urp.renderScale = value;
        }

        public static void ApplyShadowDistance(float value)
        {
            var urp = UniversalRenderPipeline.asset;
            if (urp != null) urp.shadowDistance = value;
        }

        public static void ApplySoftShadows(bool on)
        {
            var urp = UniversalRenderPipeline.asset;
            if (urp != null && SoftShadowsField != null) SoftShadowsField.SetValue(urp, on);
        }

        public static void ApplyMainLightShadows(bool on)
        {
            var urp = UniversalRenderPipeline.asset;
            if (urp != null && MainLightShadowsField != null) MainLightShadowsField.SetValue(urp, on);
        }

        public static void ApplyAdditionalLightsRenderingMode(LightRenderingMode mode)
        {
            var urp = UniversalRenderPipeline.asset;
            if (urp != null && AdditionalLightsRenderingModeField != null) AdditionalLightsRenderingModeField.SetValue(urp, mode);
        }
    }
}
