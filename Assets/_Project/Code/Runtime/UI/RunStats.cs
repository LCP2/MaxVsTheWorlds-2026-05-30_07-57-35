using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>How a run finished.</summary>
    public enum RunOutcome { InProgress, Victory, Defeat }

    /// <summary>
    /// Slice run stats behind the Result screen (YT-31, spec §4.9): run time, monsters killed,
    /// whether the factory was destroyed, and win/lose. Pure logic (no MonoBehaviour) so the
    /// timing, once-only outcome, and formatting are unit-testable. The full card (bolts, Hero
    /// Pass XP, share image, difficulty tier, hardcore) is the post-slice version.
    /// </summary>
    public sealed class RunStats
    {
        public RunOutcome Outcome { get; private set; } = RunOutcome.InProgress;
        public int Kills { get; private set; }

        /// <summary>How many factories the run broke. A count, not a flag: a level can have more than
        /// one (YT-92), and "YES" is a poor answer to "how did the run go" when there were two.</summary>
        public int FactoriesDestroyed { get; private set; }

        public bool FactoryDestroyed => FactoriesDestroyed > 0;
        public float Elapsed { get; private set; }

        /// <summary>The highest <c>DifficultyDirector.Normalized</c> (0..1) this run ever reached
        /// (YT-210) — the "near-miss" the DEFEAT card leads with: how close to the top of the dial,
        /// and the boss erupting, the run got before Max fell.</summary>
        public float PeakNormalized { get; private set; }

        public bool IsOver => Outcome != RunOutcome.InProgress;

        /// <summary>Advance the run clock. No-op once the run is over.</summary>
        public void Tick(float dt)
        {
            if (IsOver) return;
            Elapsed += Mathf.Max(0f, dt);
        }

        /// <summary>Record a fresh reading of the Invasion Level's Normalized value; only the peak
        /// survives. No-op once the run is over, same contract as <see cref="Tick"/>.</summary>
        public void RecordDifficultyPeak(float normalized)
        {
            if (IsOver) return;
            if (normalized > PeakNormalized) PeakNormalized = normalized;
        }

        public void AddKill()
        {
            if (!IsOver) Kills++;
        }

        public void MarkFactoryDestroyed() => FactoriesDestroyed++;

        /// <summary>Set the final outcome. First call wins; later calls are ignored so a death
        /// after the boss dies (or vice-versa) can't flip the result.</summary>
        public void Finish(RunOutcome outcome)
        {
            if (IsOver || outcome == RunOutcome.InProgress) return;
            Outcome = outcome;
        }

        /// <summary>"M:SS" clock for the run time.</summary>
        public static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int total = Mathf.FloorToInt(seconds);
            return $"{total / 60}:{total % 60:00}";
        }

        /// <summary>A 0..1 Normalized value as a whole-number percent, for the DEFEAT card's
        /// near-miss line (YT-210).</summary>
        public static int FormatPercent(float normalized) => Mathf.RoundToInt(Mathf.Clamp01(normalized) * 100f);

        /// <summary>Big banner title for the outcome.</summary>
        public string Title => Outcome switch
        {
            RunOutcome.Victory => "VICTORY",
            RunOutcome.Defeat => "DEFEAT",
            _ => "…"
        };
    }
}
