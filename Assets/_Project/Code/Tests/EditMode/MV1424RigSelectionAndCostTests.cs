using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1424 - THE RIG: one outline per tree node (no ring/circle/dot stacked around it, exactly one node
    /// carrying the white selected outline), and the detail panel resolves the cost block, the levels to MAX
    /// and the button state from the live wallet and the node's real price. Resolved values only (Tier 2).
    /// </summary>
    public sealed class MV1424RigSelectionAndCostTests
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

        [Test]
        public void NodesHaveOneOutline_AndDetailPanelResolvesCostNeedAndLevelsToMax()
        {
            var go = new GameObject("WeaponsScreen");
            try
            {
                var screen = go.AddComponent<WeaponsScreen>();
                PickupWallet.SetPowerCells(500);
                screen.Open();

                string root = screen.SelectedNodeId;
                int level = RigState.Level(root), max = RigBoard.MaxLevel(root);
                Assert.That(level, Is.GreaterThanOrEqualTo(1).And.LessThan(max), "fixture: the PRIMARY root is owned and upgradeable");
                int price = CellSpend.UpgradeCostFor(root, level);
                Assert.That(price, Is.GreaterThan(5), "fixture: price leaves room for a 5-part shortfall");

                // AC1: no ring / circle / dot on any tree node, exactly one selected outline, and it moves with the selection.
                Assert.That(screen.NodeStackedOutlineNames(), Is.Empty, "AC1: no node carries a progress ring, track, outer ring or cap dot");
                var selected = screen.SelectionOutline;
                Assert.That(selected, Is.Not.Null, "AC1: the selected node carries the selected outline");
                Vector2 first = selected.rectTransform.anchoredPosition;
                string other = null;
                foreach (var id in screen.VisibleTreeNodeIds()) if (id != root) { other = id; break; }
                Assert.That(other, Is.Not.Null, "fixture: the tree has a second node");
                screen.SelectNode(other);
                Assert.That(screen.SelectionOutline.rectTransform.anchoredPosition, Is.Not.EqualTo(first), "AC1: changing the selection moves the outline");
                screen.SelectNode(root);

                // AC2: short of parts.
                PickupWallet.SetPowerCells(price - 5);
                screen.SelectNode(root);
                Assert.That(screen.DetailLevelText.text, Does.Contain($"{max - level} more to MAX"), "AC2: levels remaining");
                Assert.That(screen.DetailCostRowLabel(0), Is.EqualTo("Cost"));
                Assert.That(screen.DetailCostRowValue(0), Is.EqualTo(price.ToString()), "AC2: Cost row shows the node's price");
                Assert.That(screen.DetailCostRowValue(1), Is.EqualTo((price - 5).ToString()), "AC2: You have row shows the wallet");
                Assert.That(screen.DetailCostRowLabel(2), Is.EqualTo("Need"));
                Assert.That(screen.DetailCostRowValue(2), Is.EqualTo("5 more"), "AC2: Need row shows price - wallet");
                Assert.That(screen.DetailUpgradeText.text, Is.EqualTo("NEED 5 MORE PARTS"), "AC2: short button text");
                Assert.That(screen.DetailUpgradeButton.interactable, Is.False, "AC2: short button is not interactable");

                // AC2: affordable - no Need row.
                PickupWallet.SetPowerCells(price + 2);
                screen.SelectNode(root);
                Assert.That(screen.DetailCostRowValue(2), Is.Null, "AC2: affordable omits the Need row");
                Assert.That(screen.DetailUpgradeText.text, Does.StartWith("UPGRADE").And.EndWith(price.ToString()), "AC2: affordable button text");
                Assert.That(screen.DetailUpgradeButton.interactable, Is.True, "AC2: affordable button is interactable");

                // AC2: maxed.
                PickupWallet.SetPowerCells(5000);
                for (int i = 0; i < 20 && RigState.CanSpendPart(root); i++) screen.DetailUpgradeButton.onClick.Invoke();
                Assert.That(screen.DetailLevelText.text, Is.EqualTo("MAXED"), "AC2: maxed level line");
                Assert.That(screen.DetailUpgradeText.text, Is.EqualTo("MAXED"), "AC2: maxed button");
                Assert.That(screen.DetailUpgradeButton.interactable, Is.False, "AC2: maxed button is not interactable");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
