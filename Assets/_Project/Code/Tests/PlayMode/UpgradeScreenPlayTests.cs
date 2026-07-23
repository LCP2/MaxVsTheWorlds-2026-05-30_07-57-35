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
            UpgradeState.Reset();   // Continue installs a part now (YT-133) — don't leak it into other tests
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
            UpgradeState.Reset();
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
            PickupWallet.AddPart(PartKind.BeamNozzle);

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
            PickupWallet.AddPart(PartKind.BeamNozzle);
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
            PickupWallet.AddPart(PartKind.BeamNozzle);

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
        public IEnumerator TheTitleLabelsWhichFamilyThePartBelongsTo()
        {
            // YT-166: the screen should read "HOSE UPGRADE" / "MOVEMENT UPGRADE" / "DETACH UPGRADE" so
            // which of the three families a reveal belongs to is obvious at a glance.
            yield return NewScreen();

            Screen.Open(UpgradeCatalog.AccelerationEngine);
            yield return null;
            var movementTitle = FindTextEndingWith(_screenGo, "UPGRADE");
            Assert.That(movementTitle, Is.Not.Null, "no title text found");
            Assert.That(movementTitle.text, Is.EqualTo("MOVEMENT UPGRADE"),
                "the Acceleration engine is a MOVEMENT part");
            Screen.Continue();
            yield return null;

            Screen.Open(UpgradeCatalog.Hydro);
            yield return null;
            Assert.That(FindTextEndingWith(_screenGo, "UPGRADE").text, Is.EqualTo("DETACH UPGRADE"),
                "the Hydro kit is a DETACH part");
            Screen.Continue();
            yield return null;

            Screen.Open(UpgradeCatalog.BeamNozzle);
            yield return null;
            Assert.That(FindTextEndingWith(_screenGo, "UPGRADE").text, Is.EqualTo("HOSE UPGRADE"),
                "the Beam nozzle is a HOSE part");
        }

        [UnityTest]
        public IEnumerator TheContinueHintDoesNotOverlapThePartName()
        {
            // YT-166: "TAP TO CONTINUE" used to sit close enough to the part/weapon name to overlap it
            // (a ~20px overlap in their text boxes). Assert the fitted layout instead of trusting eyes.
            yield return NewScreen();
            Screen.Open(UpgradePart.Generic);

            float waited = 0f;
            while (waited < 1.2f) { waited += Time.unscaledDeltaTime; yield return null; }

            var name = FindText(_screenGo, Screen.Part.Name);
            var hint = FindText(_screenGo, "TAP TO CONTINUE");
            Assert.That(name, Is.Not.Null, "the part name text wasn't found");
            Assert.That(hint, Is.Not.Null, "the continue hint wasn't found");

            var nameCorners = new Vector3[4];
            var hintCorners = new Vector3[4];
            name.rectTransform.GetWorldCorners(nameCorners);
            hint.rectTransform.GetWorldCorners(hintCorners);

            float nameBottom = nameCorners[0].y;
            float hintTop = hintCorners[1].y;

            Assert.That(nameBottom, Is.GreaterThan(hintTop),
                "the part name must sit clear above 'tap to continue', not overlap it");
        }

        [UnityTest]
        public IEnumerator TappingTheHudChipOpensTheScreen()
        {
            yield return NewScreen();

            _hudGo = new GameObject("HUD");
            _hudGo.AddComponent<HudController>();
            yield return null;

            PickupWallet.AddPart(PartKind.BeamNozzle);   // raises the chip
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

        [UnityTest]
        public IEnumerator TheScrimIsNearOpaque()
        {
            // YT-176: the screen must read as its own dedicated screen, not a thin overlay showing the
            // live arena moving behind it (the same fix already applied to the Home screen, YT-174).
            yield return NewScreen();

            var scrim = FindImage(_screenGo, "Scrim");
            Assert.That(scrim, Is.Not.Null, "no scrim found");
            Assert.That(scrim.color.a, Is.GreaterThan(0.9f),
                "the scrim is too translucent — the live arena would show through behind the panel");
        }

        [UnityTest]
        public IEnumerator TheMaxPortraitIsALiveRenderNotAFlatSprite()
        {
            // YT-176: replaces the off-style 2D painted headshot with the real low-poly Max, rendered
            // live into a texture the same way the weapon on the right already is.
            yield return NewScreen();

            var portrait = FindRawImage(_screenGo, "Max Portrait");
            Assert.That(portrait, Is.Not.Null, "no live-rendered Max portrait found");
            Assert.That(portrait.texture, Is.Not.Null, "the portrait's render texture never got assigned");
        }

        [UnityTest]
        public IEnumerator TheFamilyRowShowsEveryMemberOfThePartsFamilyInOrder()
        {
            // YT-176: the reveal must show the part in the context of its family/arc, not in isolation.
            yield return NewScreen();
            UpgradeState.Install(PartKind.BeamNozzle);   // already carried, ahead of this reveal

            Screen.Open(UpgradeCatalog.PowerNozzle);   // HOSE family: Beam, Power, Range, WideBore, Aug
            yield return null;

            var pips = new Image[5];
            var labels = new Text[5];
            for (int i = 0; i < 5; i++)
            {
                pips[i] = FindImage(_screenGo, $"Pip{i}");
                labels[i] = FindTextInside(pips[i]);
            }

            Assert.That(labels[0].text, Is.EqualTo("BEAM"));
            Assert.That(labels[1].text, Is.EqualTo("PWR"));
            Assert.That(labels[2].text, Is.EqualTo("RNG"));
            Assert.That(labels[3].text, Is.EqualTo("WIDE"));
            Assert.That(labels[4].text, Is.EqualTo("AUG"));

            Assert.That(pips[0].gameObject.activeSelf, Is.True, "the already-installed Beam nozzle should show");
            Assert.That(pips[1].gameObject.activeSelf, Is.True, "the part being revealed now should show");
            Assert.That(pips[0].color, Is.EqualTo(UpgradeCatalog.BeamNozzle.Accent),
                "an already-carried part should light up in its own accent, not stay locked");
            Assert.That(pips[1].color, Is.EqualTo(UpgradeCatalog.PowerNozzle.Accent),
                "the part just revealed should light up in its own accent");
            Assert.That(pips[2].color, Is.Not.EqualTo(UpgradeCatalog.RangeExtender.Accent),
                "a part not yet collected must stay locked, not light up early");
        }

        private static Image FindImage(GameObject root, string name)
        {
            foreach (var img in root.GetComponentsInChildren<Image>(true))
                if (img.gameObject.name == name) return img;
            return null;
        }

        private static RawImage FindRawImage(GameObject root, string name)
        {
            foreach (var img in root.GetComponentsInChildren<RawImage>(true))
                if (img.gameObject.name == name) return img;
            return null;
        }

        private static Text FindTextInside(Image parent) =>
            parent == null ? null : parent.GetComponentInChildren<Text>(true);

        private static Text FindText(GameObject root, string content)
        {
            foreach (var t in root.GetComponentsInChildren<Text>(true))
                if (t.text == content) return t;
            return null;
        }

        private static Text FindTextEndingWith(GameObject root, string suffix)
        {
            foreach (var t in root.GetComponentsInChildren<Text>(true))
                if (t.text != null && t.text.EndsWith(suffix)) return t;
            return null;
        }
    }
}
