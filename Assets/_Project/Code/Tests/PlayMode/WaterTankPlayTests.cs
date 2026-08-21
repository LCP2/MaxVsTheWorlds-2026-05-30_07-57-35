using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Combat;
using MaxWorlds.Player;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// MV-299 (reinstating the tank MV-290 cut): the primary draws from a water tank that depletes
    /// under continuous fire and auto-regenerates once the trigger is released — no cells, no
    /// pickups, no taps involved. The pure math (drain-per-level, the fire gate) is pinned in
    /// <see cref="Tests.EditMode.WeaponCatalogTests"/> and <see cref="Tests.EditMode.WaterBlasterFireGateTests"/>;
    /// this is the live wiring — does firing actually spend the tank, does releasing the trigger
    /// actually let it recover, and does Max's floating gauge (YT-121) actually track it.
    /// </summary>
    public sealed class WaterTankPlayTests
    {
        private GameObject _max;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            UpgradeState.Reset();
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (_max != null) Object.Destroy(_max);
            yield return null;
            UpgradeState.Reset();
            WeaponSystemState.Reset();
            PickupWallet.Reset();
        }

        private WaterBlaster NewBlaster()
        {
            _max = new GameObject("Max");
            _max.tag = "Player";
            _max.AddComponent<CharacterController>();
            _max.AddComponent<WaterBlaster>();
            _max.AddComponent<PlayerController>();
            return _max.GetComponent<WaterBlaster>();
        }

        [UnityTest]
        public IEnumerator FiringContinuously_DrainsTheTank()
        {
            var blaster = NewBlaster();
            yield return null;
            Assert.That(blaster.WaterNormalized, Is.EqualTo(1f).Within(0.001f), "the tank must start full");

            blaster.SetFiring(true);
            yield return new WaitForSeconds(0.8f);

            Assert.That(blaster.WaterNormalized, Is.LessThan(0.95f),
                "continuous fire should have measurably drained the tank — the depletion never fired (MV-299)");
        }

        [UnityTest]
        public IEnumerator ReleasingTheTrigger_LetsTheTankRegenerate()
        {
            var blaster = NewBlaster();
            yield return null;

            blaster.SetFiring(true);
            yield return new WaitForSeconds(0.8f);
            float drained = blaster.WaterNormalized;
            Assert.That(drained, Is.LessThan(1f), "precondition: the tank must have drained");

            blaster.SetFiring(false);
            yield return new WaitForSeconds(0.6f);   // past BlasterTuning.RegenDelay (0.35s)

            Assert.That(blaster.WaterNormalized, Is.GreaterThan(drained),
                "letting go of the trigger should recover the tank on its own — no cells, no pickups, no taps (MV-299)");
        }

        [UnityTest]
        public IEnumerator DepletionRateTrack_SlowsTheDrain()
        {
            var baseline = NewBlaster();
            yield return null;
            baseline.SetFiring(true);
            yield return new WaitForSeconds(0.8f);
            float baselineDrop = 1f - baseline.WaterNormalized;
            Object.Destroy(_max);
            yield return null;

            UpgradeState.Reset();
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            // MV-515: PartSpend.TrySpendOnTrack was deleted (dead — no runtime caller); raise the level
            // directly through the model layer, same as CellSpend.TryUpgradeNode does in production.
            Assert.That(WeaponSystemState.LevelUpTrack(WeaponTrackKind.DepletionRate), Is.True,
                "raising Depletion Rate's level should have succeeded");

            var upgraded = NewBlaster();
            yield return null;
            upgraded.SetFiring(true);
            yield return new WaitForSeconds(0.8f);
            float upgradedDrop = 1f - upgraded.WaterNormalized;

            Assert.That(upgradedDrop, Is.LessThan(baselineDrop),
                "spending on Depletion Rate should have slowed the drain — the track had no effect (MV-299)");
        }

        [UnityTest]
        public IEnumerator MaxsWaterGaugeTracksTheLiveTank()
        {
            _max = new GameObject("Max", typeof(CharacterController), typeof(PlayerController),
                                  typeof(WaterBlaster), typeof(PlayerHealth));
            yield return null;

            var blaster = _max.GetComponent<WaterBlaster>();
            var bar = _max.GetComponent<WorldHealthBar>();
            Assert.That(bar, Is.Not.Null, "PlayerHealth should have attached a WorldHealthBar");
            Assert.That(bar.HasSecondary, Is.True, "Max's bar must carry the water gauge (YT-121)");

            blaster.SetFiring(true);
            yield return new WaitForSeconds(0.8f);
            yield return null;   // let WorldHealthBar's LateUpdate re-read the live tank

            var waterFill = FindImage(_max, "Water Fill");
            Assert.That(waterFill, Is.Not.Null, "no water gauge fill found on Max's bar");
            Assert.That(waterFill.fillAmount, Is.EqualTo(blaster.WaterNormalized).Within(0.02f),
                "the gauge above Max must read the live blaster tank, not a stale/disconnected value");
            Assert.That(waterFill.fillAmount, Is.LessThan(0.95f), "precondition: the tank should have visibly drained");
        }

        private static UnityEngine.UI.Image FindImage(GameObject root, string name)
        {
            foreach (var i in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                if (i.name == name) return i;
            return null;
        }
    }
}
