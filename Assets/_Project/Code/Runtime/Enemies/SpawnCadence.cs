using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Spawn-cadence ramp for the factory swarm (YT-63 kiteability): the gap between spawns eases
    /// from a slow opening interval down to a fast steady-state over a ramp window, so a run starts
    /// breathable and builds pressure. Pure + unit-testable — no MonoBehaviour, no clock.
    /// </summary>
    public static class SpawnCadence
    {
        /// <summary>
        /// Seconds between spawns at a given run time. Eases linearly from
        /// <paramref name="startInterval"/> (breathable open) to <paramref name="minInterval"/>
        /// (steady-state pressure) across <paramref name="rampSeconds"/>, then holds at the min.
        /// </summary>
        public static float IntervalAt(float elapsed, float startInterval, float minInterval, float rampSeconds)
        {
            if (rampSeconds <= 0f) return minInterval;
            float t = Mathf.Clamp01(Mathf.Max(0f, elapsed) / rampSeconds);
            return Mathf.Lerp(startInterval, minInterval, t);
        }
    }
}
