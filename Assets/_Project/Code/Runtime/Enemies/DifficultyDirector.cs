using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// The Invasion Level (YT-181) — a single scalar that makes a level get HARDER as it goes on,
    /// not easier. Before this, the sheds WERE the threat source, so destroying them drained the
    /// danger and the finale was the quietest moment. This climbs with elapsed run TIME (the
    /// primary, universal driver — limited time is what creates the intensity) and takes a
    /// step-up bump each time a factory shed is destroyed, so clearing a source raises the stakes
    /// instead of lowering them.
    ///
    /// Feeds existing systems rather than adding new ones: <see cref="EnemySpawner"/> reads
    /// <see cref="SpawnIntervalMultiplier"/> to speed up its cadence and
    /// <see cref="ToughnessMultiplier"/> to scale a freshly-spawned robot's health/damage. A NEW
    /// level is just a different tuning curve (start/rate/per-shed bump/max) — data, not new code.
    ///
    /// Global and static, on purpose — the same shape as <see cref="MaxWorlds.Factories.FactoryCensus"/>:
    /// there is exactly one Invasion Level for a run, shared by every factory on the map, not one
    /// per spawner (which would double-count elapsed time the moment a level has two factories).
    /// <see cref="DifficultyDirectorRunner"/> is the only thing that calls <see cref="Tick"/>, once
    /// per frame; <see cref="Reset"/> is called when a level starts building (<c>MapRuntime.Build</c>)
    /// so a new run doesn't inherit the last one's clock.
    /// </summary>
    public static class DifficultyDirector
    {
        // --- authored curve (YT-181 first pass; Lee dials the final via the Settings panel) ---
        public const float AuthoredStart = 0f;
        public const float AuthoredRatePerSecond = 0.05f;   // ~200s (3m20s) to climb from 0 to the authored max
        public const float AuthoredPerShedBump = 1.5f;      // a shed kill is worth ~30s of climbing
        public const float AuthoredMax = 10f;

        // --- what the level actually buys, at full escalation (Normalized == 1) ---
        private const float SpawnIntervalFloor = 0.4f;  // spawns land ~2.5x as often
        private const float ToughnessCeiling = 1.75f;   // robots carry 75% more health/damage

        private static float _elapsed;
        private static int _shedsDestroyed;

        /// <summary>Seconds this run has been climbing. Read-only outside; <see cref="Tick"/> drives it.</summary>
        public static float Elapsed => _elapsed;

        /// <summary>How many factory sheds this run has destroyed — only ever goes up.</summary>
        public static int ShedsDestroyed => _shedsDestroyed;

        /// <summary>Back to a fresh run's clock. Called when a level starts building, so a scene
        /// loaded a second time — in the game or in a test — climbs from zero, not from wherever
        /// the last run left off.</summary>
        public static void Reset()
        {
            _elapsed = 0f;
            _shedsDestroyed = 0;
        }

        /// <summary>Advance the clock by one frame's worth of time. Negative/garbage dt is clamped
        /// to zero rather than allowed to run the level backwards.</summary>
        public static void Tick(float dt) => _elapsed += Mathf.Max(0f, dt);

        /// <summary>A factory shed just went down — bump the level a step, so clearing a source
        /// raises the stakes rather than lowering them.</summary>
        public static void ReportShedDestroyed() => _shedsDestroyed++;

        /// <summary>The ceiling the level climbs to, live — a Settings-panel override retunes the
        /// cap mid-run exactly like every other DevTuning knob.</summary>
        public static float Max => DevTuning.Or(DevTuning.EscalationMax, AuthoredMax);

        /// <summary>The Invasion Level right now: start + (rate * elapsed) + (per-shed bump *
        /// sheds destroyed), clamped to the ceiling. Every input is read live through
        /// <see cref="DevTuning"/>, so a moved slider retimes the escalation mid-run.</summary>
        public static float Level => LevelAt(_elapsed, _shedsDestroyed,
            DevTuning.Or(DevTuning.EscalationStart, AuthoredStart),
            DevTuning.Or(DevTuning.EscalationRate, AuthoredRatePerSecond),
            DevTuning.Or(DevTuning.EscalationPerShedBump, AuthoredPerShedBump),
            Max);

        /// <summary>0 at the authored start, 1 at the ceiling. What the two multipliers below scale
        /// against, so the curve's actual units never leak into "how much faster/tougher".</summary>
        public static float Normalized => Max > 0f ? Mathf.Clamp01(Level / Max) : 0f;

        /// <summary>Multiply a spawn interval by this: 1 at the run's start, down to
        /// <see cref="SpawnIntervalFloor"/> at full escalation — the same shed pumps out robots
        /// faster as the level climbs.</summary>
        public static float SpawnIntervalMultiplier => Mathf.Lerp(1f, SpawnIntervalFloor, Normalized);

        /// <summary>Multiply a robot's health/damage by this: 1 at the run's start, up to
        /// <see cref="ToughnessCeiling"/> at full escalation — the same shed's robots get tougher
        /// as the level climbs. Only applied to a robot at the moment it is spawned/reused, so
        /// robots already on the field don't retroactively toughen up.</summary>
        public static float ToughnessMultiplier => Mathf.Lerp(1f, ToughnessCeiling, Normalized);

        /// <summary>Pure curve evaluation — unit-testable with no clock, no Unity lifecycle.
        /// Clamped to [min(start,max), max(start,max)] so an authored curve can never produce a
        /// level outside its own declared range, whichever way start/max are dialled.</summary>
        public static float LevelAt(float elapsed, int shedsDestroyed, float start,
            float ratePerSecond, float perShedBump, float max)
        {
            float raw = start + ratePerSecond * Mathf.Max(0f, elapsed) + perShedBump * shedsDestroyed;
            float lo = Mathf.Min(start, max);
            float hi = Mathf.Max(start, max);
            return Mathf.Clamp(raw, lo, hi);
        }
    }
}
