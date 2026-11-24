using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1440 - THE RIG node symbology: three independent signals. STATE (locked / available / owned / maxed) is
    /// resolved from the live RigState; AFFORDABILITY (green badge + green price, available / owned nodes only)
    /// follows the wallet live; SELECTION (four corner brackets + white name) sits on exactly one node. A rail tab
    /// shows the afford badge only while its tree has an affordable node. Resolved values only (Tier 2).
    /// </summary>
    public sealed class MV1440RigNodeStateSymbologyTests
    {
        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset(); RigState.Reset(); RigFusionState.Reset(); PickupWallet.Reset();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset(); RigState.Reset(); RigFusionState.Reset(); PickupWallet.Reset();
            Time.timeScale = 1f;
            RigBoard.UseWorld(0);
            RigBoardLayout.ResetForTests();
            CrispUi.Resolution = 1;
        }

        private static WeaponsScreen.RigNodeSymbol Expected(string id)
        {
            if (!RigState.IsOwned(id)) return RigState.IsCellUnlockable(id) ? WeaponsScreen.RigNodeSymbol.Available : WeaponsScreen.RigNodeSymbol.Locked;
            return RigState.CanSpendPart(id) ? WeaponsScreen.RigNodeSymbol.Owned : WeaponsScreen.RigNodeSymbol.Maxed;
        }

        private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f;

        [Test]
        public void NodesResolveStateBadgeAndSelectionIndependently_AndTabsShowAffordBadge()
        {
            var go = new GameObject("WeaponsScreen");
            try
            {
                var screen = go.AddComponent<WeaponsScreen>();
                PickupWallet.SetPowerCells(500);
                screen.Open();
                string root = screen.SelectedNodeId;
                Assert.That(root, Is.Not.Null, "fixture: the tree has a selected root");
                while (RigState.Level(root) < 2 && RigState.CanSpendPart(root)) RigState.RaiseLevel(root);
                if (!RigState.IsOwned(root)) { RigState.AcquireCap(root); RigState.RaiseLevel(root); }

                // AC1: with plenty of parts - state resolves from RigState, the badge marks exactly the available/owned nodes.
                screen.SelectNode(root);
                var ids = screen.VisibleTreeNodeIds();
                bool sawLocked = false, sawAvailable = false, sawOwned = false;
                foreach (var id in ids)
                {
                    var sym = screen.NodeSymbolOf(id);
                    Assert.That(sym, Is.EqualTo(Expected(id)), $"AC1: {id} resolves its state from RigState");
                    sawLocked |= sym == WeaponsScreen.RigNodeSymbol.Locked;
                    sawAvailable |= sym == WeaponsScreen.RigNodeSymbol.Available;
                    sawOwned |= sym == WeaponsScreen.RigNodeSymbol.Owned;
                    bool buyable = sym == WeaponsScreen.RigNodeSymbol.Available || sym == WeaponsScreen.RigNodeSymbol.Owned;
                    Assert.That(screen.NodeAffordBadgeShown(id), Is.EqualTo(buyable), $"AC1: {id} badge iff buyable with 500 parts");
                    if (buyable) Assert.That(screen.NodeSubLineText(id), Does.Contain("#" + ColorUtility.ToHtmlStringRGB(WeaponsScreen.AffordColour)), $"AC1: {id} price is green when affordable");
                }
                Assert.That(sawLocked && sawAvailable && sawOwned, Is.True, "fixture: the tree shows locked, available and owned nodes");
                Assert.That(screen.RailTabAffordBadgeShown(screen.SelectedCategoryId), Is.True, "AC3: the tab shows the badge while its tree has an affordable node");
                foreach (var tab in screen.RailTabIds)
                    if (screen.ComputeRailTabState(tab).Locked) Assert.That(screen.RailTabAffordBadgeShown(tab), Is.False, $"AC3: locked tab {tab} has no badge");

                // AC1: no parts - no badge anywhere, grey prices, and the same states.
                PickupWallet.SetPowerCells(0);
                screen.SelectNode(root);
                foreach (var id in ids)
                {
                    Assert.That(screen.NodeSymbolOf(id), Is.EqualTo(Expected(id)), $"AC1: {id} state is independent of the wallet");
                    Assert.That(screen.NodeAffordBadgeShown(id), Is.False, $"AC1: {id} has no badge with 0 parts");
                    var sym = screen.NodeSymbolOf(id);
                    if (sym == WeaponsScreen.RigNodeSymbol.Available || sym == WeaponsScreen.RigNodeSymbol.Owned)
                        Assert.That(screen.NodeSubLineText(id), Does.Contain("#7D8599"), $"AC1: {id} price is grey when unaffordable");
                }
                foreach (var tab in screen.RailTabIds) Assert.That(screen.RailTabAffordBadgeShown(tab), Is.False, $"AC3: tab {tab} has no badge with 0 parts");

                // AC2: exactly one node carries the brackets and the white name; changing the selection moves them.
                Assert.That(screen.SelectionBracketCount(), Is.EqualTo(4), "AC2: four corner brackets on the selection");
                Vector2 first = screen.SelectionOutline.rectTransform.anchoredPosition;
                Assert.That(Near(screen.NodeNameText(root).color, Color.white), Is.True, "AC2: the selected node's name is white");
                string other = null;
                foreach (var id in ids) if (id != root && screen.NodeSymbolOf(id) != WeaponsScreen.RigNodeSymbol.Locked) { other = id; break; }
                Assert.That(other, Is.Not.Null, "fixture: a second unlocked node");
                screen.SelectNode(other);
                Assert.That(screen.SelectionBracketCount(), Is.EqualTo(4), "AC2: still exactly one reticle");
                Assert.That(screen.SelectionOutline.rectTransform.anchoredPosition, Is.Not.EqualTo(first), "AC2: the brackets moved");
                Assert.That(Near(screen.NodeNameText(other).color, Color.white), Is.True, "AC2: the new selection's name is white");
                Assert.That(Near(screen.NodeNameText(root).color, Color.white), Is.False, "AC2: the old selection's name is no longer white");

                // AC2: a maxed node resolves to a gold outline and "MAX".
                PickupWallet.SetPowerCells(500);
                while (RigState.CanSpendPart(root)) RigState.RaiseLevel(root);
                screen.SelectNode(root);
                Assert.That(screen.NodeSymbolOf(root), Is.EqualTo(WeaponsScreen.RigNodeSymbol.Maxed));
                Assert.That(Near(screen.NodeOutlineImage(root).color, new Color32(0xFF, 0xC0, 0x4A, 0xFF)), Is.True, "AC2: maxed outline is gold");
                Assert.That(screen.NodeSubLineText(root), Does.Contain("MAX"), "AC2: maxed sub line reads MAX");
                Assert.That(screen.NodeAffordBadgeShown(root), Is.False, "AC2: a maxed node has nothing to afford");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
