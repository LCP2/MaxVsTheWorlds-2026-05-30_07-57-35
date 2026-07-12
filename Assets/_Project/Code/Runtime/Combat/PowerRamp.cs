using UnityEngine;

namespace MaxWorlds.Combat
{
    public enum PowerBoost { Damage, FireRate }

    /// <summary>
    /// The "I'm getting stronger" curve (YT-67) — the escalation that makes the genre compulsive.
    /// The run had an XP bar and a LEVEL popup already, but levelling up did precisely nothing:
    /// the bar filled, the number went up, and Max was exactly as strong as before.
    ///
    /// Each level alternates between a damage step and a fire-rate step, so every level-up has a
    /// specific, nameable reward to shout ("+20% DAMAGE") rather than a vague glow.
    ///
    /// Every multiplier is a pure function of the CURRENT level, not an increment applied once.
    /// That's deliberate: gain two levels from a single kill (which the XP track allows) and the
    /// power still lands exactly right, because there's no "apply" that can be missed.
    /// </summary>
    public static class PowerRamp
    {
        public const float DamageStep = 0.20f;
        public const float FireRateStep = 0.15f;

        /// <summary>Which reward REACHING this level grants. Even levels pump the fire rate, odd
        /// levels the damage — so the first level-up (2) is the one you feel fastest.</summary>
        public static PowerBoost BoostAt(int level) =>
            level % 2 == 0 ? PowerBoost.FireRate : PowerBoost.Damage;

        /// <summary>Damage steps banked by level <paramref name="level"/> (levels 3, 5, 7…).</summary>
        public static int DamageSteps(int level) => (Mathf.Max(1, level) - 1) / 2;

        /// <summary>Fire-rate steps banked by level <paramref name="level"/> (levels 2, 4, 6…).</summary>
        public static int FireRateSteps(int level) => Mathf.Max(1, level) / 2;

        public static float DamageMultiplier(int level) => 1f + DamageStep * DamageSteps(level);

        public static float FireRateMultiplier(int level) => 1f + FireRateStep * FireRateSteps(level);

        /// <summary>What the player actually feels: damage × rate. This is the number that has to
        /// visibly climb, and it's what the tests assert on.</summary>
        public static float DpsMultiplier(int level) =>
            DamageMultiplier(level) * FireRateMultiplier(level);

        /// <summary>The shout. Named and specific — "you got a thing", not "you levelled".</summary>
        public static string BoostLabel(int level) =>
            BoostAt(level) == PowerBoost.Damage
                ? $"+{Mathf.RoundToInt(DamageStep * 100f)}% DAMAGE"
                : $"+{Mathf.RoundToInt(FireRateStep * 100f)}% FIRE RATE";
    }
}
