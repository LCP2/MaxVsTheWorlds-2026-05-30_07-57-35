using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MaxWorlds.Editor;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-507 — MSAA was off in every render tier (m_MSAA: 1 = disabled). Every surface in MAX is
    /// flat-tinted with no texture, normal map or gradient, so an aliased silhouette edge is the
    /// single most visible artefact on screen — this was the largest share of "Max is low res and
    /// clunky". Turning MSAA on must not silently drop the SSAO feature (m_Active) that's already
    /// tuned on both renderers.
    /// </summary>
    public sealed class MsaaRenderSettingsTests
    {
        private const string MobileRpAsset = Stage76RenderScaffold.MobileRpPath;
        private const string PcRpAsset = Stage76RenderScaffold.PcRpPath;
        private const string MobileRendererAsset = Stage76RenderScaffold.MobileRendererPath;
        private const string PcRendererAsset = Stage76RenderScaffold.PcRendererPath;

        [TestCase(MobileRpAsset, MobileRendererAsset)]
        [TestCase(PcRpAsset, PcRendererAsset)]
        public void UrpTier_HasMsaaEnabled_WithoutDroppingSsao(string rpPath, string rendererPath)
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpPath);
            Assert.IsNotNull(pipeline, $"URP asset missing: {rpPath}");

            // The resolved runtime value, not the "m_MSAA: N" text in the .asset file — msaaSampleCount
            // is what URP actually reads when it sets up the render target.
            Assert.GreaterOrEqual(pipeline.msaaSampleCount, 2,
                "MSAA must resolve to at least 2x on this tier — flat-shaded geometry has no texture " +
                "detail to hide an aliased silhouette edge.");

            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
            Assert.IsNotNull(renderer, $"renderer asset missing: {rendererPath}");

            var ssao = Stage76RenderScaffold.Find(renderer);
            Assert.IsNotNull(ssao, "SSAO renderer feature is missing — enabling MSAA must not remove it");
            Assert.IsTrue(ssao.isActive, "SSAO renderer feature was switched off");
        }
    }
}
