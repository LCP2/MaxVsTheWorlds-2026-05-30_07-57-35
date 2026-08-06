using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// The bounded hidden nudge (DDA) — World &amp; Difficulty Framework, Confluence MVW 34439170 §5:
    /// "if a player clearly struggles, quietly widen lulls / slow production by a SMALL capped amount;
    /// if steamrolling, tighten timing. Hard cap. NEVER touch enemy stats." Optional hook only — no
    /// caller wires this to a live MPL/EPL ratio yet (that scoring is ticket 3/MV-269); this is the
    /// pure, capped function the wiring will call.
    ///
    /// Deliberately has no access to <see cref="EnemyArchetype"/> or anything HP/damage/speed-shaped —
    /// the only surface it exposes is a pacing/production multiplier, so it cannot become a stat lever
    /// even by accident (§2.2: stat rubber-banding is detectable and resented; pacing changes are not).
    /// </summary>
    public static class HiddenNudge
    {
        /// <summary>The hard cap (spec: "never exceed the cap") — at most a 15% pacing/production
        /// adjustment in either direction, regardless of how far outside the band the player is.</summary>
        public const float MaxAdjustment = 0.15f;

        /// <summary>The band <see cref="DifficultyDirector"/>'s design targets (spec §4): below this,
        /// the player is struggling; above <see cref="BandHigh"/>, steamrolling.</summary>
        public const float BandLow = 0.85f;
        public const float BandHigh = 1.4f;

        /// <summary>The capped pacing nudge for a given MPL÷EPL performance ratio: positive widens
        /// lulls / slows production (struggling), negative tightens timing (steamrolling), zero inside
        /// the band. Clamped to ±<see cref="MaxAdjustment"/> no matter how extreme the ratio.</summary>
        public static float PacingNudge(float performanceRatio)
        {
            if (performanceRatio < BandLow)
                return Mathf.Clamp(BandLow - performanceRatio, 0f, MaxAdjustment);

            if (performanceRatio > BandHigh)
                return -Mathf.Clamp(performanceRatio - BandHigh, 0f, MaxAdjustment);

            return 0f;
        }

        /// <summary>Applies a <see cref="PacingNudge"/> to a production rate (robots/sec or an
        /// interval multiplier) — the only kind of value this hook is allowed to touch. A positive
        /// nudge (struggling) slows production; a negative one (steamrolling) speeds it up. Floored
        /// above zero so an extreme nudge can never invert or stall production.</summary>
        public static float ApplyToProductionRate(float baseRate, float nudge) =>
            Mathf.Max(0.01f, baseRate * (1f - Mathf.Clamp(nudge, -MaxAdjustment, MaxAdjustment)));
    }
}
