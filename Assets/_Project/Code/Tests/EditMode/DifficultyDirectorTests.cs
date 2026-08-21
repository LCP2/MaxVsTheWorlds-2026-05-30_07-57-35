using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

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
            // MV-513: reads the live authored run length rather than a hardcoded literal, so this
            // keeps proving the mechanism (Tick exactly RunLengthSeconds -> Level hits Max) without
            // going stale the next time the authored curve is re-paced.
            DifficultyDirector.Reset();
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationMax = 10f;
            DevTuning.RunLengthSeconds = DifficultyDirector.AuthoredRunLengthSeconds;

            DifficultyDirector.Tick(DifficultyDirector.AuthoredRunLengthSeconds);

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

        // --- MV-513: re-paced for the 18-area world (was authored for a ~6-minute single-arena
        // slice and maxed out by area 5) ---

        [Test]
        public void EscalationCurve_PacedAcrossFullRun_LandsMaxAndDominationInTargetAreas()
        {
            // Models a player moving through all 18 areas at Lee's measured pace (area 5 at ~337s,
            // i.e. ~67.4s/area) and destroying every factory shed on the authored schedule (areas
            // 3, 6, 8, 9, 11, 14, 15, 17) as they reach it. A shed's skip is modelled as landing from
            // the NEXT area onward (it is destroyed somewhere while traversing its own area, not
            // before the area is entered), so the check for area N happens before that area's own
            // shed (if any) is reported.
            //
            // Asserts the AUTHORED defaults — no DevTuning overrides — so this is the arithmetic in
            // DifficultyDirector.AuthoredRunLengthSeconds/AuthoredPerShedBump actually paying off,
            // not a hand-rolled formula standing in for it.
            //
            // ReportShedDestroyed divides the per-shed budget across FactoryCensus.Total (MV-261),
            // which falls back to 1 when nothing is registered — a bare EditMode test would otherwise
            // hand out the WHOLE budget per shed instead of an eighth of it. Register 8 factories, the
            // world 1 shed count this ticket's arithmetic is modelled against, so the divisor matches.
            DifficultyDirector.Reset();
            DevTuning.Reset();
            FactoryCensus.Reset();

            const float SecondsPerArea = 337f / 5f; // Lee's measured pace (MV-513)
            const int AreaCount = 18;
            const int ShedCount = 8;
            var shedAreas = new System.Collections.Generic.HashSet<int> { 3, 6, 8, 9, 11, 14, 15, 17 };
            Assert.AreEqual(ShedCount, shedAreas.Count, "test fixture's shed schedule must match ShedCount");

            var hutches = new GameObject[ShedCount];
            try
            {
                for (int i = 0; i < ShedCount; i++)
                {
                    hutches[i] = new GameObject($"Hutch {i}");
                    FactoryCensus.Register(hutches[i].AddComponent<MowerHutch>());
                }
                Assert.AreEqual(ShedCount, FactoryCensus.Total, "test fixture must register all 8 sheds");

                int normalizedReachesMaxAtArea = -1;
                int dominationFirstOpensAtArea = -1;

                for (int area = 1; area <= AreaCount; area++)
                {
                    DifficultyDirector.Tick(SecondsPerArea);

                    if (normalizedReachesMaxAtArea < 0 && DifficultyDirector.Normalized >= 1f)
                        normalizedReachesMaxAtArea = area;
                    if (dominationFirstOpensAtArea < 0 &&
                        DifficultyDirector.CurrentStage == DifficultyDirector.Stage.Domination)
                        dominationFirstOpensAtArea = area;

                    if (shedAreas.Contains(area)) DifficultyDirector.ReportShedDestroyed();
                }

                Assert.That(normalizedReachesMaxAtArea, Is.InRange(17, 18),
                    $"Normalized must first reach 1.0 in area 17-18, landed at area {normalizedReachesMaxAtArea}");
                Assert.That(dominationFirstOpensAtArea, Is.InRange(12, 14),
                    $"Domination must first open in area 12-14, landed at area {dominationFirstOpensAtArea}");
            }
            finally
            {
                foreach (var go in hutches) if (go != null) Object.DestroyImmediate(go);
                FactoryCensus.Reset();
            }
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
        public void ShedBudget_IsDividedAcrossTheRunsActualShedCount_NotAppliedFlatPerShed()
        {
            // MV-261: EscalationPerShedBump (or its authored default) is the TOTAL clock-skip budget
            // clearing every source in the run is worth, not a flat amount charged per shed. A run
            // with three registered factories must only skip a THIRD of that budget per kill — or two
            // of three kills alone would max the level, which is the bug this guards against. 180s of
            // skip at rate 10/360 is a 5-level jump (Normalized 0.5) — see
            // ShedDestroyed_SkipsTheClockForward_RatherThanBumpingTheLevelDirectly above for that same
            // arithmetic against a single (undivided) shed; all three of THIS run's sheds together
            // must land on that identical total, not three times it.
            DifficultyDirector.Reset();
            FactoryCensus.Reset();
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationMax = 10f;
            DevTuning.RunLengthSeconds = 360f;
            DevTuning.EscalationPerShedBump = 180f; // the authored TOTAL budget for the whole run

            var hutches = new GameObject[3];
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    hutches[i] = new GameObject($"Hutch {i}");
                    // AddComponent alone is not enough here: EditMode tests run outside Play Mode, so
                    // MowerHutch.Awake (where it normally registers itself) never fires. Register it
                    // by hand — FactoryCensus.Total is all this test needs to be true.
                    FactoryCensus.Register(hutches[i].AddComponent<MowerHutch>());
                }
                Assert.AreEqual(3, FactoryCensus.Total, "test fixture must register three factories");

                DifficultyDirector.ReportShedDestroyed();
                DifficultyDirector.ReportShedDestroyed();
                Assert.Less(DifficultyDirector.Normalized, 1f,
                    "two of three shed kills must not alone max a three-shed run's Invasion Level");

                DifficultyDirector.ReportShedDestroyed();
                Assert.AreEqual(0.5f, DifficultyDirector.Normalized, 1e-3,
                    "all three shed kills together must land on the SAME total skip a single-shed " +
                    "run's one kill would have produced, not three times it");
            }
            finally
            {
                foreach (var go in hutches) if (go != null) Object.DestroyImmediate(go);
                FactoryCensus.Reset();
            }
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
