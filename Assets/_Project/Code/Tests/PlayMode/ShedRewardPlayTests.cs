using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Sheds are the ability-unlock mechanic now (WV-229, spec §4/§6): destroying one grants one random
    /// ability Max doesn't already own, drawn from the six-ability pool WV-230 backs. Once every ability
    /// is owned, a shed has nothing left to grant and falls back to a part + a bigger power-cell cache.
    /// </summary>
    public sealed class ShedRewardPlayTests
    {
        private GameObject _director;
        private GameObject _max;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Reset in setup too, not just teardown (YT-129/130): a prior test's leftover acquired
            // abilities would silently shrink this test's Unacquired pool.
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            DevTuning.Reset();
            if (_max != null) Object.Destroy(_max);
            if (_director != null) Object.Destroy(_director);
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

        private static int LivePickups(PickupKind kind)
        {
            int n = 0;
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None))
                if (p.gameObject.activeInHierarchy && p.Kind == kind) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator DestroyingAShedDropsExactlyOneDevice_NoPartOrCells()
        {
            yield return NewDirector();

            HudSignals.EmitFactoryDestroyed(new Vector3(5f, 0f, 5f));
            yield return null;

            Assert.That(LivePickups(PickupKind.Device), Is.EqualTo(1),
                "a shed with abilities left to grant must drop exactly one device");
            Assert.That(LivePickups(PickupKind.Part), Is.EqualTo(0),
                "a shed must not drop a part while it still has an ability to grant");
            Assert.That(LivePickups(PickupKind.PowerCell), Is.EqualTo(0),
                "a shed must not drop a cell cache while it still has an ability to grant");
        }

        [UnityTest]
        public IEnumerator TheDroppedDeviceGrantsAnAbilityMaxDidNotAlreadyOwn()
        {
            yield return NewDirector();

            // Own every ability except Teleport — the device this shed drops has exactly one candidate
            // left, so the grant is deterministic instead of a 1-in-N roll.
            foreach (AbilityKind kind in WeaponCatalog.AllAbilityKinds)
                if (kind != AbilityKind.Teleport) WeaponSystemState.Acquire(kind);

            HudSignals.EmitFactoryDestroyed(new Vector3(5f, 0f, 5f));
            yield return null;

            Pickup device = null;
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None))
                if (p.gameObject.activeInHierarchy && p.Kind == PickupKind.Device) device = p;
            Assert.IsNotNull(device, "the shed did not drop a device");
            Assert.AreEqual(AbilityKind.Teleport, device.Ability,
                "the device must carry the one ability Max doesn't already own");
        }

        [UnityTest]
        public IEnumerator WalkingOverTheDeviceGrantsTheAbilityAndRemovesIt()
        {
            yield return NewDirector();
            foreach (AbilityKind kind in WeaponCatalog.AllAbilityKinds)
                if (kind != AbilityKind.Dash) WeaponSystemState.Acquire(kind);

            _max = new GameObject("Max");
            _max.tag = "Player";
            _max.transform.position = new Vector3(20f, 0f, 20f); // far from the drop for now

            HudSignals.EmitFactoryDestroyed(Vector3.zero);
            yield return null;
            Assert.IsFalse(WeaponSystemState.IsAcquired(AbilityKind.Dash),
                "the ability must not be granted while Max is across the yard");

            _max.transform.position = Vector3.zero;   // walk onto the device
            yield return null;   // director's Update does the walk-over check

            Assert.IsTrue(WeaponSystemState.IsAcquired(AbilityKind.Dash),
                "walking onto the device must grant the ability outright — no menu, no button");
            Assert.That(LivePickups(PickupKind.Device), Is.EqualTo(0),
                "a collected device must leave the ground");
        }

        [UnityTest]
        public IEnumerator OnceEveryAbilityIsOwned_AShedDropsAPartAndACellCacheInstead()
        {
            yield return NewDirector();
            foreach (AbilityKind kind in WeaponCatalog.AllAbilityKinds) WeaponSystemState.Acquire(kind);

            HudSignals.EmitFactoryDestroyed(new Vector3(5f, 0f, 5f));
            yield return null;

            Assert.That(LivePickups(PickupKind.Device), Is.EqualTo(0),
                "there is no ability left to grant, so no device should drop");
            Assert.That(LivePickups(PickupKind.Part), Is.EqualTo(1),
                "a fully-unlocked shed must fall back to dropping a part");
            Assert.That(LivePickups(PickupKind.PowerCell), Is.EqualTo(PickupDirector.ShedCellCacheAmount),
                $"a fully-unlocked shed must drop a {PickupDirector.ShedCellCacheAmount}-cell cache");
        }
    }
}
