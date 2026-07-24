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
    /// The in-game HOME button (YT-191): one tap from a live run back to the Home/save-slot screen,
    /// with the run saved first so Continue picks it back up from exactly there — not a death/wipe.
    ///
    /// Loads the real shipped scene, the way <see cref="SceneReloadPlayTests"/> does, because the
    /// button's own job IS a scene reload — stood up by hand, there would be nothing to reload into.
    /// </summary>
    public sealed class HudHomeButtonPlayTests
    {
        private const int Slice = 0; // Backyard_Slice — scene 0 is the playable scene
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveSystem.ResetForTests();
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-home-button-tests");
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

            Button newGame = home.GetComponentsInChildren<Button>(true)
                .Where(b => b.gameObject.name == "NEW GAME")
                .ElementAt(slot);
            newGame.onClick.Invoke();
            yield return null;
        }

        private static RectTransform FindInHud(HudController hud, string name)
        {
            foreach (var rt in hud.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt;
            return null;
        }

        [UnityTest]
        public IEnumerator Tapped_SavesTheRunThenReturnsToAResumableHomeScreen()
        {
            yield return EnterSlot(1);
            Assert.That(SaveSystem.ActiveSlot, Is.EqualTo(1), "picking slot 1's New Game must hand off to it");

            var hud = Object.FindFirstObjectByType<HudController>();
            Assert.That(hud, Is.Not.Null, "the HUD must be live once a slot is picked");

            RectTransform homeRoot = FindInHud(hud, "Home Button");
            Assert.That(homeRoot, Is.Not.Null, "the HOME button is missing from the HUD");
            Button homeBtn = homeRoot.GetComponentInChildren<Button>(true);
            Assert.That(homeBtn, Is.Not.Null, "the HOME button has no clickable Button component");

            homeBtn.onClick.Invoke();

            // The reload happens synchronously inside the click; the new scene's own Awake/Start
            // still each need a frame, same as SceneReloadPlayTests.
            yield return null;
            yield return null;

            Assert.That(SaveSystem.Load(1).HasData, Is.True,
                "the HOME button must save the run before leaving it, so the slot is resumable");
            Assert.That(SaveSystem.ActiveSlot, Is.EqualTo(-1),
                "returning home must drop the active slot, the same way a fresh boot starts");

            var homeAgain = Object.FindFirstObjectByType<HomeScreen>();
            Assert.That(homeAgain, Is.Not.Null, "the HOME button must reopen the Home screen");
            Assert.That(homeAgain.IsOpen, Is.True, "the reopened Home screen must actually be showing");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "the reopened Home screen must pause the game");
        }

        [UnityTest]
        public IEnumerator Tapped_KeepsUpgradesAndCellsForContinue()
        {
            yield return EnterSlot(0);

            MaxWorlds.Upgrades.UpgradeState.Install(MaxWorlds.Upgrades.PartKind.Hydro);
            MaxWorlds.Pickups.PickupWallet.AddPowerCell();
            yield return null;

            var hud = Object.FindFirstObjectByType<HudController>();
            RectTransform homeRoot = FindInHud(hud, "Home Button");
            Button homeBtn = homeRoot.GetComponentInChildren<Button>(true);
            homeBtn.onClick.Invoke();
            yield return null;
            yield return null;

            SaveSlotData data = SaveSystem.Load(0);
            Assert.That(data.InstalledParts, Does.Contain(MaxWorlds.Upgrades.PartKind.Hydro),
                "the HOME save must carry installed parts, exactly like the YT-151 resume path");
            Assert.That(data.PowerCells, Is.EqualTo(1),
                "the HOME save must carry banked cells, exactly like the YT-151 resume path");
        }
    }
}
