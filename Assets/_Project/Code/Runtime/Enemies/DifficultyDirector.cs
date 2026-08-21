using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Factories;

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
    /// YT-210 made the run itself bounded: it reaches Max at <see cref="RunLengthSeconds"/> — ~6
    /// minutes — with no shed kills at all. The rate is DERIVED from Max/RunLengthSeconds rather
    /// than hand-tuned, so the two numbers can never silently disagree. A shed kill no longer bumps
    /// the level directly; it SKIPS THE CLOCK FORWARD by <see cref="AuthoredPerShedBump"/> seconds
    /// instead, so aggression shortens the run rather than just padding a score.
    ///
    /// MV-279: <c>BigBermudaBoss</c> no longer wakes off this clock. YT-210 had it erupt the moment
    /// <see cref="Normalized"/> hit 1 "whichever happens first" — but with 3 sheds instead of the
    /// slice's 1, that ceiling could be reached (via elapsed time plus the per-shed skip) well
    /// before all 3 sheds were actually down, which read as the boss appearing before the gate to
    /// its own room had opened. The boss now wakes only off <c>FactoryCensus.Cleared</c> — the last
    /// shed falling, the same condition that unlocks the boss gate.
    ///
    /// Feeds existing systems rather than adding new ones: <see cref="EnemySpawner"/> reads
    /// <see cref="SpawnIntervalMultiplier"/> to speed up its cadence and
    /// <see cref="ToughnessMultiplier"/> to scale a freshly-spawned robot's health/damage. A NEW
    /// level is just a different tuning curve (start/run-length/per-shed skip/max) — data, not new
    /// code.
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
        // --- authored curve (YT-181 first pass, re-baked to Lee's on-device numbers YT-200,
        // restructured into a bounded run at YT-210) ---
        public const float AuthoredStart = 0f;
        public const float AuthoredMax = 10f;

        /// <summary>How long a run is authored to last, in seconds, with no shed kills: the Invasion
        /// Level's rate is derived from this and <see cref="AuthoredMax"/>, not hand-tuned, so the
        /// escalation curve and "the run is ~N minutes" can never quietly drift apart.
        ///
        /// MV-513 re-pace: this was 360s (~6 minutes), authored for the YT-210 single-arena slice and
        /// never re-baked when world 1 grew to 18 areas — a player moving at Lee's measured ~67s/area
        /// (337s to reach area 5) hit this ceiling by area 5, with 13 of 18 areas played at the
        /// permanent Domination cap. See <see cref="AuthoredPerShedBump"/> for the full worked
        /// arithmetic; this value is 2750s so that, modelled against that pace and the 8-shed
        /// destruction schedule (areas 3/6/8/9/11/14/15/17), <c>Normalized</c> first reaches 1.0 at
        /// area 18 and Domination (the top third) first opens at area 13 — see
        /// <c>DifficultyDirectorTests.EscalationCurve_PacedAcrossFullRun_...</c>.</summary>
        public const float AuthoredRunLengthSeconds = 2750f;

        /// <summary>Seconds the clock SKIPS FORWARD when EVERY factory shed in the run has been
        /// destroyed. This is the TOTAL budget clearing every source is worth, not a flat per-shed
        /// number: <see cref="ReportShedDestroyed"/> divides it across however many sheds
        /// <see cref="FactoryCensus"/> says this run actually has (MV-261).
        ///
        /// MV-513 re-pace: was half the run length (a carry-over from the old per-shed level bump's
        /// own weight, half of a 10-point ceiling). Naively keeping that 0.5 fraction while raising
        /// <see cref="AuthoredRunLengthSeconds"/> overshoots — the per-shed budget scales WITH the run
        /// length, so a bigger run also hands out a bigger skip, and a thorough player (who fells
        /// sheds on the way through) still arrives at the ceiling far earlier than a full 18-area
        /// run. Solving for "Normalized hits 1.0 at area 17-18 AND Domination opens at area 12-14",
        /// against Lee's ~67s/area pace and the 8-shed schedule (areas 3/6/8/9/11/14/15/17), pins the
        /// fraction at 4/7 (~0.571): with <see cref="AuthoredRunLengthSeconds"/> = 2750s that is
        /// ~1571.43s total, ~196.4s per shed across the 8. Worked areas (shed destroyed while
        /// traversing its own area, contributing from the NEXT area onward):
        /// area 12 → Normalized ≈0.651 (Infestation), area 13 → ≈0.676 (Domination opens),
        /// area 17 → ≈0.917 (still short), area 18 → ≈1.013 → clamped to 1.0 (ceiling reached).</summary>
        public const float AuthoredPerShedBump = AuthoredRunLengthSeconds * (4f / 7f);

        /// <summary>The rate implied by the authored curve: reach <see cref="AuthoredMax"/> at
        /// <see cref="AuthoredRunLengthSeconds"/> with zero shed kills. Kept as a named default for
        /// the Settings panel's reference row — see <see cref="DerivedRatePerSecond"/> for the live,
        /// DevTuning-aware version gameplay actually reads.</summary>
        public static float AuthoredRatePerSecond => (AuthoredMax - AuthoredStart) / AuthoredRunLengthSeconds;

        // --- what the level actually buys, at full escalation (Normalized == 1) ---
        private const float SpawnIntervalFloor = 0.4f;  // spawns land ~2.5x as often
        // YT-194: 1.75 -> 2.5 (robots carry 150% more health/damage, was 75%). The field-wide live
        // cap (YT-186) means raw numbers can't carry late-game threat any more, so toughness has to
        // lean harder to keep a fully-armed Max honest — a few tough robots to respect, not a puddle
        // of weak ones he melts.
        private const float ToughnessCeiling = 2.5f;

        private static float _elapsed;
        private static float _shedSkipSeconds;
        private static int _shedsDestroyed;

        /// <summary>Seconds this run has been climbing, REAL time — does not include the shed
        /// skip-ahead (see <see cref="Level"/>). Read-only outside; <see cref="Tick"/> drives it.</summary>
        public static float Elapsed => _elapsed;

        /// <summary>How many factory sheds this run has destroyed — only ever goes up.</summary>
        public static int ShedsDestroyed => _shedsDestroyed;

        /// <summary>Back to a fresh run's clock. Called when a level starts building, so a scene
        /// loaded a second time — in the game or in a test — climbs from zero, not from wherever
        /// the last run left off.</summary>
        public static void Reset()
        {
            _elapsed = 0f;
            _shedSkipSeconds = 0f;
            _shedsDestroyed = 0;
        }

        /// <summary>Advance the clock by one frame's worth of time. Negative/garbage dt is clamped
        /// to zero rather than allowed to run the level backwards.</summary>
        public static void Tick(float dt) => _elapsed += Mathf.Max(0f, dt);

        /// <summary>A factory shed just went down — skip the clock forward a step (YT-210), so
        /// clearing a source shortens the run and raises the stakes rather than lowering them.
        ///
        /// The step is the per-shed budget (<see cref="DevTuning.EscalationPerShedBump"/>, or
        /// <see cref="AuthoredPerShedBump"/> unset) divided across this run's actual shed count
        /// (<see cref="FactoryCensus.Total"/>) — MV-261. Every factory registers in <c>Awake</c>,
        /// before any of them can die in <c>Start</c>-or-later gameplay, so the count is stable by
        /// the time a kill is ever reported. Falls back to a single shed if the census is empty (a
        /// hand-built test fixture with no factories at all), so the budget still means something
        /// with nothing registered to divide it by.</summary>
        public static void ReportShedDestroyed()
        {
            _shedsDestroyed++;
            float perShedBudget = DevTuning.Or(DevTuning.EscalationPerShedBump, AuthoredPerShedBump);
            float shedCount = Mathf.Max(1, FactoryCensus.Total);
            _shedSkipSeconds += perShedBudget / shedCount;
        }

        /// <summary>The ceiling the level climbs to, live — a Settings-panel override retunes the
        /// cap mid-run exactly like every other DevTuning knob.</summary>
        public static float Max => DevTuning.Or(DevTuning.EscalationMax, AuthoredMax);

        /// <summary>How long THIS run is authored to last, live — a Settings-panel override retunes
        /// it mid-run exactly like every other DevTuning knob. Clamped above zero so a mis-dialled
        /// override can never divide the rate by zero below.</summary>
        public static float RunLengthSeconds =>
            Mathf.Max(0.01f, DevTuning.Or(DevTuning.RunLengthSeconds, AuthoredRunLengthSeconds));

        /// <summary>The live escalation rate implied by Max/RunLengthSeconds — read every frame, so
        /// dialling either the ceiling or the run length live retimes the climb to still land Max
        /// exactly at the end of the (possibly redialled) run.</summary>
        public static float DerivedRatePerSecond =>
            (Max - DevTuning.Or(DevTuning.EscalationStart, AuthoredStart)) / RunLengthSeconds;

        /// <summary>The Invasion Level right now: start + (rate * effective elapsed), clamped to the
        /// ceiling. "Effective elapsed" is the real clock plus every shed's skip-ahead, so a shed
        /// kill reads as time passing rather than a level bump. Every input is read live through
        /// <see cref="DevTuning"/>, so a moved slider retimes the escalation mid-run.</summary>
        public static float Level => LevelAt(_elapsed + _shedSkipSeconds,
            DevTuning.Or(DevTuning.EscalationStart, AuthoredStart),
            DevTuning.Or(DevTuning.EscalationRate, DerivedRatePerSecond),
            Max);

        /// <summary>0 at the authored start, 1 at the ceiling. What the two multipliers below scale
        /// against, so the curve's actual units never leak into "how much faster/tougher".</summary>
        public static float Normalized => Max > 0f ? Mathf.Clamp01(Level / Max) : 0f;

        /// <summary>The three named bands the HUD dial (YT-197) shows: INVASION (bottom third) →
        /// INFESTATION (middle third) → DOMINATION (top third).</summary>
        public enum Stage { Invasion, Infestation, Domination }

        /// <summary>Which band the run is in right now, off <see cref="Normalized"/> — the HUD dial
        /// and any stage-crossing beat always agree with what the spawn/toughness multipliers above
        /// are actually doing.</summary>
        public static Stage CurrentStage => StageAt(Normalized);

        /// <summary>Pure band lookup — unit-testable with no clock, same shape as <see cref="LevelAt"/>.</summary>
        public static Stage StageAt(float normalized)
        {
            if (normalized < 1f / 3f) return Stage.Invasion;
            if (normalized < 2f / 3f) return Stage.Infestation;
            return Stage.Domination;
        }

        /// <summary>Multiply a spawn interval by this: 1 at the run's start, down to
        /// <see cref="SpawnIntervalFloor"/> at full escalation — the same shed pumps out robots
        /// faster as the level climbs.</summary>
        public static float SpawnIntervalMultiplier => Mathf.Lerp(1f, SpawnIntervalFloor, Normalized);

        /// <summary>Multiply a robot's health/damage by this: 1 at the run's start, up to
        /// <see cref="ToughnessCeiling"/> at full escalation — the same shed's robots get tougher
        /// as the level climbs. Only applied to a robot at the moment it is spawned/reused, so
        /// robots already on the field don't retroactively toughen up.</summary>
        public static float ToughnessMultiplier => Mathf.Lerp(1f, ToughnessCeiling, Normalized);

        /// <summary>Pure curve evaluation — unit-testable with no clock, no Unity lifecycle. A shed
        /// kill is folded into <paramref name="elapsed"/> before this is called (see
        /// <see cref="Level"/>) — the same formula that used to take time and a shed count
        /// separately now only ever takes time. Clamped to [min(start,max), max(start,max)] so an
        /// authored curve can never produce a level outside its own declared range, whichever way
        /// start/max are dialled.</summary>
        public static float LevelAt(float elapsed, float start, float ratePerSecond, float max)
        {
            float raw = start + ratePerSecond * Mathf.Max(0f, elapsed);
            float lo = Mathf.Min(start, max);
            float hi = Mathf.Max(start, max);
            return Mathf.Clamp(raw, lo, hi);
        }
    }
}
