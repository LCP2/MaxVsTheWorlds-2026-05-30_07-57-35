using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-365 (Lee, 13 Aug 2026 DECISION): Rushers are hard-capped at <see cref="PerLevel"/> across a
    /// run — a ceiling on the CUMULATIVE total ever queued, not a concurrent-on-screen limit. Not a
    /// floor on total adversaries; no other kind is capped by this rule. Pure + unit-testable, same
    /// idiom as <see cref="SpawnBias"/>/<see cref="DifficultyEngine"/> — no MonoBehaviour, no live run
    /// state beyond what the caller threads through explicitly (<see cref="AreaAccumulationDirector"/>
    /// owns the actual running total).
    /// </summary>
    public static class RusherCap
    {
        public const int PerLevel = 10;

        /// <summary>Trims <paramref name="composition"/>'s Rusher count so no more than
        /// <see cref="PerLevel"/> are ever allowed through in total, given <paramref name="alreadyUsed"/>
        /// Rushers already counted against the cap earlier this run. Every other kind in the
        /// composition passes through untouched.</summary>
        public static DifficultyEngine.Composition Apply(DifficultyEngine.Composition composition, int alreadyUsed)
        {
            int allowed = Mathf.Max(0, PerLevel - alreadyUsed);
            int rusher = Mathf.Min(composition.Rusher, allowed);

            if (rusher == composition.Rusher) return composition;
            return new DifficultyEngine.Composition(rusher, composition.Bruiser, composition.Heavy,
                composition.Brute, composition.Gunner, composition.Launcher, composition.Blinker,
                composition.Bolter);
        }
    }
}
