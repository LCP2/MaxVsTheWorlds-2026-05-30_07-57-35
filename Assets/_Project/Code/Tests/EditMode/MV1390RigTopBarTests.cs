using System.IO;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1390: the RIG top bar is four equal buttons, right to left CLOSE, PARTS, SETTINGS, QUIT TO MENU;
    /// PARTS text sits inside its border; SETTINGS opens the SettingsPanel over the paused RIG; Home no
    /// longer builds a SETTINGS button. Resolved rects / preferredWidth only (Tier 2).
    /// </summary>
    public sealed class MV1390RigTopBarTests
    {
        [Test]
        public void TopBar_FourEqualButtons_PartsTextFits_SettingsOpensPanelPaused_HomeHasNoSettings()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ytgame-save-tests-mv1390");
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            SaveSystem.DirectoryOverride = dir;
            SaveSystem.ActiveSlot = -1;
            WeaponSystemState.Reset(); RigState.Reset(); RigFusionState.Reset(); PickupWallet.Reset();

            var go = new GameObject("WeaponsScreen");
            var settingsGo = new GameObject("SettingsPanel");
            var homeGo = new GameObject("HomeScreen");
            try
            {
                settingsGo.AddComponent<SettingsPanel>();
                var screen = go.AddComponent<WeaponsScreen>();
                screen.Open();   // parks Time.timeScale at 0

                var close = FindRect(go, "Close Button");
                var parts = FindRect(go, "Parts Chip");
                var settings = FindRect(go, "Settings Button");
                var quit = FindRect(go, "Quit Button");
                Assert.That(close, Is.Not.Null); Assert.That(parts, Is.Not.Null);
                Assert.That(settings, Is.Not.Null, "RIG top bar must carry a Settings Button");
                Assert.That(quit, Is.Not.Null);

                // AC1: equal size, right-to-left order CLOSE, PARTS, SETTINGS, QUIT.
                var all = new[] { close, parts, settings, quit };
                for (int i = 1; i < all.Length; i++)
                {
                    Assert.That(all[i].rect.width, Is.EqualTo(all[0].rect.width).Within(1f), $"{all[i].name} width");
                    Assert.That(all[i].rect.height, Is.EqualTo(all[0].rect.height).Within(1f), $"{all[i].name} height");
                    Assert.That(WorldRect(all[i]).xMax, Is.LessThanOrEqualTo(WorldRect(all[i - 1]).xMin),
                        $"{all[i].name} must sit entirely left of {all[i - 1].name}");
                }

                // AC2: the widest PARTS text clears the chip by >= 24 px.
                var partsText = parts.GetComponentInChildren<TMP_Text>();
                partsText.text = "999/999 PARTS";
                Assert.That(partsText.preferredWidth, Is.LessThanOrEqualTo(parts.rect.width - 24f),
                    "PARTS text must sit inside its border");

                // AC3: SETTINGS opens SettingsPanel and the game stays paused.
                Assert.That(Time.timeScale, Is.EqualTo(0f), "fixture: RIG pauses");
                settings.GetComponentInChildren<Button>(true).onClick.Invoke();
                var panelCanvas = settingsGo.GetComponentInChildren<Canvas>(true);
                Assert.That(panelCanvas != null && panelCanvas.gameObject.activeInHierarchy, Is.True, "SettingsPanel opened");
                Assert.That(panelCanvas.sortingOrder, Is.GreaterThan(210), "panel draws over the RIG");
                Assert.That(Time.timeScale, Is.EqualTo(0f), "still paused with Settings open");

                // AC3b: Home builds no SETTINGS button.
                var home = homeGo.AddComponent<HomeScreen>();
                home.Open();
                foreach (var b in homeGo.GetComponentsInChildren<Button>(true))
                    Assert.That(b.gameObject.name, Is.Not.EqualTo("SETTINGS"), "Home must not build SETTINGS");
            }
            finally
            {
                Object.DestroyImmediate(homeGo);
                Object.DestroyImmediate(settingsGo);
                Object.DestroyImmediate(go);
                SaveSystem.ResetForTests();
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                PickupWallet.Reset(); RigFusionState.Reset(); RigState.Reset(); WeaponSystemState.Reset();
                Time.timeScale = 1f;
            }
        }

        private static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        private static RectTransform FindRect(GameObject go, string name)
        {
            foreach (var t in go.GetComponentsInChildren<RectTransform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
