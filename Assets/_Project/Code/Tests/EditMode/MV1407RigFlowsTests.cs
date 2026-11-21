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
    /// MV-1407 — the RIG's ceremony, Morphing Module draft modal, family reveals, SUPPORT pills and FORGE
    /// sub-labels all sit inside the phone-first layout. Sole guard on that re-seating (AC1, AC2); do not
    /// cull. Tier 2: every rect is read back as corner points in the RIG root's local space.
    /// </summary>
    public sealed class MV1407RigFlowsTests
    {
        [SetUp]
        public void SetUp() => Reset();

        [TearDown]
        public void TearDown()
        {
            Reset();
            RigBoard.UseWorld(0);
            RigBoardLayout.ResetForTests();
            CrispUi.Resolution = 1;
        }

        private static void Reset()
        {
            WeaponSystemState.Reset(); RigState.Reset(); RigFusionState.Reset(); PickupWallet.Reset();
            PendingMorphingModule.Reset();
            Time.timeScale = 1f;
        }

        [Test]
        public void EveryFlowDrawsInsideTheSafeAreaAndTheDraftModalInsideTheTreePanel_AndWorldsRebuildTheRail()
        {
            var problems = new StringBuilder();
            foreach (var (w, h) in new[] { (852, 393), (2556, 1179) })
            {
                // Each flow is triggered through its real entry point on a fresh screen.
                var flows = new List<(string name, System.Action<WeaponsScreen> arm, bool draft)>
                {
                    ("unlock ceremony (single-candidate module)", s => PendingMorphingModule.Set(new[] { "SUPPORT" }), false),
                    ("morphing-module draft", s => PendingMorphingModule.Set(new[] { "SECONDARY", "SUPPORT" }), true),
                    ("rack module reveal", s => PendingMorphingModule.SetRackModule(), false),
                    ("secondary family reveal", s => PendingMorphingModule.SetSecondaryFamily(), false),
                    ("sentinel family reveal", s => PendingMorphingModule.SetSentinelFamily(), false),
                };
                foreach (var (name, arm, draft) in flows)
                {
                    Reset();
                    var go = new GameObject("WeaponsScreen");
                    try
                    {
                        var screen = go.AddComponent<WeaponsScreen>();
                        PickupWallet.SetPowerCells(500);
                        screen.Open();                      // builds the canvas
                        screen.Close();
                        arm(screen);
                        screen.ApplyBoardScale((float)w / h, h);
                        screen.Open();
                        string at = $"{w}x{h} {name}";
                        Assert.That(screen.IsDraftActive, Is.EqualTo(draft), $"{at}: draft state");
                        Measure(screen, go, at, w, h, draft, midCeremony: true, problems);
                        screen.Close();
                    }
                    catch (System.Exception e) { problems.AppendLine($"{w}x{h} {name}: threw {e.GetType().Name}: {e.Message}"); }
                    finally { Object.DestroyImmediate(go); }
                }

                // SUPPORT pills / price lines and FORGE sub-labels, and the PARTS chip tap.
                Reset();
                var g2 = new GameObject("WeaponsScreen");
                try
                {
                    var screen = g2.AddComponent<WeaponsScreen>();
                    PickupWallet.SetPowerCells(500);
                    screen.Open();
                    foreach (var tab in new[] { "SUPPORT", "FORGE" })
                    {
                        screen.ApplyBoardScale((float)w / h, h);
                        screen.SelectCategory(tab);
                        Measure(screen, g2, $"{w}x{h} {tab} tab", w, h, false, midCeremony: false, problems);
                    }
                    var chip = Find(g2, "Parts Chip");
                    chip.GetComponentInChildren<Button>(true).onClick.Invoke();
                    screen.Close();
                }
                catch (System.Exception e) { problems.AppendLine($"{w}x{h} tabs/chip: threw {e.GetType().Name}: {e.Message}"); }
                finally { Object.DestroyImmediate(g2); }
            }
            Assert.That(problems.ToString(), Is.Empty, problems.ToString());

            // AC2: each world rebuilds the rail with that world's categories; a morph rebuild keeps the tab.
            for (int world = 0; world < 5; world++)
            {
                Reset();
                var go = new GameObject("WeaponsScreen");
                try
                {
                    RigBoard.UseWorld(0);
                    var screen = go.AddComponent<WeaponsScreen>();
                    screen.Open();
                    screen.SelectCategory("SECONDARY");
                    RigBoard.UseWorld(world);
                    screen.ApplyBoardScale(852f / 393f, 393);
                    var expected = new List<string>();
                    foreach (var c in RigBoardLayout.PhoneCategories) expected.Add(c.Id);
                    if (RigFusionState.EnabledInWorld(world)) expected.Add("FORGE");
                    Assert.That(screen.RailTabIds, Is.EqualTo(expected), $"world {world}: the rail lists that world's categories");
                    if (expected.Contains("SECONDARY"))
                        Assert.That(screen.SelectedCategoryId, Is.EqualTo("SECONDARY"), $"world {world}: a rebuild keeps the selected tab");
                    string tab = screen.SelectedCategoryId;
                    screen.RebuildBoard();
                    Assert.That(screen.SelectedCategoryId, Is.EqualTo(tab), $"world {world}: RebuildBoard keeps the selected tab");
                    foreach (var id in screen.VisibleTreeNodeIds())
                        Assert.That(screen.BoardNode(id), Is.Not.Null, $"world {world}: tree node {id} exists");
                }
                finally { Object.DestroyImmediate(go); }
            }
        }

        private static void Measure(WeaponsScreen screen, GameObject go, string at, int w, int h, bool draft, bool midCeremony, StringBuilder problems)
        {
            var canvas = screen.RootCanvas;
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.scaleFactor = 1f;
            if (midCeremony) screen.ApplyCeremonyTiming(1.6f);

            var root = Find(go, "Weapons Root");
            var ss = screen.SafeSize;
            var safe = new Rect(-ss.x * 0.5f, -ss.y * 0.5f, ss.x, ss.y);
            var tree = LocalRect(root, Find(go, "Tree Panel"));

            foreach (var rt in go.GetComponentsInChildren<RectTransform>(false))
            {
                string n = rt.name;
                bool flow = n.StartsWith("Ceremony") || n.StartsWith("draft") || n.StartsWith("Draft")
                            || n.StartsWith("Pill") || n.Contains("Sentinel") || n.StartsWith("Sub") || n.Contains("Price");
                if (!flow || !rt.gameObject.activeInHierarchy) continue;
                var r = LocalRect(root, rt);
                if (r.xMin < safe.xMin - 0.5f || r.xMax > safe.xMax + 0.5f || r.yMin < safe.yMin - 0.5f || r.yMax > safe.yMax + 0.5f)
                    problems.AppendLine($"{at}: '{n}' {r} is outside the safe area {safe}");
            }
            foreach (var id in screen.VisibleTreeNodeIds())
            {
                foreach (var t in new Graphic[] { screen.NodePillBg(id), screen.NodePillText(id), screen.NodeSentinelPriceText(id), screen.FusionNodeSub(id), screen.FusionNodeLabel(id) })
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    var r = LocalRect(root, t.rectTransform);
                    if (r.xMin < safe.xMin - 0.5f || r.xMax > safe.xMax + 0.5f || r.yMin < safe.yMin - 0.5f || r.yMax > safe.yMax + 0.5f)
                        problems.AppendLine($"{at}: node {id} '{t.name}' {r} is outside the safe area {safe}");
                }
            }
            if (draft)
            {
                var band = Find(go, "draft_band");
                if (band == null || !band.gameObject.activeInHierarchy) { problems.AppendLine($"{at}: no active draft modal"); return; }
                var b = LocalRect(root, band);
                if (b.xMin < tree.xMin - 0.5f || b.xMax > tree.xMax + 0.5f || b.yMin < tree.yMin - 0.5f || b.yMax > tree.yMax + 0.5f)
                    problems.AppendLine($"{at}: draft modal {b} is not inside the tree panel {tree}");
                foreach (var id in screen.DraftCandidateIds)
                {
                    var chip = Find(go, "Draft Candidate " + id);
                    if (chip == null || !chip.gameObject.activeInHierarchy) { problems.AppendLine($"{at}: candidate {id} has no visible tap target"); continue; }
                    var c = LocalRect(root, chip);
                    if (c.xMin < b.xMin - 0.5f || c.xMax > b.xMax + 0.5f || c.yMin < b.yMin - 0.5f || c.yMax > b.yMax + 0.5f)
                        problems.AppendLine($"{at}: candidate {id} {c} is outside the draft modal {b}");
                    if (c.width < 44f - 0.01f || c.height < 44f - 0.01f) problems.AppendLine($"{at}: candidate {id} tap rect {c.width:0.#}x{c.height:0.#} < 44x44");
                }
            }
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
