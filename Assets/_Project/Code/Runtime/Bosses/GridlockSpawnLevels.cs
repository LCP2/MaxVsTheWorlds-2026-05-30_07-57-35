using MaxWorlds.Enemies;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Which robot kinds GRIDLOCK's SQUAD attack may draw from at each escalation level (MV-1306's
    /// own "L1 Rushers, L2 adds Bruisers, L3 adds Bolters and one Jammer, L4 adds a Brute") — the
    /// ONGOING draw pool only. The singular "one Jammer"/"a Brute" are one-time adds
    /// <see cref="GridlockBoss"/> queues itself the instant a level is first reached, not members of
    /// this ongoing set — Bruiser/Bolter are plural in the ticket's own wording (regular pool
    /// members), Jammer/Brute are singular (a one-off). Pure and stateless, same shape as
    /// <see cref="BroodSpawnLevels"/>.
    /// </summary>
    public static class GridlockSpawnLevels
    {
        private static readonly EnemyKind[] Level1 = { EnemyKind.Rusher };
        private static readonly EnemyKind[] Level2 = { EnemyKind.Rusher, EnemyKind.Bruiser };
        private static readonly EnemyKind[] Level3Plus = { EnemyKind.Rusher, EnemyKind.Bruiser, EnemyKind.Bolter };

        /// <summary>The kind set a SQUAD wave may draw from at <paramref name="level"/> (1..4,
        /// clamped at either end) — each robot in a wave draws uniformly from this set.</summary>
        public static EnemyKind[] KindsFor(int level) => level switch
        {
            <= 1 => Level1,
            2 => Level2,
            _ => Level3Plus,
        };
    }
}
