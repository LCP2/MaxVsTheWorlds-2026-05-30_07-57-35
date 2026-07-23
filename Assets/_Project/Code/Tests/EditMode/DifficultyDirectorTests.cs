using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>Unit tests for the Invasion Level curve (YT-181) — pure maths, no clock, no scene.</summary>
    public sealed class DifficultyDirectorTests
    {
        [TearDown]
        public void ClearOverrides()
        {
            DevTuning.Reset();
            DifficultyDirector.Reset();
        }

        [Test]
        public void StartsAtTheAuthoredStart_WithNoTimeAndNoSheds()
        {
            Assert.AreEqual(0f, DifficultyDirector.LevelAt(0f, 0, 0f, 0.05f, 1.5f, 10f), 1e-4);
        }

        [Test]
        public void RisesWithElapsedTime()
        {
            float at10s = DifficultyDirector.LevelAt(10f, 0, 0f, 0.05f, 1.5f, 10f);
            Assert.AreEqual(0.5f, at10s, 1e-4);
        }

        [Test]
        public void BumpsUpOnEachShedDestroyed()
        {
            float noSheds = DifficultyDirector.LevelAt(0f, 0, 0f, 0.05f, 1.5f, 10f);
            float oneShed = DifficultyDirector.LevelAt(0f, 1, 0f, 0.05f, 1.5f, 10f);
            float twoSheds = DifficultyDirector.LevelAt(0f, 2, 0f, 0.05f, 1.5f, 10f);

            Assert.AreEqual(1.5f, oneShed - noSheds, 1e-4);
            Assert.AreEqual(1.5f, twoSheds - oneShed, 1e-4);
        }

        [Test]
        public void TimeAndShedsCombine()
        {
            float level = DifficultyDirector.LevelAt(20f, 2, 0f, 0.05f, 1.5f, 10f);
            Assert.AreEqual(1f + 3f, level, 1e-4); // 0.05*20 + 1.5*2
        }

        [Test]
        public void ClampsToTheCeiling_EvenWithSheds()
        {
            float level = DifficultyDirector.LevelAt(1000f, 50, 0f, 0.05f, 1.5f, 10f);
            Assert.AreEqual(10f, level, 1e-4);
        }

        [Test]
        public void ClampsToTheStart_NegativeElapsedNeverGoesBelowIt()
        {
            float level = DifficultyDirector.LevelAt(-100f, 0, 2f, 0.05f, 1.5f, 10f);
            Assert.AreEqual(2f, level, 1e-4);
        }

        [Test]
        public void HandlesAnInvertedCurve_MaxBelowStart()
        {
            // A degenerate curve (max authored below start) must still produce a value inside the
            // range it actually declared, whichever way round that is.
            float level = DifficultyDirector.LevelAt(1000f, 50, 5f, 0.05f, 1.5f, 1f);
            Assert.AreEqual(5f, level, 1e-4);
        }

        // --- the two multipliers gameplay actually reads ---

        [Test]
        public void SpawnAndToughnessMultipliers_AreNeutralAtRunStart()
        {
            DifficultyDirector.Reset();
            DevTuning.EscalationRate = 0f;
            DevTuning.EscalationPerShedBump = 0f;

            Assert.AreEqual(1f, DifficultyDirector.SpawnIntervalMultiplier, 1e-4);
            Assert.AreEqual(1f, DifficultyDirector.ToughnessMultiplier, 1e-4);
        }

        [Test]
        public void SpawnIntervalMultiplier_ShrinksAsTheLevelClimbs()
        {
            DifficultyDirector.Reset();
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationRate = 0f;
            DevTuning.EscalationPerShedBump = 1f;
            DevTuning.EscalationMax = 1f;

            DifficultyDirector.ReportShedDestroyed(); // level -> 1 (the ceiling): fully escalated

            Assert.Less(DifficultyDirector.SpawnIntervalMultiplier, 1f,
                "a fully escalated run must speed spawns up, not leave the interval unchanged");
        }

        [Test]
        public void ToughnessMultiplier_GrowsAsTheLevelClimbs()
        {
            DifficultyDirector.Reset();
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationRate = 0f;
            DevTuning.EscalationPerShedBump = 1f;
            DevTuning.EscalationMax = 1f;

            DifficultyDirector.ReportShedDestroyed(); // level -> 1 (the ceiling): fully escalated

            Assert.Greater(DifficultyDirector.ToughnessMultiplier, 1f,
                "a fully escalated run must make robots tougher, not leave them unchanged");
        }

        [Test]
        public void DestroyingAShed_RaisesTheLevel_RatherThanLoweringIt()
        {
            // The whole point of the ticket: clearing a source must RAISE the stakes.
            DifficultyDirector.Reset();
            DevTuning.EscalationRate = 0f;

            float before = DifficultyDirector.Level;
            DifficultyDirector.ReportShedDestroyed();
            float after = DifficultyDirector.Level;

            Assert.Greater(after, before,
                "destroying a factory shed must raise the Invasion Level, not lower it");
        }

        [Test]
        public void Tick_AdvancesElapsed_AndClampsNegativeDt()
        {
            DifficultyDirector.Reset();
            DifficultyDirector.Tick(1.5f);
            Assert.AreEqual(1.5f, DifficultyDirector.Elapsed, 1e-4);

            DifficultyDirector.Tick(-100f); // must never run the clock backwards
            Assert.AreEqual(1.5f, DifficultyDirector.Elapsed, 1e-4);
        }

        [Test]
        public void Reset_ZerosBothTheClockAndTheShedCount()
        {
            DifficultyDirector.Tick(5f);
            DifficultyDirector.ReportShedDestroyed();

            DifficultyDirector.Reset();

            Assert.AreEqual(0f, DifficultyDirector.Elapsed, 1e-4);
            Assert.AreEqual(0, DifficultyDirector.ShedsDestroyed);
        }
    }
}
