using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1411 — THE RIG's detail panel: tapping a node only SELECTS it; buying happens on the panel's
    /// UPGRADE / UNLOCK button. Sole guard on select-then-UPGRADE (AC1-AC4); do not cull. Resolved values
    /// only (Tier 2): wallet and level read from the live state, rects read back as corner points in the
    /// RIG root's local space.
    /// </summary>
    public sealed class MV1411RigDetailPanelTests
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
        public void TappingANodeOnlySelects_UpgradeButtonBuys_OpensOnPrimaryRoot_AndPanelFitsAt852And2556()
        {
            var go = new GameObject("WeaponsScreen");
            try
            {
                var screen = go.AddComponent<WeaponsScreen>();
                PickupWallet.SetPowerCells(500);
                screen.Open();

                // AC3: opening selects PRIMARY and its root node.
                string primaryRoot = null;
                foreach (var ab in RigBoardLayout.PhoneAbilities)
                    if (ab.Category == "PRIMARY" && string.IsNullOrEmpty(ab.Parent)) { primaryRoot = ab.Id; break; }
                Assert.That(primaryRoot, Is.Not.Null, "fixture: PRIMARY has a root node");
                Assert.That(screen.SelectedCategoryId, Is.EqualTo("PRIMARY"), "AC3: opening selects the PRIMARY tab");
                Assert.That(screen.SelectedNodeId, Is.EqualTo(primaryRoot), "AC3: opening selects PRIMARY's root node");

                // AC1: tapping another node changes the selection only.
                string other = null;
                foreach (var id in screen.VisibleTreeNodeIds()) if (id != primaryRoot) { other = id; break; }
                Assert.That(other, Is.Not.Null, "fixture: PRIMARY's tree has a second node");
                int walletBefore = PickupWallet.PowerCells;
                int otherLevel = RigState.Level(other);
                TapNode(screen, other);
                Assert.That(PickupWallet.PowerCells, Is.EqualTo(walletBefore), "AC1: a node tap must not spend");
                Assert.That(RigState.Level(other), Is.EqualTo(otherLevel), "AC1: a node tap must not change its level");
                Assert.That(screen.SelectedNodeId, Is.EqualTo(other), "AC1: the tap selects the node");
                Assert.That(screen.DetailNameText.text, Is.EqualTo(LabelOf(other)), "AC1: the panel shows the node's name");
                Assert.That(screen.DetailLevelText.text, Is.EqualTo($"Level {otherLevel} of {RigBoard.MaxLevel(other)}"), "AC1: the panel shows Level n of m");

                // AC2: UPGRADE on an affordable node spends exactly its price and raises it one level.
                TapNode(screen, primaryRoot);
                int level = RigState.Level(primaryRoot);
                Assert.That(level, Is.GreaterThanOrEqualTo(1).And.LessThan(RigBoard.MaxLevel(primaryRoot)), "fixture: the PRIMARY root starts owned and upgradeable");
                int price = CellSpend.UpgradeCostFor(primaryRoot, level);
                Assert.That(screen.DetailUpgradeText.text, Does.StartWith("UPGRADE").And.EndWith(price.ToString()), "AC2: the button reads UPGRADE with the node's price");
                int before = PickupWallet.PowerCells;
                screen.DetailUpgradeButton.onClick.Invoke();
                Assert.That(before - PickupWallet.PowerCells, Is.EqualTo(price), "AC2: UPGRADE spends exactly the node's price");
                Assert.That(RigState.Level(primaryRoot), Is.EqualTo(level + 1), "AC2: UPGRADE raises the level by 1");

                // AC2: a maxed node reads MAXED and pressing it spends nothing.
                for (int i = 0; i < 20 && RigState.CanSpendPart(primaryRoot); i++) screen.DetailUpgradeButton.onClick.Invoke();
                Assert.That(RigState.Level(primaryRoot), Is.EqualTo(RigBoard.MaxLevel(primaryRoot)), "fixture: maxed the root");
                Assert.That(screen.DetailUpgradeText.text, Is.EqualTo("MAXED"), "AC2: a maxed node's button reads MAXED");
                int maxedWallet = PickupWallet.PowerCells;
                screen.DetailUpgradeButton.onClick.Invoke();
                Assert.That(PickupWallet.PowerCells, Is.EqualTo(maxedWallet), "AC2: pressing MAXED spends nothing");

                // AC2: a node in a locked category reads UNLOCK with the category's unlock price.
                string lockedTab = null, lockedRoot = null;
                foreach (var tab in screen.RailTabIds)
                {
                    if (tab == "FORGE" || RigState.IsCategoryUnlocked(tab)) continue;
                    lockedTab = tab;
                    foreach (var ab in RigBoardLayout.PhoneAbilities)
                        if (ab.Category == tab && string.IsNullOrEmpty(ab.Parent)) { lockedRoot = ab.Id; break; }
                    break;
                }
                Assert.That(lockedTab, Is.Not.Null, "fixture: World 1 starts with a locked category");
                screen.SelectCategory(lockedTab);
                TapNode(screen, lockedRoot);
                Assert.That(screen.DetailUpgradeText.text, Is.EqualTo("UNLOCK   " + CellSpend.UnlockCostFor(lockedRoot)), "AC2: a locked category's node reads UNLOCK with its unlock price");

                // AC4: the panel's resolved rect is inside the safe area and clear of the top bar, rail and tree panel.
                var problems = new StringBuilder();
                foreach (var (w, h) in new[] { (852, 393), (2556, 1179) })
                {
                    screen.SelectCategory("PRIMARY");
                    screen.ApplyBoardScale((float)w / h, h);
                    var canvas = screen.RootCanvas;
                    canvas.GetComponent<CanvasScaler>().enabled = false;
                    canvas.scaleFactor = 1f;

                    var root = Find(go, "Weapons Root");
                    var ss = screen.SafeSize;
                    var safe = new Rect(-ss.x * 0.5f, -ss.y * 0.5f, ss.x, ss.y);
                    var panel = LocalRect(root, Find(go, "Detail Panel"));
                    string at = $"{w}x{h}";
                    if (panel.xMin < safe.xMin - 0.5f || panel.xMax > safe.xMax + 0.5f || panel.yMin < safe.yMin - 0.5f || panel.yMax > safe.yMax + 0.5f)
                        problems.AppendLine($"{at}: detail panel {panel} is not inside the safe area {safe}");
                    var bar = LocalRect(root, screen.TopBar);
                    if (panel.Overlaps(bar)) problems.AppendLine($"{at}: detail panel {panel} overlaps the top bar {bar}");
                    var tree = LocalRect(root, Find(go, "Tree Panel"));
                    if (panel.Overlaps(tree)) problems.AppendLine($"{at}: detail panel {panel} overlaps the tree panel {tree}");
                    foreach (var tab in screen.RailTabIds)
                    {
                        var t = LocalRect(root, Find(go, "Tab " + tab));
                        if (panel.Overlaps(t)) problems.AppendLine($"{at}: detail panel overlaps rail tab {tab}");
                    }
                    var up = LocalRect(root, (RectTransform)screen.DetailUpgradeButton.transform);
                    if (up.width < 44f - 0.01f || up.height < 44f - 0.01f) problems.AppendLine($"{at}: UPGRADE tap rect {up.width:0.##}x{up.height:0.##} < 44x44");
                }
                Assert.That(problems.ToString(), Is.Empty, problems.ToString());
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static void TapNode(WeaponsScreen screen, string id)
        {
            var hit = screen.BoardNode(id).Find("Select Hit").GetComponent<Button>();
            hit.onClick.Invoke();
        }

        private static string LabelOf(string id)
        {
            foreach (var ab in RigBoardLayout.PhoneAbilities) if (ab.Id == id) return ab.Label;
            return null;
        }

        private static Rect LocalRect(RectTransform space, RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            var a = space.InverseTransformPoint(c[0]);
            var b = space.InverseTransformPoint(c[2]);
            return new Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        }

        private static RectTransform Find(GameObject go, string name)
        {
            foreach (var t in go.GetComponentsInChildren<RectTransform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
