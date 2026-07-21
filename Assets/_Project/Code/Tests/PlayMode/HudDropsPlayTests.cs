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
        public IEnumerator TheCounterWearsTheBatteryIcon_NotAGenericDisc()
        {
            // YT-134 — the counter must show the purpose-built power-cell sprite so it reads as a
            // battery, not "a cyan dot". WeaponHudIcons.PowerCell names its sprite "powercell".
            Image icon = null;
            foreach (var img in _hudGo.GetComponentsInChildren<Image>(true))
                if (img.name == "Cell Icon") icon = img;

            Assert.That(icon, Is.Not.Null, "the power-cell counter has no icon");
            Assert.That(icon.sprite, Is.Not.Null, "the counter icon draws no sprite");
            Assert.That(icon.sprite.name, Is.EqualTo("powercell"),
                "the counter must wear the battery icon, not a generic disc");
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

        /// <summary>
        /// YT-147: the raised chip the player actually sees must be the shared collectible orange and it
        /// must FLASH — not the old static gold. Drives the REAL widget (not just the pure pulse
        /// function), so a correct function left un-wired to the chip would fail here.
        /// </summary>
        [UnityTest]
        public IEnumerator TheRaisedChipIsTheCollectibleOrange_AndFlashes()
        {
            PickupWallet.AddPart(PartKind.BeamNozzle);
            yield return null;

            Image chip = null;
            foreach (var img in _hudGo.GetComponentsInChildren<Image>(true))
                if (img.name == "Chip") chip = img;
            Assert.That(chip, Is.Not.Null, "the raised chip has no 'Chip' background image");

            // Hue matches the on-ground pickup glow — a relationship, invariant under the flash
            // brightness, so it survives an art retune of the collectible orange.
            var glow = MaxWorlds.VFX.PickupArtDirector.CollectibleGlow;
            Color c = chip.color;
            Assert.That(c.g / c.r, Is.EqualTo(glow.g / glow.r).Within(0.05f),
                "the chip's hue drifted from the on-ground pickup glow");
            Assert.That(c.g, Is.LessThan(c.r * 0.65f), $"the chip reads yellow (g/r={c.g / c.r:0.00}), not orange");
            Assert.That(c.b, Is.LessThan(0.35f), "too blue to be the warm collectible orange");

            // It FLASHES: the real widget's brightness swings over time — not a static badge.
            float min = float.MaxValue, max = float.MinValue;
            float end = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < end)
            {
                Color cc = chip.color;
                float b = (cc.r + cc.g + cc.b) * cc.a;
                min = Mathf.Min(min, b); max = Mathf.Max(max, b);
                yield return null;
            }
            Assert.That(max - min, Is.GreaterThan(0.3f),
                "the raised chip barely changes brightness over half a second — it is not flashing");
        }
    }
}
