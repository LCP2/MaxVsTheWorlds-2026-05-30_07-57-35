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
    /// Sheds are the ability-unlock mechanic (WV-229, spec §4/§6; draft-pick MV-357). MV-358 moved the
    /// draft-pick off the mid-fight modal it used to open the instant a shed died: destroying a shed
    /// with any ability still unowned dropped an <see cref="AbilityCreditBank"/> credit — no pause, no
    /// screen, the fight is never interrupted. MV-382 reinstated the visible walk-over collectible
    /// MV-357/358 had reduced to an instant invisible grant: the shed now drops a real
    /// <see cref="PickupKind.Device"/> pickup, and the credit only banks once Max walks over it — same
    /// walk-over idiom as every other drop. The player later spends the banked credit from the Abilities
    /// screen's BUILD ABILITY button, which is what actually draws candidates and opens the paused
    /// choice screen (2-3 left) or grants directly (exactly 1 left). Once every ability is owned, a shed
    /// still falls back to a part + a bigger power-cell cache, unchanged from WV-229.
    /// </summary>
    public sealed class ShedRewardPlayTests
    {
        private GameObject _director;
        private GameObject _upgradeScreenGo;
        private GameObject _weaponsScreenGo;
        private GameObject _max;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Reset in setup too, not just teardown (YT-129/130): a prior test's leftover acquired
            // abilities would silently shrink this test's Unacquired pool.
            WeaponSystemState.Reset();
            AbilityCreditBank.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
            // Both screens self-install and persist across the run; clear them so each test owns exactly
            // one, same guard NewDirector() applies to PickupDirector below.
            foreach (var s in Object.FindObjectsByType<UpgradeScreen>(FindObjectsSortMode.None))
                Object.Destroy(s.gameObject);
            foreach (var s in Object.FindObjectsByType<WeaponsScreen>(FindObjectsSortMode.None))
                Object.Destroy(s.gameObject);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            WeaponSystemState.Reset();
            AbilityCreditBank.Reset();
            PickupWallet.Reset();
            DevTuning.Reset();
            Time.timeScale = 1f;   // never leave the world frozen for the next test
            if (_director != null) Object.Destroy(_director);
            if (_upgradeScreenGo != null) Object.Destroy(_upgradeScreenGo);
            if (_weaponsScreenGo != null) Object.Destroy(_weaponsScreenGo);
            if (_max != null) Object.Destroy(_max);
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

        private IEnumerator NewUpgradeScreen()
        {
            _upgradeScreenGo = new GameObject("UpgradeScreen");
            _upgradeScreenGo.AddComponent<UpgradeScreen>();
            yield return null;   // Start builds the canvas
        }

        private IEnumerator NewWeaponsScreen()
        {
            _weaponsScreenGo = new GameObject("WeaponsScreen");
            _weaponsScreenGo.AddComponent<WeaponsScreen>();
            yield return null;   // Start builds the canvas
        }

        private UpgradeScreen UpgradeScr => _upgradeScreenGo.GetComponent<UpgradeScreen>();
        private WeaponsScreen WeaponsScr => _weaponsScreenGo.GetComponent<WeaponsScreen>();

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

        /// <summary>MV-382: the shed's device only banks a credit once Max walks onto it — same
        /// walk-over idiom as <c>RobotDropPlayTests.WalkingOverACellBanksItAndRemovesIt</c>. Starts Max
        /// far away so the drop itself is never collected as a side effect, then walks him onto
        /// <paramref name="pos"/> and lets the director's Update tick the collection.</summary>
        private IEnumerator WalkMaxOntoTheDevice(Vector3 pos)
        {
            _max = new GameObject("Max");
            _max.tag = "Player";
            _max.transform.position = pos + new Vector3(50f, 0f, 50f);
            yield return null;

            _max.transform.position = pos;
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyingAShedWithAnyUnownedAbilityDropsAVisibleDeviceAndNeverPausesOrOpensAScreen_MV382()
        {
            yield return NewUpgradeScreen();
            yield return NewWeaponsScreen();
            yield return NewDirector();

            HudSignals.EmitFactoryDestroyed(new Vector3(5f, 0f, 5f));
            yield return null;

            // MV-382: the credit no longer banks the instant the shed dies — a real, visible device
            // pickup drops instead, and the credit only banks once Max walks over it (see the next test).
            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(0), "the credit must not bank until the device is collected");
            Assert.That(Time.timeScale, Is.EqualTo(1f), "dropping a shed's device must never pause the game");
            Assert.That(UpgradeScr.IsOpen, Is.False, "dropping a shed's device must never open a screen");
            Assert.That(WeaponsScr.IsOpen, Is.False, "dropping a shed's device must never open a screen");
            Assert.That(LivePickups(PickupKind.Device), Is.EqualTo(1), "a shed with abilities left must drop a visible device pickup");
            Assert.That(LivePickups(PickupKind.Part), Is.EqualTo(0));
            Assert.That(LivePickups(PickupKind.PowerCell), Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator WalkingOverTheShedsDeviceBanksACreditAndNeverPausesOrOpensAScreen_MV382()
        {
            yield return NewUpgradeScreen();
            yield return NewWeaponsScreen();
            yield return NewDirector();

            var dropPos = new Vector3(5f, 0f, 5f);
            HudSignals.EmitFactoryDestroyed(dropPos);
            yield return null;

            yield return WalkMaxOntoTheDevice(dropPos);

            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(1), "walking onto the device must bank exactly one credit");
            Assert.That(LivePickups(PickupKind.Device), Is.EqualTo(0), "the collected device must leave the ground");
            Assert.That(Time.timeScale, Is.EqualTo(1f), "collecting a shed's device must never pause the game");
            Assert.That(UpgradeScr.IsOpen, Is.False, "collecting a shed's device must never open a screen");
            Assert.That(WeaponsScr.IsOpen, Is.False, "collecting a shed's device must never open a screen");
        }

        [UnityTest]
        public IEnumerator TheAbilitiesButtonFlashBadgeMirrorsBankedCredits_MV358()
        {
            yield return NewDirector();

            Assert.That(HudController.ShouldShowPartAlert(PickupWallet.PartsBanked, AbilityCreditBank.Banked), Is.False);

            var dropPos = new Vector3(5f, 0f, 5f);
            HudSignals.EmitFactoryDestroyed(dropPos);
            yield return null;
            yield return WalkMaxOntoTheDevice(dropPos);

            Assert.That(HudController.ShouldShowPartAlert(PickupWallet.PartsBanked, AbilityCreditBank.Banked), Is.True,
                "a banked ability credit must flash the same badge a banked part does");
        }

        [UnityTest]
        public IEnumerator BuildAbilityWithThreeCandidatesBankedOpensTheChoiceScreenOnTopOfTheAbilitiesScreen_MV358()
        {
            yield return NewUpgradeScreen();
            yield return NewWeaponsScreen();
            yield return NewDirector();

            var dropPos = new Vector3(5f, 0f, 5f);
            HudSignals.EmitFactoryDestroyed(dropPos);   // drops a device; a full pool draws AbilityDraft.MaxCandidates (3) once banked
            yield return null;
            yield return WalkMaxOntoTheDevice(dropPos);   // banks the 1 credit

            WeaponsScr.Open();
            yield return null;
            Assert.That(FindButtonNamed(_weaponsScreenGo, "Build Ability Button"), Is.Not.Null,
                "the BUILD ABILITY button must be present while a credit is banked");

            FindButtonNamed(_weaponsScreenGo, "Build Ability Button").onClick.Invoke();
            yield return null;

            Assert.That(UpgradeScr.IsOpen, Is.True, "3 unowned abilities must open the draft-pick choice screen");
            Assert.That(WeaponsScr.IsOpen, Is.True, "the choice screen must layer on top, not replace, the Abilities screen");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "the fight must stay paused while choosing");
        }

        [UnityTest]
        public IEnumerator TappingACardGrantsThatAbilitySpendsTheCreditAndReturnsToTheAbilitiesScreen_MV358()
        {
            yield return NewUpgradeScreen();
            yield return NewWeaponsScreen();
            yield return NewDirector();

            // Own everything except two, so the draw is exactly this pair — deterministic instead of a
            // roll across whatever's left.
            foreach (AbilityKind kind in WeaponCatalog.AllAbilityKinds)
                if (kind != AbilityKind.WeaponCooldown && kind != AbilityKind.Teleport) WeaponSystemState.Acquire(kind);

            var dropPos = new Vector3(5f, 0f, 5f);
            HudSignals.EmitFactoryDestroyed(dropPos);
            yield return null;
            yield return WalkMaxOntoTheDevice(dropPos);

            WeaponsScr.Open();
            yield return null;
            FindButtonNamed(_weaponsScreenGo, "Build Ability Button").onClick.Invoke();
            yield return null;
            Assert.That(UpgradeScr.IsOpen, Is.True);

            FindButtonNamed(_upgradeScreenGo, "Choice Card 0").onClick.Invoke();
            yield return null;

            Assert.That(UpgradeScr.IsOpen, Is.False, "choosing a card must close the choice screen");
            Assert.That(WeaponsScr.IsOpen, Is.True, "and return to the still-open Abilities screen");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "the Abilities screen underneath is still paused");
            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(0), "choosing a card must spend the credit that paid for the draw");

            bool exactlyOneGranted = WeaponSystemState.IsAcquired(AbilityKind.WeaponCooldown) ^ WeaponSystemState.IsAcquired(AbilityKind.Teleport);
            Assert.That(exactlyOneGranted, Is.True, "tapping a card must grant exactly the ability that card showed, not both");
        }

        [UnityTest]
        public IEnumerator UnpickedCandidatesStayInThePoolForALaterBuild_MV358()
        {
            yield return NewUpgradeScreen();
            yield return NewWeaponsScreen();
            yield return NewDirector();

            foreach (AbilityKind kind in WeaponCatalog.AllAbilityKinds)
                if (kind != AbilityKind.WeaponCooldown && kind != AbilityKind.Teleport) WeaponSystemState.Acquire(kind);

            var dropPos = new Vector3(5f, 0f, 5f);
            HudSignals.EmitFactoryDestroyed(dropPos);
            yield return null;
            yield return WalkMaxOntoTheDevice(dropPos);
            WeaponsScr.Open();
            yield return null;
            FindButtonNamed(_weaponsScreenGo, "Build Ability Button").onClick.Invoke();
            yield return null;
            FindButtonNamed(_upgradeScreenGo, "Choice Card 0").onClick.Invoke();
            yield return null;

            // Exactly one of the pair got granted; the other must still be sitting in Unacquired for a
            // later build to offer again — nothing is ever lost, and nothing is ever granted twice.
            int stillUnacquired = 0;
            foreach (var kind in new[] { AbilityKind.WeaponCooldown, AbilityKind.Teleport })
                if (!WeaponSystemState.IsAcquired(kind)) stillUnacquired++;
            Assert.That(stillUnacquired, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator BuildAbilityWithExactlyOneCandidateGrantsItDirectlyWithoutOpeningTheChoiceScreen_MV358()
        {
            yield return NewUpgradeScreen();
            yield return NewWeaponsScreen();
            yield return NewDirector();

            // Own every ability except Teleport — the build's draw has exactly one candidate left, so the
            // grant is deterministic instead of a choice.
            foreach (AbilityKind kind in WeaponCatalog.AllAbilityKinds)
                if (kind != AbilityKind.Teleport) WeaponSystemState.Acquire(kind);

            var dropPos = new Vector3(5f, 0f, 5f);
            HudSignals.EmitFactoryDestroyed(dropPos);
            yield return null;
            yield return WalkMaxOntoTheDevice(dropPos);
            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(1));

            WeaponsScr.Open();
            yield return null;
            FindButtonNamed(_weaponsScreenGo, "Build Ability Button").onClick.Invoke();
            yield return null;

            Assert.That(WeaponSystemState.IsAcquired(AbilityKind.Teleport), Is.True,
                "with only one ability left, BUILD ABILITY must grant it outright — a one-card screen is a pointless tap");
            Assert.That(UpgradeScr.IsOpen, Is.False, "a single candidate must not open the choice screen");
            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(0), "the credit must still be spent");
        }

        [UnityTest]
        public IEnumerator OnceEveryAbilityIsOwned_AShedDropsAPartAndACellCacheInsteadAndBanksNoCredit()
        {
            yield return NewUpgradeScreen();
            yield return NewWeaponsScreen();
            yield return NewDirector();
            foreach (AbilityKind kind in WeaponCatalog.AllAbilityKinds) WeaponSystemState.Acquire(kind);

            HudSignals.EmitFactoryDestroyed(new Vector3(5f, 0f, 5f));
            yield return null;

            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(0), "there is no ability left to build, so no credit should bank");
            Assert.That(UpgradeScr.IsOpen, Is.False, "there is no ability left to grant, so no choice screen should open");
            Assert.That(LivePickups(PickupKind.Part), Is.EqualTo(1),
                "a fully-unlocked shed must fall back to dropping a part");
            Assert.That(LivePickups(PickupKind.PowerCell), Is.EqualTo(PickupDirector.ShedCellCacheAmount),
                $"a fully-unlocked shed must drop a {PickupDirector.ShedCellCacheAmount}-cell cache");
        }
    }
}
