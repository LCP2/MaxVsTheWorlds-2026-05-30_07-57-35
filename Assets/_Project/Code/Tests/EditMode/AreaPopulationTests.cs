using NUnit.Framework;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Unit tests for the robot-accumulation maths (v0.5 recut spec §2, MV-223), pinned against the
    /// ticket's own acceptance criteria and the settings' authored defaults
    /// (<see cref="RobotCompositionTuning"/>).
    /// </summary>
    public sealed class AreaPopulationTests
    {
        [Test]
        public void Area1_SpawnsFourLargeAndFourSmall()
        {
            var (large, small) = AreaPopulation.ComposeForArea(1,
                RobotCompositionTuning.DefaultStartLargeCount, RobotCompositionTuning.DefaultStartSmallCount,
                RobotCompositionTuning.DefaultAreaGrowthPct, RobotCompositionTuning.DefaultLargeToSmallRatio,
                RobotCompositionTuning.DefaultLargeShareDriftPerArea);

            Assert.AreEqual(4, large);
            Assert.AreEqual(4, small);
        }

        [Test]
        public void EachArea_HasTenPercentMoreTotalThanThePrevious()
        {
            int area1 = AreaPopulation.TotalForArea(1,
                RobotCompositionTuning.DefaultStartLargeCount, RobotCompositionTuning.DefaultStartSmallCount,
                RobotCompositionTuning.DefaultAreaGrowthPct);
            int area2 = AreaPopulation.TotalForArea(2,
                RobotCompositionTuning.DefaultStartLargeCount, RobotCompositionTuning.DefaultStartSmallCount,
                RobotCompositionTuning.DefaultAreaGrowthPct);

            Assert.AreEqual(8, area1);
            Assert.AreEqual(9, area2, "round(8 * 1.10) = 9");
        }

        [Test]
        public void Population_CompoundsAcrossTenAreas()
        {
            // round(8 * 1.10^9) = round(18.8636...) = 19
            int area10 = AreaPopulation.TotalForArea(10,
                RobotCompositionTuning.DefaultStartLargeCount, RobotCompositionTuning.DefaultStartSmallCount,
                RobotCompositionTuning.DefaultAreaGrowthPct);

            Assert.AreEqual(19, area10);
        }

        [Test]
        public void LargeShare_StartsAtFiftyPercentInArea1()
        {
            float share = AreaPopulation.LargeShareForArea(1,
                RobotCompositionTuning.DefaultLargeToSmallRatio, RobotCompositionTuning.DefaultLargeShareDriftPerArea);

            Assert.AreEqual(0.5f, share, 1e-4);
        }

        [Test]
        public void LargeShare_DriftsToroughlySeventyPercentByArea10()
        {
            float share = AreaPopulation.LargeShareForArea(10,
                RobotCompositionTuning.DefaultLargeToSmallRatio, RobotCompositionTuning.DefaultLargeShareDriftPerArea);

            // 0.5 + 0.022 * 9 = 0.698
            Assert.AreEqual(0.698f, share, 1e-4);
        }

        [Test]
        public void LargeShare_NeverExceedsAFullPopulation()
        {
            float share = AreaPopulation.LargeShareForArea(200, 1f, 0.022f);

            Assert.LessOrEqual(share, 1f);
        }

        [Test]
        public void Compose_LargeAndSmallAlwaysSumToTheAreaTotal()
        {
            for (int area = 1; area <= 10; area++)
            {
                var (large, small) = AreaPopulation.ComposeForArea(area,
                    RobotCompositionTuning.DefaultStartLargeCount, RobotCompositionTuning.DefaultStartSmallCount,
                    RobotCompositionTuning.DefaultAreaGrowthPct, RobotCompositionTuning.DefaultLargeToSmallRatio,
                    RobotCompositionTuning.DefaultLargeShareDriftPerArea);
                int total = AreaPopulation.TotalForArea(area,
                    RobotCompositionTuning.DefaultStartLargeCount, RobotCompositionTuning.DefaultStartSmallCount,
                    RobotCompositionTuning.DefaultAreaGrowthPct);

                Assert.AreEqual(total, large + small, $"area {area}: composition must not drop or invent robots");
            }
        }

        [Test]
        public void Compose_LargeShareOfTheSplitClimbsAcrossAreas()
        {
            var (large1, small1) = AreaPopulation.ComposeForArea(1, 4f, 4f, 10f, 1f, 0.022f);
            var (large10, small10) = AreaPopulation.ComposeForArea(10, 4f, 4f, 10f, 1f, 0.022f);

            float shareAt1 = (float)large1 / (large1 + small1);
            float shareAt10 = (float)large10 / (large10 + small10);

            Assert.Greater(shareAt10, shareAt1);
        }
    }
}
