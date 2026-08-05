using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using MaxWorlds.UI;
using MaxWorlds.Save;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// MV-257: the HUD's own HOME button (YT-191) sits at sorting order 100, underneath the opaque
    /// scrims Settings (200) and Weapons (210) paint over it — so while either of those "pause"
    /// screens is open, there used to be no way back to the main menu at all. Each now carries its
    /// own Quit to menu control, sharing <see cref="RunFlow.QuitToMenu"/> with the HOME button.
    ///
    /// Loads the real shipped scene, same as <see cref="HudHomeButtonPlayTests"/>, because the
    /// button's own job IS a scene reload — stood up by hand, there would be nothing to reload into.
    /// </summary>
    public sealed class QuitToMenuPlayTests
    {
        private const int Slice = 0; // Backyard_Slice — scene 0 is the playable scene
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveSystem.ResetForTests();
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-quit-to-menu-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;

            SceneManager.LoadScene(Slice);
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            SaveSystem.ResetForTests();
            MaxWorlds.Upgrades.UpgradeState.Reset();
            MaxWorlds.Pickups.PickupWallet.Reset();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            yield return null;
        }

        private static IEnumerator EnterSlot(int slot)
        {
            var home = Object.FindFirstObjectByType<HomeScreen>();
            Assert.That(home, Is.Not.Null, "the Home screen should be up on a fresh scene load");

            Button play = home.GetComponentsInChildren<Button>(true)
                .Where(b => b.gameObject.name == "PLAY")
                .ElementAt(slot);
            play.onClick.Invoke();
            yield return null;
        }

        private static Button FindButtonNamed(string name)
        {
            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (b.gameObject.name == name) return b;
            return null;
        }

        [UnityTest]
        public IEnumerator WeaponsScreen_QuitButton_ReturnsToAFreshHomeScreen()
        {
            yield return EnterSlot(0);

            var weapons = Object.FindFirstObjectByType<WeaponsScreen>();
            Assert.That(weapons, Is.Not.Null, "the weapons screen must be live once a slot is picked");
            weapons.Open();
            yield return null;

            var quit = FindButtonNamed("Quit Button");
            Assert.That(quit, Is.Not.Null, "the weapons screen has no Quit To Menu button");

            quit.onClick.Invoke();
            // The reload happens synchronously inside the click; the new scene's own Awake/Start
            // still each need a frame, same as SceneReloadPlayTests/HudHomeButtonPlayTests.
            yield return null;
            yield return null;

            Assert.That(SaveSystem.ActiveSlot, Is.EqualTo(-1),
                "quitting from the weapons screen must drop the active slot, same as HOME");

            var homeAgain = Object.FindFirstObjectByType<HomeScreen>();
            Assert.That(homeAgain, Is.Not.Null, "quitting must reopen the Home screen");
            Assert.That(homeAgain.IsOpen, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f), "the reopened Home screen must pause the game");
        }

        [UnityTest]
        public IEnumerator SettingsPanel_QuitButton_ReturnsToAFreshHomeScreen()
        {
            yield return EnterSlot(0);

            Canvas settingsCanvas = null;
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.name == "Settings Canvas") settingsCanvas = c;
            Assert.That(settingsCanvas, Is.Not.Null, "the Settings panel must be live once a slot is picked");

            var gear = settingsCanvas.transform.Find("Safe Area/Gear").GetComponent<Button>();
            Assert.That(gear, Is.Not.Null, "no gear button to open Settings with");
            gear.onClick.Invoke();
            yield return null;

            var quit = FindButtonNamed("Quit to menu");
            Assert.That(quit, Is.Not.Null, "the Settings panel has no Quit to menu button");

            quit.onClick.Invoke();
            yield return null;
            yield return null;

            Assert.That(SaveSystem.ActiveSlot, Is.EqualTo(-1),
                "quitting from Settings must drop the active slot, same as HOME");

            var homeAgain = Object.FindFirstObjectByType<HomeScreen>();
            Assert.That(homeAgain, Is.Not.Null, "quitting must reopen the Home screen");
            Assert.That(homeAgain.IsOpen, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f), "the reopened Home screen must pause the game");
        }
    }
}
