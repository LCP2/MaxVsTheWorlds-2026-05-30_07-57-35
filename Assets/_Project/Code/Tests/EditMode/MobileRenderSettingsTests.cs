using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering.Universal;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-98 — guards the mobile URP tier the iOS build runs on. The iPhone quality level uses
    /// <c>Mobile_RPAsset</c>; these are the settings that keep it at 60fps on a phone. If someone
    /// later flips MSAA on or pushes the render scale above 1 on the mobile asset, that is a silent
    /// iOS-perf regression the WebGL CI would never surface — this catches it before merge.
    /// On-device profiling of the look-vs-cost knobs (SSAO, HDR) is Lee's device pass.
    /// </summary>
    public sealed class MobileRenderSettingsTests
    {
        private const string MobileAsset = "Assets/Settings/Mobile_RPAsset.asset";

        private static UniversalRenderPipelineAsset Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileAsset);
            Assert.IsNotNull(asset, $"Mobile URP asset missing: {MobileAsset}");
            return asset;
        }

        [Test]
        public void MobileTier_HasMsaaDisabled()
        {
            // MSAA is expensive bandwidth on tiled mobile GPUs; a low-poly cartoon look does not need it.
            Assert.AreEqual(1, Load().msaaSampleCount,
                "MSAA must stay off on the mobile URP tier (1 sample) to hold 60fps on iOS.");
        }

        [Test]
        public void MobileTier_DoesNotSupersampleAbove1()
        {
            // renderScale > 1 supersamples — a straight framerate killer on a phone.
            Assert.LessOrEqual(Load().renderScale, 1f,
                "Mobile render scale must not exceed 1 — supersampling a phone drops it below 60fps.");
        }
    }
}
