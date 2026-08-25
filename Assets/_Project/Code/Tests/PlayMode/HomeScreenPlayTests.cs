using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using MaxWorlds.UI;
using MaxWorlds.Save;
using MaxWorlds.Player;
using MaxWorlds.Pickups;
using MaxWorlds.Upgrades;
using MaxWorlds.Intro;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The Home screen (YT-151; profiles per YT-218): three player-profile slots, pausing the game
    /// until one is picked, and handing off to <see cref="SaveSystem"/> — plus, on a derived true
    /// first launch (MV-550: no save slot has ever had data), the opening cinematic (YT-155/156). A
    /// profile is an identity plus a personal best, not a paused game, so there is no Continue/resume
    /// path any more.
    /// </summary>
    public sealed class HomeScreenPlayTests
    {
        private GameObject _screenGo;
        private GameObject _playerGo;
        private GameObject _camGo;
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-home-screen-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            UpgradeState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;

            foreach (var s in Object.FindObjectsByType<HomeScreen>(FindObjectsSortMode.None))
                Object.Destroy(s.gameObject);
            foreach (var i in Object.FindObjectsByType<IntroCinematic>(FindObjectsSortMode.None))
                Object.Destroy(i.gameObject);
            IntroCinematic.ResetForTests();   // YT-216 — the authored default; tests restore it explicitly

            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.AddComponent<PlayerController>();   // RequireComponent brings the CharacterController

            _camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            _camGo.AddComponent<Camera>();   // something for the intro cinematic to take over / hand back to

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (_screenGo != null) Object.Destroy(_screenGo);
            if (_playerGo != null) Object.Destroy(_playerGo);
            if (_camGo != null) Object.Destroy(_camGo);
            var es = Object.FindFirstObjectByType<EventSystem>();
            if (es != null) Object.Destroy(es.gameObject);
            foreach (var i in Object.FindObjectsByType<IntroCinematic>(FindObjectsSortMode.None))
                Object.Destroy(i.gameObject);

            SaveSystem.ResetForTests();
            UpgradeState.Reset();
            PickupWallet.Reset();
            IntroCinematic.ResetForTests();   // never leak the flag/consumed-state into another test
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            yield return null;
        }

        private IEnumerator NewScreen()
        {
            _screenGo = new GameObject("HomeScreen");
            _screenGo.AddComponent<HomeScreen>();
            yield return null;   // Start() opens it
        }

        private HomeScreen Screen => _screenGo.GetComponent<HomeScreen>();

        private Button PlayButton(int slot) =>
            _screenGo.GetComponentsInChildren<Button>(true)
                .Where(b => b.gameObject.name == "PLAY")
                .ElementAt(slot);

        private Button ResetButton(int slot) =>
            _screenGo.GetComponentsInChildren<Button>(true)
                .Where(b => b.gameObject.name == "RESET")
                .ElementAt(slot);

        private Button FindButton(string name) =>
            _screenGo.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(b => b.gameObject.name == name);

        [UnityTest]
        public IEnumerator OnFreshBoot_ItOpensAndPausesWithThreeEmptySlots()
        {
            yield return NewScreen();

            Assert.That(Screen.IsOpen, Is.True, "the Home screen should open on a fresh boot");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "it must pause the game while a slot is undecided");

            var texts = _screenGo.GetComponentsInChildren<Text>(true);
            Assert.That(texts.Count(t => t.text == "Empty"), Is.EqualTo(SaveSystem.SlotCount),
                "an untouched profile should show three empty slots");

            var buttons = _screenGo.GetComponentsInChildren<Button>(true);
            Assert.That(buttons.Count(b => b.gameObject.name == "PLAY"), Is.EqualTo(SaveSystem.SlotCount),
                "every slot needs a PLAY button");
        }

        [UnityTest]
        public IEnumerator Play_OnAReturningProfile_ResumesTimeWithoutTheIntro()
        {
            // MV-550: the intro is now a true-first-launch treatment, derived from SaveSystem's slot
            // state — once ANY slot has data, this device is no longer a first launch, so even a pick
            // on a still-empty slot must never wait on the ~24s sequence.
            SaveSystem.Save(1, new SaveSlotData { HasData = true, DisplayName = "DEXTER" });
            yield return NewScreen();

            PlayButton(0).onClick.Invoke();
            yield return null;

            Assert.That(Screen.IsOpen, Is.False, "picking a slot should close the Home screen");
            Assert.That(Time.timeScale, Is.EqualTo(1f), "the game must resume once a slot is picked");
            Assert.That(SaveSystem.ActiveSlot, Is.EqualTo(0), "the first PLAY button belongs to slot 0");
            Assert.That(SaveSystem.Load(0).HasData, Is.True, "picking a slot must seed its profile immediately");
            Assert.That(Object.FindFirstObjectByType<IntroCinematic>(), Is.Null,
                "a device with existing save data must never show the cinematic (MV-550)");
        }

        [UnityTest]
        public IEnumerator Play_OnATrueFirstLaunch_PlaysTheIntro()
        {
            // MV-550: no slot has ever had data — the derived gate must trigger the cinematic even
            // though IntroCinematic.Enabled is left at its authored-off default.
            yield return NewScreen();

            PlayButton(0).onClick.Invoke();
            yield return null;

            Assert.That(Object.FindFirstObjectByType<IntroCinematic>(), Is.Not.Null,
                "a genuinely first-ever PLAY must trigger the intro cinematic (MV-550)");
        }

        [UnityTest]
        public IEnumerator Play_StillPlaysTheIntroWhenTheFlagIsExplicitlyEnabled()
        {
            // MV-550: IntroCinematic.Enabled stays a manual/test override on top of the derived
            // first-launch gate — seed a slot so the derived gate alone would say "no", and prove
            // Enabled still forces it on regardless.
            SaveSystem.Save(1, new SaveSlotData { HasData = true, DisplayName = "DEXTER" });
            IntroCinematic.Enabled = true;
            yield return NewScreen();

            PlayButton(0).onClick.Invoke();
            yield return null;

            Assert.That(Object.FindFirstObjectByType<IntroCinematic>(), Is.Not.Null,
                "PLAY is still the intro cinematic's trigger once Enabled is opted back in (YT-155), " +
                "even on a device that already has save data");
        }

        [UnityTest]
        public IEnumerator Play_OnAnExistingProfile_StartsFreshWithoutResettingItsPersonalBest()
        {
            SaveSystem.Save(1, new SaveSlotData { HasData = true, DisplayName = "DEXTER", BestDeathsToVictory = 3 });
            UpgradeState.Install(PartKind.Hydro);
            PickupWallet.AddPowerCell();

            yield return NewScreen();

            PlayButton(1).onClick.Invoke();
            yield return null;

            Assert.That(SaveSystem.ActiveSlot, Is.EqualTo(1));
            Assert.That(SaveSystem.Load(1).BestDeathsToVictory, Is.EqualTo(3),
                "picking an existing profile must never reset its personal best");
            Assert.That(UpgradeState.IsInstalled(PartKind.Hydro), Is.False,
                "a profile carries no mid-run state — every play starts fresh (YT-218)");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0),
                "a profile carries no mid-run state — every play starts fresh (YT-218)");
        }

        [UnityTest]
        public IEnumerator AnExistingProfilesCardShowsItsNameAndPersonalBest()
        {
            SaveSystem.Save(2, new SaveSlotData { HasData = true, DisplayName = "DEXTER", BestDeathsToVictory = 2 });

            yield return NewScreen();

            var texts = _screenGo.GetComponentsInChildren<Text>(true);
            Assert.That(texts.Any(t => t.text == "DEXTER — best: 2 deaths"), Is.True,
                "the slot card must read '<name> — best: N deaths'");
        }

        [UnityTest]
        public IEnumerator ActiveSlotAlreadySet_ItNeverOpens()
        {
            SaveSystem.ActiveSlot = 0;   // e.g. a Replay-triggered reload after a slot was already picked

            yield return NewScreen();

            Assert.That(Screen.IsOpen, Is.False, "a live slot means the Home screen must stay out of the way");
            Assert.That(Time.timeScale, Is.EqualTo(1f), "it must not pause a run already in progress");
        }

        [UnityTest]
        public IEnumerator TheCrestIsALiveLowPolyRenderNotTheRejectedPaintedHeadshot()
        {
            // YT-189: the crest must reuse UpgradeScreen's live low-poly Max render (YT-176), not the
            // 2D painted "Art/max_portrait" headshot Lee already rejected once for that screen.
            yield return NewScreen();

            var portrait = _screenGo.GetComponentsInChildren<RawImage>(true)
                .FirstOrDefault(img => img.gameObject.name == "Badge Portrait");
            Assert.That(portrait, Is.Not.Null, "no live-rendered Max crest found");
            Assert.That(portrait.texture, Is.Not.Null, "the crest's render texture never got assigned");
        }

        // ------------------------------------------------------------------ MV-282: per-slot reset

        [UnityTest]
        public IEnumerator AnEmptySlotsResetButtonIsNotInteractable()
        {
            yield return NewScreen();

            Assert.That(ResetButton(0).interactable, Is.False,
                "there is nothing to wipe on a slot that was never played");
        }

        [UnityTest]
        public IEnumerator TappingResetOnAnOccupiedSlotOpensAConfirmDialogWithoutWipingYet()
        {
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER", BestDeathsToVictory = 1 });
            yield return NewScreen();

            Assert.That(ResetButton(0).interactable, Is.True, "an occupied slot must offer Reset");
            ResetButton(0).onClick.Invoke();
            yield return null;

            Assert.That(FindButton("CONFIRM"), Is.Not.Null, "tapping Reset must ask for confirmation first");
            Assert.That(SaveSystem.Load(0).HasData, Is.True, "a bare tap on Reset must not wipe anything yet");
        }

        [UnityTest]
        public IEnumerator CancellingTheResetConfirmLeavesTheSlotUntouched()
        {
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER", BestDeathsToVictory = 1 });
            yield return NewScreen();

            ResetButton(0).onClick.Invoke();
            yield return null;
            FindButton("CANCEL").onClick.Invoke();
            yield return null;

            Assert.That(FindButton("CONFIRM"), Is.Null, "Cancel must close the confirm dialog");
            SaveSlotData data = SaveSystem.Load(0);
            Assert.That(data.HasData, Is.True);
            Assert.That(data.DisplayName, Is.EqualTo("DEXTER"));
            Assert.That(data.BestDeathsToVictory, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ConfirmingTheResetWipesOnlyThatSlotAndRedrawsItAsEmpty()
        {
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER", BestDeathsToVictory = 1 });
            SaveSystem.Save(1, new SaveSlotData { HasData = true, DisplayName = "MAX", BestDeathsToVictory = 4 });
            yield return NewScreen();

            ResetButton(0).onClick.Invoke();
            yield return null;
            FindButton("CONFIRM").onClick.Invoke();
            yield return null;

            Assert.That(SaveSystem.Load(0).HasData, Is.False, "the targeted slot must be wiped");
            Assert.That(SaveSystem.Load(1).HasData, Is.True, "resetting one slot must never touch another");
            Assert.That(SaveSystem.Load(1).DisplayName, Is.EqualTo("MAX"));

            var texts = _screenGo.GetComponentsInChildren<Text>(true);
            Assert.That(texts.Any(t => t.text == "Empty"), Is.True, "a reset slot must redraw as Empty like a fresh slot");
            Assert.That(FindButton("CONFIRM"), Is.Null, "the confirm dialog must close after Confirm");
        }

        [UnityTest]
        public IEnumerator AResetSlotStartsAGenuinelyNewGame()
        {
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER", BestDeathsToVictory = 5 });
            yield return NewScreen();

            ResetButton(0).onClick.Invoke();
            yield return null;
            FindButton("CONFIRM").onClick.Invoke();
            yield return null;

            PlayButton(0).onClick.Invoke();
            yield return null;

            Assert.That(SaveSystem.Load(0).BestDeathsToVictory, Is.EqualTo(-1),
                "a reset slot must start with no carried-over personal best");
        }
    }
}
