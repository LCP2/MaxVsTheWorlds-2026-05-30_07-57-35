using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1408 — the RIG is a phone-first 852x393 canvas (<c>ScreenMatchMode.Expand</c>) with a 40-high top
    /// bar of four 36-high buttons, the board fitted below it, and nothing scrolling. Sole guard on that
    /// layout; do not cull. Resolved rects only (Tier 2): every rect is read back as corner points in the
    /// RIG root's own local space (canvas units, so the canvas scale cancels) and compared with the safe
    /// area an Expand canvas gives that screen size.
    /// </summary>
    public sealed class MV1408RigPhoneCanvasTests
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
        public void TopBarAndBoardFitTheSafeArea_NoScrollRect_At852x393_2556x1179_And2048x1536()
        {
            var go = new GameObject("WeaponsScreen");
            try
            {
                var screen = go.AddComponent<WeaponsScreen>();
                screen.Open();

                var problems = new StringBuilder();
                foreach (var (w, h) in new[] { (852, 393), (2556, 1179), (2048, 1536) })
                {
                    screen.ApplyBoardScale((float)w / h, h);

                    // What an Expand canvas resolves to at this size, and the safe area (no notch) inside it.
                    float scale = Mathf.Min(w / RefW, h / RefH);
                    float safeW = w / scale, safeH = h / scale;

                    var canvas = screen.RootCanvas;
                    canvas.GetComponent<CanvasScaler>().enabled = false;
                    canvas.scaleFactor = scale;

                    var root = Find(go, "Weapons Root");
                    var bar = Find(go, "Top Bar");
                    Assert.That(root, Is.Not.Null); Assert.That(bar, Is.Not.Null);
                    var safe = new Rect(-safeW * 0.5f, -safeH * 0.5f, safeW, safeH);

                    string tag = $"{w}x{h}";
                    var barRect = LocalRect(root, bar);
                    Inside(problems, tag + " top bar", barRect, safe);

                    var buttons = new[] { "Close Button", "Quit Button", "Settings Button", "Parts Chip" };
                    foreach (var name in buttons)
                    {
                        var rt = Find(go, name);
                        Assert.That(rt, Is.Not.Null, tag + " " + name);
                        var r = LocalRect(root, rt);
                        Inside(problems, tag + " " + name, r, safe);
                        Inside(problems, tag + " " + name + " in bar", r, barRect);
                        if (Mathf.Abs(r.height - 36f) > 0.5f) problems.AppendLine($"{tag} {name} height {r.height:0.##} != 36");
                    }

                    var parts = Find(go, "Parts Chip");
                    var partsText = parts.GetComponentInChildren<TMP_Text>(true);
                    Inside(problems, tag + " PARTS text in chip", LocalRect(root, partsText.rectTransform), LocalRect(root, parts));

                    var content = Find(go, "Tree Panel");   // MV-1410: the board content is the rail + tree panel now
                    Assert.That(content, Is.Not.Null, tag + " tree panel");
                    var contentRect = LocalRect(root, content);
                    Inside(problems, tag + " board content", contentRect, safe);
                    if (contentRect.yMax > barRect.yMin + 0.01f)
                        problems.AppendLine($"{tag} board content top {contentRect.yMax:0.##} is above the top bar's bottom {barRect.yMin:0.##}");
                }

                var scrolls = screen.RootCanvas.GetComponentsInChildren<ScrollRect>(true);
                Assert.That(scrolls.Length, Is.EqualTo(0), "no ScrollRect anywhere under the RIG canvas");
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
