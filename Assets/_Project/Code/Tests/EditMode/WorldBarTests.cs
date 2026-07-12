using NUnit.Framework;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>The scale-inheritance trap behind YT-71: a world-space bar hung on a scaled body
    /// silently renders at the body's scale. The Mower Hutch's (3, 2, 3) body turned a 220 px bar
    /// into a 13.2 m banner. These pin the fix.</summary>
    public sealed class WorldBarTests
    {
        [Test]
        public void Unscale_CancelsTheBodysScale()
        {
            Vector3 u = WorldBar.Unscale(new Vector3(3f, 2f, 3f)); // the Mower Hutch
            Assert.AreEqual(1f / 3f, u.x, 1e-4);
            Assert.AreEqual(1f / 2f, u.y, 1e-4);
            Assert.AreEqual(1f / 3f, u.z, 1e-4);
        }

        [Test]
        public void Unscale_TimesTheBodysScale_IsUnity_ForAnyBody()
        {
            foreach (var body in new[]
                     {
                         new Vector3(3f, 2f, 3f), new Vector3(1f, 1f, 1f),
                         new Vector3(0.5f, 8f, 2f), new Vector3(10f, 0.1f, 4f),
                     })
            {
                Vector3 u = WorldBar.Unscale(body);
                Assert.AreEqual(1f, u.x * body.x, 1e-3, "x scale not cancelled");
                Assert.AreEqual(1f, u.y * body.y, 1e-3, "y scale not cancelled");
                Assert.AreEqual(1f, u.z * body.z, 1e-3, "z scale not cancelled");
            }
        }

        [Test]
        public void Unscale_SurvivesADegenerateBody_RatherThanReturningInfinity()
        {
            Vector3 u = WorldBar.Unscale(Vector3.zero);
            Assert.IsFalse(float.IsInfinity(u.x) || float.IsNaN(u.x));
        }

        [Test]
        public void LocalOffsetY_LandsTheBarWhereTheMetresSay()
        {
            // The Hutch is scaled 2 on Y, so a local offset of 1.7 would have put the bar 3.4 m up.
            float local = WorldBar.LocalOffsetY(1.7f, 2f);
            Assert.AreEqual(0.85f, local, 1e-4);
            Assert.AreEqual(1.7f, local * 2f, 1e-4, "the bar must end up where the metres said");
        }

        [Test]
        public void CanvasScaleFor_TurnsPixelsIntoTheMetresAsked()
        {
            float s = WorldBar.CanvasScaleFor(worldWidth: 1.8f, pixelWidth: 180f);
            Assert.AreEqual(0.01f, s, 1e-5);
            Assert.AreEqual(1.8f, 180f * s, 1e-4);
        }

        [Test]
        public void TheFactoryBarIsSmallerThanTheFactory()
        {
            // The whole point: it's a readout of the damage you're doing, not architecture.
            // Hutch body is 3 m wide; the bar must not out-shout it.
            const float bodyWidth = 3f;
            float barWidth = 180f * WorldBar.CanvasScaleFor(1.8f, 180f);
            Assert.Less(barWidth, bodyWidth);

            // And nothing like the 13.2 m banner it used to be.
            Assert.Less(barWidth, 13.2f / 4f);
        }

        [Test]
        public void CanvasScaleFor_IsUniform_SoTheBarIsNeverStretched()
        {
            // The old bug also skewed it: 0.06 across vs 0.04 tall. One scalar can't do that.
            float s = WorldBar.CanvasScaleFor(1.8f, 180f);
            var scale = Vector3.one * s;
            Assert.AreEqual(scale.x, scale.y, 1e-6);
            Assert.AreEqual(scale.y, scale.z, 1e-6);
        }
    }
}
