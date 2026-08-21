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

        // ---------------------------------------------------------------- built widget reacts to real state

        [Test]
        public void TheBuiltButtonBadgesReactToRealBankedState()
        {
            PickupWallet.Reset();
            AbilityCreditBank.Reset();
            PendingMorphingModule.Reset();

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");
            try
            {
                var moduleBadge = FindRect(hudGo, "Module Badge");
                Assert.That(moduleBadge, Is.Not.Null);
                Assert.That(moduleBadge.gameObject.activeSelf, Is.False, "no module waiting yet");

                // MV-519: the old always-on "Rig Part Counter" chip is gone outright (a Supercell grants
                // its cells on pickup now, never banked here) — the built HUD must never carry it.
                Assert.That(FindRect(hudGo, "Rig Part Counter"), Is.Null,
                    "MV-519: the Supercell/parts counter chip must not exist — it was deleted, not merely hidden");

                PendingMorphingModule.Set(new[] { "s_bal", "e_ff", "m_spd" });
                Assert.That(moduleBadge.gameObject.activeSelf, Is.True, "a banked draft must raise the module badge");

                // MV-515 AC6 (still true post-MV-519): no active user-facing Text says Part/Parts —
                // scans the built widget's own rendered strings, not source, so a runtime-composed
                // string can't slip past a source grep the way AC1/AC5's greps could miss.
                foreach (var text in hudGo.GetComponentsInChildren<Text>(true))
                    Assert.That(System.Text.RegularExpressions.Regex.IsMatch(text.text ?? string.Empty, @"\bParts?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase), Is.False,
                        $"HUD Text on '{text.gameObject.name}' still reads \"{text.text}\"");
            }
            finally
            {
                InvokeLifecycle(hud, "OnDisable");
                Object.DestroyImmediate(hudGo);
                PickupWallet.Reset();
                AbilityCreditBank.Reset();
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
