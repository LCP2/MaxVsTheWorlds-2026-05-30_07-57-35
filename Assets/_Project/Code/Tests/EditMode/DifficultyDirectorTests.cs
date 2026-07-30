using NUnit.Framework;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>Unit tests for the Invasion Level curve (YT-181, restructured into a bounded run at
    /// YT-210) — pure maths, no clock, no scene.</summary>
    public sealed class DifficultyDirectorTests
    {
        [TearDown]
        public void ClearOverrides()
        {
            DevTuning.Reset();
            DifficultyDirector.Reset();
        }

        [Test]
        public void StartsAtTheAuthoredStart_WithNoTimeElapsed()
        {
            Assert.AreEqual(0f, DifficultyDirector.LevelAt(0f, 0f, 0.05f, 10f), 1e-4);
        }

        [Test]
        public void RisesWithElapsedTime()
        {
            float at10s = DifficultyDirector.LevelAt(10f, 0f, 0.05f, 10f);
            Assert.AreEqual(0.5f, at10s, 1e-4);
        }

        [Test]
        public void ClampsToTheCeiling()
        {
            float level = DifficultyDirector.LevelAt(1000f, 0f, 0.05f, 10f);
            Assert.AreEqual(10f, level, 1e-4);
        }

        [Test]
        public void ClampsToTheStart_NegativeElapsedNeverGoesBelowIt()
        {
            float level = DifficultyDirector.LevelAt(-100f, 2f, 0.05f, 10f);
            Assert.AreEqual(2f, level, 1e-4);
        }

        [Test]
        public void HandlesAnInvertedCurve_MaxBelowStart()
        {
            // A degenerate curve (max authored below start) must still produce a value inside the
            // range it actually declared, whichever way round that is.
            float level = DifficultyDirector.LevelAt(1000f, 5f, 0.05f, 1f);
            Assert.AreEqual(5f, level, 1e-4);
        }

        // --- YT-210: the bounded run — Level reaches Max at RunLengthSeconds with no shed kills ---

        [Test]
        public void LevelAt_ReachesMax_ExactlyAtRunLengthSeconds_WithNoSheds()
        {
            DifficultyDirector.Reset();
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationMax = 10f;
            DevTuning.RunLengthSeconds = 360f;

            DifficultyDirector.Tick(360f);

            Assert.AreEqual(10f, DifficultyDirector.Level, 1e-3,
                "the Invasion Level must hit Max exactly at RunLengthSeconds with no shed kills");
        }

        [Test]
        public void LevelAt_IsHalfway_AtHalfTheRunLength_WithNoSheds()
        {
            DifficultyDirector.Reset();
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationMax = 10f;
            DevTuning.RunLengthSeconds = 360f;

            DifficultyDirector.Tick(180f);

            Assert.AreEqual(5f, DifficultyDirector.Level, 1e-3);
        }

        [Test]
        public void DerivedRatePerSecond_IsMaxOverRunLength()
        {
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationMax = 10f;
            DevTuning.RunLengthSeconds = 360f;

            Assert.AreEqual(10f / 360f, DifficultyDirector.DerivedRatePerSecond, 1e-5);
        }

        [Test]
        public void ExplicitEscalationRateOverride_WinsOverTheDerivedRate()
        {
            DifficultyDirector.Reset();
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationMax = 10f;
            DevTuning.RunLengthSeconds = 360f;
            DevTuning.EscalationRate = 1f; // a pinned rate should outrank the derived one

            DifficultyDirector.Tick(1f);

            Assert.AreEqual(1f, DifficultyDirector.Level, 1e-4,
                "a pinned Escalation rate override must outrank the derived Max/RunLength rate");
        }

        // --- shed kills SKIP THE CLOCK FORWARD instead of bumping the level directly ---

        [Test]
        public void ShedDestroyed_SkipsTheClockForward_RatherThanBumpingTheLevelDirectly()
        {
            DifficultyDirector.Reset();
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationMax = 10f;
            DevTuning.RunLengthSeconds = 360f;
            DevTuning.EscalationPerShedBump = 180f; // half the run

            float before = DifficultyDirector.Level;
            DifficultyDirector.ReportShedDestroyed();
            float after = DifficultyDirector.Level;

            // 180s skip at rate 10/360 == a 5-level jump, same as if 180s of real time had elapsed.
            Assert.AreEqual(before + 5f, after, 1e-3);
        }

        [Test]
        public void MultipleShedKills_AccumulateTheSkip()
        {
            DifficultyDirector.Reset();
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationMax = 10f;
            DevTuning.RunLengthSeconds = 360f;
            DevTuning.EscalationPerShedBump = 180f;

            DifficultyDirector.ReportShedDestroyed();
            DifficultyDirector.ReportShedDestroyed();

            Assert.AreEqual(10f, DifficultyDirector.Level, 1e-3,
                "two 180s skips on a 360s run must land exactly on the ceiling");
        }

        [Test]
        public void DestroyingAShed_RaisesTheLevel_RatherThanLoweringIt()
        {
            // The whole point of the ticket: clearing a source must RAISE the stakes (and now,
            // shorten the run) rather than lower them. No EscalationRate override here — a shed
            // kill only ever moves the Level by skipping the clock forward, so pinning the rate to
            // zero would (correctly) neutralise the skip along with everything else.
            DifficultyDirector.Reset();

            float before = DifficultyDirector.Level;
            DifficultyDirector.ReportShedDestroyed();
            float after = DifficultyDirector.Level;

            Assert.Greater(after, before,
                "destroying a factory shed must raise the Invasion Level, not lower it");
        }

        [Test]
        public void Elapsed_DoesNotIncludeTheShedSkip()
        {
            // Elapsed is the REAL clock — the shed skip only ever feeds the Level calculation, so a
            // Result-screen "run time" stays honest even after an aggressive run full of shed kills.
            DifficultyDirector.Reset();
            DifficultyDirector.Tick(10f);
            DifficultyDirector.ReportShedDestroyed();

            Assert.AreEqual(10f, DifficultyDirector.Elapsed, 1e-4);
        }

        // --- the two multipliers gameplay actually reads ---

        [Test]
        public void SpawnAndToughnessMultipliers_AreNeutralAtRunStart()
        {
            DifficultyDirector.Reset();
            DevTuning.EscalationRate = 0f;

            Assert.AreEqual(1f, DifficultyDirector.SpawnIntervalMultiplier, 1e-4);
            Assert.AreEqual(1f, DifficultyDirector.ToughnessMultiplier, 1e-4);
        }

        [Test]
        public void SpawnIntervalMultiplier_ShrinksAsTheLevelClimbs()
        {
            // A shed kill only moves the Level by skipping the clock forward, so drive it through
            // RunLengthSeconds/the derived rate rather than pinning EscalationRate to zero.
            DifficultyDirector.Reset();
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationMax = 1f;
            DevTuning.RunLengthSeconds = 1f;
            DevTuning.EscalationPerShedBump = 1f; // one shed's skip == the whole run length

            DifficultyDirector.ReportShedDestroyed(); // level -> 1 (the ceiling): fully escalated

            Assert.Less(DifficultyDirector.SpawnIntervalMultiplier, 1f,
                "a fully escalated run must speed spawns up, not leave the interval unchanged");
        }

        [Test]
        public void ToughnessMultiplier_GrowsAsTheLevelClimbs()
        {
            // A shed kill only moves the Level by skipping the clock forward, so drive it through
            // RunLengthSeconds/the derived rate rather than pinning EscalationRate to zero.
            DifficultyDirector.Reset();
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationMax = 1f;
            DevTuning.RunLengthSeconds = 1f;
            DevTuning.EscalationPerShedBump = 1f; // one shed's skip == the whole run length

            DifficultyDirector.ReportShedDestroyed(); // level -> 1 (the ceiling): fully escalated

            Assert.Greater(DifficultyDirector.ToughnessMultiplier, 1f,
                "a fully escalated run must make robots tougher, not leave them unchanged");
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
        public void Reset_ZerosTheClock_TheShedCount_AndTheShedSkip()
        {
            DifficultyDirector.Tick(5f);
            DifficultyDirector.ReportShedDestroyed();

            DifficultyDirector.Reset();

            Assert.AreEqual(0f, DifficultyDirector.Elapsed, 1e-4);
            Assert.AreEqual(0, DifficultyDirector.ShedsDestroyed);
            Assert.AreEqual(0f, DifficultyDirector.Level, 1e-4,
                "a shed skip left over from the previous run must not survive a Reset");
        }

        // --- the HUD dial's bands (YT-197) ---

        [Test]
        public void StageAt_BottomThird_IsInvasion()
        {
            Assert.AreEqual(DifficultyDirector.Stage.Invasion, DifficultyDirector.StageAt(0f));
            Assert.AreEqual(DifficultyDirector.Stage.Invasion, DifficultyDirector.StageAt(0.3f));
        }

        [Test]
        public void StageAt_MiddleThird_IsInfestation()
        {
            Assert.AreEqual(DifficultyDirector.Stage.Infestation, DifficultyDirector.StageAt(1f / 3f));
            Assert.AreEqual(DifficultyDirector.Stage.Infestation, DifficultyDirector.StageAt(0.6f));
        }

        [Test]
        public void StageAt_TopThird_IsDomination()
        {
            Assert.AreEqual(DifficultyDirector.Stage.Domination, DifficultyDirector.StageAt(2f / 3f));
            Assert.AreEqual(DifficultyDirector.Stage.Domination, DifficultyDirector.StageAt(1f));
        }

        [Test]
        public void CurrentStage_TracksNormalized()
        {
            // A shed kill only moves the Level by skipping the clock forward, so drive it through
            // RunLengthSeconds/the derived rate rather than pinning EscalationRate to zero.
            DifficultyDirector.Reset();
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationMax = 3f; // each shed is worth exactly one band
            DevTuning.RunLengthSeconds = 3f;
            DevTuning.EscalationPerShedBump = 1f;

            Assert.AreEqual(DifficultyDirector.Stage.Invasion, DifficultyDirector.CurrentStage);

            DifficultyDirector.ReportShedDestroyed(); // 1/3 -> Infestation
            Assert.AreEqual(DifficultyDirector.Stage.Infestation, DifficultyDirector.CurrentStage);

            DifficultyDirector.ReportShedDestroyed(); // 2/3 -> Domination
            DifficultyDirector.ReportShedDestroyed(); // 3/3, clamped -> still Domination
            Assert.AreEqual(DifficultyDirector.Stage.Domination, DifficultyDirector.CurrentStage);
        }
    }
}
