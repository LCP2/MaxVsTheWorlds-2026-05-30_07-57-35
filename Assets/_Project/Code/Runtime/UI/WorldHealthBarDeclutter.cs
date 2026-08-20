using System.Diagnostics;
using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Drives <see cref="WorldHealthBar.ResolveClutter"/> once a frame (MV-473) — a single, later
    /// MonoBehaviour rather than each bar resolving itself, because the pass needs every showing
    /// bar's <see cref="WorldHealthBar.SyncToBody"/> for THIS frame to have already run before it
    /// compares positions; <see cref="DefaultExecutionOrder"/> is Unity's actual cross-type ordering
    /// guarantee, not "install this script after that one in the hierarchy" (which orders nothing).
    ///
    /// Self-installing the same way every other Dev-adjacent singleton in this project is (see
    /// <see cref="MaxWorlds.Dev.RobotRosterDirector"/>) — one per scene, created on first load, never
    /// destroyed. No arm flag: unlike the capture directors this is not test-only tooling, it is the
    /// shipped de-clutter behaviour.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class WorldHealthBarDeclutter : MonoBehaviour
    {
        /// <summary>XZ metres within which two showing bars count as clustered. Close to a robot's own
        /// bar width (BarWidth in RobotEnemy/PlayerHealth) — the point of the check is "would these two
        /// bars occupy the same patch of screen", not "are the robots adjacent".</summary>
        private const float ClusterRadius = 1.7f;

        /// <summary>Extra world-up metres per stacked rank. Bigger than a robot bar's own canvas world
        /// height (~0.2-0.3 m at the current BarPixelHeight/worldWidth ratio) so a stacked pair reads as
        /// two distinct bars, not one touching the other's edge.</summary>
        private const float StackStep = 0.5f;

        /// <summary>Last pass's wall-clock cost, microseconds — read by <c>HealthBarClusterCapture</c>
        /// (MV-473) so the fix comment can report a measured number instead of an estimate.</summary>
        public static double LastResolveMicroseconds { get; private set; }

        private static readonly Stopwatch Timer = new Stopwatch();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<WorldHealthBarDeclutter>() != null) return;
            var go = new GameObject("WorldHealthBarDeclutter");
            go.AddComponent<WorldHealthBarDeclutter>();
            DontDestroyOnLoad(go);
        }

        private void LateUpdate()
        {
            Timer.Restart();
            WorldHealthBar.ResolveClutter(ClusterRadius, StackStep);
            Timer.Stop();
            LastResolveMicroseconds = Timer.Elapsed.TotalMilliseconds * 1000.0;
        }
    }
}
