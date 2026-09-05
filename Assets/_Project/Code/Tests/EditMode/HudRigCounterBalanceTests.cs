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
    /// MV-510 review round 1 (Lee, 2026-08-21) — added acceptance criteria A1-A3. Round 1 shipped a
    /// cell pill that was the loudest element on the HUD: its background swapped to a saturated
    /// CellColor slab while actionable, wider (220) than the RIG hexagon above it (216). A2/A3 below are
    /// what's left of that round's coverage. MV-519 removed the parts chip A1 used to compare the pill
    /// against (the peer counter these numbers were "balanced" relative to) — a lone counter has no peer
    /// to balance against, so A1 is gone with it, not adapted.
    /// </summary>
    public sealed class HudRigCounterBalanceTests
    {
        [Test]
        public void CellPillStaysRecessiveWithinHexWidth()
        {
            PickupWallet.Reset();
            RigState.Reset();
            RigFusionState.Reset();

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");
            try
            {
                // Bank a full reserve so at least one RIG node is affordable (Capacity always clears
                // CellSpend.UnlockCostCells against a fresh, all-locked board) — this drives the MV-471
                // actionable flash, the state A2 must hold through.
                PickupWallet.SetPowerCells(PickupWallet.Capacity);
                Assert.That(RigActions.AnyCellActionAffordable(PickupWallet.PowerCells), Is.True,
                    "fixture assumption: a full cell reserve on a fresh board must be actionable");
                InvokeLifecycle(hud, "UpdateRigCounters");

                var hexRoot = FindRect(hudGo, "Weapons Button");
                var cellRoot = FindRect(hudGo, "Parts");
                Assert.That(hexRoot, Is.Not.Null, "RIG hex mark must exist");
                Assert.That(cellRoot, Is.Not.Null, "cell pill must exist");

                var cellBg = FindImage(cellRoot, "BG");
                Assert.That(cellBg, Is.Not.Null, "cell pill must carry a BG image");

                var panelColor = (Color)typeof(HudController)
                    .GetField("PanelColor", BindingFlags.NonPublic | BindingFlags.Static)
                    .GetValue(null);

                // A2 — the pill's background must be the shared PanelColor while actionable, not a
                // bespoke saturated slab.
                Assert.That(cellBg.color, Is.EqualTo(panelColor),
                    "the cell pill's background must stay PanelColor even while flashing actionable");

                // A3 — the pill must not be wider than the hex mark above it.
                Assert.That(cellRoot.sizeDelta.x, Is.LessThanOrEqualTo(hexRoot.sizeDelta.x),
                    "the cell pill must not be wider than the RIG hexagon it sits under");
            }
            finally
            {
                InvokeLifecycle(hud, "OnDisable");
                Object.DestroyImmediate(hudGo);
                PickupWallet.Reset();
                RigState.Reset();
                RigFusionState.Reset();
            }
        }

        private static RectTransform FindRect(GameObject go, string name)
        {
            foreach (var rt in go.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt;
            return null;
        }

        private static Image FindImage(RectTransform under, string name)
        {
            foreach (var img in under.GetComponentsInChildren<Image>(true))
                if (img.name == name) return img;
            return null;
        }

        private static void InvokeLifecycle(Object component, string methodName)
        {
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        }
    }
}
