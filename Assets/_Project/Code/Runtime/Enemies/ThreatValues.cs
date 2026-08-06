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

        public static float Of(EnemyKind kind) => kind switch
        {
            EnemyKind.Bruiser => Bruiser,
            EnemyKind.Heavy => Heavy,
            EnemyKind.Brute => Brute,
            _ => Rusher,
        };
    }
}
