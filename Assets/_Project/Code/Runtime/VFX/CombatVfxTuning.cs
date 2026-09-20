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

        // MV-825 item 7: "a 0.35m white-orange flash at the muzzle for 0.06s per shot" -- tighter and
        // shorter than MV-770's own 1.1m/0.14s now that the bolt itself carries the brightness (a
        // sleek core + glow sheath), not the muzzle punctuating a sliver too thin to see.
        public static LppeMuzzleFlashTuning LppeMuzzle() =>
            new LppeMuzzleFlashTuning(size: 0.35f, lifetime: 0.06f, spreadDegrees: 22f, forwardOffset: 0.4f);

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

        /// <summary>A normal pulse hit — sized to the damage (spec). MV-770: base/lifetime doubled
        /// alongside the muzzle and bolt — the old 0.35m/0.1s flash was scaled for a bolt that no
        /// longer exists at that size.</summary>
        public static LppeImpactTuning LppeImpact(float damage)
        {
            float d = Mathf.Max(damage, 0f);
            int sparks = Mathf.Clamp(Mathf.RoundToInt(3f + d * 0.15f), 3, 5);
            float flashSize = Mathf.Clamp(0.8f + d * 0.025f, 0.8f, 1.3f);
            return new LppeImpactTuning(
                flashSize: flashSize, flashLifetime: 0.18f, sparkCount: sparks, spreadDegrees: 65f,
                sparkSpeedMin: 2.2f, sparkSpeedMax: 5f, sparkSizeMin: 0.06f, sparkSizeMax: 0.14f,
                sparkLifeMin: 0.1f, sparkLifeMax: 0.22f);
        }

        /// <summary>The Shock-carrying 4th hit: brighter and bigger than <see cref="LppeImpact"/> so
        /// "a player must be able to count to the stun by eye" (spec) without reading the zigzag
        /// alone. MV-770: pushed from 0.85m/0.16s to 2.0m/0.3s — the old size still read as "a bigger
        /// dot" next to a 3.8px bolt; against the new 0.26m-wide bolt it has to read as its own event.</summary>
        public static LppeImpactTuning LppeShockImpact() =>
            new LppeImpactTuning(
                flashSize: 2.0f, flashLifetime: 0.3f, sparkCount: 7, spreadDegrees: 100f,
                sparkSpeedMin: 3.5f, sparkSpeedMax: 7.5f, sparkSizeMin: 0.09f, sparkSizeMax: 0.2f,
                sparkLifeMin: 0.16f, sparkLifeMax: 0.32f);

        /// <summary>MV-858 ARC's own tell — an instant jagged line from the hit point to the arc target
        /// (spec: "0.12 m wide, ... visible for 0.12 s") plus a small point flash at the second robot,
        /// its own event distinct from the ordinary <see cref="LppeImpact"/> beat that just landed on the
        /// FIRST robot.</summary>
        public readonly struct LppeArcTuning
        {
            public readonly float LineWidth;
            public readonly float LineLifetime;
            public readonly float FlashSize;
            public readonly float FlashLifetime;

            public LppeArcTuning(float lineWidth, float lineLifetime, float flashSize, float flashLifetime)
            {
                LineWidth = lineWidth;
                LineLifetime = lineLifetime;
                FlashSize = flashSize;
                FlashLifetime = flashLifetime;
            }
        }

        public static LppeArcTuning LppeArc() =>
            new LppeArcTuning(lineWidth: 0.12f, lineLifetime: 0.12f, flashSize: 1.0f, flashLifetime: 0.12f);

        /// <summary>MV-825 item 7: an added spark burst on every hit -- 8 short streaks, layered on
        /// top of whichever flash <see cref="LppeImpact"/>/<see cref="LppeShockImpact"/> just played,
        /// never replacing it.</summary>
        public readonly struct LppeBoltStreakTuning
        {
            public readonly int Count;
            public readonly float Lifetime;
            public readonly float SpreadDegrees;
            public readonly float SpeedMin;
            public readonly float SpeedMax;
            public readonly float SizeMin;
            public readonly float SizeMax;

            public LppeBoltStreakTuning(int count, float lifetime, float spreadDegrees, float speedMin,
                float speedMax, float sizeMin, float sizeMax)
            {
                Count = count;
                Lifetime = lifetime;
                SpreadDegrees = spreadDegrees;
                SpeedMin = speedMin;
                SpeedMax = speedMax;
                SizeMin = sizeMin;
                SizeMax = sizeMax;
            }
        }

        public static LppeBoltStreakTuning LppeBoltImpactStreaks() =>
            new LppeBoltStreakTuning(count: 8, lifetime: 0.12f, spreadDegrees: 110f, speedMin: 2.0f,
                speedMax: 3.2f, sizeMin: 0.08f, sizeMax: 0.12f);

        // --- MV-770/825: the bolt/rocket/salvo weight pass. Every magnitude the visual rescale needs
        // lives here too, same "nothing buried in an emit path" rule LppeVfx's own header states.

        /// <summary>MV-825: Max's own LPPE bolt, rebuilt straight and ALONG the travel axis -- Lee
        /// rejected the MV-815 crescent as reading like "a giant arrow sign" (a 0.9m chord lying
        /// ACROSS travel, the trail spilling out of its own midpoint like an arrow's shaft). A thin
        /// white-hot core carries the shape, a soft additive glow sheath around it carries the
        /// brightness, three crackling filaments carry the "electric" read, and the trail now emits
        /// from the bolt's own TAIL.</summary>
        public readonly struct LppeBoltTuning
        {
            public readonly float CoreLength;
            public readonly float CoreDiameter;
            public readonly float SheathDiameter;
            public readonly float SheathExtension;
            public readonly float SheathAlpha;
            public readonly float TrailWidth;
            public readonly float TrailLifetime;
            public readonly float GroundGlowDiameter;
            public readonly int CrackleFilamentCount;
            public readonly int CrackleVertexCount;
            public readonly float CrackleWidth;
            public readonly float CrackleMaxOffset;
            public readonly float CrackleRerandomizeInterval;
            public readonly float FlickerInterval;
            public readonly float FlickerAmount;

            public LppeBoltTuning(float coreLength, float coreDiameter, float sheathDiameter,
                float sheathExtension, float sheathAlpha, float trailWidth, float trailLifetime,
                float groundGlowDiameter, int crackleFilamentCount, int crackleVertexCount,
                float crackleWidth, float crackleMaxOffset, float crackleRerandomizeInterval,
                float flickerInterval, float flickerAmount)
            {
                CoreLength = coreLength;
                CoreDiameter = coreDiameter;
                SheathDiameter = sheathDiameter;
                SheathExtension = sheathExtension;
                SheathAlpha = sheathAlpha;
                TrailWidth = trailWidth;
                TrailLifetime = trailLifetime;
                GroundGlowDiameter = groundGlowDiameter;
                CrackleFilamentCount = crackleFilamentCount;
                CrackleVertexCount = crackleVertexCount;
                CrackleWidth = crackleWidth;
                CrackleMaxOffset = crackleMaxOffset;
                CrackleRerandomizeInterval = crackleRerandomizeInterval;
                FlickerInterval = flickerInterval;
                FlickerAmount = flickerAmount;
            }
        }

        /// <summary>MV-844: <paramref name="powerLevelFraction"/> (0 at World 2's POWER/<c>p_dmg</c>
        /// L1, 1 at its L8 cap — <see cref="MaxWorlds.Weapons.PulseLaser.PowerVisualStrength"/>) scales
        /// the core/sheath/trail wider as POWER levels up: sheath 0.20 -&gt; 0.44m, core 0.05 -&gt; 0.11m,
        /// trail 0.08 -&gt; 0.18m, all linear in the fraction. Defaults to 0 so every existing caller
        /// (<see cref="MaxWorlds.Arena.SentinelBolt"/>, the tests) that never passes it keeps today's L1
        /// look unchanged.</summary>
        public static LppeBoltTuning LppeBolt(float powerLevelFraction = 0f)
        {
            float s = Mathf.Clamp01(powerLevelFraction);
            return new LppeBoltTuning(
                coreLength: 1.4f, coreDiameter: Mathf.Lerp(0.05f, 0.11f, s),
                sheathDiameter: Mathf.Lerp(0.20f, 0.44f, s), sheathExtension: 0.1f, sheathAlpha: 0.55f,
                trailWidth: Mathf.Lerp(0.08f, 0.18f, s), trailLifetime: 0.10f,
                groundGlowDiameter: 0.9f,
                crackleFilamentCount: 3, crackleVertexCount: 7, crackleWidth: 0.025f,
                crackleMaxOffset: 0.09f, crackleRerandomizeInterval: 0.04f,
                flickerInterval: 0.03f, flickerAmount: 0.2f);
        }


        /// <summary>The Rack rocket body — was a 0.16m capsule (7.7px, roughly a third the bolt's own
        /// new length).</summary>
        public readonly struct RocketBodyTuning
        {
            public readonly float Length;
            public readonly float ExhaustFlameSize;

            public RocketBodyTuning(float length, float exhaustFlameSize)
            {
                Length = length;
                ExhaustFlameSize = exhaustFlameSize;
            }
        }

        public static RocketBodyTuning RocketBody() => new RocketBodyTuning(length: 0.5f, exhaustFlameSize: 0.35f);

        /// <summary>The rocket's own impact beat, layered on top of the existing splash ring
        /// (<c>RocketImpactVfx.PlaySplashRing</c>) rather than replacing it.</summary>
        public readonly struct RocketImpactFlashTuning
        {
            public readonly float FlashSize;
            public readonly float FlashLifetime;
            public readonly int SparkCount;

            public RocketImpactFlashTuning(float flashSize, float flashLifetime, int sparkCount)
            {
                FlashSize = flashSize;
                FlashLifetime = flashLifetime;
                SparkCount = sparkCount;
            }
        }

        public static RocketImpactFlashTuning RocketImpact() =>
            new RocketImpactFlashTuning(flashSize: 1.4f, flashLifetime: 0.22f, sparkCount: 6);

        /// <summary>Seconds a full Shoulder Rack salvo takes to leave, staggered rather than firing on
        /// one frame — a volley that stutters out reads as a weapon system, four things appearing at
        /// once reads as a spawn (spec). Rockets are spaced evenly across this window (see
        /// <see cref="MaxWorlds.Weapons.ShoulderRack"/>) rather than at a hardcoded interval, so the
        /// window stays this exact length however many rockets <c>s_sal</c> is currently worth.</summary>
        public const float ShoulderRackSalvoWindowSeconds = 0.24f;

        /// <summary>The LPPE's own pre-Shock tell (spec part 2, item 1): "over the 60 ms before it
        /// fires, the emitter brightens and a ring collapses into the muzzle. The player learns to
        /// count to it." Never delays the actual shot — <see cref="MaxWorlds.Combat.PulseLaser"/> only
        /// ever reads this to decide when to play the visual inside its existing, unchanged cadence
        /// countdown (the ticket's own "do not change fire cadence" line).</summary>
        public readonly struct LppeWindupTuning
        {
            public readonly float LeadSeconds;
            public readonly float RingSize;
            public readonly float ForwardOffset;

            public LppeWindupTuning(float leadSeconds, float ringSize, float forwardOffset)
            {
                LeadSeconds = leadSeconds;
                RingSize = ringSize;
                ForwardOffset = forwardOffset;
            }
        }

        public static LppeWindupTuning LppeWindup() =>
            new LppeWindupTuning(leadSeconds: 0.06f, ringSize: 0.5f, forwardOffset: 0.3f);
    }
}
