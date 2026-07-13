using NUnit.Framework;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The REPLAY button hung 90px outside the results panel (YT-81). These pin the containment and
    /// the alignment, because "it looks right" is not a thing a build can check — and the reason the
    /// bug shipped is that nothing anywhere asserted the card's own edges.
    /// </summary>
    public sealed class ResultLayoutTests
    {
        private static float LeftEdge(float centreX) => centreX - ResultLayout.ButtonWidth * 0.5f;
        private static float RightEdge(float centreX) => centreX + ResultLayout.ButtonWidth * 0.5f;

        [Test]
        public void BothButtonsSitFullyInsideThePanel()
        {
            // The actual bug: REPLAY's centre was -300 with a 300-wide, centre-pivoted rect, so its
            // left edge landed at -450 against a panel edge of -360.
            Assert.GreaterOrEqual(LeftEdge(ResultLayout.LeftButtonX), ResultLayout.PanelLeft,
                "REPLAY overhangs the panel's left edge — this is YT-81, back again");
            Assert.LessOrEqual(RightEdge(ResultLayout.RightButtonX), ResultLayout.PanelRight,
                "NEXT WORLD overhangs the panel's right edge");
        }

        [Test]
        public void BothButtonsShareTheStatRowsContentMargin()
        {
            // The AC: the same left inset as the TIME / ROBOTS DESTROYED rows above them.
            float statLabelLeft = ResultLayout.StatLabelX - ResultLayout.StatCellWidth * 0.5f;
            float statValueRight = ResultLayout.StatValueX + ResultLayout.StatCellWidth * 0.5f;

            Assert.AreEqual(statLabelLeft, LeftEdge(ResultLayout.LeftButtonX), 1e-3,
                "REPLAY must start where the TIME row starts");
            Assert.AreEqual(statValueRight, RightEdge(ResultLayout.RightButtonX), 1e-3,
                "NEXT WORLD must end where the stat values end");
        }

        [Test]
        public void TheCardIsSymmetric()
        {
            Assert.AreEqual(ResultLayout.ContentMargin,
                LeftEdge(ResultLayout.LeftButtonX) - ResultLayout.PanelLeft, 1e-3);
            Assert.AreEqual(ResultLayout.ContentMargin,
                ResultLayout.PanelRight - RightEdge(ResultLayout.RightButtonX), 1e-3);
        }

        [Test]
        public void TheTwoButtonsDoNotOverlap_AndAreEvenlySpaced()
        {
            Assert.Greater(ResultLayout.ButtonGap, 0f, "the CTAs are on top of each other");

            // Even spacing: the gap between them should read as deliberate next to the margins
            // holding them off the panel edges, not as a squeeze or a chasm.
            Assert.GreaterOrEqual(ResultLayout.ButtonGap, ResultLayout.ContentMargin * 0.5f);
            Assert.LessOrEqual(ResultLayout.ButtonGap, ResultLayout.ContentMargin * 2f);
        }

        [Test]
        public void TheContentColumnActuallyFitsTwoButtons()
        {
            // Guards the next person who widens a button or the margin without widening the panel:
            // it would silently push REPLAY back out of the card, which is exactly how we got here.
            Assert.LessOrEqual(ResultLayout.ButtonWidth * 2f, ResultLayout.ContentWidth,
                "two buttons no longer fit the content column — widen the panel, don't overflow it");
        }
    }
}
