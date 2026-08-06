using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// The two power scores and the fun band (World &amp; Difficulty Framework, Confluence MVW 34439170
    /// §4, MV-269): Max Power Level (MPL), Enemy Power Level (EPL), and their ratio R. Pure and
    /// unit-testable, the same idiom as <see cref="DifficultyEngine"/> — every method takes its inputs
    /// explicitly rather than reading a live player or run. Wiring MPL to Max's actual equipped-weapon
    /// stats is real-stat calibration, out of this ticket's scope (ticket 4/MV-270); this only proves
    /// the formulas themselves.
    /// </summary>
    public static class PowerScoring
    {
        /// <summary>The target band's low/high ratio (spec §4's table) — R below this is the
        /// "hold-your-ground" beat, above it the "invincible" beat.</summary>
        public const float BandLow = 0.85f;
        public const float BandHigh = 1.4f;

        /// <summary><c>damage × fireRate × hitFraction × areaFactor × rangeFactor</c> — the execution-
        /// adjusted DPS a real player actually lands, not the weapon's theoretical ceiling.
        /// <paramref name="hitFraction"/> (spec ~0.5-0.8) is clamped to [0,1]: it is a fraction of shots
        /// landed, never a multiplier past "every shot connects".</summary>
        public static float PrimaryEffectiveDps(float damage, float fireRate, float hitFraction,
            float areaFactor, float rangeFactor) =>
            Mathf.Max(0f, damage) * Mathf.Max(0f, fireRate) * Mathf.Clamp01(hitFraction) *
            Mathf.Max(0f, areaFactor) * Mathf.Max(0f, rangeFactor);

        /// <summary><c>(primary + secondary + ability) × survivability_factor</c> — Max's Power Level.
        /// Contributions are summed before survivability multiplies the whole offensive total, exactly
        /// as spec §4 orders the formula, not survivability applied per-term.</summary>
        public static float MaxPowerLevel(float primaryEffectiveDps, float secondaryContribution,
            float abilityContribution, float survivabilityFactor)
        {
            float offense = Mathf.Max(0f, primaryEffectiveDps + secondaryContribution + abilityContribution);
            return offense * Mathf.Max(0f, survivabilityFactor);
        }

        /// <summary><c>EPL(area n) = ΣTHV(n)×1.0 + ΣTHV(n+1)×0.5</c> — the next area's reinforcements
        /// bleeding forward through the gate, counted at half weight (spec §2.5/§4). Both terms come
        /// from <see cref="WorldConfig.SigmaThreatValue"/>, which is already zero for any area outside
        /// the world's own <c>[1, areaCount]</c> range — so the last area's EPL naturally carries no
        /// look-ahead past the world's end (there is nothing beyond it to bleed forward from).</summary>
        public static float EnemyPowerLevel(int areaIndex, WorldConfig cfg)
        {
            if (cfg == null) return 0f;
            return cfg.SigmaThreatValue(areaIndex) + cfg.SigmaThreatValue(areaIndex + 1) * 0.5f;
        }

        /// <summary>R = MPL ÷ EPL — zero (not a divide-by-zero) when an area has no enemy power at
        /// all, since a ratio against nothing is not a meaningful calibration signal.</summary>
        public static float BandRatio(float mpl, float epl) => epl > 0f ? mpl / epl : 0f;

        /// <summary>Whether a ratio sits inside the designed fun band (spec §4's 0.85-1.4).</summary>
        public static bool WithinBand(float ratio) => ratio >= BandLow && ratio <= BandHigh;

        /// <summary>R for every combat area 1..<c>dials.areaCount</c>, at a fixed <paramref name="mpl"/>
        /// — the per-area calibration report the ticket's AC2 asks for.</summary>
        public static float[] BandRatioPerArea(WorldConfig cfg, float mpl)
        {
            int count = Mathf.Max(0, cfg?.dials?.areaCount ?? 0);
            var ratios = new float[count];
            for (int i = 0; i < count; i++)
                ratios[i] = BandRatio(mpl, EnemyPowerLevel(i + 1, cfg));
            return ratios;
        }
    }
}
