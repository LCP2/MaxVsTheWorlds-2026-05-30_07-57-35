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
    /// until one is picked, and handing off to <see cref="SaveSystem"/> — plus, on every pick, the
    /// (YT-216: now opt-in, default OFF) opening cinematic (YT-155/156). A profile is an identity
    /// plus a personal best, not a paused game, so there is no Continue/resume path any more.
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
        public IEnumerator Play_SeedsTheProfileAndResumesTimeWithoutTheIntro()
        {
            // YT-216: the cinematic is gated OFF by default so a fresh run starts instantly — restart
            // must never wait on the ~24s sequence.
            yield return NewScreen();

            PlayButton(0).onClick.Invoke();
            yield return null;

            Assert.That(Screen.IsOpen, Is.False, "picking a slot should close the Home screen");
            Assert.That(Time.timeScale, Is.EqualTo(1f), "the game must resume once a slot is picked");
            Assert.That(SaveSystem.ActiveSlot, Is.EqualTo(0), "the first PLAY button belongs to slot 0");
            Assert.That(SaveSystem.Load(0).HasData, Is.True, "picking a slot must seed its profile immediately");
            Assert.That(Object.FindFirstObjectByType<IntroCinematic>(), Is.Null,
                "picking a slot must never show the cinematic while it defaults OFF (YT-216)");
        }

        [UnityTest]
        public IEnumerator Play_StillPlaysTheIntroWhenTheFlagIsExplicitlyEnabled()
        {
            // The sequence is parked, not deleted — flipping the authored flag back on must still
            // trigger it exactly as YT-155 built it.
            IntroCinematic.Enabled = true;
            yield return NewScreen();

            PlayButton(0).onClick.Invoke();
            yield return null;

            Assert.That(Object.FindFirstObjectByType<IntroCinematic>(), Is.Not.Null,
                "PLAY is still the intro cinematic's trigger once Enabled is opted back in (YT-155)");
        }

        [UnityTest]
        public IEnumerator Play_OnAnExistingProfile_StartsFreshWithoutResettingItsPersonalBest()
        {
            SaveSystem.Save(1, new SaveSlotData { HasData = true, DisplayName = "DEXTER", PersonalBestNormalized = 0.6f });
            UpgradeState.Install(PartKind.Hydro);
            PickupWallet.AddPowerCell();

            yield return NewScreen();

            PlayButton(1).onClick.Invoke();
            yield return null;

            Assert.That(SaveSystem.ActiveSlot, Is.EqualTo(1));
            Assert.That(SaveSystem.Load(1).PersonalBestNormalized, Is.EqualTo(0.6f).Within(1e-4f),
                "picking an existing profile must never reset its personal best");
            Assert.That(UpgradeState.IsInstalled(PartKind.Hydro), Is.False,
                "a profile carries no mid-run state — every play starts fresh (YT-218)");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0),
                "a profile carries no mid-run state — every play starts fresh (YT-218)");
        }

        [UnityTest]
        public IEnumerator AnExistingProfilesCardShowsItsNameAndPersonalBest()
        {
            SaveSystem.Save(2, new SaveSlotData { HasData = true, DisplayName = "DEXTER", PersonalBestNormalized = 0.82f });

            yield return NewScreen();

            var texts = _screenGo.GetComponentsInChildren<Text>(true);
            Assert.That(texts.Any(t => t.text == "DEXTER — best: 82%"), Is.True,
                "the slot card must read '<name> — best: <pct>%'");
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
    }
}
