namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Per-type Threat Value (THV) — World &amp; Difficulty Framework (Confluence MVW 34439170 §4-5,
    /// MV-268). <c>THV(type) = (hp + effective_shielding) × dps × speed_factor × archetype_weight</c>
    /// collapsed to the flat relative placeholders the ticket names, since no world has real per-type
    /// stat calibration yet (that lands with World 1 content in ticket 4/MV-270) — these are what the
    /// budget solver spends against until then.
    /// </summary>
    public static class ThreatValues
    {
        public const float Rusher = 1.0f;   // "small"
        public const float Bruiser = 2.5f;  // "large"
        public const float Heavy = 4.5f;
        public const float Brute = 7.0f;

        // MV-293's ranged/teleport kinds (MV-310): placeholder relative values, same footing as the
        // four above until a real per-type calibration lands — sit between Bruiser and Heavy, since
        // each trades the bruiser's raw melee threat for standoff/mobility pressure instead.
        public const float Gunner = 3.0f;
        public const float Launcher = 3.6f;
        public const float Blinker = 3.3f;

        public static float Of(EnemyKind kind) => kind switch
        {
            EnemyKind.Bruiser => Bruiser,
            EnemyKind.Heavy => Heavy,
            EnemyKind.Brute => Brute,
            EnemyKind.Gunner => Gunner,
            EnemyKind.Launcher => Launcher,
            EnemyKind.Blinker => Blinker,
            _ => Rusher,
        };
    }
}
