namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Every number the Sludgequeen fight is made of (MV-696), in one place — same "a const cannot be
    /// shadowed by a serialized scene copy" reasoning as <see cref="BossTuning"/>.
    /// </summary>
    public static class SludgequeenTuning
    {
        // ---------------------------------------------------------------- the fight's length

        /// <summary>HP: 6 × Big Bermuda's (the ticket's own multiplier) — a longer fight to match the
        /// bigger arena and the two-flood-stage structure.</summary>
        public const float Health = BossTuning.Health * 6f;

        /// <summary>Below this fraction the flood goes from half the well to the whole floor.</summary>
        public const float PhaseTwoThreshold = 0.5f;

        /// <summary>Crown-spin/alarm tell between crossing <see cref="PhaseTwoThreshold"/> and the full
        /// flood actually landing — long enough to read as a warning, not an instant flip.</summary>
        public const float PhaseTwoTellTime = 3f;

        // ---------------------------------------------------------------- movement (modelled on BigBermudaBoss)

        public const float MoveSpeed = BossTuning.MoveSpeed;
        public const float Standoff = BossTuning.Standoff;

        // ---------------------------------------------------------------- the flood floor

        /// <summary>Same slow amount every other sludge hazard in the roster uses
        /// (<see cref="MaxWorlds.Enemies.SludgePuddle.SpeedMultiplier"/>) — one shared feel for "you are
        /// standing in sludge", whichever hazard put you there.</summary>
        public const float FloodSlowMultiplier = 0.6f;

        /// <summary>Damage per second to Max while standing on flooded floor. Robots are unaffected
        /// (the ticket's own "robots unaffected") — nothing calls this against a <see cref="MaxWorlds.Enemies.RobotEnemy"/>.</summary>
        public const float FloodDamagePerSecond = 4f;

        // ---------------------------------------------------------------- glob volley (reuses the Pipe Turret's glob, MV-691)

        public const float GlobSpeed = 7f;             // matches EnemyArchetype.Turret's lungeSpeed
        public const float GlobDamage = 14f;
        public const float GlobSplashRadius = 1.5f;
        public const float GlobPuddleRadius = 1.5f;
        public const float GlobPuddleDuration = 3f;

        public const float Phase1GlobInterval = 4f;
        public const int Phase1GlobCount = 5;
        public const float Phase2GlobInterval = 3f;
        public const int Phase2GlobCount = 7;

        // ---------------------------------------------------------------- brood (reuses BroodArc's landing maths, MV-696 §2)

        /// <summary>Robots flung per brood wave. The ticket's own "L2 adds a Cart Charger" escalation is
        /// deferred — there is no Cart Charger <c>EnemyKind</c> in the roster yet, and adding a wholly
        /// new enemy kind is its own ticket's worth of scope, not this one's.</summary>
        public const int BroodCount = 2;

        public const float Phase1BroodInterval = 9f;
        public const float Phase2BroodInterval = 7f;

        /// <summary>Hatches open this long before the brood is actually flung.</summary>
        public const float BroodTellTime = 1f;

        public const float HatchSide = 2.2f;
        public const float HatchLandingForward = 1.5f;
    }
}
