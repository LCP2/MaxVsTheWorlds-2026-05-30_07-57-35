namespace MaxWorlds.Arena
{
    /// <summary>
    /// Authored magnitudes for the gated 10-area arena (v0.5 recut spec §1/§9) that don't yet have an
    /// owning system to live on — the gate/area mechanic itself is WV-222. Settings only for now
    /// (WV-234), ready for that ticket to spend, same idiom as
    /// <see cref="MaxWorlds.Enemies.RobotCompositionTuning"/>.
    /// </summary>
    public static class ArenaTuning
    {
        /// <summary>Sequential outdoor rooms in a run (<c>areaCount</c>).</summary>
        public const float DefaultAreaCount = 10f;

        /// <summary>Sustained primary fire, seconds, to break a gate (<c>gateBreakSeconds</c>).</summary>
        public const float DefaultGateBreakSeconds = 4f;

        /// <summary>Whether a gate requires its room cleared of robots before it can be attacked
        /// (<c>gateRequiresClear</c>) — off by default, stored as 0/1.</summary>
        public const float DefaultGateRequiresClear = 0f;

        /// <summary>Sheds placed across the run's areas (spec §4: "roughly one shed every other
        /// room, ~7 sheds").</summary>
        public const float DefaultShedCount = 7f;
    }
}
