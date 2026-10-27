namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Every number the Helmsman fight is made of (MV-1239) — same "a const cannot be shadowed by a
    /// serialized scene copy" reasoning as <see cref="BossTuning"/>/<see cref="SludgequeenTuning"/>.
    /// </summary>
    public static class HelmsmanTuning
    {
        /// <summary>1.25x the Reef's Anchorhead maximum health — Anchorhead carries no health of its
        /// own (it's a <see cref="BigBermudaBoss"/> companion), so "Anchorhead's max health" IS
        /// <see cref="BossTuning.Health"/>, referenced here rather than inlining the product as a
        /// literal (the ticket's own "read it from code; do not hard-code a guess").</summary>
        public const float Health = BossTuning.Health * 1.25f;

        /// <summary>Below this fraction, Phase 1 (seated) ends: it tears free of the throne and walks.
        /// No tell — the switch is immediate, unlike Sludgequeen's 3 s enrage tell.</summary>
        public const float PhaseTwoThreshold = 0.5f;

        // ---------------------------------------------------------------- lens sweep (both phases)

        public const float SweepCooldownPhase1 = 5f;
        public const float SweepCooldownPhase2 = 6f;

        // ---------------------------------------------------------------- phase 2: walks, touches

        public const float MoveSpeed = BossTuning.MoveSpeed;
        public const float WalkStandoff = 3f;

        public const float ContactDamagePerTick = BossTuning.ContactDamagePerTick;
        public const float ContactCooldown = BossTuning.ContactCooldown;
        public const float ContactSkin = BossTuning.ContactSkin;

        // ---------------------------------------------------------------- pod volley (phase 1 only,
        // from the side hatches — reuses Big Bermuda's own brood-volley shape: BigBermudaBrain for the
        // shared time-based L1-L4 ramp, BroodSpawnLevels for composition, BroodArc for the landing
        // maths — "as today" (the ticket's own words), not a reimplementation.

        public const float PodVolleyInterval = 10f;

        /// <summary>The ticket also authors a 3-pod volley "below half health" — unreachable here since
        /// crossing that exact threshold is what ends Phase 1 (<see cref="PhaseTwoThreshold"/>) and pods
        /// only fire while seated, so a seated Helmsman is, by construction, always above it.</summary>
        public const int PodsPerVolley = 2;

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
