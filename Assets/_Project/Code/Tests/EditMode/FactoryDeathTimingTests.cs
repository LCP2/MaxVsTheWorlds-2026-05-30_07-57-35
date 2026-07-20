using MaxWorlds.VFX;
using NUnit.Framework;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-109. The complaint was that the factory's death resolved too fast to read, so the thing
    /// worth pinning is the SHAPE of the beat — that it is meaningfully longer than the single frame
    /// it replaced, that the stages land in order, and that the husk is still standing when the blast
    /// that takes it down goes off.
    /// </summary>
    public class FactoryDeathTimingTests
    {
        [Test]
        public void TheBeatIsNoticeablyLongerThanTheSingleBangItReplaced()
        {
            // The old version emitted everything on one frame; the longest particle life was 1.7 s,
            // so that was the whole event. This has to be clearly more than that or the ticket is
            // not addressed.
            Assert.That(FactoryDeathTiming.Total, Is.GreaterThan(2f));
        }

        [Test]
        public void StagesLandInOrder()
        {
            Assert.That(FactoryDeathTiming.FailToBlast, Is.GreaterThan(0f));
            Assert.That(FactoryDeathTiming.BlastToSmoke, Is.GreaterThan(0f));
            Assert.That(FactoryDeathTiming.SmokeToEmbers, Is.GreaterThan(0f));
        }

        [Test]
        public void TheBuildingStandsWhileItIsMerelyFailing()
        {
            // If it started sagging during the first, contained failure, the blast would land on a
            // building that had already half gone.
            Assert.That(FactoryDeathTiming.CollapseProgress(0f), Is.EqualTo(0f));
            Assert.That(FactoryDeathTiming.CollapseProgress(FactoryDeathTiming.ShudderSeconds * 0.5f),
                Is.EqualTo(0f));
        }

        [Test]
        public void ItStartsFallingExactlyWhenTheBlastLands()
        {
            float justAfter = FactoryDeathTiming.ShudderSeconds + 0.05f;
            Assert.That(FactoryDeathTiming.CollapseProgress(justAfter), Is.GreaterThan(0f));
        }

        [Test]
        public void TheCollapseCompletes()
        {
            float end = FactoryDeathTiming.ShudderSeconds + FactoryDeathTiming.CollapseSeconds;
            Assert.That(FactoryDeathTiming.CollapseProgress(end), Is.EqualTo(1f).Within(1e-4f));
            // And stays finished — the husk destroys itself at 1, so anything past the end must not
            // wrap back under it and resurrect the wreck.
            Assert.That(FactoryDeathTiming.CollapseProgress(end + 5f), Is.GreaterThanOrEqualTo(1f));
        }

        [Test]
        public void TheCollapseOnlyEverGoesDown()
        {
            float previous = -1f;
            for (float t = 0f; t < 3f; t += 0.05f)
            {
                float p = FactoryDeathTiming.CollapseProgress(t);
                Assert.That(p, Is.GreaterThanOrEqualTo(previous - 1e-4f), $"rose again at {t}s");
                previous = p;
            }
        }

        [Test]
        public void TheHuskIsGoneBeforeTheSmokeThins()
        {
            // A wreck still visibly sinking after the smoke has cleared reads as a second, separate
            // event. The collapse has to finish inside the beat.
            float collapseEnds = FactoryDeathTiming.ShudderSeconds + FactoryDeathTiming.CollapseSeconds;
            Assert.That(collapseEnds, Is.LessThan(FactoryDeathTiming.Total));
        }

        [Test]
        public void ShudderStopsWhenTheCollapseStarts()
        {
            Assert.That(FactoryDeathTiming.ShudderOffset(FactoryDeathTiming.ShudderSeconds + 0.1f, 0.06f),
                Is.EqualTo(0f));
        }

        [Test]
        public void ShudderBuilds_SoTheFailureReadsAsGettingWorse()
        {
            // Sampled at the sine's own peaks would be fragile; compare envelopes over a window.
            float early = 0f, late = 0f;
            for (float t = 0f; t < FactoryDeathTiming.ShudderSeconds * 0.35f; t += 0.005f)
                early = System.Math.Max(early, System.Math.Abs(FactoryDeathTiming.ShudderOffset(t, 0.06f)));
            for (float t = FactoryDeathTiming.ShudderSeconds * 0.65f;
                 t < FactoryDeathTiming.ShudderSeconds; t += 0.005f)
                late = System.Math.Max(late, System.Math.Abs(FactoryDeathTiming.ShudderOffset(t, 0.06f)));

            Assert.That(late, Is.GreaterThan(early));
        }

        [Test]
        public void ShudderStaysSubtle()
        {
            // It is a building shaking, not a building dancing. Anything much past a few centimetres
            // reads as the mesh being wobbled.
            for (float t = 0f; t <= FactoryDeathTiming.ShudderSeconds; t += 0.01f)
                Assert.That(System.Math.Abs(FactoryDeathTiming.ShudderOffset(t, 0.06f)),
                    Is.LessThanOrEqualTo(0.06f + 1e-4f));
        }
    }
}
