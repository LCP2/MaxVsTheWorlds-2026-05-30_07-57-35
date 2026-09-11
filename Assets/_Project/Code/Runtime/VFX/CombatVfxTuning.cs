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

        // MV-770: at the ~48px/m play camera a 0.5m/0.08s flash punctuates a bolt too thin to see in
        // the first place. Sized up alongside the bolt itself so the muzzle reads as the source of
        // something, not a spark in front of nothing.
        public static LppeMuzzleFlashTuning LppeMuzzle() =>
            new LppeMuzzleFlashTuning(size: 1.1f, lifetime: 0.14f, spreadDegrees: 22f, forwardOffset: 0.4f);

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

        // --- MV-770: the bolt/rocket/salvo weight pass. Every magnitude the visual rescale needs
        // lives here too, same "nothing buried in an emit path" rule LppeVfx's own header states.

        /// <summary>The LPPE bolt's own resolved shape — was 0.08m across and 0.35m long (3.8px/16.8px
        /// at the ~48px/m play camera, thinner than the nameplate text above the robot it hits).</summary>
        public readonly struct LppeBoltTuning
        {
            public readonly float CrossSection;
            public readonly float Length;
            public readonly float TrailWidth;
            public readonly float TrailLifetime;
            public readonly float GroundGlowDiameter;

            public LppeBoltTuning(float crossSection, float length, float trailWidth, float trailLifetime,
                float groundGlowDiameter)
            {
                CrossSection = crossSection;
                Length = length;
                TrailWidth = trailWidth;
                TrailLifetime = trailLifetime;
                GroundGlowDiameter = groundGlowDiameter;
            }
        }

        public static LppeBoltTuning LppeBolt() =>
            new LppeBoltTuning(crossSection: 0.26f, length: 0.9f, trailWidth: 0.3f, trailLifetime: 0.12f,
                groundGlowDiameter: 0.9f);

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
