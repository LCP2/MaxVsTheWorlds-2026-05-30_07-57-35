namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Every number GRIDLOCK — the City's boss (MV-1299) — is made of, in one place, same "a const
    /// cannot be shadowed by a serialized scene copy" reasoning as <see cref="BossTuning"/>. MV-1299
    /// authored her body, wake, walk and passive touch; this file now also carries her three attacks
    /// (SQUAD/JUMP/STRIKE) and escalation clock (MV-1306). The arena/shield/finale are MV-1309's;
    /// nothing here adds a knob for those yet.
    /// </summary>
    public static class GridlockTuning
    {
        /// <summary>The ticket's own authored health (Lee, 10 Oct design, 27 Oct go-ahead).</summary>
        public const float Health = 8600f;

        // ---------------------------------------------------------------- movement (modelled on
        // SludgequeenBoss/BigBermudaBoss: walks on planted feet, stops at the edge of her own live
        // touch reach, and drifts there rather than standing still — "no charge or ram" rules out
        // BigBermuda's old ram entirely, not just its presence).

        public const float MoveSpeed = BossTuning.MoveSpeed;

        // ---------------------------------------------------------------- touch (the ticket's own
        // "15 damage/s ... rate-limited like RobotEnemy contact damage" is exactly the shared
        // BossTuning numbers every other boss's passive touch already uses: 15 dmg every 1.0 s.

        public const float ContactDamagePerTick = BossTuning.ContactDamagePerTick;
        public const float ContactCooldown = BossTuning.ContactCooldown;
        public const float ContactSkin = BossTuning.ContactSkin;
        public const float StandoffMargin = BossTuning.StandoffMargin;

        // ---------------------------------------------------------------- attacks (MV-1306): the
        // ticket's own "at half health, her lights run twice as fast and every timer above is 30%
        // shorter" — JUMP is the one exception, carrying its own explicit half-health number (8 s)
        // rather than the generic 30% cut, so GridlockAttackClock reads it directly instead of
        // scaling GridlockTuning.JumpInterval.

        public const float HalfHealthThreshold = 0.5f;
        public const float HalfHealthTimerScale = 0.7f;
        public const float HalfHealthLightScale = 2f;

        // SQUAD: a 1 s warning, then 3 robots beam in round her through the shared landing clamp,
        // 0.2 s apart, every 10 s, capped at 10 of her robots alive at once.
        public const float SquadInterval = 10f;
        public const float SquadWarning = 1f;
        public const int SquadRobotCount = 3;
        public const float SquadBeamStagger = 0.2f;
        public const float SquadLandRadius = 5f;
        public const int MaxGridlockRobots = 10;

        // JUMP: a dashed ring marks the landing spot for 1 s, then she breaks into pixels and
        // reassembles there — every 12 s, every 8 s at half health (the ticket's own explicit number).
        public const float JumpInterval = 12f;
        public const float JumpIntervalHalfHealth = 8f;
        public const float JumpWarning = 1f;
        public const float JumpMinDistanceFromSelf = 12f;
        public const float JumpMinDistanceFromMax = 6f;

        // STRIKE: four 2 m circles near Max, arming 1.2 s (harmless) then live for 0.4 s (30 dmg to
        // Max/Sentinels inside), every 7 s. The arm delay must always stay shorter than the circle's
        // whole lifetime (arm + stay), or a circle would never actually go live — asserted directly
        // in MV-1306's own EditMode test.
        public const float StrikeInterval = 7f;
        public const float StrikeArmDelay = 1.2f;
        public const float StrikeStayDuration = 0.4f;
        public const int StrikeCircleCount = 4;
        public const float StrikeCircleRadius = 2f;
        public const float StrikeSpreadRadius = 3f;
        public const float StrikeDamage = 30f;

        // Escalation: one spawn level per 30 s, capped at 4 — L1 Rushers, L2 adds Bruisers, L3 adds
        // Bolters (plus a one-time Jammer), L4 adds a one-time Brute (GridlockSpawnLevels/GridlockBoss).
        public const float EscalationLevelInterval = 30f;
        public const int MaxEscalationLevel = 4;
    }
}
