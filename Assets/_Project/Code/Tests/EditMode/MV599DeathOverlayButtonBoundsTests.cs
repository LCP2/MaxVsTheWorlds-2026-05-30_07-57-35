using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-599 — DeathOverlay's QUIT/CONTINUE buttons were built with STRETCH anchors and a positive
    /// sizeDelta.x, which Unity adds to the anchor span rather than treating as a width: each button
    /// came out at (half the panel width) + 360, overlapping at the centre and overflowing the panel on
    /// both outer edges. Sole guard on the fix; do not cull.
    ///
    /// Asserts resolved RectTransform world corners, not the authored sizeDelta — reading sizeDelta
    /// would reproduce the bug (it looked like "360" the whole time) rather than catch it.
    /// </summary>
    public sealed class MV599DeathOverlayButtonBoundsTests
    {
        [Test]
        public void QuitAndContinueButtons_DoNotOverlap_SitInsidePanel_QuitLeftOfContinue()
        {
            var go = new GameObject("DeathOverlay");
            var overlay = go.AddComponent<DeathOverlay>();
            try
            {
                overlay.Show("The Carport", gateRecloses: true, deathsTaken: 1, onContinue: () => { });

                // MV-516/MV-594 idiom: pin scaleFactor to 1 so GetWorldCorners reads back in ref px.
                var canvas = overlay.QuitButton.GetComponentInParent<Canvas>();
                var scaler = canvas.GetComponent<CanvasScaler>();
                scaler.enabled = false;
                canvas.scaleFactor = 1f;

                // BuildButton parents each button's background image directly under the panel's own
                // RectTransform, so the button's transform parent IS the panel rect — no separate
                // test-only accessor needed.
                var panelRect = (RectTransform)overlay.QuitButton.transform.parent;

                Rect WorldRect(RectTransform rt)
                {
                    var c = new Vector3[4];
                    rt.GetWorldCorners(c);
                    float xMin = Mathf.Min(c[0].x, c[2].x), xMax = Mathf.Max(c[0].x, c[2].x);
                    float yMin = Mathf.Min(c[0].y, c[2].y), yMax = Mathf.Max(c[0].y, c[2].y);
                    return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
                }

                Rect quitRect = WorldRect(overlay.QuitButton.GetComponent<RectTransform>());
                Rect continueRect = WorldRect(overlay.ContinueButton.GetComponent<RectTransform>());
                Rect panelWorldRect = WorldRect(panelRect);

                // AC1: resolved rects must not intersect.
                Assert.IsFalse(quitRect.Overlaps(continueRect),
                    $"QUIT ({quitRect}) and CONTINUE ({continueRect}) resolved rects overlap");

                // AC2: both resolved rects must sit fully inside the panel's resolved rect.
                Assert.That(quitRect.xMin, Is.GreaterThanOrEqualTo(panelWorldRect.xMin - 0.5f), "QUIT left edge escapes the panel");
                Assert.That(quitRect.xMax, Is.LessThanOrEqualTo(panelWorldRect.xMax + 0.5f), "QUIT right edge escapes the panel");
                Assert.That(quitRect.yMin, Is.GreaterThanOrEqualTo(panelWorldRect.yMin - 0.5f), "QUIT bottom edge escapes the panel");
                Assert.That(quitRect.yMax, Is.LessThanOrEqualTo(panelWorldRect.yMax + 0.5f), "QUIT top edge escapes the panel");

                Assert.That(continueRect.xMin, Is.GreaterThanOrEqualTo(panelWorldRect.xMin - 0.5f), "CONTINUE left edge escapes the panel");
                Assert.That(continueRect.xMax, Is.LessThanOrEqualTo(panelWorldRect.xMax + 0.5f), "CONTINUE right edge escapes the panel");
                Assert.That(continueRect.yMin, Is.GreaterThanOrEqualTo(panelWorldRect.yMin - 0.5f), "CONTINUE bottom edge escapes the panel");
                Assert.That(continueRect.yMax, Is.LessThanOrEqualTo(panelWorldRect.yMax + 0.5f), "CONTINUE top edge escapes the panel");

                // AC4: order preserved -- QUIT strictly left of CONTINUE.
                Assert.That(quitRect.xMax, Is.LessThanOrEqualTo(continueRect.xMin),
                    "QUIT must sit fully left of CONTINUE (left-destructive/right-primary convention)");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
