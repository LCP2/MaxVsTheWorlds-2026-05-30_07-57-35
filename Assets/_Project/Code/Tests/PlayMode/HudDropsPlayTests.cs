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
    /// The HUD reacts to robot drops (YT-131): a banked power cell bumps the counter, and picking up a
    /// part raises the flashing edge chip that drives the upgrade flow (YT-132).
    /// </summary>
    public sealed class HudDropsPlayTests
    {
        private GameObject _hudGo;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PickupWallet.Reset();
            _hudGo = new GameObject("HUD");
            _hudGo.AddComponent<HudController>();
            yield return null;   // Awake builds the canvas and subscribes to the wallet
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_hudGo != null) Object.Destroy(_hudGo);
            yield return null;
            PickupWallet.Reset();
        }

        private T Find<T>(string name) where T : Component
        {
            foreach (var t in _hudGo.GetComponentsInChildren<T>(true))
                if (t.name == name || t.transform.parent != null && t.transform.parent.name == name) return t;
            return null;
        }

        private RectTransform FindRect(string name)
        {
            foreach (var rt in _hudGo.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt;
            return null;
        }

        [UnityTest]
        public IEnumerator TheCounterAndPartChipExist_AndTheChipStartsHidden()
        {
            Assert.That(FindRect("Power Cells"), Is.Not.Null, "the power-cell counter is missing from the HUD");
            var alert = FindRect("Part Alert");
            Assert.That(alert, Is.Not.Null, "the part-alert chip is missing from the HUD");
            Assert.That(alert.gameObject.activeSelf, Is.False, "with no part collected the chip must be hidden");
            yield return null;
        }

        [UnityTest]
        public IEnumerator BankingACellBumpsTheCounter()
        {
            PickupWallet.AddPowerCell();
            PickupWallet.AddPowerCell();
            PickupWallet.AddPowerCell();
            yield return null;

            Text count = null;
            foreach (var t in _hudGo.GetComponentsInChildren<Text>(true))
                if (t.transform.parent != null && t.transform.parent.name == "Power Cells") count = t;

            Assert.That(count, Is.Not.Null, "the counter has no number label");
            Assert.That(count.text, Is.EqualTo("3"), "the HUD counter must track the banked cells");
        }

        [UnityTest]
        public IEnumerator PickingUpAPartRaisesTheFlashingChip()
        {
            var alert = FindRect("Part Alert");

            PickupWallet.AddPart(PartKind.BeamNozzle);
            yield return null;
            Assert.That(alert.gameObject.activeSelf, Is.True, "a collected part must raise the edge chip");

            // And it clears once the upgrade flow spends the part (YT-132 will call SpendPart).
            PickupWallet.SpendPart();
            yield return null;
            Assert.That(alert.gameObject.activeSelf, Is.False, "spending the part must clear the chip");
        }
    }
}
