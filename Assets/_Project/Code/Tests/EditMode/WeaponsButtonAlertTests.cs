using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
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
        // ---------------------------------------------------------------- the four states (pure)

        [Test]
        public void IdleState_ShowsNoBadges()
        {
            var alert = HudController.ComputeWeaponsButtonAlert(partsToFit: false, moduleCaptured: false);
            Assert.That(alert, Is.EqualTo(HudController.WeaponsButtonAlert.Idle));
            Assert.That(HudController.ShowsPartsBadge(alert), Is.False);
            Assert.That(HudController.ShowsModuleBadge(alert), Is.False);
        }

        [Test]
        public void PartsToFitState_ShowsOnlyThePartsBadge()
        {
            var alert = HudController.ComputeWeaponsButtonAlert(partsToFit: true, moduleCaptured: false);
            Assert.That(alert, Is.EqualTo(HudController.WeaponsButtonAlert.PartsToFit));
            Assert.That(HudController.ShowsPartsBadge(alert), Is.True);
            Assert.That(HudController.ShowsModuleBadge(alert), Is.False);
        }

        [Test]
        public void ModuleCapturedState_ShowsOnlyTheModuleBadge()
        {
            var alert = HudController.ComputeWeaponsButtonAlert(partsToFit: false, moduleCaptured: true);
            Assert.That(alert, Is.EqualTo(HudController.WeaponsButtonAlert.ModuleCaptured));
            Assert.That(HudController.ShowsPartsBadge(alert), Is.False);
            Assert.That(HudController.ShowsModuleBadge(alert), Is.True);
        }

        [Test]
        public void BothState_ShowsBothBadges()
        {
            var alert = HudController.ComputeWeaponsButtonAlert(partsToFit: true, moduleCaptured: true);
            Assert.That(alert, Is.EqualTo(HudController.WeaponsButtonAlert.Both));
            Assert.That(HudController.ShowsPartsBadge(alert), Is.True, "the amber count keeps its corner in 'Both'");
            Assert.That(HudController.ShowsModuleBadge(alert), Is.True);
        }

        // ---------------------------------------------------------------- MV-471: alert follows affordability, not "holding"

        /// <summary>The live bug this ticket fixes: pre-MV-471, <c>CurrentWeaponsButtonAlert</c> flagged
        /// "parts to fit" off <c>ShouldShowPartAlert(partsBanked, ...)</c> alone — a held part with
        /// nowhere to spend it still lit the button. p_dmg is the only node owned at run start; drive it
        /// to its own cap so nothing anywhere on the board is part-spendable, then bank a part.</summary>
        [Test]
        public void HoldingAPartWithNoLegalSpendDoesNotComputeAPartsToFitAlert()
        {
            PickupWallet.Reset();
            PendingMorphingModule.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            try
            {
                while (RigState.CanSpendPart("p_dmg")) RigState.RaiseLevel("p_dmg");
                PickupWallet.AddPart();

                var method = typeof(HudController).GetMethod("CurrentWeaponsButtonAlert", BindingFlags.NonPublic | BindingFlags.Static);
                var alert = (HudController.WeaponsButtonAlert)method.Invoke(null, null);

                Assert.That(alert, Is.EqualTo(HudController.WeaponsButtonAlert.Idle),
                    "a banked part with nothing spendable anywhere on the board must not flag PartsToFit");
            }
            finally
            {
                PickupWallet.Reset();
                PendingMorphingModule.Reset();
                RigState.Reset();
                RigFusionState.Reset();
            }
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
                // MV-471: the old "Parts Badge" (hidden until something was banked, regardless of
                // whether it could buy anything) is gone — replaced by an always-visible RIG-mark
                // part counter. Existence + live text is what this test can pin without a canvas
                // render; the flash/affordability behaviour itself is RigActions' own job.
                var partCounter = FindRect(hudGo, "Rig Part Counter");
                var moduleBadge = FindRect(hudGo, "Module Badge");
                Assert.That(partCounter, Is.Not.Null);
                Assert.That(moduleBadge, Is.Not.Null);
                Assert.That(partCounter.gameObject.activeSelf, Is.True, "the RIG mark's part counter is always visible");
                Assert.That(moduleBadge.gameObject.activeSelf, Is.False, "no module waiting yet");

                var partText = partCounter.GetComponentInChildren<Text>();
                Assert.That(partText.text, Is.EqualTo("0"));

                PickupWallet.AddPart();
                Assert.That(partText.text, Is.EqualTo("1"), "a banked part must raise the live count");
                Assert.That(moduleBadge.gameObject.activeSelf, Is.False);

                PendingMorphingModule.Set(new[] { "s_bal", "e_ff", "m_spd" });
                Assert.That(moduleBadge.gameObject.activeSelf, Is.True, "a banked draft must raise the module badge");
                Assert.That(partText.text, Is.EqualTo("1"), "the part counter is unaffected by the module badge");
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
