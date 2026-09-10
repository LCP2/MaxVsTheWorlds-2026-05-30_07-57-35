using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>Pure maths behind the combat feedback VFX — unit-testable without a scene.</summary>
    public static class CombatVfxTuning
    {
        /// <summary>Sparks thrown off an enemy that just took a hit. Scales with the size of
        /// the hit and doubles up on a crit, but stays small: a sustained water stream lands a
        /// tick every 0.1s on every enemy it touches, so a big burst here would bury the screen.</summary>
        public static int HitSparkCount(float damage, bool crit)
        {
            if (damage <= 0f) return 0;
            int n = Mathf.RoundToInt(3f + damage * 0.45f);
            if (crit) n *= 2;
            return Mathf.Clamp(n, 3, 12);
        }

        // --- MV-758: the LPPE's own firing VFX. Every magnitude LppeVfx's Emit() calls use lives
        // here, so nothing is a buried literal (spec: "so the numbers are tunable rather than buried").

        /// <summary>The muzzle punctuation: under 0.08s so it never smears into the next 0.22s-cadence
        /// shot (spec).</summary>
        public readonly struct LppeMuzzleFlashTuning
        {
            public readonly float Size;
            public readonly float Lifetime;
            public readonly float SpreadDegrees;
            public readonly float ForwardOffset;

            public LppeMuzzleFlashTuning(float size, float lifetime, float spreadDegrees, float forwardOffset)
            {
                Size = size;
                Lifetime = lifetime;
                SpreadDegrees = spreadDegrees;
                ForwardOffset = forwardOffset;
            }
        }

        public static LppeMuzzleFlashTuning LppeMuzzle() =>
            new LppeMuzzleFlashTuning(size: 0.5f, lifetime: 0.08f, spreadDegrees: 22f, forwardOffset: 0.35f);

        /// <summary>A pulse impact: a flash sized to the damage plus a handful of sparks along the
        /// surface normal (spec: "3-5 short sparks").</summary>
        public readonly struct LppeImpactTuning
        {
            public readonly float FlashSize;
            public readonly float FlashLifetime;
            public readonly int SparkCount;
            public readonly float SpreadDegrees;
            public readonly float SparkSpeedMin;
            public readonly float SparkSpeedMax;
            public readonly float SparkSizeMin;
            public readonly float SparkSizeMax;
            public readonly float SparkLifeMin;
            public readonly float SparkLifeMax;

            public LppeImpactTuning(float flashSize, float flashLifetime, int sparkCount, float spreadDegrees,
                float sparkSpeedMin, float sparkSpeedMax, float sparkSizeMin, float sparkSizeMax,
                float sparkLifeMin, float sparkLifeMax)
            {
                FlashSize = flashSize;
                FlashLifetime = flashLifetime;
                SparkCount = sparkCount;
                SpreadDegrees = spreadDegrees;
                SparkSpeedMin = sparkSpeedMin;
                SparkSpeedMax = sparkSpeedMax;
                SparkSizeMin = sparkSizeMin;
                SparkSizeMax = sparkSizeMax;
                SparkLifeMin = sparkLifeMin;
                SparkLifeMax = sparkLifeMax;
            }
        }

        /// <summary>A normal pulse hit — sized to the damage (spec).</summary>
        public static LppeImpactTuning LppeImpact(float damage)
        {
            float d = Mathf.Max(damage, 0f);
            int sparks = Mathf.Clamp(Mathf.RoundToInt(3f + d * 0.15f), 3, 5);
            float flashSize = Mathf.Clamp(0.35f + d * 0.025f, 0.35f, 0.7f);
            return new LppeImpactTuning(
                flashSize: flashSize, flashLifetime: 0.1f, sparkCount: sparks, spreadDegrees: 65f,
                sparkSpeedMin: 2.2f, sparkSpeedMax: 5f, sparkSizeMin: 0.06f, sparkSizeMax: 0.14f,
                sparkLifeMin: 0.1f, sparkLifeMax: 0.22f);
        }

        /// <summary>The Shock-carrying 4th hit: brighter and bigger than <see cref="LppeImpact"/> so
        /// "a player must be able to count to the stun by eye" (spec) without reading the zigzag
        /// alone.</summary>
        public static LppeImpactTuning LppeShockImpact() =>
            new LppeImpactTuning(
                flashSize: 0.85f, flashLifetime: 0.16f, sparkCount: 7, spreadDegrees: 100f,
                sparkSpeedMin: 3.5f, sparkSpeedMax: 7.5f, sparkSizeMin: 0.09f, sparkSizeMax: 0.2f,
                sparkLifeMin: 0.16f, sparkLifeMax: 0.32f);
    }
}
