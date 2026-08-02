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

        // --- ToughSplitForArea (v0.5 recut spec §2-3, MV-224) -----------------------------------

        [Test]
        public void ToughSplit_BeforeHeavyIntroArea_IsAllBruiser()
        {
            var split = AreaPopulation.ToughSplitForArea(4, largeCount: 10,
                heavyIntroArea: 5f, bruteIntroArea: 8f, toughSubstitutionPct: 25f);

            Assert.AreEqual((10, 0, 0), split);
        }

        [Test]
        public void ToughSplit_AtHeavyIntroArea_SubstitutesTheConfiguredPercent()
        {
            var split = AreaPopulation.ToughSplitForArea(5, largeCount: 12,
                heavyIntroArea: 5f, bruteIntroArea: 8f, toughSubstitutionPct: 25f);

            Assert.AreEqual((9, 3, 0), split, "25% of 12 large slots = 3 heavy, brute not introduced yet");
        }

        [Test]
        public void ToughSplit_AtBruteIntroArea_StacksBothTiersOnTopOfEachOther()
        {
            // Spec §2 table, Area 8: Heavy + Brute both present. largeCount chosen so 25% lands on
            // a whole number and the assertion isn't at the mercy of a rounding tie-break.
            var split = AreaPopulation.ToughSplitForArea(8, largeCount: 12,
                heavyIntroArea: 5f, bruteIntroArea: 8f, toughSubstitutionPct: 25f);

            Assert.AreEqual((6, 3, 3), split,
                "25% heavy + 25% brute stack once both are introduced, leaving the rest bruiser");
        }

        [Test]
        public void ToughSplit_NeverExceedsTheAreasActualLargeCount()
        {
            // An extreme substitution % must not invent robots beyond what the area actually has.
            var split = AreaPopulation.ToughSplitForArea(10, largeCount: 5,
                heavyIntroArea: 5f, bruteIntroArea: 8f, toughSubstitutionPct: 90f);

            Assert.AreEqual(5, split.Bruiser + split.Heavy + split.Brute);
            Assert.GreaterOrEqual(split.Bruiser, 0);
        }

        [Test]
        public void ToughSplit_PartsAlwaysSumToTheLargeCount()
        {
            for (int area = 1; area <= 10; area++)
            {
                var split = AreaPopulation.ToughSplitForArea(area, largeCount: 13,
                    heavyIntroArea: 5f, bruteIntroArea: 8f, toughSubstitutionPct: 25f);

                Assert.AreEqual(13, split.Bruiser + split.Heavy + split.Brute, $"area {area}");
            }
        }
    }
}
