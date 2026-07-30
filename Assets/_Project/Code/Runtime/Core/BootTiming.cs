using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// Logs named milestones against <see cref="Time.realtimeSinceStartup"/> — a clock that runs from
    /// process start and, unlike <see cref="Time.time"/>, survives a single-scene reload untouched.
    /// That makes it the one clock that can time BOTH of YT-216's targets from the same log: cold
    /// app-launch → controllable Max, and Replay → fighting again, just by diffing two marks.
    /// </summary>
    public static class BootTiming
    {
        public static void Mark(string label) =>
            Debug.Log($"[Boot] {label} at {Time.realtimeSinceStartup:0.000}s");
    }
}
