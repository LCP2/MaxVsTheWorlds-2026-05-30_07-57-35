using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering.Universal;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-98 — guards the mobile URP tier the iOS build runs on. The iPhone quality level uses
    /// <c>Mobile_RPAsset</c>; these are the settings that keep it at 60fps on a phone.
    /// On-device profiling of the look-vs-cost knobs (SSAO, HDR) is Lee's device pass.
    ///
    /// MV-507 culled <c>MobileTier_HasMsaaDisabled</c> from here — MSAA is now deliberately on for
    /// this tier (measured close to free on tile-based mobile GPUs; see <see cref="MsaaRenderSettingsTests"/>),
    /// so an assertion that it must stay off directly contradicted the shipped setting.
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
        public void MobileTier_DoesNotSupersampleAbove1()
        {
            // renderScale > 1 supersamples — a straight framerate killer on a phone.
            Assert.LessOrEqual(Load().renderScale, 1f,
                "Mobile render scale must not exceed 1 — supersampling a phone drops it below 60fps.");
        }
    }
}
