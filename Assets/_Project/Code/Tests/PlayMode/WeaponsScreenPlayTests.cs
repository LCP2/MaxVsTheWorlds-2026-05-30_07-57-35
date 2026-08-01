using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using MaxWorlds.UI;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The weapons area (WV-232): entering pauses the game, the Primary section shows RCDA's four
    /// tracks at their live level, the Abilities section shows only abilities Max has acquired (and
    /// grows as he acquires more), and a tap spends one banked part on any owned track/ability.
    /// </summary>
    public sealed class WeaponsScreenPlayTests
    {
        private GameObject _screenGo;
        private GameObject _hudGo;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PickupWallet.Reset();
            WeaponSystemState.Reset();
            Time.timeScale = 1f;
            foreach (var s in Object.FindObjectsByType<WeaponsScreen>(FindObjectsSortMode.None))
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
            WeaponSystemState.Reset();
            yield return null;
        }

        private IEnumerator NewScreen()
        {
            _screenGo = new GameObject("WeaponsScreen");
            _screenGo.AddComponent<WeaponsScreen>();
            yield return null;   // Start builds the canvas
        }

        private WeaponsScreen Screen => _screenGo.GetComponent<WeaponsScreen>();

        [UnityTest]
        public IEnumerator OpeningPausesTheGame()
        {
            yield return NewScreen();

            Screen.Open();
            yield return null;

            Assert.That(Screen.IsOpen, Is.True, "the screen didn't open");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "the game must pause while the weapons area is up");
        }

        [UnityTest]
        public IEnumerator ClosingRestoresWhateverTimescaleItPausedFrom()
        {
            yield return NewScreen();

            Time.timeScale = 0.5f;   // e.g. a slow-mo beat
            Screen.Open();
            yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(0f), "open must freeze regardless of the prior speed");

            Screen.Close();
            yield return null;
            Assert.That(Screen.IsOpen, Is.False, "the screen didn't close");
            Assert.That(Time.timeScale, Is.EqualTo(0.5f), "close must restore the speed it paused from, not assume 1");
        }

        [UnityTest]
        public IEnumerator ShowsRcdaAndAllFourTracksAtTheirCurrentLevel()
        {
            yield return NewScreen();
            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range);   // Range -> Lv 2

            Screen.Open();
            yield return null;

            Assert.That(FindText(_screenGo, WeaponCatalog.PrimaryShortName), Is.Not.Null, "RCDA isn't labelled");
            foreach (var kind in WeaponCatalog.AllTrackKinds)
                Assert.That(FindText(_screenGo, WeaponCatalog.DisplayName(kind)), Is.Not.Null,
                    $"{kind} track isn't listed");
            Assert.That(FindText(_screenGo, "Lv 2/6"), Is.Not.Null, "Range's live level (2) isn't shown");
            Assert.That(FindText(_screenGo, "Lv 1/4"), Is.Not.Null, "an unspent track should read Lv 1");
        }

        [UnityTest]
        public IEnumerator AbilitiesSectionShowsOnlyAcquiredAbilities()
        {
            yield return NewScreen();
            WeaponSystemState.Acquire(AbilityKind.Dash);

            Screen.Open();
            yield return null;

            Assert.That(FindText(_screenGo, WeaponCatalog.DisplayName(AbilityKind.Dash)), Is.Not.Null,
                "the acquired ability isn't shown");
            Assert.That(FindText(_screenGo, WeaponCatalog.DisplayName(AbilityKind.Teleport)), Is.Null,
                "an unacquired ability must not be shown at all — no locked teasers");
        }

        [UnityTest]
        public IEnumerator AbilitiesSectionGrowsAsMoreAreAcquired()
        {
            yield return NewScreen();
            Screen.Open();
            yield return null;

            foreach (var kind in WeaponCatalog.AllAbilityKinds)
                Assert.That(FindText(_screenGo, WeaponCatalog.DisplayName(kind)), Is.Null,
                    $"{kind} shouldn't show before Max owns anything");

            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            yield return null;
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            yield return null;

            Assert.That(FindText(_screenGo, WeaponCatalog.DisplayName(AbilityKind.WaterBalloon)), Is.Not.Null);
            Assert.That(FindText(_screenGo, WeaponCatalog.DisplayName(AbilityKind.Teleport)), Is.Not.Null);
            Assert.That(FindText(_screenGo, WeaponCatalog.DisplayName(AbilityKind.Dash)), Is.Null,
                "still-unacquired abilities must stay hidden");
        }

        [UnityTest]
        public IEnumerator SpendingATrackLevelsItUpAndConsumesABankedPart()
        {
            yield return NewScreen();
            PickupWallet.AddPart();
            Screen.Open();
            yield return null;

            FindRowButton(_screenGo, WeaponCatalog.DisplayName(WeaponTrackKind.Capacity)).onClick.Invoke();
            yield return null;

            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Capacity), Is.EqualTo(2),
                "tapping the row's button must raise the track by one level");
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(0), "and spend the banked part");
        }

        [UnityTest]
        public IEnumerator SpendingAnAbilityLevelsItUpAndConsumesABankedPart()
        {
            yield return NewScreen();
            WeaponSystemState.Acquire(AbilityKind.Speed);
            PickupWallet.AddPart();
            Screen.Open();
            yield return null;

            FindRowButton(_screenGo, WeaponCatalog.DisplayName(AbilityKind.Speed)).onClick.Invoke();
            yield return null;

            Assert.That(WeaponSystemState.AbilityLevel(AbilityKind.Speed), Is.EqualTo(2));
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator SpendingWithNoBankedPartsDoesNothing()
        {
            yield return NewScreen();
            Screen.Open();
            yield return null;

            FindRowButton(_screenGo, WeaponCatalog.DisplayName(WeaponTrackKind.Range)).onClick.Invoke();
            yield return null;

            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Range), Is.EqualTo(1),
                "an empty bank must not level anything up");
        }

        [UnityTest]
        public IEnumerator SpendingAtCapDoesNothing()
        {
            yield return NewScreen();
            int cap = WeaponCatalog.MaxLevel(WeaponTrackKind.Spread);
            for (int i = 1; i < cap; i++) WeaponSystemState.LevelUpTrack(WeaponTrackKind.Spread);
            PickupWallet.AddPart();
            Screen.Open();
            yield return null;

            FindRowButton(_screenGo, WeaponCatalog.DisplayName(WeaponTrackKind.Spread)).onClick.Invoke();
            yield return null;

            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Spread), Is.EqualTo(cap),
                "a maxed track must not level past its cap");
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1), "and must not spend the part either");
        }

        [UnityTest]
        public IEnumerator PartsBankedCountIsShownAndUpdatesLiveWhileOpen()
        {
            yield return NewScreen();
            PickupWallet.AddPart();
            Screen.Open();
            yield return null;

            Assert.That(FindText(_screenGo, "PARTS BANKED: 1"), Is.Not.Null);

            PickupWallet.AddPart();   // e.g. a kill drops one while the screen happens to be open
            yield return null;

            Assert.That(FindText(_screenGo, "PARTS BANKED: 2"), Is.Not.Null,
                "the banked count must reflect live state, not just what it was on open");
        }

        [UnityTest]
        public IEnumerator CloseButtonClosesAndResumes()
        {
            yield return NewScreen();
            Screen.Open();
            yield return null;

            FindButtonNamed(_screenGo, "Close Button").onClick.Invoke();
            yield return null;

            Assert.That(Screen.IsOpen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator TappingTheWeaponsButtonOpensIt()
        {
            yield return NewScreen();

            _hudGo = new GameObject("HUD");
            _hudGo.AddComponent<HudController>();
            yield return null;

            var weaponsButton = FindWeaponsButton(_hudGo);
            Assert.That(weaponsButton, Is.Not.Null, "the WEAPONS button has no Button component");

            weaponsButton.onClick.Invoke();
            yield return null;

            Assert.That(Screen.IsOpen, Is.True, "tapping the WEAPONS button should open the weapons area");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "and pause the game");
        }

        // ---------------------------------------------------------------- helpers

        private static Button FindWeaponsButton(GameObject hud)
        {
            foreach (var b in hud.GetComponentsInChildren<Button>(true))
                if (b.transform.parent != null && b.transform.parent.name == "Weapons Button") return b;
            return null;
        }

        private static Button FindButtonNamed(GameObject root, string name)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(true))
                if (b.gameObject.name == name) return b;
            return null;
        }

        /// <summary>Finds the "+" button inside the row whose name label reads <paramref name="rowName"/> —
        /// walks up from the matching Text to its row, then finds that row's Button.</summary>
        private static Button FindRowButton(GameObject root, string rowName)
        {
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                if (t.text != rowName) continue;
                var row = t.transform.parent;
                return row != null ? row.GetComponentInChildren<Button>(true) : null;
            }
            return null;
        }

        private static Text FindText(GameObject root, string content)
        {
            foreach (var t in root.GetComponentsInChildren<Text>(true))
                if (t.text == content) return t;
            return null;
        }
    }
}
