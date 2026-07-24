namespace MaxWorlds.Enemies
{
    /// <summary>
    /// How many robots are alive across EVERY factory on the map, right now (YT-186).
    ///
    /// Each <see cref="EnemySpawner"/> already caps its OWN concurrent count (<c>maxLiveEnemies</c>,
    /// 8) — a number tuned when the map had two factories, for a survivable ceiling of 16. YT-185
    /// gave the map a fourth factory without touching that number, so four independent caps of 8 now
    /// sum to 32 — twice the field the frame budget (rig, shadows, per-frame VFX/telegraph polling)
    /// was ever tuned against, and the direct cause of the 60→30fps regression on device.
    ///
    /// This is the one place that knows the total, so a spawner can hold a robot back for room on the
    /// FIELD, not just room in its own shed — same shape as <see cref="MaxWorlds.Factories.FactoryCensus"/>
    /// and <see cref="DifficultyDirector"/>: one number for the whole run, not one per spawner.
    ///
    /// The Invasion Level still escalates exactly as before (spawn cadence and toughness are untouched)
    /// — this only ever trims the top of the count, trading uncapped swarm size for the tougher/faster
    /// robots <see cref="DifficultyDirector.ToughnessMultiplier"/> already provides, per the Craft
    /// Bible's readability-over-density tie-break.
    /// </summary>
    public static class EnemyCensus
    {
        /// <summary>Concurrent robots the field can hold across every factory combined. Restores
        /// roughly the pre-YT-185 peak (two-to-three factories at their individual cap) rather than
        /// the 32 four uncapped factories now sum to.</summary>
        public const int GlobalMax = 20;

        private static int _live;

        /// <summary>Robots alive right now, across every factory. Read-only outside; spawners report
        /// through <see cref="Register"/>/<see cref="Forget"/>.</summary>
        public static int Live => _live;

        /// <summary>Is there room on the FIELD for one more, regardless of which factory is asking.</summary>
        public static bool HasRoom => _live < GlobalMax;

        /// <summary>Back to zero. Called when a level starts building (<c>MapRuntime.Build</c>), so a
        /// scene loaded a second time — in the game or in a test — starts from an empty field, not
        /// wherever the last run's robots left the count.</summary>
        public static void Reset() => _live = 0;

        /// <summary>A robot just landed on the field.</summary>
        public static void Register() => _live++;

        /// <summary>A robot just left the field (died, or its factory was torn down). Floored at zero
        /// so a mismatched Forget from a test/edge case can never take the count negative.</summary>
        public static void Forget() => _live = _live > 0 ? _live - 1 : 0;
    }
}
