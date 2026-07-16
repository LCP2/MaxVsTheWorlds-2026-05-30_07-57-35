using NUnit.Framework;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-98 — the pure safe-area → anchor maths that keeps HUD controls out from under an iPhone
    /// notch / home indicator. Runs in EditMode (cc-verify) so a regression is caught before merge,
    /// without needing a device.
    /// </summary>
    public sealed class SafeAreaTests
    {
        private static void Compute(Rect safe, float w, float h, out Vector2 min, out Vector2 max)
            => SafeArea.ComputeAnchors(safe, w, h, out min, out max);

        [Test]
        public void FullScreenSafeArea_MapsToFullAnchors()
        {
            Compute(new Rect(0, 0, 1170, 2532), 1170, 2532, out var min, out var max);
            Assert.AreEqual(0f, min.x, 1e-4f);
            Assert.AreEqual(0f, min.y, 1e-4f);
            Assert.AreEqual(1f, max.x, 1e-4f);
            Assert.AreEqual(1f, max.y, 1e-4f);
        }

        [Test]
        public void InsetSafeArea_MapsToFractionalAnchors()
        {
            // 2000×1000 screen with a 100px left/right and 50px top/bottom inset.
            Compute(new Rect(100, 50, 1800, 900), 2000, 1000, out var min, out var max);
            Assert.AreEqual(0.05f, min.x, 1e-4f);
            Assert.AreEqual(0.05f, min.y, 1e-4f);
            Assert.AreEqual(0.95f, max.x, 1e-4f);
            Assert.AreEqual(0.95f, max.y, 1e-4f);
        }

        [Test]
        public void AsymmetricNotch_InsetsOnlyTheAffectedEdge()
        {
            // Landscape phone: 130px cut on the left (notch side), nothing elsewhere.
            Compute(new Rect(130, 0, 2400 - 130, 1080), 2400, 1080, out var min, out var max);
            Assert.AreEqual(130f / 2400f, min.x, 1e-4f);
            Assert.AreEqual(0f, min.y, 1e-4f);
            Assert.AreEqual(1f, max.x, 1e-4f);
            Assert.AreEqual(1f, max.y, 1e-4f);
        }

        [Test]
        public void ZeroSizedScreen_FallsBackToFullScreen()
        {
            Compute(new Rect(0, 0, 0, 0), 0, 0, out var min, out var max);
            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
        }

        [Test]
        public void DegenerateSafeArea_FallsBackToFullScreen()
        {
            // A zero-height safe area must never collapse the HUD to a line.
            Compute(new Rect(0, 500, 1000, 0), 1000, 1000, out var min, out var max);
            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
        }

        [Test]
        public void OverReportedInset_IsClampedNotInverted()
        {
            // A safe area larger than the screen (bad platform report) must clamp to [0,1], not invert.
            Compute(new Rect(-50, -50, 3000, 3000), 1000, 1000, out var min, out var max);
            Assert.GreaterOrEqual(min.x, 0f);
            Assert.GreaterOrEqual(min.y, 0f);
            Assert.LessOrEqual(max.x, 1f);
            Assert.LessOrEqual(max.y, 1f);
            Assert.Greater(max.x, min.x);
            Assert.Greater(max.y, min.y);
        }
    }
}
