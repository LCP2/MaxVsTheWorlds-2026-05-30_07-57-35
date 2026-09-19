using System;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Whole-world run progress that must survive a checkpoint resume the same way
    /// <see cref="DeathRunState.DeathsTaken"/> does (MV-841). <c>RunStats</c> (UI namespace) is a
    /// plain per-instance object owned by <c>RunTracker</c>, not a static, so on a checkpoint resume
    /// (<c>SaveSystem.RestoreCheckpoint</c> — the Home screen's RESUME tap, which never reloads the
    /// scene: the same <c>RunTracker</c> instance just keeps ticking) there is nothing that would
    /// otherwise carry a captured elapsed time / kill count back into it, unlike
    /// <see cref="DeathRunState"/> which <c>SaveSystem</c> can restore directly because it's already
    /// static. <c>RunTracker</c> mirrors its own <c>RunStats</c> into here on every tick/kill, and
    /// applies a restore back onto its live <c>RunStats</c> via <see cref="Restored"/> — this is the
    /// bridge, not a second source of truth for the tick/kill logic itself.
    /// </summary>
    public static class RunProgressState
    {
        public static float Elapsed { get; private set; }
        public static int Kills { get; private set; }

        /// <summary>Fired whenever a checkpoint restore (or a world/run reset) overwrites these
        /// values — a live <c>RunTracker</c> subscribes so a RESUME that never reloads the scene
        /// still picks the restored elapsed time/kill count up immediately.</summary>
        public static event Action<float, int> Restored;

        /// <summary>Mirror a live run's current values (called every tick/kill) so a checkpoint
        /// capture — triggered externally, on area entry or app backgrounding — always reads the
        /// latest state with no direct reference to the live <c>RunTracker</c>.</summary>
        public static void Sync(float elapsed, int kills)
        {
            Elapsed = Math.Max(0f, elapsed);
            Kills = Math.Max(0, kills);
        }

        /// <summary>Overwrite from a captured checkpoint (MV-841) — same "overwrite, not add"
        /// contract as <see cref="DeathRunState.RestoreDeathsTaken"/>. Fires <see cref="Restored"/>
        /// so a <c>RunTracker</c> already alive (no scene reload happened) applies it immediately.</summary>
        public static void Restore(float elapsed, int kills)
        {
            Sync(elapsed, kills);
            Restored?.Invoke(Elapsed, Kills);
        }

        /// <summary>Back to a fresh world's baseline: a brand new run, or advancing into the next
        /// world (<c>RunFlow.StartNextWorld</c>) — these stats cover one world each, not the whole
        /// game.</summary>
        public static void Reset() => Restore(0f, 0);
    }
}
