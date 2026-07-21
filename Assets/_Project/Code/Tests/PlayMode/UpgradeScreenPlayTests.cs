using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using MaxWorlds.UI;
using MaxWorlds.Pickups;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The upgrade moment (YT-132): tapping the chip opens a paused screen that reveals and fits the
    /// picked-up part, and dismissing it installs the part and resumes cleanly.
    /// </summary>
    public sealed class UpgradeScreenPlayTests
    {
        private GameObject _screenGo;
        private GameObject _hudGo;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PickupWallet.Reset();
            Time.timeScale = 1f;
            // Both the screen and the HUD self-install and persist across the run; clear them so each
            // test owns exactly one of each and FindFirstObjectByType can't return a stray.
            foreach (var s in Object.FindObjectsByType<UpgradeScreen>(FindObjectsSortMode.None))
                Object.Destroy(s.gameObject);
            foreach (var h in Object.FindObjectsByType<HudController>(FindObjectsSortMode.None))
                Object.Destroy(h.gameObject);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;   // never leave the world frozen for the next test
            if (_screenGo != null) Object.Destroy(_screenGo);
            if (_hudGo != null) Object.Destroy(_hudGo);
            PickupWallet.Reset();
            yield return null;
        }

        private IEnumerator NewScreen()
        {
            _screenGo = new GameObject("UpgradeScreen");
            _screenGo.AddComponent<UpgradeScreen>();
            yield return null;   // Start builds the canvas
        }

        private UpgradeScreen Screen => _screenGo.GetComponent<UpgradeScreen>();

        [UnityTest]
        public IEnumerator OpeningPausesTheGameAndShowsThePart()
        {
            yield return NewScreen();
            PickupWallet.AddPart();

            Screen.Open(UpgradePart.Generic);
            yield return null;

            Assert.That(Screen.IsOpen, Is.True, "the screen didn't open");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "the game must pause while the upgrade screen is up");
            Assert.That(Screen.Part.Name, Is.EqualTo(UpgradePart.Generic.Name), "the collected part isn't shown");
        }

        [UnityTest]
        public IEnumerator ContinueInstallsThePartAndResumes()
        {
            yield return NewScreen();
            PickupWallet.AddPart();
            Assert.That(PickupWallet.PartsPending, Is.EqualTo(1));

            Screen.Open(UpgradePart.Generic);
            yield return null;
            Screen.Continue();
            yield return null;

            Assert.That(Screen.IsOpen, Is.False, "the screen didn't close");
            Assert.That(Time.timeScale, Is.EqualTo(1f), "the game must resume to its previous speed on dismiss");
            Assert.That(PickupWallet.PartsPending, Is.EqualTo(0), "dismissing must install (spend) the pending part");
        }

        [UnityTest]
        public IEnumerator ItRestoresWhateverTimescaleItPausedFrom()
        {
            yield return NewScreen();
            PickupWallet.AddPart();

            Time.timeScale = 0.5f;   // e.g. a slow-mo beat
            Screen.Open(UpgradePart.Generic);
            yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(0f), "open must freeze regardless of the prior speed");

            Screen.Continue();
            yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(0.5f), "continue must restore the speed it paused from, not assume 1");
        }

        [UnityTest]
        public IEnumerator TheContinueHintAppearsOnceThePartHasFitted()
        {
            yield return NewScreen();
            Screen.Open(UpgradePart.Generic);

            // Let the reveal + fit play out on unscaled time (the world is frozen).
            float waited = 0f;
            while (waited < 1.2f) { waited += Time.unscaledDeltaTime; yield return null; }

            var hint = FindText(_screenGo, "TAP TO CONTINUE");
            Assert.That(hint, Is.Not.Null, "there's no continue prompt");
            Assert.That(hint.gameObject.activeInHierarchy, Is.True,
                "the 'tap to continue' prompt should be shown once the part has fitted");
        }

        [UnityTest]
        public IEnumerator TappingTheHudChipOpensTheScreen()
        {
            yield return NewScreen();

            _hudGo = new GameObject("HUD");
            _hudGo.AddComponent<HudController>();
            yield return null;

            PickupWallet.AddPart();   // raises the chip
            yield return null;

            // Find the chip's button and tap it, the way a finger would.
            Button chip = null;
            foreach (var b in _hudGo.GetComponentsInChildren<Button>(true))
                if (b.gameObject.name == "Chip") chip = b;
            Assert.That(chip, Is.Not.Null, "the part chip isn't a button");

            chip.onClick.Invoke();
            yield return null;

            Assert.That(Screen.IsOpen, Is.True, "tapping the chip should open the upgrade screen");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "and pause the game");
        }

        private static Text FindText(GameObject root, string content)
        {
            foreach (var t in root.GetComponentsInChildren<Text>(true))
                if (t.text == content) return t;
            return null;
        }
    }
}
