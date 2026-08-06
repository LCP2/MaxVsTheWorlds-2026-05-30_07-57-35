using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The two power scores and the fun band (Confluence MVW 34439170 §4, MV-269), pinned against this
    /// ticket's AC2: MPL/EPL compute per the spec's formulas, EPL's half-weight look-ahead term is
    /// real, and R is reported per area.
    /// </summary>
    public sealed class PowerScoringTests
    {
        /// <summary>A small 4-combat-area world, no shed/geometry concerns — PowerScoring never touches
        /// gates or wall geometry, so this fixture skips them entirely (unlike <c>WorldMapLoaderTests</c>'
        /// fixtures, which need valid walls). heavyFromArea/bruteFromArea sit past the fixture's own
        /// area count so every area solves a pure Rusher/Bruiser mix — kept simple on purpose, since
        /// this ticket is proving the EPL/MPL formulas, not re-proving MV-268's tank-share drift.</summary>
        private static WorldConfig FixtureWorld() => new WorldConfig
        {
            world = "Test World",
            dials = new WorldDials
            {
                areaCount = 4,
                baseThreat = 14f,
                threatGrowth = 0.10f,
                band = new WorldBand { up = 0.4f, down = -0.15f },
                pacingRhythm = new[] { 1.0f, 1.05f, 0.9f, 1.1f },
                toughnessCurve = new WorldToughnessCurve
                {
                    heavyFromArea = 10, bruteFromArea = 12, toughSubstitutionPct = 0.25f, tankShareEnd = 0.7f,
                },
                powerupCadence = 2,
            },
            enemyTypes = new WorldEnemyTypes
            {
                small = new WorldEnemyTypeEntry { thv = 1.0f },
                large = new WorldEnemyTypeEntry { thv = 2.5f },
                heavy = new WorldEnemyTypeEntry { thv = 4.5f },
                brute = new WorldEnemyTypeEntry { thv = 7.0f },
            },
            areas = new[]
            {
                new WorldArea { id = "a1", index = 1, role = "normal", garrisonDensity = "light" },
                new WorldArea { id = "a2", index = 2, role = "normal", garrisonDensity = "normal" },
                new WorldArea { id = "a3", index = 3, role = "normal", garrisonDensity = "heavy", hasShed = true },
                new WorldArea { id = "a4", index = 4, role = "normal", garrisonDensity = "none" },
            },
            gates = System.Array.Empty<WorldGate>(),
        };

        // --- MPL --------------------------------------------------------------------------------------

        [Test]
        public void PrimaryEffectiveDps_MultipliesEveryFactorTogether()
        {
            float dps = PowerScoring.PrimaryEffectiveDps(damage: 4f, fireRate: 10f, hitFraction: 0.65f,
                areaFactor: 1f, rangeFactor: 1f);

            Assert.AreEqual(26f, dps, 1e-4f);
        }

        [Test]
        public void PrimaryEffectiveDps_ClampsHitFractionToZeroOne()
        {
            float dps = PowerScoring.PrimaryEffectiveDps(damage: 10f, fireRate: 1f, hitFraction: 5f,
                areaFactor: 1f, rangeFactor: 1f);

            Assert.AreEqual(10f, dps, 1e-4f, "hitFraction must clamp to 1.0, not multiply by 5");
        }

        [Test]
        public void MaxPowerLevel_SumsContributionsThenAppliesSurvivability()
        {
            float mpl = PowerScoring.MaxPowerLevel(primaryEffectiveDps: 20f, secondaryContribution: 5f,
                abilityContribution: 3f, survivabilityFactor: 1.2f);

            Assert.AreEqual((20f + 5f + 3f) * 1.2f, mpl, 1e-4f);
        }

        // --- EPL and its half-weight look-ahead term ---------------------------------------------------

        [Test]
        public void EnemyPowerLevel_CombinesCurrentAreaFullWeightWithNextAreaHalfWeight()
        {
            WorldConfig cfg = FixtureWorld();
            float sigmaAt3 = cfg.SigmaThreatValue(3);
            float sigmaAt4 = cfg.SigmaThreatValue(4);
            Assert.Greater(sigmaAt4, 0f, "fixture must have a nonzero next-area term for this test to mean anything");

            float epl = PowerScoring.EnemyPowerLevel(3, cfg);

            Assert.AreEqual(sigmaAt3 + sigmaAt4 * 0.5f, epl, 1e-3f);
            Assert.Greater(epl, sigmaAt3, "the half-weight look-ahead term must actually add something");
        }

        [Test]
        public void EnemyPowerLevel_LastArea_HasNoLookAheadPastTheWorldsEnd()
        {
            WorldConfig cfg = FixtureWorld(); // dials.areaCount == 4

            float epl = PowerScoring.EnemyPowerLevel(4, cfg);

            Assert.AreEqual(0f, cfg.SigmaThreatValue(5), 1e-6f, "area 5 does not exist in this 4-area fixture");
            Assert.AreEqual(cfg.SigmaThreatValue(4), epl, 1e-4f);
        }

        // --- R reported per area -------------------------------------------------------------------

        [Test]
        public void BandRatioPerArea_ReturnsOneEntryPerCombatArea_MatchingBandRatio()
        {
            WorldConfig cfg = FixtureWorld();
            const float mpl = 20f;

            float[] ratios = PowerScoring.BandRatioPerArea(cfg, mpl);

            Assert.AreEqual(cfg.dials.areaCount, ratios.Length);
            for (int i = 0; i < ratios.Length; i++)
            {
                float expected = PowerScoring.BandRatio(mpl, PowerScoring.EnemyPowerLevel(i + 1, cfg));
                Assert.AreEqual(expected, ratios[i], 1e-4f, $"area {i + 1}");
            }
        }

        [Test]
        public void BandRatio_IsZeroWhenEplIsZero()
        {
            Assert.AreEqual(0f, PowerScoring.BandRatio(50f, 0f));
        }

        [Test]
        public void WithinBand_TrueInsideRangeFalseOutside()
        {
            Assert.IsTrue(PowerScoring.WithinBand(1.0f));
            Assert.IsTrue(PowerScoring.WithinBand(PowerScoring.BandLow));
            Assert.IsTrue(PowerScoring.WithinBand(PowerScoring.BandHigh));
            Assert.IsFalse(PowerScoring.WithinBand(0.5f));
            Assert.IsFalse(PowerScoring.WithinBand(2.0f));
        }
    }
}
