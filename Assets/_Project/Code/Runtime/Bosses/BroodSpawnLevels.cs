using MaxWorlds.Enemies;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// Which robot kinds the brood volley may draw from at each spawn level (MV-588) — the fight's
    /// actual weapon now that the charge is gone: "kill it before its army outgrows you". The level
    /// itself is purely time-based (<see cref="BigBermudaBrain.SpawnLevel"/>); this is only the fixed
    /// table of what each level unlocks. Pure and stateless, so it is unit-testable on its own.
    /// </summary>
    public static class BroodSpawnLevels
    {
        private static readonly EnemyKind[] Level1 = { EnemyKind.Rusher };
        private static readonly EnemyKind[] Level2 = { EnemyKind.Rusher, EnemyKind.Bruiser };
        private static readonly EnemyKind[] Level3 =
            { EnemyKind.Rusher, EnemyKind.Bruiser, EnemyKind.Gunner, EnemyKind.Blinker };
        private static readonly EnemyKind[] Level4 =
            { EnemyKind.Rusher, EnemyKind.Bruiser, EnemyKind.Gunner, EnemyKind.Blinker, EnemyKind.Heavy, EnemyKind.Bolter };

        /// <summary>The kind set a volley may draw from at <paramref name="level"/> (1..4, clamped at
        /// either end) — each robot in a volley draws uniformly from this set.</summary>
        public static EnemyKind[] KindsFor(int level) => level switch
        {
            <= 1 => Level1,
            2 => Level2,
            3 => Level3,
            _ => Level4,
        };
    }
}
