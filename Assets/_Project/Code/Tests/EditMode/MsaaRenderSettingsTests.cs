using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MaxWorlds.Editor;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-507 turned MSAA on in every render tier (m_MSAA: 1 = disabled, was the prior state). Every
    /// surface in MAX is flat-tinted with no texture, normal map or gradient, so an aliased silhouette
    /// edge is the single most visible artefact on screen — this was the largest share of "Max is low
    /// res and clunky". MV-574 reverses that for the Mobile tier only: MSAA 4x at full native iPhone
    /// resolution with HDR was found to be the largest bandwidth item on a tile-based mobile GPU and the
    /// leading cause of iOS heat/battery drain, while <c>BackyardLighting.EnablePostProcessingOnCamera</c>'s
    /// FXAA pass (untouched by this ticket) keeps the image anti-aliased at near-zero extra cost. PC and
    /// WebGL keep MSAA 4x. Either way, turning MSAA on or off must not silently drop the SSAO feature
    /// (m_Active) that's already tuned on both renderers.
    /// </summary>
    public sealed class MsaaRenderSettingsTests
    {
        private const string MobileRpAsset = Stage76RenderScaffold.MobileRpPath;
        private const string PcRpAsset = Stage76RenderScaffold.PcRpPath;
        private const string MobileRendererAsset = Stage76RenderScaffold.MobileRendererPath;
        private const string PcRendererAsset = Stage76RenderScaffold.PcRendererPath;

        private static ScreenSpaceAmbientOcclusion SsaoOf(string rendererPath)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
            Assert.IsNotNull(renderer, $"renderer asset missing: {rendererPath}");
            var ssao = Stage76RenderScaffold.Find(renderer);
            Assert.IsNotNull(ssao, "SSAO renderer feature is missing");
            return ssao;
        }

        [Test]
        public void PcTier_KeepsMsaaEnabled_WithoutDroppingSsao()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcRpAsset);
            Assert.IsNotNull(pipeline, $"URP asset missing: {PcRpAsset}");

            // The resolved runtime value, not the "m_MSAA: N" text in the .asset file — msaaSampleCount
            // is what URP actually reads when it sets up the render target.
            Assert.GreaterOrEqual(pipeline.msaaSampleCount, 2,
                "MSAA must resolve to at least 2x on PC — flat-shaded geometry has no texture " +
                "detail to hide an aliased silhouette edge, and PC has the bandwidth to spare.");

            Assert.IsTrue(SsaoOf(PcRendererAsset).isActive, "SSAO renderer feature was switched off");
        }

        [Test]
        public void MobileTier_DropsMsaaForHeat_WithoutDroppingSsao()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileRpAsset);
            Assert.IsNotNull(pipeline, $"URP asset missing: {MobileRpAsset}");

            // MV-574: MSAA 4x at native iPhone resolution was the largest bandwidth item on a
            // tile-based mobile GPU and the leading cause of the reported heat/battery drain. Resolved
            // value, not the "m_MSAA: N" source line.
            Assert.AreEqual(1, pipeline.msaaSampleCount,
                "MSAA must resolve to disabled (1x) on Mobile — 4x at native res + HDR is what generates " +
                "the heat; FXAA (BackyardLighting.EnablePostProcessingOnCamera) is the AA pass this tier keeps.");

            Assert.IsTrue(SsaoOf(MobileRendererAsset).isActive,
                "SSAO renderer feature was switched off — dropping MSAA must not touch this dial");
        }
    }
}
