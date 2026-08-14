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

            // MV-293's ranged/teleport kinds (MV-310) — solved as their own small budget slice
            // alongside the tank/light split below, not substituted into it.
            public readonly int Gunner;
            public readonly int Bomber;
            public readonly int Blinker;

            public Composition(int rusher, int bruiser, int heavy, int brute,
                int gunner = 0, int bomber = 0, int blinker = 0)
            {
                Rusher = rusher; Bruiser = bruiser; Heavy = heavy; Brute = brute;
                Gunner = gunner; Bomber = bomber; Blinker = blinker;
            }

            public int TotalCount => Rusher + Bruiser + Heavy + Brute + Gunner + Bomber + Blinker;

            /// <summary>Robots this composition counts as "large" for economy purposes (MV-375) —
            /// matches <see cref="MaxWorlds.Enemies.EnemyArchetype.IsLarge"/>: everything except the
            /// rusher tier drops loot, so this is every solved count but <see cref="Rusher"/>.</summary>
            public int LargeCount => TotalCount - Rusher;

            public float TotalThreatValue =>
                Rusher * ThreatValues.Rusher + Bruiser * ThreatValues.Bruiser +
                Heavy * ThreatValues.Heavy + Brute * ThreatValues.Brute +
                Gunner * ThreatValues.Gunner + Bomber * ThreatValues.Bomber +
                Blinker * ThreatValues.Blinker;

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

            // Each unlocked special kind (MV-293/MV-310) draws its own fixed slice of the AREA'S FULL
            // budget, independently of the tank/light split below and of each other — once all three
            // are live they stack, same idiom as Heavy/Brute's toughSubstitutionPct. Carved out first
            // so the existing tank-share maths below still reads as a share of what's left, not of the
            // original total.
            float specialSharePct = toughness != null ? Mathf.Clamp01(toughness.specialSharePct / 100f) : 0f;
            bool gunnerUnlocked = toughness != null && toughness.GunnerUnlockedAt(areaIndex);
            bool bomberUnlocked = toughness != null && toughness.BomberUnlockedAt(areaIndex);
            bool blinkerUnlocked = toughness != null && toughness.BlinkerUnlockedAt(areaIndex);

            float gunnerBudget = gunnerUnlocked ? budget * specialSharePct : 0f;
            float bomberBudget = bomberUnlocked ? budget * specialSharePct : 0f;
            float blinkerBudget = blinkerUnlocked ? budget * specialSharePct : 0f;

            int gunner = gunnerBudget > 0f ? Mathf.RoundToInt(gunnerBudget / ThreatValues.Gunner) : 0;
            int bomber = bomberBudget > 0f ? Mathf.RoundToInt(bomberBudget / ThreatValues.Bomber) : 0;
            int blinker = blinkerBudget > 0f ? Mathf.RoundToInt(blinkerBudget / ThreatValues.Blinker) : 0;

            float remaining = Mathf.Max(0f, budget - gunnerBudget - bomberBudget - blinkerBudget);

            float tankShare = toughness != null ? toughness.TankShareForArea(areaIndex) : 0f;
            bool heavyUnlocked = toughness != null && toughness.HeavyUnlockedAt(areaIndex);
            bool bruteUnlocked = toughness != null && toughness.BruteUnlockedAt(areaIndex);

            float tankBudget = heavyUnlocked ? remaining * tankShare : 0f;
            float lightBudget = remaining - tankBudget;

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

            return new Composition(rusher, bruiser, heavy, brute, gunner, bomber, blinker);
        }
    }
}
