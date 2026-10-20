using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Dev;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1270 — every Reef look measurement taken in a browser (MV-1267) was contaminated by the HUD's
    /// Screen-Space-Overlay chrome, with no browser-reachable way to exclude it the way CaptureDirector's
    /// in-editor presets already do (<c>hud.gameObject.SetActive(false)</c>). This asserts the resolved
    /// Tier 2 value <see cref="DevModeController.ApplyHudHide"/> is responsible for: the HUD GameObject's
    /// own <c>activeSelf</c> once the hide path runs with the param present — not the parsed bool, not a
    /// constant.
    /// </summary>
    public sealed class MV1270HudHideParamTests
    {
        [Test]
        public void HudHideParamPresent_DeactivatesTheHudGameObject()
        {
            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();

            try
            {
                DevModeController.ApplyHudHide(hud, hudHideRequested: true);

                Assert.IsFalse(hud.gameObject.activeSelf,
                    "?hud=0/?hud=off must resolve to the HudController's own GameObject being inactive");
            }
            finally
            {
                Object.DestroyImmediate(hudGo);
            }
        }
    }
}
