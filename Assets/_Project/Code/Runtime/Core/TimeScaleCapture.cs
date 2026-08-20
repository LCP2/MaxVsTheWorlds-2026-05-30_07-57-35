namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-506: the shared clamp every pause-on-open screen (HomeScreen, SettingsPanel, WeaponsScreen,
    /// UpgradeScreen) applies at its own <c>_prevTimeScale = Time.timeScale</c> capture line.
    /// <c>Time.timeScale == 0</c> is never a valid "previous speed" to save and hand back later — on a
    /// cold boot the engine can itself start already frozen (Project Settings > Time > Time Scale),
    /// and capturing that 0 verbatim latches the freeze forever the moment the capturing screen closes.
    /// A screen that opens over an already-frozen world must still be able to close into a running one.
    /// No shared pause manager exists in this codebase (each screen still owns its own
    /// <c>_prevTimeScale</c> field and open/close flow) — this is just the one line of arithmetic every
    /// capture site needs, kept in one place instead of copied four times.
    /// </summary>
    public static class TimeScaleCapture
    {
        /// <summary>The value to remember as "what to restore to", given the timescale observed at
        /// capture time. A non-positive observation is never legitimate to restore to, so it clamps to
        /// 1; any positive observation (including a deliberate slow-mo like 0.3) round-trips as-is.</summary>
        public static float ClampForCapture(float observedTimeScale) => observedTimeScale > 0f ? observedTimeScale : 1f;
    }
}
