using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering.Universal;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-59 — guards the render settings that the WebGL build actually runs on.
    ///
    /// The bug this exists to prevent: WebGL defaults to the "Mobile" quality level, whose URP asset
    /// had renderScale 0.8 with the upscaling filter on Automatic. Automatic selects FSR, whose
    /// shader ('Hidden/Universal Render Pipeline/Edge Adaptive Spatial Upsampling') is unsupported on
    /// WebGL — and URP's response is not to fall back, it is to skip POST-PROCESSING ENTIRELY:
    ///
    ///     "Shader ... is not supported (in 'Blit FSR Upscaling'). PostProcessing render passes
    ///      will not execute."
    ///
    /// So the whole YT-49 lighting/post pass (bloom, grading, tonemapping, vignette) silently did
    /// nothing on the web build while looking perfect in the editor. Nothing failed, nothing threw,
    /// and every test passed. This one wouldn't have.
    /// </summary>
    public sealed class WebGlRenderSettingsTests
    {
        private const string MobileAsset = "Assets/Settings/Mobile_RPAsset.asset";
        private const string PcAsset = "Assets/Settings/PC_RPAsset.asset";

        [TestCase(MobileAsset)]
        [TestCase(PcAsset)]
        public void UrpAsset_DoesNotRequireAnUpscalerWebGlCannotCompile(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            Assert.IsNotNull(asset, $"URP asset missing: {path}");

            Assert.AreNotEqual(UpscalingFilterSelection.FSR, asset.upscalingFilter,
                "FSR's upscaling shader is unsupported on WebGL, and URP responds by disabling the " +
                "entire post-processing stack — not by falling back.");

            // Automatic picks FSR whenever renderScale < 1, which is how this happened in the first
            // place. Either pin the filter, or don't upscale.
            if (asset.upscalingFilter == UpscalingFilterSelection.Auto)
            {
                Assert.AreEqual(1f, asset.renderScale, 1e-3f,
                    "Automatic upscaling + renderScale < 1 selects FSR, which kills post-processing " +
                    "on WebGL. Pin the filter or set renderScale to 1.");
            }
        }
    }
}
