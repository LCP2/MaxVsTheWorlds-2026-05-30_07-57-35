namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Deployable Sentinels' (MV-362) three independent upgrade tracks — the ticket's DECISION
    /// (13 Aug 2026, comment 11626): "two deployables under one system... each with its own single
    /// upgrade track (blocker strength; hose power), plus a shared Deployment Count upgrade track".
    /// Same shape as <see cref="WaterBalloonTrackKind"/>: every track is owned/leveled from run
    /// start at Level 1 (there is no "not owned" state on a track itself), but — unlike Water
    /// Balloon's tracks — spending a part on one is gated on <see cref="AbilityKind.Sentinels"/>
    /// actually being acquired first (<see cref="PartSpend.TrySpendOnSentinelTrack"/>), since these
    /// tracks are meaningless before the system they belong to exists.
    /// </summary>
    public enum SentinelTrackKind
    {
        /// <summary>The Wall (Blocker) sentinel's only track — how much punishment it can absorb
        /// before it breaks.</summary>
        WallStrength,

        /// <summary>The Gunner (Attack) sentinel's only track — how hard its auto-fire hits, always
        /// kept below Max's own current primary output (see <see cref="AbilityTuning.SentinelGunnerDamagePerShot"/>).</summary>
        GunnerPower,

        /// <summary>Shared by both kinds (DECISION, 13 Aug 2026): how many sentinels — any mix of
        /// Wall and Gunner — Max may have deployed at once. Starts at 1, upgradeable to 2, 3, 4.</summary>
        DeploymentCount,
    }
}
