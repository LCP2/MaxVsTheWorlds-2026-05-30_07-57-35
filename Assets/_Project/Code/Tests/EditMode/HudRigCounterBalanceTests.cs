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
    /// CellColor slab while actionable, wider (220) than the RIG hexagon above it (216), against a
    /// fixed, non-best-fit 34pt parts count. This is the sole new test this round adds (MV-465 Rule 1);
    /// its three assertions are the resolved facts Lee's review turned "balanced" into. A1: the two
    /// counters' best-fit-resolved font sizes must land within 15% of each other, resolved via Unity's
    /// own TextGenerator, not the authored fontSize field. A2: the cell pill's background must stay the
    /// shared PanelColor in every state, including while flashing actionable, so the saturated slab
    /// cannot come back. A3: the cell pill must never be wider than the RIG hex mark it sits under.
    /// Proven to fail on cebc077 (round 1's own commit, the base this round works from): pill width 220
    /// &gt; hex 216 fails A3; the pill's BG swaps away from PanelColor while actionable fails A2.
    /// </summary>
    public sealed class HudRigCounterBalanceTests
    {
        [Test]
        public void CellPillStaysRecessiveWithinHexWidthAndPeerFontSize()
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
                var cellRoot = FindRect(hudGo, "Power Cells");
                var partRoot = FindRect(hudGo, "Rig Part Counter");
                Assert.That(hexRoot, Is.Not.Null, "RIG hex mark must exist");
                Assert.That(cellRoot, Is.Not.Null, "cell pill must exist");
                Assert.That(partRoot, Is.Not.Null, "parts chip must exist");

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

                // A1 — the two counters' resolved (best-fit) font sizes must land within 15% of each
                // other, resolved via Unity's own TextGenerator, not the authored fontSize field.
                float cellSize = ResolvedFontSize(cellRoot.GetComponentInChildren<Text>());
                float partSize = ResolvedFontSize(partRoot.GetComponentInChildren<Text>());
                float diff = Mathf.Abs(cellSize - partSize) / Mathf.Max(cellSize, partSize);
                Assert.That(diff, Is.LessThanOrEqualTo(0.15f),
                    $"resolved font sizes must be within 15% of each other — cell {cellSize}, parts {partSize}");
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

        private static float ResolvedFontSize(Text text)
        {
            Assert.That(text, Is.Not.Null, "counter text must exist");
            var settings = text.GetGenerationSettings(text.rectTransform.rect.size);
            text.cachedTextGenerator.Populate(text.text, settings);
            return text.cachedTextGenerator.fontSizeUsedForBestFit;
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
