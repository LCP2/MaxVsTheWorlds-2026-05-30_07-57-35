using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-961 — Settings is reached from the Home screen's own SETTINGS button now, so the in-game
    /// HUD must no longer carry the gear SettingsPanel used to build into its own left column
    /// (MV-645). Sole guard on this removal; do not cull (MV-465).
    /// </summary>
    public sealed class MV961NoInGameGearTests
    {
        [Test]
        public void NoGearButtonExistsUnderAnyCanvasWithHudAndSettingsPanelInstalled()
        {
            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");

            var settingsGo = new GameObject("SettingsPanel");
            var settings = settingsGo.AddComponent<SettingsPanel>();
            typeof(SettingsPanel).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(settings, null);

            try
            {
                foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    foreach (var rt in canvas.GetComponentsInChildren<RectTransform>(true))
                    {
                        Assert.That(rt.name == "Gear" && rt.gameObject.activeInHierarchy, Is.False,
                            $"an active 'Gear' GameObject exists under canvas '{canvas.name}' — the in-game " +
                            "Settings gear must be gone now that Settings opens from the Home screen (MV-961)");
                    }
                }
            }
            finally
            {
                InvokeLifecycle(hud, "OnDisable");
                Object.DestroyImmediate(hudGo);
                Object.DestroyImmediate(settingsGo);
            }
        }

        private static void InvokeLifecycle(Object component, string methodName) =>
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
    }
}
