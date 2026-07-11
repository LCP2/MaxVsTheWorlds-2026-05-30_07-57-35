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
        public bool FactoryDestroyed { get; private set; }
        public float Elapsed { get; private set; }

        public bool IsOver => Outcome != RunOutcome.InProgress;

        /// <summary>Advance the run clock. No-op once the run is over.</summary>
        public void Tick(float dt)
        {
            if (IsOver) return;
            Elapsed += Mathf.Max(0f, dt);
        }

        public void AddKill()
        {
            if (!IsOver) Kills++;
        }

        public void MarkFactoryDestroyed() => FactoryDestroyed = true;

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

        /// <summary>Big banner title for the outcome.</summary>
        public string Title => Outcome switch
        {
            RunOutcome.Victory => "VICTORY",
            RunOutcome.Defeat => "DEFEAT",
            _ => "…"
        };
    }
}
