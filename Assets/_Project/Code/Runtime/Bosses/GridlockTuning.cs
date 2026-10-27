namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Every number GRIDLOCK — the City's boss (MV-1299) — is made of, in one place, same "a const
    /// cannot be shadowed by a serialized scene copy" reasoning as <see cref="BossTuning"/>. This
    /// ticket is her body, wake, walk and passive touch only — her attacks (SQUAD/JUMP/STRIKE,
    /// escalation) are MV-1306's, and the arena/shield/finale are MV-1309's; neither adds a knob here
    /// yet.
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
    }
}
