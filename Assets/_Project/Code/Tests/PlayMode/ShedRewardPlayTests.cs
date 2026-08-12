using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Sheds are the ability-unlock mechanic (WV-229, spec §4/§6), reversed from a random grant to a
    /// player-picked one by MV-357: destroying one with 2-3 unowned abilities left opens
    /// <see cref="UpgradeScreen"/>'s paused draft-pick screen; with exactly 1 left it grants that ability
    /// directly (a one-card screen would be a pointless tap); once every ability is owned it falls back
    /// to a part + a bigger power-cell cache, unchanged from WV-229.
    /// </summary>
    public sealed class ShedRewardPlayTests
    {
        private GameObject _director;
        private GameObject _screenGo;
        private GameObject _max;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Reset in setup too, not just teardown (YT-129/130): a prior test's leftover acquired
            // abilities would silently shrink this test's Unacquired pool.
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
            // The screen self-installs and persists across the run; clear it so each test owns exactly
            // one, same guard NewDirector() applies to PickupDirector below.
            foreach (var s in Object.FindObjectsByType<UpgradeScreen>(FindObjectsSortMode.None))
                Object.Destroy(s.gameObject);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            DevTuning.Reset();
            Time.timeScale = 1f;   // never leave the world frozen for the next test
            if (_max != null) Object.Destroy(_max);
            if (_director != null) Object.Destroy(_director);
            if (_screenGo != null) Object.Destroy(_screenGo);
            yield return null;
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.Destroy(p.gameObject);
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.Destroy(d.gameObject);
            yield return null;
        }

        private IEnumerator NewDirector()
        {
            // A PickupDirector self-installs at PlayMode bootstrap and persists across the run, so it
            // would receive the same signal as our test's director and double every drop (YT-129/130's
            // lesson, same guard RobotDropPlayTests uses).
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.Destroy(d.gameObject);
            yield return null;

            _director = new GameObject("PickupDirector");
            _director.AddComponent<PickupDirector>();
            yield return null;   // OnEnable subscribes to HudSignals.FactoryDestroyed
        }

        private IEnumerator NewScreen()
        {
            _screenGo = new GameObject("UpgradeScreen");
            _screenGo.AddComponent<UpgradeScreen>();
            yield return null;   // Start builds the canvas
        }

        private UpgradeScreen Screen => _screenGo.GetComponent<UpgradeScreen>();

        private static Button FindButtonNamed(GameObject root, string name)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(true))
                if (b.gameObject.name == name) return b;
            return null;
        }

        private static int LivePickups(PickupKind kind)
        {
            int n = 0;
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None))
                if (p.gameObject.activeInHierarchy && p.Kind == kind) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator DestroyingAShedWithThreeUnownedAbilitiesOpensTheChoiceScreen_MV357()
        {
            yield return NewScreen();
            yield return NewDirector();

            HudSignals.EmitFactoryDestroyed(new Vector3(5f, 0f, 5f));
            yield return null;

            Assert.That(Screen.IsOpen, Is.True, "a shed with 3+ abilities left must open the draft-pick screen");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "the fight must pause while the player is choosing");
            Assert.That(LivePickups(PickupKind.Device), Is.EqualTo(0), "the choice screen replaces the walk-over device");
            Assert.That(LivePickups(PickupKind.Part), Is.EqualTo(0));
            Assert.That(LivePickups(PickupKind.PowerCell), Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator TappingACardInTheShedChoiceGrantsExactlyThatAbilityAndResumes_MV357()
        {
            yield return NewScreen();
            yield return NewDirector();

            // Own everything except two, so the shed's draw is exactly this pair — deterministic instead
            // of a 3-of-5 roll.
            foreach (AbilityKind kind in WeaponCatalog.AllAbilityKinds)
                if (kind != AbilityKind.Dash && kind != AbilityKind.Teleport) WeaponSystemState.Acquire(kind);

            HudSignals.EmitFactoryDestroyed(new Vector3(5f, 0f, 5f));
            yield return null;
            Assert.That(Screen.IsOpen, Is.True);

            FindButtonNamed(_screenGo, "Choice Card 0").onClick.Invoke();
            yield return null;

            Assert.That(Screen.IsOpen, Is.False, "choosing a card must close the screen");
            Assert.That(Time.timeScale, Is.EqualTo(1f), "and resume the fight");
            bool exactlyOneGranted = WeaponSystemState.IsAcquired(AbilityKind.Dash) ^ WeaponSystemState.IsAcquired(AbilityKind.Teleport);
            Assert.That(exactlyOneGranted, Is.True, "tapping a card must grant exactly the ability that card showed, not both");
        }

        [UnityTest]
        public IEnumerator UnpickedCandidatesStayInThePoolForALaterShed_MV357()
        {
            yield return NewScreen();
            yield return NewDirector();

            foreach (AbilityKind kind in WeaponCatalog.AllAbilityKinds)
                if (kind != AbilityKind.Dash && kind != AbilityKind.Teleport) WeaponSystemState.Acquire(kind);

            HudSignals.EmitFactoryDestroyed(new Vector3(5f, 0f, 5f));
            yield return null;
            FindButtonNamed(_screenGo, "Choice Card 0").onClick.Invoke();
            yield return null;

            // Exactly one of the pair got granted; the other must still be sitting in Unacquired for a
            // later shed to offer again — nothing is ever lost, and nothing is ever granted twice.
            int stillUnacquired = 0;
            foreach (var kind in new[] { AbilityKind.Dash, AbilityKind.Teleport })
                if (!WeaponSystemState.IsAcquired(kind)) stillUnacquired++;
            Assert.That(stillUnacquired, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DestroyingAShedWithExactlyOneUnownedAbilityGrantsItDirectly_MV357()
        {
            yield return NewScreen();
            yield return NewDirector();

            // Own every ability except Teleport — the shed's draw has exactly one candidate left, so the
            // grant is deterministic instead of a choice.
            foreach (AbilityKind kind in WeaponCatalog.AllAbilityKinds)
                if (kind != AbilityKind.Teleport) WeaponSystemState.Acquire(kind);

            HudSignals.EmitFactoryDestroyed(new Vector3(5f, 0f, 5f));
            yield return null;

            Assert.That(WeaponSystemState.IsAcquired(AbilityKind.Teleport), Is.True,
                "with only one ability left, the shed must grant it outright — a one-card screen is a pointless tap");
            Assert.That(Screen.IsOpen, Is.False, "a single candidate must not open the choice screen");
            Assert.That(LivePickups(PickupKind.Device), Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator OnceEveryAbilityIsOwned_AShedDropsAPartAndACellCacheInstead()
        {
            yield return NewScreen();
            yield return NewDirector();
            foreach (AbilityKind kind in WeaponCatalog.AllAbilityKinds) WeaponSystemState.Acquire(kind);

            HudSignals.EmitFactoryDestroyed(new Vector3(5f, 0f, 5f));
            yield return null;

            Assert.That(Screen.IsOpen, Is.False, "there is no ability left to grant, so no choice screen should open");
            Assert.That(LivePickups(PickupKind.Part), Is.EqualTo(1),
                "a fully-unlocked shed must fall back to dropping a part");
            Assert.That(LivePickups(PickupKind.PowerCell), Is.EqualTo(PickupDirector.ShedCellCacheAmount),
                $"a fully-unlocked shed must drop a {PickupDirector.ShedCellCacheAmount}-cell cache");
        }
    }
}
