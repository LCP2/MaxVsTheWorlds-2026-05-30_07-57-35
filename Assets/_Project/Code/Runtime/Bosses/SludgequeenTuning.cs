namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Every number the Sludgequeen fight is made of (MV-696, retuned by MV-1127 for the real fight:
    /// marked sludge lobs instead of a floor flood), in one place — same "a const cannot be shadowed by
    /// a serialized scene copy" reasoning as <see cref="BossTuning"/>.
    /// </summary>
    public static class SludgequeenTuning
    {
        // ---------------------------------------------------------------- the fight's length

        /// <summary>MV-1127: 4x Big Bermuda's (was 12x, the old flood-phase multiplier) — Sludgequeen is
        /// World 2's only boss and this is the one scaling knob Lee asked for directly ("Health 6,900
        /// (four times BossTuning.Health)").</summary>
        public const float Health = BossTuning.Health * 4f;

        /// <summary>Below this fraction she's enraged: faster lobs, faster sludgers — after a 3 s tell.</summary>
        public const float PhaseTwoThreshold = 0.5f;

        /// <summary>Crown-spin/alarm tell between crossing <see cref="PhaseTwoThreshold"/> and the
        /// faster timings actually landing — long enough to read as a warning, not an instant flip.</summary>
        public const float PhaseTwoTellTime = 3f;

        // ---------------------------------------------------------------- movement (modelled on BigBermudaBoss)

        public const float MoveSpeed = BossTuning.MoveSpeed;
        public const float Standoff = BossTuning.Standoff;

        // ---------------------------------------------------------------- contact damage (MV-1083)
        //
        // "Bosses must do damage to Max and Sentinels in every world" (Lee, 2026-09-30) applies to
        // every boss class, not just BigBermudaBoss -- Sludgequeen gets the same passive
        // damaging-presence mechanic (BossTuning's own MV-720/1037 block), not a second one.

        public const float ContactDamagePerTick = BossTuning.ContactDamagePerTick;
        public const float ContactCooldown = BossTuning.ContactCooldown;
        public const float ContactSkin = BossTuning.ContactSkin;
        public const float StandoffMargin = BossTuning.StandoffMargin;

        // ---------------------------------------------------------------- sludge lobs (MV-1127) --
        // marked landing spots, reusing the Pipe Turret's own glob/puddle (MV-691), drawn for a boss.

        public const float GlobSpeed = 7f;             // unused by the lob system -- speed is derived
                                                        // per-shot from distance / GlobFlightTime below,
                                                        // so every lob's own flight time is the same
                                                        // regardless of how far it has to travel.
        public const float GlobDamage = 14f;
        public const float GlobSplashRadius = 1.5f;
        public const float GlobPuddleRadius = 1.5f;
        public const float GlobPuddleDuration = 6f;

        /// <summary>Seconds a lob spends in the air, door to door -- also the landing ring's own
        /// minimum warning window (the ticket's own "flight time is at least 1.0 s, so the ring is at
        /// least a 1 s warning").</summary>
        public const float GlobFlightTime = 1.0f;

        /// <summary>The lob's own rendered blob diameter -- bigger than the Pipe Turret's 0.35 m stand-
        /// in glob (the ticket's own "at least 0.6 m across").</summary>
        public const float GlobVisualDiameter = 0.7f;

        /// <summary>Seconds between one lob leaving a cannon mouth and the next leaving the other --
        /// the ticket's own "alternately from the two cannon mouths, 0.12 s apart".</summary>
        public const float GlobLaunchStagger = 0.12f;

        /// <summary>How far off the first (aimed) landing spot the others scatter -- the ticket's own
        /// "2 to 5 m from it".</summary>
        public const float LobScatterMin = 2f;
        public const float LobScatterMax = 5f;

        public const float Phase1GlobInterval = 4f;
        public const int Phase1GlobCount = 5;
        public const float Phase2GlobInterval = 3f;
        public const int Phase2GlobCount = 7;

        /// <summary>Between volleys, each mouth dribbles one droplet at this cadence -- dressing only,
        /// no damage (the ticket's own "between volleys both mouths dribble").</summary>
        public const float DribbleInterval = 0.4f;

        // ---------------------------------------------------------------- sludgers (reuses BroodArc's
        // landing maths, MV-696 §2; escalation and the concurrent cap are MV-1127)

        /// <summary>Sludgers flung per brood wave. MV-1127: 2 -> 3 (the ticket's own "3 at a time").</summary>
        public const int BroodCount = 3;

        public const float Phase1BroodInterval = 9f;
        public const float Phase2BroodInterval = 7f;

        /// <summary>Hatches open this long before the brood is actually released down the chutes.</summary>
        public const float BroodTellTime = 1f;

        /// <summary>MV-1127: the ceiling on her own robots alive at once -- same "kiteable, not a wall
        /// of bodies" guarantee <see cref="BossTuning.MaxConcurrentAdds"/> gives Big Bermuda's volley.</summary>
        public const int MaxConcurrentBrood = 10;

        public const float HatchSide = 2.2f;
        public const float HatchLandingForward = 1.5f;
    }
}
