using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-974 — Lee chose 0.85 (2026-09-27) as the iOS-only render-scale baseline: TestFlight 0.9.10
    /// measured 17-43ms GPU/frame at native retina. Forces the iOS platform path via
    /// <see cref="MobileRenderTuning.DefaultRenderScaleFor"/> (a parameter, not a compile define — same
    /// testable shape as <c>Bootstrap.ShouldShowDebugOverlay</c>) since this project's own editor build
    /// target is Windows standalone, never iOS. Sole guard on the iOS baseline; do not cull (MV-465).
    /// </summary>
    public sealed class MV974IosRenderScaleTests
    {
        private const string MobileAssetPath = "Assets/Settings/Mobile_RPAsset.asset";

        [Test]
        public void IosPlatformResolvesRenderScaleTo085_AndHudCanvasStaysScreenSpaceOverlay()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileAssetPath);
            Assert.IsNotNull(urp, $"Mobile URP asset missing: {MobileAssetPath}");
            Assert.IsNotNull(UniversalRenderPipeline.asset, "no active URP asset to resolve renderScale onto");

            float origRenderScale = UniversalRenderPipeline.asset.renderScale;
            try
            {
                // --- (1) forced iOS path resolves the baseline to 0.85 and applies onto the active asset ---
                float iosDefault = MobileRenderTuning.DefaultRenderScaleFor(RuntimePlatform.IPhonePlayer);
                Assert.AreEqual(0.85f, iosDefault, 1e-4f,
                    "MV-974: Lee chose 0.85 as the iOS-only render-scale baseline (2026-09-27).");

                MobileRenderTuning.ApplyRenderScale(iosDefault);
                Assert.AreEqual(0.85f, UniversalRenderPipeline.asset.renderScale, 1e-4f,
                    "forcing the iOS baseline must resolve onto the active URP asset's renderScale");

                // --- every other platform keeps the asset's own authored native (1) baseline ---
                Assert.AreEqual(1f, MobileRenderTuning.DefaultRenderScaleFor(RuntimePlatform.WebGLPlayer), 1e-4f,
                    "WebGL must not be affected by the iOS-only render-scale reduction (MV-974 item 3).");
                Assert.AreEqual(1f, MobileRenderTuning.DefaultRenderScaleFor(RuntimePlatform.WindowsEditor), 1e-4f,
                    "the Editor must not be affected by the iOS-only render-scale reduction (MV-974 item 3).");
            }
            finally
            {
                UniversalRenderPipeline.asset.renderScale = origRenderScale;
            }

            // --- (2) the HUD canvas is screen-space overlay, so URP render scale never touches it ---
            var hudGo = new GameObject("MV-974 HUD Probe");
            try
            {
                var hud = hudGo.AddComponent<HudController>();
                InvokeLifecycle(hud, "Awake");

                var hudCanvas = hudGo.transform.Find("HUD Canvas");
                Assert.IsNotNull(hudCanvas, "HudController must build its own child 'HUD Canvas'");

                var canvas = hudCanvas.GetComponent<Canvas>();
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode,
                    "the HUD canvas must stay ScreenSpaceOverlay — it draws straight to the backbuffer, " +
                    "unaffected by URP render scale, regardless of the iOS baseline above.");
            }
            finally
            {
                Object.DestroyImmediate(hudGo);
            }
        }

        private static void InvokeLifecycle(Object component, string methodName)
        {
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        }
    }
}
