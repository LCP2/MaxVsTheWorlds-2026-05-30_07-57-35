using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1415 - THE RIG polish: (AC1) QUIT lives at the rail's foot, at least 300 pt from CLOSE, and only the
    /// confirm dialog's QUIT quits; (AC2) no node or label rect overlaps another in any category tree of any world;
    /// (AC3) a rail tab resolves its bar / MAX / padlock state from live progress. Resolved values only (Tier 2):
    /// world-space rects read back from the built RectTransforms, state read from the live RigState.
    /// </summary>
    public sealed class MV1415RigPolishTests
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
            SaveSystem.ActiveSlot = -1;
            SaveSystem.DirectoryOverride = null;
            RigBoard.UseWorld(0);
            RigBoardLayout.ResetForTests();
            CrispUi.Resolution = 1;
        }

        private static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        private static RectTransform FindRect(GameObject go, string name)
        {
            foreach (var t in go.GetComponentsInChildren<RectTransform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static float Gap(Rect a, Rect b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax));
            float dy = Mathf.Max(0f, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static bool Overlap(Rect a, Rect b)
        {
            const float e = 0.01f;
            return a.xMin < b.xMax - e && b.xMin < a.xMax - e && a.yMin < b.yMax - e && b.yMin < a.yMax - e;
        }

        [Test]
        public void QuitIsAtTheRailFootBehindAConfirm_NoTreeRectsOverlap_AndTabsResolveProgressMaxAndLocked()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ytgame-save-tests-mv1415");
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            SaveSystem.DirectoryOverride = dir;
            SaveSystem.ActiveSlot = 0;
            PickupWallet.SetPowerCells(500);

            var go = new GameObject("WeaponsScreen");
            try
            {
                var screen = go.AddComponent<WeaponsScreen>();
                screen.Open();   // parks Time.timeScale at 0

                // ---- AC1: QUIT is on the rail, far from CLOSE, and only the dialog's QUIT quits.
                foreach (var (w, h) in new[] { (852, 393), (1920, 1080) })
                {
                    screen.ApplyBoardScale((float)w / h, h);
                    var canvas = screen.RootCanvas;
                    canvas.GetComponent<CanvasScaler>().enabled = false;
                    canvas.scaleFactor = 1f;

                    var close = FindRect(go, "Close Button");
                    var quit = FindRect(go, "Quit Rail Button");
                    Assert.That(close, Is.Not.Null, $"{w}x{h}: CLOSE exists");
                    Assert.That(quit, Is.Not.Null, $"{w}x{h}: the rail's QUIT TO MENU exists");
                    Assert.That(FindRect(go, "Quit Button"), Is.Null, $"{w}x{h}: QUIT is gone from the top bar");
                    Assert.That(Gap(WorldRect(close), WorldRect(quit)), Is.GreaterThanOrEqualTo(300f), $"{w}x{h}: CLOSE and QUIT are at least 300 pt apart");
                    Assert.That(WorldRect(quit).height, Is.GreaterThanOrEqualTo(36f), $"{w}x{h}: QUIT TO MENU is at least 36 pt high");
                }

                screen.ApplyBoardScale(852f / 393f, 393);
                Assert.That(screen.QuitDialogOpen, Is.False, "the dialog starts closed");
                screen.QuitRailButton.onClick.Invoke();
                Assert.That(screen.QuitDialogOpen, Is.True, "pressing the rail's QUIT opens the confirm dialog");
                Assert.That(SaveSystem.ActiveSlot, Is.EqualTo(0), "...and does not quit");
                Assert.That(Time.timeScale, Is.EqualTo(0f), "...the RIG is still paused");
                screen.QuitCancelButton.onClick.Invoke();
                Assert.That(screen.QuitDialogOpen, Is.False, "CANCEL closes the dialog");
                Assert.That(SaveSystem.ActiveSlot, Is.EqualTo(0), "CANCEL does not quit");
                screen.QuitRailButton.onClick.Invoke();
                screen.QuitScrimButton.onClick.Invoke();
                Assert.That(screen.QuitDialogOpen, Is.False, "a tap outside the dialog cancels");
                Assert.That(SaveSystem.ActiveSlot, Is.EqualTo(0), "a tap outside does not quit");

                // ---- AC3: tab state. World 0: PRIMARY is partly levelled, MOVE is maxed, a closed category is locked.
                string locked = null;
                foreach (var id in screen.RailTabIds)
                    if (id != "FORGE" && !RigState.IsCategoryUnlocked(id) && id != "MOVE") { locked = id; break; }
                Assert.That(locked, Is.Not.Null, "fixture: a category is still locked at run start");

                Assert.That(RigState.UnlockCategory("MOVE") || RigState.IsCategoryUnlocked("MOVE"), Is.True, "fixture: MOVE unlocks");
                foreach (var ab in RigBoardLayout.PhoneAbilities)
                {
                    if (ab.Category != "MOVE") continue;
                    int max = RigBoard.MaxLevel(ab.Id);
                    RigState.AcquireCap(ab.Id);
                    for (int i = 0; i < max && RigState.Level(ab.Id) < max; i++) RigState.RaiseLevel(ab.Id);
                    Assert.That(RigState.Level(ab.Id), Is.EqualTo(max), $"fixture: {ab.Id} reached its cap");
                }
                screen.RebuildBoard();

                // maxed: the bar is full and the right side reads MAX
                var moveTrack = screen.RailTabBarTrack("MOVE");
                var moveFill = screen.RailTabBarFill("MOVE");
                Assert.That(moveTrack.gameObject.activeInHierarchy, Is.True, "a maxed tab keeps its bar");
                Assert.That(moveFill.rectTransform.rect.width / moveTrack.rectTransform.rect.width, Is.EqualTo(1f).Within(0.001f), "maxed: bar at 100%");
                Assert.That(screen.RailTabCountText("MOVE").text, Is.EqualTo("MAX"), "maxed: the right side reads MAX");
                Assert.That(screen.RailTabPadlock("MOVE").gameObject.activeInHierarchy, Is.False, "maxed: no padlock");

                // partly levelled: bar fraction is owned nodes over the category's nodes
                int owned = 0, total = 0;
                foreach (var ab in RigBoardLayout.PhoneAbilities)
                {
                    if (ab.Category != "PRIMARY") continue;
                    total++;
                    if (RigState.IsOwned(ab.Id)) owned++;
                }
                Assert.That(owned, Is.GreaterThan(0).And.LessThan(total), "fixture: PRIMARY is partly levelled");
                var pTrack = screen.RailTabBarTrack("PRIMARY");
                var pFill = screen.RailTabBarFill("PRIMARY");
                Assert.That(pFill.rectTransform.rect.width / pTrack.rectTransform.rect.width, Is.EqualTo((float)owned / total).Within(0.001f), "in progress: bar fraction = owned / total");
                Assert.That(screen.RailTabCountText("PRIMARY").text, Is.EqualTo($"{owned}/{total}"), "in progress: the right side reads x/y");

                // locked: no bar, a padlock instead of the count, LOCKED on the top line, dimmed
                Assert.That(screen.RailTabBarTrack(locked).gameObject.activeInHierarchy, Is.False, "locked: no bar");
                Assert.That(screen.RailTabPadlock(locked).gameObject.activeInHierarchy, Is.True, "locked: a padlock");
                Assert.That(screen.RailTabCountText(locked).gameObject.activeInHierarchy, Is.False, "locked: no count");
                Assert.That(screen.RailTabTopText(locked).text, Is.EqualTo("LOCKED"), "locked: the top line reads LOCKED");
                Assert.That(screen.RailTabAlpha(locked), Is.EqualTo(0.45f).Within(0.1f), "locked: dimmed to about 45%");

                // ---- AC2: in every category tree of every world, no node or label rect overlaps another.
                var problems = new StringBuilder();
                for (int world = 0; world < WorldCatalog.Count; world++)
                {
                    RigBoard.UseWorld(world);
                    screen.ApplyBoardScale(852f / 393f, 393);
                    foreach (var tab in new System.Collections.Generic.List<string>(screen.RailTabIds))
                    {
                        screen.SelectCategory(tab);
                        var rects = new System.Collections.Generic.List<(string what, Rect r)>();
                        foreach (var id in screen.VisibleTreeNodeIds())
                        {
                            rects.Add(($"{id} hex", screen.TreeNodeRect(id)));
                            var label = screen.TreeLabelRect(id);
                            if (label.width > 0f) rects.Add(($"{id} label", label));
                        }
                        for (int a = 0; a < rects.Count; a++)
                            for (int b = a + 1; b < rects.Count; b++)
                                if (Overlap(rects[a].r, rects[b].r))
                                    problems.AppendLine($"world {world} tree {tab}: {rects[a].what} {rects[a].r} overlaps {rects[b].what} {rects[b].r}");
                    }
                }
                Assert.That(problems.ToString(), Is.Empty, problems.ToString());
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
