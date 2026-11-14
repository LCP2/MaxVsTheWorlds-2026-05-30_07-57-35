namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Every number the Helmsman fight is made of (MV-1239) — same "a const cannot be shadowed by a
    /// serialized scene copy" reasoning as <see cref="BossTuning"/>/<see cref="SludgequeenTuning"/>.
    /// </summary>
    public static class HelmsmanTuning
    {
        /// <summary>MV-1370: 18,000 flat (was <c>BossTuning.Health * 1.25</c>). Tuned only in the fight-time
        /// ticket (4/4), with measured fight time - not here.</summary>
        public const float Health = 18000f;

        /// <summary>Below this fraction, Phase 1 (seated, "Wired In") ends: <see cref="TransitionSeconds"/>
        /// of invulnerable beat, then it walks. Phases are P1 100-66%, P2 66-33%, P3 33-0%.</summary>
        public const float PhaseTwoThreshold = 0.66f;

        /// <summary>Below this fraction, Phase 3 begins. Until ticket 4 lands it walks exactly as phase 2.</summary>
        public const float PhaseThreeThreshold = 0.33f;

        /// <summary>The P1 -> P2 beat: cables snap, legs unfold, camera holds on the boss, invulnerable.</summary>
        public const float TransitionSeconds = 2f;

        // ---------------------------------------------------------------- phase 1: twin lens beams

        /// <summary>Seconds from one beam cycle's tell starting to the next one's.</summary>
        public const float BeamInterval = 10f;
        public const float BeamTellSeconds = 0.8f;
        public const float BeamSweepSeconds = 6f;
        public const float BeamSweepDegrees = 180f;
        public const float BeamDamage = 20f;
        public const float BeamRehitSeconds = 0.5f;
        /// <summary>Half the beam's floor width, metres - how close to its centre line counts as a touch.</summary>
        public const float BeamHalfWidth = 0.6f;
        public const float BeamRange = 40f;
        /// <summary>Height the beam is cast at, below the shortest cover (1.6 m) so cover blocks it.</summary>
        public const float BeamHeight = 1f;

        // ---------------------------------------------------------------- phase 1: spiral volley

        public const float SpiralInterval = 7f;
        public const int SpiralMissiles = 8;
        public const float SpiralLaunchGapSeconds = 0.15f;
        public const float SpiralDamage = 12f;
        public const float SpiralTrailSeconds = 1.6f;

        // ---------------------------------------------------------------- lens sweep (both phases)

        public const float SweepCooldownPhase1 = 5f;
        public const float SweepCooldownPhase2 = 6f;

        // ---------------------------------------------------------------- phase 2: walks, touches

        /// <summary>MV-1371: 2.2 m/s on the four legs (was BossTuning's 0.9).</summary>
        public const float MoveSpeed = 2.2f;
        public const float WalkStandoff = 3f;

        public const float ContactDamagePerTick = BossTuning.ContactDamagePerTick;
        public const float ContactCooldown = BossTuning.ContactCooldown;
        public const float ContactSkin = BossTuning.ContactSkin;

        // ---------------------------------------------------------------- phase 2: single beam + leap slam (MV-1371)

        public const float SingleBeamInterval = 8f;

        public const float SlamInterval = 6f;
        public const float SlamTellSeconds = 1f;
        public const float SlamLeapSeconds = 0.5f;
        public const float SlamLeapHeight = 3f;
        public const float SlamRadius = 3f;
        public const float SlamDamage = 40f;
        public const float ShockwaveRadius = 12f;
        public const float ShockwaveSeconds = 1.2f;
        public const float ShockwaveGapDegrees = 40f;
        public const float ShockwaveDamage = 15f;
        /// <summary>Height the shockwave's cover check is cast at - below the 1.6 m cover so cover blocks it.</summary>
        public const float ShockwaveCastHeight = 1f;

        // ---------------------------------------------------------------- pod volley (phase 1 only,
        // from the side hatches — reuses Big Bermuda's own brood-volley shape: BigBermudaBrain for the
        // shared time-based L1-L4 ramp, BroodSpawnLevels for composition, BroodArc for the landing
        // maths — "as today" (the ticket's own words), not a reimplementation.

        public const float PodVolleyInterval = 12f;

        public const int PodsPerVolley = 2;

        /// <summary>MV-1370: below this health fraction a volley is <see cref="PodsPerVolleyLow"/>.</summary>
        public const float PodLowHealthFraction = 0.8f;
        public const int PodsPerVolleyLow = 3;

        public const int MaxConcurrentPods = 8;

        public const float HatchSide = 2.2f;
        public const float HatchLandingForward = 1.5f;

        // ---------------------------------------------------------------- phase 2: Bridge airlocks
        // (MV-1313) -- the two a30 airlocks (MV-1310) blow in turn, alternating sides, once phase 2
        // begins; see HelmsmanBoss.TickAirlockCycle.

        /// <summary>Seconds between one airlock blowing and the next (alternating side) blowing.</summary>
        public const float AirlockCycleSeconds = 12f;

        /// <summary>How long before each blow its panel shows the white warning flash.</summary>
        public const float AirlockWarnSeconds = 1f;
    }
}
