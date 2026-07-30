using NUnit.Framework;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>Unit tests for the Result-screen run stats (YT-31): clock, counters, and the
    /// once-only win/lose outcome.</summary>
    public sealed class RunStatsTests
    {
        [Test]
        public void FormatTime_MinutesAndPaddedSeconds()
        {
            Assert.AreEqual("0:00", RunStats.FormatTime(0f));
            Assert.AreEqual("0:09", RunStats.FormatTime(9f));
            Assert.AreEqual("1:05", RunStats.FormatTime(65f));
            Assert.AreEqual("2:30", RunStats.FormatTime(150.9f));
        }

        [Test]
        public void Tick_AccumulatesWhileInProgress()
        {
            var s = new RunStats();
            s.Tick(1.5f); s.Tick(2.0f);
            Assert.AreEqual(3.5f, s.Elapsed, 1e-4);
            Assert.AreEqual(RunOutcome.InProgress, s.Outcome);
            Assert.IsFalse(s.IsOver);
        }

        [Test]
        public void Tick_FrozenAfterOutcome()
        {
            var s = new RunStats();
            s.Tick(5f);
            s.Finish(RunOutcome.Victory);
            s.Tick(10f);                       // ignored — run is over
            Assert.AreEqual(5f, s.Elapsed, 1e-4);
        }

        [Test]
        public void Kills_CountWhileRunningNotAfter()
        {
            var s = new RunStats();
            s.AddKill(); s.AddKill();
            s.Finish(RunOutcome.Defeat);
            s.AddKill();                        // ignored
            Assert.AreEqual(2, s.Kills);
        }

        [Test]
        public void Finish_FirstOutcomeWins()
        {
            var s = new RunStats();
            s.Finish(RunOutcome.Victory);
            s.Finish(RunOutcome.Defeat);        // a later death can't flip a win
            Assert.AreEqual(RunOutcome.Victory, s.Outcome);
            Assert.AreEqual("VICTORY", s.Title);
        }

        [Test]
        public void Finish_InProgressIsIgnored()
        {
            var s = new RunStats();
            s.Finish(RunOutcome.InProgress);
            Assert.IsFalse(s.IsOver);
        }

        [Test]
        public void FactoryDestroyed_Latches()
        {
            var s = new RunStats();
            Assert.IsFalse(s.FactoryDestroyed);
            s.MarkFactoryDestroyed();
            Assert.IsTrue(s.FactoryDestroyed);
        }

        // --- YT-210: the near-miss peak the DEFEAT card leads with ---

        [Test]
        public void RecordDifficultyPeak_OnlyTheHighestReadingSurvives()
        {
            var s = new RunStats();
            s.RecordDifficultyPeak(0.2f);
            s.RecordDifficultyPeak(0.6f);
            s.RecordDifficultyPeak(0.4f); // a dip after the peak must not lower it

            Assert.AreEqual(0.6f, s.PeakNormalized, 1e-4);
        }

        [Test]
        public void RecordDifficultyPeak_FrozenAfterOutcome()
        {
            var s = new RunStats();
            s.RecordDifficultyPeak(0.5f);
            s.Finish(RunOutcome.Defeat);
            s.RecordDifficultyPeak(0.9f); // ignored — run is over

            Assert.AreEqual(0.5f, s.PeakNormalized, 1e-4);
        }

        [Test]
        public void FormatPercent_RoundsToTheNearestWholeNumber()
        {
            Assert.AreEqual(0, RunStats.FormatPercent(0f));
            Assert.AreEqual(50, RunStats.FormatPercent(0.5f));
            Assert.AreEqual(100, RunStats.FormatPercent(1f));
            Assert.AreEqual(74, RunStats.FormatPercent(0.735f));
        }
    }
}
