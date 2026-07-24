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
    /// The Home screen (YT-151): three save slots, pausing the game until one is picked, and handing
    /// off to <see cref="SaveSystem"/> — plus, on New Game only, the opening cinematic (YT-155/156).
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

        [UnityTest]
        public IEnumerator OnFreshBoot_ItOpensAndPausesWithThreeEmptySlots()
        {
            yield return NewScreen();

            Assert.That(Screen.IsOpen, Is.True, "the Home screen should open on a fresh boot");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "it must pause the game while a slot is undecided");

            var texts = _screenGo.GetComponentsInChildren<Text>(true);
            Assert.That(texts.Count(t => t.text == "Empty"), Is.EqualTo(SaveSystem.SlotCount),
                "an untouched save should show three empty slots");

            var buttons = _screenGo.GetComponentsInChildren<Button>(true);
            Assert.That(buttons.Count(b => b.gameObject.name == "NEW GAME"), Is.EqualTo(SaveSystem.SlotCount),
                "every empty slot needs a New Game button");
        }

        [UnityTest]
        public IEnumerator NewGame_SeedsTheSlotResumesTimeAndPlaysTheIntro()
        {
            yield return NewScreen();

            Button newGame = _screenGo.GetComponentsInChildren<Button>(true)
                .First(b => b.gameObject.name == "NEW GAME");
            newGame.onClick.Invoke();
            yield return null;

            Assert.That(Screen.IsOpen, Is.False, "picking a slot should close the Home screen");
            Assert.That(Time.timeScale, Is.EqualTo(1f), "the game must resume once a slot is picked");
            Assert.That(SaveSystem.ActiveSlot, Is.EqualTo(0), "the first New Game button belongs to slot 0");
            Assert.That(SaveSystem.Load(0).HasData, Is.True, "New Game must seed the slot immediately");
            Assert.That(Object.FindFirstObjectByType<IntroCinematic>(), Is.Not.Null,
                "New Game is the intro cinematic's trigger (YT-155)");
        }

        [UnityTest]
        public IEnumerator Continue_RestoresPositionUpgradesAndWallet_WithoutTheIntro()
        {
            UpgradeState.Install(PartKind.Hydro);
            PickupWallet.AddPowerCell();
            SaveSlotData saved = SaveSystem.Capture(new Vector3(40f, 0f, -6f), 90f, SaveSystem.DefaultLevelId);
            SaveSystem.Save(1, saved);
            UpgradeState.Reset();
            PickupWallet.Reset();

            yield return NewScreen();

            Button continueBtn = _screenGo.GetComponentsInChildren<Button>(true)
                .First(b => b.gameObject.name == "CONTINUE");
            continueBtn.onClick.Invoke();
            yield return null;

            Assert.That(SaveSystem.ActiveSlot, Is.EqualTo(1));
            Assert.That(UpgradeState.IsInstalled(PartKind.Hydro), Is.True, "Continue must restore installed parts");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(1), "Continue must restore the banked cells");

            Vector3 pos = _playerGo.transform.position;
            Assert.That(pos.x, Is.EqualTo(40f).Within(1e-3f), "Continue must drop Max exactly where the save left him");
            Assert.That(pos.z, Is.EqualTo(-6f).Within(1e-3f));

            Assert.That(Object.FindFirstObjectByType<IntroCinematic>(), Is.Null,
                "Continue must never replay the intro (YT-155)");
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
