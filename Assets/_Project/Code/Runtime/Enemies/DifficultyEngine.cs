using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// The threat-budget solver (World &amp; Difficulty Framework, Confluence MVW 34439170 §5, MV-268)
    /// — replaces hand-set per-area enemy counts (<see cref="AreaPopulation"/>) with a fixed, designed
    /// curve derived from a small dial set. Enemies do NOT scale to Max (§2.1): every method here
    /// takes its dials explicitly and reads no live player/run state, so the curve for a given world
    /// is the same regardless of how the player is doing — the only thing that ever answers "how is
    /// the player doing" is the small, capped <see cref="HiddenNudge"/>, and even that only ever
    /// touches pacing.
    /// </summary>
    public static class DifficultyEngine
    {
        /// <summary>The fixed enemy-threat ceiling for area <paramref name="areaIndex"/> (1-based):
        /// <c>baseThreat × (1 + threatGrowth)^(n-1) × pacingMultiplier(n)</c>. The exponential term is
        /// the designed escalation; <paramref name="pacing"/> is what turns a straight ramp (which
        /// "feels grim") into the saw-tooth rhythm the spec asks for.</summary>
        public static float TargetBudget(int areaIndex, float baseThreat, float threatGrowth, PacingRhythm pacing)
        {
            float growth = Mathf.Pow(1f + threatGrowth, Mathf.Max(0, areaIndex - 1));
            float pacingMultiplier = pacing != null ? pacing.MultiplierForArea(areaIndex) : 1f;
            return Mathf.Max(0f, baseThreat) * growth * pacingMultiplier;
        }

        /// <summary>A solved per-type robot count for one area — Σ THV approximates the area's
        /// <see cref="TargetBudget"/> without ever inventing a negative or fractional robot.</summary>
        public readonly struct Composition
        {
            public readonly int Rusher;
            public readonly int Bruiser;
            public readonly int Heavy;
            public readonly int Brute;

            public Composition(int rusher, int bruiser, int heavy, int brute)
            {
                Rusher = rusher; Bruiser = bruiser; Heavy = heavy; Brute = brute;
            }

            public int TotalCount => Rusher + Bruiser + Heavy + Brute;

            public float TotalThreatValue =>
                Rusher * ThreatValues.Rusher + Bruiser * ThreatValues.Bruiser +
                Heavy * ThreatValues.Heavy + Brute * ThreatValues.Brute;

            /// <summary>Heavy+Brute's realised share [0,1] of this composition's Σ THV — what
            /// actually landed, for comparing against <see cref="ToughnessCurve.TankShareForArea"/>'s
            /// target after integer rounding.</summary>
            public float TankShare
            {
                get
                {
                    float total = TotalThreatValue;
                    if (total <= 0f) return 0f;
                    return (Heavy * ThreatValues.Heavy + Brute * ThreatValues.Brute) / total;
                }
            }
        }

        /// <summary>Solves <paramref name="targetBudget"/> into per-type robot counts (Budget → robots,
        /// spec §5): split the budget by <paramref name="toughness"/>'s tank-share into a tanky purse
        /// (Heavy/Brute, split evenly once both are unlocked) and a light purse (Bruiser/Rusher, split
        /// evenly by THV) — replaces <see cref="AreaPopulation"/>'s manual per-area counts.</summary>
        public static Composition SolveComposition(int areaIndex, float targetBudget, ToughnessCurve toughness)
        {
            float budget = Mathf.Max(0f, targetBudget);
            float tankShare = toughness != null ? toughness.TankShareForArea(areaIndex) : 0f;
            bool heavyUnlocked = toughness != null && toughness.HeavyUnlockedAt(areaIndex);
            bool bruteUnlocked = toughness != null && toughness.BruteUnlockedAt(areaIndex);

            float tankBudget = heavyUnlocked ? budget * tankShare : 0f;
            float lightBudget = budget - tankBudget;

            int heavy = 0, brute = 0;
            if (tankBudget > 0f)
            {
                if (bruteUnlocked)
                {
                    heavy = Mathf.RoundToInt(tankBudget * 0.5f / ThreatValues.Heavy);
                    brute = Mathf.RoundToInt(tankBudget * 0.5f / ThreatValues.Brute);
                }
                else
                {
                    heavy = Mathf.RoundToInt(tankBudget / ThreatValues.Heavy);
                }
            }

            int bruiser = 0, rusher = 0;
            if (lightBudget > 0f)
            {
                bruiser = Mathf.RoundToInt(lightBudget * 0.5f / ThreatValues.Bruiser);
                float rusherBudget = Mathf.Max(0f, lightBudget - bruiser * ThreatValues.Bruiser);
                rusher = Mathf.RoundToInt(rusherBudget / ThreatValues.Rusher);
            }

            return new Composition(rusher, bruiser, heavy, brute);
        }
    }
}
