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
        public const float DefaultShadowDistance = 55f;
        public const bool DefaultSoftShadowsSupported = true;

        private static readonly FieldInfo SoftShadowsField = typeof(UniversalRenderPipelineAsset)
            .GetField("m_SoftShadowsSupported", BindingFlags.NonPublic | BindingFlags.Instance);

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
    }
}
