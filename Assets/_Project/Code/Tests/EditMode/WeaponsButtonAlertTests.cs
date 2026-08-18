using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// THE RIG 4/5 (MV-425) — the WEAPONS button's tap-target fix and its four-state alert (idle,
    /// parts-to-fit, module-captured, both). The state/colour/badge logic is pure (no canvas needed);
    /// the size assertion builds the real HUD once, the same reflection-driven Awake/OnEnable pattern
    /// <c>MorphingModuleDraftTests</c> already uses for a HudController outside Play mode.
    /// </summary>
    public sealed class WeaponsButtonAlertTests
    {
        // ---------------------------------------------------------------- AC1: the live 44pt bug

        private const float Scale6Inch = 0.44f; // SettingsPanel.Scale6Inch — 932x430pt target's matchWidthOrHeight scale

        [Test]
        public void DocumentsTheOldNinetySixPxFootprintFailedTheFortyFourPointFloor()
        {
            float pt = 96f * Scale6Inch;
            Assert.That(pt, Is.LessThan(44f),
                $"MV-425's own live bug: the pre-fix 96px footprint reads {pt:0.0}pt, under Apple's 44pt minimum tap target");
        }

        [Test]
        public void TheWeaponsButtonIsOneOhEightPx_AndClearsTheFortyFourPointFloor()
        {
            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            try
            {
                var button = FindRect(hudGo, "Weapons Button");
                Assert.That(button, Is.Not.Null, "the HUD must build a 'Weapons Button' root");
                Assert.That(button.sizeDelta, Is.EqualTo(new Vector2(108f, 108f)),
                    "MV-425 AC1: the button's footprint must be 108x108");

                float pt = button.sizeDelta.x * Scale6Inch;
                Assert.That(pt, Is.GreaterThanOrEqualTo(44f),
                    $"108px should read {pt:0.0}pt, clearing Apple's 44pt minimum");
            }
            finally
            {
                Object.DestroyImmediate(hudGo);
            }
        }

        // ---------------------------------------------------------------- the four states (pure)

        [Test]
        public void IdleState_NoBadges_NeitherAmberNorModuleRing()
        {
            var alert = HudController.ComputeWeaponsButtonAlert(partsToFit: false, moduleCaptured: false);
            Assert.That(alert, Is.EqualTo(HudController.WeaponsButtonAlert.Idle));
            Assert.That(HudController.ShowsPartsBadge(alert), Is.False);
            Assert.That(HudController.ShowsModuleBadge(alert), Is.False);

            Color ring = HudController.WeaponsButtonRingColor(alert);
            Assert.That(RoughlyEqual(ring, PickupArtDirector.CollectibleGlow), Is.False, "idle ring must not read amber");
            Assert.That(RoughlyEqual(ring, RigBoardLayout.Colour("module")), Is.False, "idle ring must not read module cyan");
        }

        [Test]
        public void PartsToFitState_AmberRing_OnlyThePartsBadge()
        {
            var alert = HudController.ComputeWeaponsButtonAlert(partsToFit: true, moduleCaptured: false);
            Assert.That(alert, Is.EqualTo(HudController.WeaponsButtonAlert.PartsToFit));
            Assert.That(HudController.ShowsPartsBadge(alert), Is.True);
            Assert.That(HudController.ShowsModuleBadge(alert), Is.False);
            Assert.That(RoughlyEqual(HudController.WeaponsButtonRingColor(alert), PickupArtDirector.CollectibleGlow), Is.True,
                "'parts to fit' must ring amber (the shared collectible orange)");
        }

        [Test]
        public void ModuleCapturedState_CyanRing_OnlyTheModuleBadge()
        {
            var alert = HudController.ComputeWeaponsButtonAlert(partsToFit: false, moduleCaptured: true);
            Assert.That(alert, Is.EqualTo(HudController.WeaponsButtonAlert.ModuleCaptured));
            Assert.That(HudController.ShowsPartsBadge(alert), Is.False);
            Assert.That(HudController.ShowsModuleBadge(alert), Is.True);
            Assert.That(RoughlyEqual(HudController.WeaponsButtonRingColor(alert), RigBoardLayout.Colour("module")), Is.True,
                "'module captured' must ring the board's own module cyan");
        }

        [Test]
        public void BothState_CyanWinsTheRing_ButTheAmberCountKeepsItsCorner()
        {
            var alert = HudController.ComputeWeaponsButtonAlert(partsToFit: true, moduleCaptured: true);
            Assert.That(alert, Is.EqualTo(HudController.WeaponsButtonAlert.Both));
            Assert.That(HudController.ShowsPartsBadge(alert), Is.True, "the amber count keeps its corner in 'Both'");
            Assert.That(HudController.ShowsModuleBadge(alert), Is.True);
            Assert.That(RoughlyEqual(HudController.WeaponsButtonRingColor(alert), RigBoardLayout.Colour("module")), Is.True,
                "cyan always wins the ring, even with parts also waiting");
            Assert.That(RoughlyEqual(HudController.WeaponsButtonRingColor(alert), PickupArtDirector.CollectibleGlow), Is.False,
                "amber must not also claim the ring in 'Both' — they never share a colour");
        }

        // ---------------------------------------------------------------- built widget reacts to real state

        [Test]
        public void TheBuiltButtonBadgesReactToRealBankedState()
        {
            PickupWallet.Reset();
            PendingMorphingModule.Reset();

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");
            try
            {
                var partsBadge = FindRect(hudGo, "Parts Badge");
                var moduleBadge = FindRect(hudGo, "Module Badge");
                Assert.That(partsBadge, Is.Not.Null);
                Assert.That(moduleBadge, Is.Not.Null);
                Assert.That(partsBadge.gameObject.activeSelf, Is.False, "nothing banked yet");
                Assert.That(moduleBadge.gameObject.activeSelf, Is.False, "no module waiting yet");

                PickupWallet.AddPart();
                Assert.That(partsBadge.gameObject.activeSelf, Is.True, "a banked part must raise the parts badge");
                Assert.That(moduleBadge.gameObject.activeSelf, Is.False);

                PendingMorphingModule.Set(new[] { "s_bal", "e_ff", "m_spd" });
                Assert.That(moduleBadge.gameObject.activeSelf, Is.True, "a banked draft must raise the module badge");
                Assert.That(partsBadge.gameObject.activeSelf, Is.True, "the parts badge must survive into 'Both'");
            }
            finally
            {
                InvokeLifecycle(hud, "OnDisable");
                Object.DestroyImmediate(hudGo);
                PickupWallet.Reset();
                PendingMorphingModule.Reset();
            }
        }

        // ---------------------------------------------------------------- helpers

        private static bool RoughlyEqual(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        private static RectTransform FindRect(GameObject go, string name)
        {
            foreach (var rt in go.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt;
            return null;
        }

        private static void InvokeLifecycle(Object component, string methodName)
        {
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        }
    }
}
