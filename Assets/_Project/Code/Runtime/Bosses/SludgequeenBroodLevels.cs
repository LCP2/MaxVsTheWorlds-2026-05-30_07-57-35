using MaxWorlds.Enemies;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Which robot kinds Sludgequeen's own brood wave may draw from at each spawn level (MV-1127 §6:
    /// "level 1 sludgers; level 2 adds chargers; level 3 adds bolters; level 4 adds one brute per
    /// group"). Same shared time-based clock as <see cref="BroodSpawnLevels"/>
    /// (<see cref="BigBermudaBrain.SpawnLevel"/>), but a different pool — Sludgequeen's own roster, not
    /// Big Bermuda's. Levels 1-3 grow this pool the same "adds" way <see cref="BroodSpawnLevels"/>
    /// does; level 4's own guaranteed one-Brute-per-wave is NOT a pool addition (a uniform draw over 4
    /// kinds would make a Brute a 1-in-4 chance, not "one per group") and is handled by
    /// <see cref="SludgequeenBoss.SpawnBrood"/> directly, drawing everything else from
    /// <see cref="Level3"/> (also returned here for level 4, so a caller that does draw uniformly still
    /// gets a sensible pool).
    /// </summary>
    public static class SludgequeenBroodLevels
    {
        private static readonly EnemyKind[] Level1 = { EnemyKind.Sludger };
        private static readonly EnemyKind[] Level2 = { EnemyKind.Sludger, EnemyKind.Charger };
        private static readonly EnemyKind[] Level3 = { EnemyKind.Sludger, EnemyKind.Charger, EnemyKind.Bolter };

        /// <summary>The kind set a brood wave may draw from at <paramref name="level"/> (1..4, clamped
        /// at either end) — each sludger in a wave draws uniformly from this set, except level 4's own
        /// guaranteed Brute slot (see this class's own doc comment).</summary>
        public static EnemyKind[] KindsFor(int level) => level switch
        {
            <= 1 => Level1,
            2 => Level2,
            _ => Level3,
        };
    }
}
