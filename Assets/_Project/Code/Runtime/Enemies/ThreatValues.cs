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

        // MV-539: the Bolter is a small-tier ranged kind like the Gunner, but simpler (no beam-hold
        // DPS, no splash) and less tanky (kept below the rusher's HP, unlike the Gunner) — placed
        // just under the Gunner's 3.0 on the same placeholder footing.
        public const float Bolter = 2.8f;

        // MV-688: the ticket's own authored THV — a submerge-cycling ambush kind, placed above the
        // Gunner (3.0) on the same placeholder footing until real per-type calibration lands.
        public const float Lurker = 3.0f;

        // MV-691: the ticket's own authored THV — a static lobber, placed just under the Bolter
        // (2.8) on the same placeholder footing: it trades mobility for area denial, so it sits
        // below every kind that can also close distance.
        public const float Turret = 2.5f;

        /// <summary>MV-705: the ticket's own authored THV — priced below the Bolter/Lurker/Turret band
        /// on purpose: a Sludger's death spawns two more robots (each priced at their own THV once they
        /// exist), so the drone itself is billed as the lighter half of the pair's total threat.</summary>
        public const float Sludger = 2.0f;

        /// <summary>MV-707: the ticket's own authored THV — placed just above the Blinker's 3.3, below
        /// the Launcher's 3.6, on the same placeholder footing as every other kind here.</summary>
        public const float Charger = 3.4f;

        /// <summary>MV-1217: the ticket's own authored THV — a slow, hard-to-kill garrison-only
        /// platform, priced well above every other kind here on the same placeholder footing (it is
        /// twice a shed's health and carries two independent ranged weapons) until real per-type
        /// calibration lands.</summary>
        public const float Dreadnought = 9.0f;

        /// <summary>MV-1224: the ticket's own authored THV — priced below every other kind here, on
        /// the same placeholder footing: 12 HP and no ranged payoff of its own, so even packed 4-6 to
        /// a group it never out-prices the Sludger band.</summary>
        public const float Skitter = 0.8f;

        /// <summary>MV-1228: the ticket's own authored THV — priced well above every other kind here,
        /// just under the Dreadnought's 9.0: 600 HP and two high-damage melee attacks (34/28), but no
        /// ranged payoff and no twin-weapon redundancy, on the same placeholder footing until real
        /// per-type calibration lands.</summary>
        public const float Breaker = 6.5f;

        public static float Of(EnemyKind kind) => kind switch
        {
            EnemyKind.Bruiser => Bruiser,
            EnemyKind.Heavy => Heavy,
            EnemyKind.Brute => Brute,
            EnemyKind.Gunner => Gunner,
            EnemyKind.Launcher => Launcher,
            EnemyKind.Blinker => Blinker,
            EnemyKind.Bolter => Bolter,
            EnemyKind.Lurker => Lurker,
            EnemyKind.Turret => Turret,
            EnemyKind.Sludger => Sludger,
            EnemyKind.Charger => Charger,
            EnemyKind.Dreadnought => Dreadnought,
            EnemyKind.Skitter => Skitter,
            EnemyKind.Breaker => Breaker,
            _ => Rusher,
        };
    }
}
