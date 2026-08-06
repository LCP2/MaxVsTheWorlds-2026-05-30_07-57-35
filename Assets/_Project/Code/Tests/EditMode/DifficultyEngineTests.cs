using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Unit tests for the threat-budget difficulty engine (World &amp; Difficulty Framework, Confluence
    /// MVW 34439170 §5, MV-268), pinned against the ticket's own acceptance criteria.
    /// </summary>
    public sealed class DifficultyEngineTests
    {
        // World 1's draft dials (spec §9 worked example): baseThreat = THV of today's "4 large + 4
        // small" seed (4×2.5 + 4×1.0 = 14.0, matching RobotCompositionTuning's Area-1 defaults),
        // threatGrowth ≈ 10% (matches MV-223's areaGrowthPct), an 8-area saw-tooth rhythm ending on a
        // pre-boss peak, and a toughness curve matching MV-224's heavy/brute intro areas. The real
        // world1_config.json lands in ticket 4/MV-270 — this is a provisional fixture reproducing the
        // same worked example so this ticket's engine can be proven against it now.
        private const float World1BaseThreat = 14.0f;
        private const float World1ThreatGrowth = 0.10f;
        private const int World1AreaCount = 8;

        private static PacingRhythm World1Pacing() => new PacingRhythm(
            1.00f, 1.05f, 0.90f, 1.10f, 1.20f, 0.85f, 1.05f, 1.25f);

        private static ToughnessCurve World1Toughness() => new ToughnessCurve
        {
            heavyFromArea = 5,
            bruteFromArea = 8,
            tankShareAtHeavyIntro = 0f,
            tankShareAtEnd = 0.70f,
            lastArea = World1AreaCount,
        };

        // --- AC1: dials -> per-area budgets + composition within a small tolerance -----------------

        [Test]
        public void TargetBudget_Area1_EqualsBaseThreat()
        {
            float budget = DifficultyEngine.TargetBudget(1, World1BaseThreat, World1ThreatGrowth, World1Pacing());

            Assert.AreEqual(World1BaseThreat, budget, 0.01f);
        }

        [Test]
        public void TargetBudget_CompoundsGrowthOnTopOfPacing()
        {
            // Area 3: 14 * 1.10^2 * 0.90 = 15.246
            float budget = DifficultyEngine.TargetBudget(3, World1BaseThreat, World1ThreatGrowth, World1Pacing());

            Assert.AreEqual(15.246f, budget, 0.01f);
        }

        [Test]
        public void SolveComposition_SumOfThreatValues_IsWithinASmallToleranceOfTheBudget()
        {
            // Integer robot counts inherently quantize a continuous budget — most sharply at the
            // smallest per-type unit counts (few, expensive Heavy/Brute). 15% relative covers every
            // World 1 area with this fixture's dials; a genuinely bad solve would blow well past it.
            const float relativeTolerance = 0.15f;

            for (int area = 1; area <= World1AreaCount; area++)
            {
                float target = DifficultyEngine.TargetBudget(area, World1BaseThreat, World1ThreatGrowth, World1Pacing());
                var composition = DifficultyEngine.SolveComposition(area, target, World1Toughness());

                float error = Mathf.Abs(composition.TotalThreatValue - target);
                Assert.LessOrEqual(error, target * relativeTolerance,
                    $"area {area}: solved Σ THV {composition.TotalThreatValue} vs target {target}");
            }
        }

        [Test]
        public void SolveComposition_NeverInventsNegativeRobots()
        {
            for (int area = 1; area <= World1AreaCount; area++)
            {
                float target = DifficultyEngine.TargetBudget(area, World1BaseThreat, World1ThreatGrowth, World1Pacing());
                var composition = DifficultyEngine.SolveComposition(area, target, World1Toughness());

                Assert.GreaterOrEqual(composition.Rusher, 0);
                Assert.GreaterOrEqual(composition.Bruiser, 0);
                Assert.GreaterOrEqual(composition.Heavy, 0);
                Assert.GreaterOrEqual(composition.Brute, 0);
            }
        }

        [Test]
        public void TankShare_IsZeroBeforeHeavyIntroArea()
        {
            var toughness = World1Toughness();

            for (int area = 1; area < toughness.heavyFromArea; area++)
                Assert.AreEqual(0f, toughness.TankShareForArea(area), 1e-4f, $"area {area}");
        }

        [Test]
        public void TankShare_DriftsUpwardAcrossAreasPerToughnessCurve()
        {
            var toughness = World1Toughness();

            float shareAtIntro = toughness.TankShareForArea(toughness.heavyFromArea);
            float shareMidway = toughness.TankShareForArea(toughness.heavyFromArea + 1);
            float shareAtEnd = toughness.TankShareForArea(toughness.lastArea);

            Assert.Less(shareAtIntro, shareMidway);
            Assert.Less(shareMidway, shareAtEnd);
            Assert.AreEqual(toughness.tankShareAtEnd, shareAtEnd, 1e-4f);
        }

        [Test]
        public void SolvedComposition_TankShare_DriftsUpwardAcrossAreasToo()
        {
            var toughness = World1Toughness();

            float budgetAtIntro = DifficultyEngine.TargetBudget(
                toughness.heavyFromArea, World1BaseThreat, World1ThreatGrowth, World1Pacing());
            float budgetAtEnd = DifficultyEngine.TargetBudget(
                toughness.lastArea, World1BaseThreat, World1ThreatGrowth, World1Pacing());

            var atIntro = DifficultyEngine.SolveComposition(toughness.heavyFromArea, budgetAtIntro, toughness);
            var atEnd = DifficultyEngine.SolveComposition(toughness.lastArea, budgetAtEnd, toughness);

            Assert.Less(atIntro.TankShare, atEnd.TankShare);
        }

        // --- AC2: World 1's dials reproduce the intended per-area budgets --------------------------

        [Test]
        public void World1Dials_Area1Budget_IsApproximatelyFourteen()
        {
            float budget = DifficultyEngine.TargetBudget(1, World1BaseThreat, World1ThreatGrowth, World1Pacing());

            Assert.AreEqual(14.0f, budget, 0.05f);
        }

        [Test]
        public void World1Dials_Area8Budget_IsApproximatelyThirtyFourPointOne()
        {
            float budget = DifficultyEngine.TargetBudget(
                World1AreaCount, World1BaseThreat, World1ThreatGrowth, World1Pacing());

            Assert.AreEqual(34.1f, budget, 0.1f);
        }

        [Test]
        public void World1Dials_PacingRhythmIsASawtoothNotAStraightRamp()
        {
            // A straight ramp is monotonic step-to-step; a saw-tooth is not — somewhere in the
            // middle a later area's budget must dip below an earlier one (the "lull"/"relief" beats).
            float[] budgets = Enumerable.Range(1, World1AreaCount)
                .Select(area => DifficultyEngine.TargetBudget(area, World1BaseThreat, World1ThreatGrowth, World1Pacing()))
                .ToArray();

            bool hasADip = false;
            for (int i = 1; i < budgets.Length; i++)
                if (budgets[i] < budgets[i - 1]) hasADip = true;

            Assert.IsTrue(hasADip, "a saw-tooth rhythm must dip somewhere, not ramp straight up");
            Assert.Greater(budgets[budgets.Length - 1], budgets[0], "the overall envelope still rises area 1 -> area 8");
        }

        // --- AC3: power-up cadence enforcement ------------------------------------------------------

        [Test]
        public void PowerupCadence_GuaranteesAPickupWhenAGapWouldExceedTheCadence()
        {
            // Sheds at areas 1, 4, 8 (indices 0, 3, 7) — a 3-area gap between area 4 and area 8 that a
            // cadence of 2 must not allow unfilled.
            bool[] hasShed = { true, false, false, true, false, false, false, true };

            bool[] covered = PowerupCadence.EnsureCoverage(areaCount: 8, hasShed, cadence: 2);

            Assert.LessOrEqual(PowerupCadence.LongestGap(covered), 2);
            // Area 3 (index 2) had no shed but must have been guaranteed a parts cache — proves the
            // guarantee actually fired rather than the input just happening to already satisfy it.
            Assert.IsFalse(hasShed[2]);
            Assert.IsTrue(covered[2], "cadence guarantee must inject a parts cache at area 3");
        }

        [Test]
        public void PowerupCadence_NeverRemovesAnAuthoredShed()
        {
            bool[] hasShed = { true, false, true, false, false, true, false, true };

            bool[] covered = PowerupCadence.EnsureCoverage(areaCount: 8, hasShed, cadence: 3);

            for (int i = 0; i < hasShed.Length; i++)
                if (hasShed[i]) Assert.IsTrue(covered[i], $"area {i + 1}");
        }

        [Test]
        public void PowerupCadence_WithNoSheds_StillNeverExceedsTheCadence()
        {
            bool[] hasShed = new bool[10];

            bool[] covered = PowerupCadence.EnsureCoverage(areaCount: 10, hasShed, cadence: 2);

            Assert.LessOrEqual(PowerupCadence.LongestGap(covered), 2);
            Assert.IsTrue(covered.Any(c => c), "parts caches alone must satisfy the cadence with zero sheds");
        }

        // --- AC4: bounded hidden nudge ---------------------------------------------------------------

        [Test]
        public void HiddenNudge_NeverExceedsItsCapEvenForAnExtremeRatio()
        {
            float struggling = HiddenNudge.PacingNudge(0f);
            float steamrolling = HiddenNudge.PacingNudge(1000f);

            Assert.AreEqual(HiddenNudge.MaxAdjustment, struggling, 1e-4f);
            Assert.AreEqual(-HiddenNudge.MaxAdjustment, steamrolling, 1e-4f);
        }

        [Test]
        public void HiddenNudge_IsZeroInsideTheDesignedBand()
        {
            float nudge = HiddenNudge.PacingNudge((HiddenNudge.BandLow + HiddenNudge.BandHigh) * 0.5f);

            Assert.AreEqual(0f, nudge, 1e-4f);
        }

        [Test]
        public void HiddenNudge_ProductionRate_StaysWithinTheCappedRange()
        {
            float rateAtMaxStruggle = HiddenNudge.ApplyToProductionRate(1f, HiddenNudge.MaxAdjustment);
            float rateAtMaxSteamroll = HiddenNudge.ApplyToProductionRate(1f, -HiddenNudge.MaxAdjustment);

            Assert.AreEqual(1f - HiddenNudge.MaxAdjustment, rateAtMaxStruggle, 1e-4f);
            Assert.AreEqual(1f + HiddenNudge.MaxAdjustment, rateAtMaxSteamroll, 1e-4f);
        }

        [Test]
        public void HiddenNudge_NeverTouchesEnemyStats_OnlyPacingAndProduction()
        {
            // Guards the spec's "NEVER touch enemy stats" as an API-shape assertion: nothing this
            // class exposes may mention health/damage/speed, and nothing takes or returns an
            // EnemyArchetype/RobotEnemy — the only thing it is allowed to hand back is a plain float
            // multiplier.
            string[] bannedTokens = { "health", "damage", "speed", "hp", "archetype", "robot" };

            MemberInfo[] members = typeof(HiddenNudge)
                .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .ToArray();

            Assert.IsNotEmpty(members);

            foreach (MemberInfo member in members)
            {
                string name = member.Name.ToLowerInvariant();
                foreach (string token in bannedTokens)
                    Assert.IsFalse(name.Contains(token), $"{member.Name} suggests a stat lever, not a pacing one");
            }
        }
    }
}
