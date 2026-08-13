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
    /// The weapons area (MV-248, MV-262): entering pauses the game, the Primary section shows RCDA's
    /// four tracks as pip bars at their live level, the Abilities section always shows all four slots —
    /// owned ones by name, the rest as greyed unnamed placeholders — and a tap spends one banked part
    /// on any owned track/ability. Row copy is Title Case on screen
    /// (<see cref="WeaponCatalog.TitleCase"/> over <see cref="WeaponCatalog.DisplayName(WeaponTrackKind)"/>
    /// / <see cref="WeaponCatalog.DisplayName(AbilityKind)"/>), which the HUD pickup toast keeps in
    /// ALL CAPS — so every lookup here goes through the same TitleCase call the screen itself uses.
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

        private static string Name(WeaponTrackKind kind) => WeaponCatalog.TitleCase(WeaponCatalog.DisplayName(kind));
        private static string Name(AbilityKind kind) => WeaponCatalog.TitleCase(WeaponCatalog.DisplayName(kind));

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
        public IEnumerator ShowsThePrimaryWeaponNameAndAllTracks()
        {
            yield return NewScreen();

            Screen.Open();
            yield return null;

            Assert.That(FindText(_screenGo, WeaponCatalog.TitleCase(WeaponCatalog.PrimaryName)), Is.Not.Null,
                "the primary weapon's full name isn't shown in the hero column");
            foreach (var kind in WeaponCatalog.AllTrackKinds)
                Assert.That(FindText(_screenGo, Name(kind)), Is.Not.Null, $"{kind} track isn't listed");
        }

        [UnityTest]
        public IEnumerator TrackLevelsRenderAsPipBarsNotText()
        {
            yield return NewScreen();
            WeaponSystemState.LevelUpTrack(WeaponTrackKind.Range);   // Range -> level 2 of 6

            Screen.Open();
            yield return null;

            CountPips(_screenGo, Name(WeaponTrackKind.Range), out int rangeTotal, out int rangeFilled);
            Assert.That(rangeTotal, Is.EqualTo(WeaponCatalog.MaxLevel(WeaponTrackKind.Range)), "Range should show 6 pip segments");
            Assert.That(rangeFilled, Is.EqualTo(2), "Range's live level (2) should show as 2 filled pips");

            CountPips(_screenGo, Name(WeaponTrackKind.Spread), out int spreadTotal, out int spreadFilled);
            Assert.That(spreadTotal, Is.EqualTo(WeaponCatalog.MaxLevel(WeaponTrackKind.Spread)), "Spread should show 6 pip segments (MV-291: unified with Range/Damage)");
            Assert.That(spreadFilled, Is.EqualTo(1), "an unspent track should show a single filled pip");

            Assert.That(FindText(_screenGo, "Lv 2/6"), Is.Null, "levels must render as pips, not \"Lv x/y\" text");
        }

        [UnityTest]
        public IEnumerator AbilitiesSectionShowsOnlyAcquiredAbilities()
        {
            yield return NewScreen();
            WeaponSystemState.Acquire(AbilityKind.Speed);

            Screen.Open();
            yield return null;

            Assert.That(FindText(_screenGo, Name(AbilityKind.Speed)), Is.Not.Null, "the acquired ability isn't shown");
            Assert.That(FindText(_screenGo, Name(AbilityKind.Teleport)), Is.Null,
                "an unacquired ability must not be shown as its own row — no locked teasers");
        }

        [UnityTest]
        public IEnumerator NewlyAcquiredAbilityIconIsHighlightedOnlyTheFirstTimeItsSeen()
        {
            // MV-250: "picking something up gives clear immediate feedback" — an ability's icon lights
            // up the first time the player actually looks at the screen after acquiring it, then blends
            // back in with the rest on every open after that.
            yield return NewScreen();
            WeaponSystemState.Acquire(AbilityKind.Speed);

            Screen.Open();
            yield return null;
            Color firstOpenColor = FindIcon(_screenGo, Name(AbilityKind.Speed)).color;

            Screen.Close();
            yield return null;
            Screen.Open();
            yield return null;
            Color secondOpenColor = FindIcon(_screenGo, Name(AbilityKind.Speed)).color;

            Assert.That(firstOpenColor, Is.Not.EqualTo(secondOpenColor),
                "a newly-acquired ability should look different the first time it's shown, then not repeat");
        }

        [UnityTest]
        public IEnumerator AbilitiesGridAlwaysShowsAllSlotsGreyedUntilOwned()
        {
            // MV-262: the abilities grid is a fixed-slot grid from the start — locked slots are
            // greyed, unnamed placeholder tiles (no name, no pips, no + button), not hidden rows and
            // not a text list naming what's still locked. MV-370: the pool shrank to 3 (Water Balloon
            // left it for the Primary Add-ons section).
            yield return NewScreen();
            Screen.Open();
            yield return null;

            foreach (var kind in WeaponCatalog.AllAbilityKinds)
                Assert.That(FindText(_screenGo, Name(kind)), Is.Null, $"{kind} shouldn't show before Max owns anything");
            Assert.That(CountActiveAbilityRows(_screenGo), Is.EqualTo(3), "all three ability slots should be visible from the start");
            Assert.That(FindText(_screenGo, "ABILITIES — 0 of 3 unlocked"), Is.Not.Null);

            WeaponSystemState.Acquire(AbilityKind.Speed);
            yield return null;
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            yield return null;

            Assert.That(FindText(_screenGo, Name(AbilityKind.Speed)), Is.Not.Null);
            Assert.That(FindText(_screenGo, Name(AbilityKind.Teleport)), Is.Not.Null);
            Assert.That(FindText(_screenGo, Name(AbilityKind.WeaponCooldown)), Is.Null, "still-unacquired abilities must stay unnamed");
            Assert.That(CountActiveAbilityRows(_screenGo), Is.EqualTo(3), "the grid stays at three slots as more are acquired");
            Assert.That(FindText(_screenGo, "ABILITIES — 2 of 3 unlocked"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator AllAbilitiesOwnedShowsNoLockedSlots()
        {
            yield return NewScreen();
            foreach (var kind in WeaponCatalog.AllAbilityKinds) WeaponSystemState.Acquire(kind);

            Screen.Open();
            yield return null;

            Assert.That(FindText(_screenGo, "ABILITIES — 3 of 3 unlocked"), Is.Not.Null);
            foreach (var kind in WeaponCatalog.AllAbilityKinds)
                Assert.That(FindText(_screenGo, Name(kind)), Is.Not.Null, $"{kind} should be named once owned");
        }

        // ---------------------------------------------------------------- MV-370: Primary Add-ons (Water Balloon)

        private static string Name(WaterBalloonTrackKind kind) => WeaponCatalog.TitleCase(WeaponCatalog.DisplayName(kind));

        [UnityTest]
        public IEnumerator ShowsThePrimaryAddOnsSectionWithAllThreeWaterBalloonTracks()
        {
            yield return NewScreen();

            Screen.Open();
            yield return null;

            Assert.That(FindText(_screenGo, "PRIMARY ADD-ONS"), Is.Not.Null, "the Primary Add-ons section header is missing");
            foreach (var kind in WeaponCatalog.AllWaterBalloonTrackKinds)
                Assert.That(FindText(_screenGo, Name(kind)), Is.Not.Null, $"{kind} track isn't listed");
        }

        [UnityTest]
        public IEnumerator SpendingAWaterBalloonTrackLevelsItUpAndConsumesABankedPart()
        {
            yield return NewScreen();
            PickupWallet.AddPart();
            Screen.Open();
            yield return null;

            FindRowButton(_screenGo, Name(WaterBalloonTrackKind.SplashArea)).onClick.Invoke();
            yield return null;

            Assert.That(WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.SplashArea), Is.EqualTo(2),
                "tapping the row's button must raise the track by one level");
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(0), "and spend the banked part");
        }

        [UnityTest]
        public IEnumerator SpendingATrackLevelsItUpAndConsumesABankedPart()
        {
            yield return NewScreen();
            PickupWallet.AddPart();
            Screen.Open();
            yield return null;

            FindRowButton(_screenGo, Name(WeaponTrackKind.Range)).onClick.Invoke();
            yield return null;

            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Range), Is.EqualTo(2),
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

            FindRowButton(_screenGo, Name(AbilityKind.Speed)).onClick.Invoke();
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

            FindRowButton(_screenGo, Name(WeaponTrackKind.Range)).onClick.Invoke();
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

            FindRowButton(_screenGo, Name(WeaponTrackKind.Spread)).onClick.Invoke();
            yield return null;

            Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Spread), Is.EqualTo(cap),
                "a maxed track must not level past its cap");
            Assert.That(PickupWallet.PartsBanked, Is.EqualTo(1), "and must not spend the part either");
        }

        [UnityTest]
        public IEnumerator CellsAndPartsBanksShowLiveValues()
        {
            yield return NewScreen();
            PickupWallet.AddPart();
            PickupWallet.AddPowerCell();
            Screen.Open();
            yield return null;

            // MV-327: the parts chip spells out its unit too now, same as CELLS, so both banks read
            // at a glance rather than one being a bare number.
            Assert.That(FindText(_screenGo, "1 PARTS"), Is.Not.Null);
            Assert.That(FindText(_screenGo, "1 CELLS"), Is.Not.Null);

            PickupWallet.AddPart();   // e.g. a kill drops one while the screen happens to be open
            PickupWallet.AddPowerCell();
            yield return null;

            Assert.That(FindText(_screenGo, "2 PARTS"), Is.Not.Null, "the parts bank must reflect live state, not just what it was on open");
            Assert.That(FindText(_screenGo, "2 CELLS"), Is.Not.Null, "the cells bank must reflect live state too");
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
            var label = FindTextComponent(root, rowName);
            if (label == null) return null;
            var row = label.transform.parent;
            return row != null ? row.GetComponentInChildren<Button>(true) : null;
        }

        /// <summary>Finds the icon slot inside the row whose name label reads <paramref name="rowName"/>.</summary>
        private static Image FindIcon(GameObject root, string rowName)
        {
            var label = FindTextComponent(root, rowName);
            if (label == null) return null;
            var row = label.transform.parent;
            var icon = row != null ? row.Find("Icon") : null;
            return icon != null ? icon.GetComponent<Image>() : null;
        }

        /// <summary>Counts a row's pip segments — active ones (the track/ability's cap) and, among
        /// those, the ones named "Pip Filled" by <c>WeaponsScreen.SetPips</c> (its current level).</summary>
        private static void CountPips(GameObject root, string rowName, out int total, out int filled)
        {
            total = 0; filled = 0;
            var label = FindTextComponent(root, rowName);
            Assert.That(label, Is.Not.Null, $"no row named '{rowName}' found");
            var pipsContainer = label.transform.parent.Find("Pips");
            Assert.That(pipsContainer, Is.Not.Null, $"row '{rowName}' has no Pips container");

            for (int i = 0; i < pipsContainer.childCount; i++)
            {
                var pip = pipsContainer.GetChild(i);
                if (!pip.gameObject.activeSelf) continue;
                total++;
                if (pip.name == "Pip Filled") filled++;
            }
        }

        /// <summary>Counts active "Ability Row" slots — MV-262's fixed 4-slot grid (MV-359) should
        /// always report 4, whether a slot is showing real data or a greyed placeholder.</summary>
        private static int CountActiveAbilityRows(GameObject root)
        {
            int count = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Ability Row" && t.gameObject.activeSelf) count++;
            return count;
        }

        private static Text FindTextComponent(GameObject root, string content)
        {
            foreach (var t in root.GetComponentsInChildren<Text>(true))
                if (t.text == content) return t;
            return null;
        }

        private static Text FindText(GameObject root, string content) => FindTextComponent(root, content);
    }
}
