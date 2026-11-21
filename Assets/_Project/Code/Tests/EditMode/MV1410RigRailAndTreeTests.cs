using System.Text;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1410 — THE RIG is a category rail plus ONE auto-laid-out tree at a time. Sole guard on that
    /// layout (AC1 + AC2); do not cull. Resolved rects only (Tier 2): every rect is read back as corner
    /// points in the RIG root's own local space (canvas units, so the canvas scale cancels) at 852x393,
    /// for every category of every world board.
    /// </summary>
    public sealed class MV1410RigRailAndTreeTests
    {
        private const float RefW = 852f, RefH = 393f;

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
        public void EveryCategoryTreeFitsItsPanelWithoutOverlap_AndTheRailHasOneTallTabPerCategory_At852x393()
        {
            var go = new GameObject("WeaponsScreen");
            try
            {
                var screen = go.AddComponent<WeaponsScreen>();
                screen.Open();

                var problems = new StringBuilder();
                int treesChecked = 0;
                for (int world = 0; world < WorldCatalog.Count; world++)
                {
                    RigBoard.UseWorld(world);
                    RigBoardLayout.UseWorld(world);
                    screen.RebuildBoard();
                    screen.ApplyBoardScale(RefW / RefH, (int)RefH);

                    var canvas = screen.RootCanvas;
                    canvas.GetComponent<UnityEngine.UI.CanvasScaler>().enabled = false;
                    canvas.scaleFactor = 1f;

                    var root = Find(go, "Weapons Root");
                    var ss = screen.SafeSize;   // the live safe area (canvas units)
                    var safe = new Rect(-ss.x * 0.5f, -ss.y * 0.5f, ss.x, ss.y);
                    var panel = LocalRect(root, Find(go, "Tree Panel"));

                    // AC2: one tab per category, in board order (+ FORGE when the world has fusions).
                    var tabs = screen.RailTabIds;
                    Assert.That(tabs.Count, Is.GreaterThan(0), $"world {world} rail has tabs");
                    var expected = new System.Collections.Generic.List<string>();
                    foreach (var c in RigBoardLayout.PhoneCategories) expected.Add(c.Id);
                    if (RigFusionState.EnabledInWorld(world)) expected.Add("FORGE");
                    Assert.That(new System.Collections.Generic.List<string>(tabs), Is.EqualTo(expected), $"world {world}: rail tabs in board order");
                    foreach (var id in tabs)
                    {
                        var tab = LocalRect(root, Find(go, "Tab " + id));
                        string what = $"world {world} tab {id}";
                        Inside(problems, what, tab, safe);
                        if (tab.height < 50f - 0.01f) problems.AppendLine($"{what} is {tab.height:0.##} high (< 50)");
                    }

                    foreach (var id in tabs)
                    {
                        screen.SelectCategory(id);
                        Assert.That(screen.SelectedCategoryId, Is.EqualTo(id), $"world {world}: selecting tab {id}");
                        var ids = screen.VisibleTreeNodeIds();
                        Assert.That(ids.Count, Is.GreaterThan(0), $"world {world} tree {id} shows nodes");
                        treesChecked++;

                        var rects = new System.Collections.Generic.List<(string id, Rect r)>();
                        foreach (var nodeId in ids)
                        {
                            var rt = screen.BoardNode(nodeId);
                            Assert.That(rt, Is.Not.Null, $"world {world} node {nodeId}");
                            Assert.That(rt.gameObject.activeInHierarchy, Is.True, $"world {world} tree {id}: {nodeId} is shown");
                            var r = LocalRect(root, rt);
                            rects.Add((nodeId, r));
                            string what = $"world {world} tree {id} node {nodeId}";
                            Inside(problems, what + " in panel", r, panel);
                            if (r.width < 44f - 0.01f || r.height < 44f - 0.01f) problems.AppendLine($"{what} tap rect {r.width:0.##}x{r.height:0.##} < 44x44");
                            bool isRoot = string.IsNullOrEmpty(screen.NodeParentId(nodeId));
                            float min = isRoot ? 72f : 58f;
                            if (r.width < min - 0.01f) problems.AppendLine($"{what} size {r.width:0.##} < {min} ({(isRoot ? "root" : "child")})");
                        }
                        for (int a = 0; a < rects.Count; a++)
                            for (int b = a + 1; b < rects.Count; b++)
                                if (rects[a].r.Overlaps(rects[b].r))
                                    problems.AppendLine($"world {world} tree {id}: {rects[a].id} overlaps {rects[b].id}");

                        // The others' nodes are not shown.
                        foreach (var other in tabs)
                        {
                            if (other == id) continue;
                            screen.SelectCategory(other);
                            foreach (var otherId in screen.VisibleTreeNodeIds())
                                Assert.That(screen.BoardNode(otherId).gameObject.activeInHierarchy, Is.True);
                            foreach (var nodeId in ids)
                                Assert.That(screen.BoardNode(nodeId).gameObject.activeInHierarchy, Is.False, $"world {world}: {nodeId} must hide when {other} is selected");
                            break;
                        }
                    }
                }

                Assert.That(treesChecked, Is.GreaterThan(5), "checked every category tree of every world");
                Assert.That(problems.ToString(), Is.Empty, problems.ToString());
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static void Inside(StringBuilder sb, string what, Rect inner, Rect outer)
        {
            const float e = 0.5f;
            if (inner.xMin < outer.xMin - e || inner.xMax > outer.xMax + e || inner.yMin < outer.yMin - e || inner.yMax > outer.yMax + e)
                sb.AppendLine($"{what} [{inner.xMin:0.#},{inner.yMin:0.#} .. {inner.xMax:0.#},{inner.yMax:0.#}] is not inside [{outer.xMin:0.#},{outer.yMin:0.#} .. {outer.xMax:0.#},{outer.yMax:0.#}]");
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
