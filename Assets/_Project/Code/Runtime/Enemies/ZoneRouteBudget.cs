using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Throttles how often a chasing robot re-solves its in-room <see cref="ZoneRouteGrid"/> path
    /// (MV-611) — an A* search over the room's cover grid that, pre-fix, ran fresh every single Chase
    /// tick for every grid-routed robot navigating around cover, however little had changed since the
    /// last tick. The cached step is reused for <see cref="ResolveInterval"/> seconds, dropped
    /// immediately (not waited out) the moment the route itself actually changes — a gate opening or
    /// shutting, or a level reset (<see cref="EnemyNavigation.RouteEpoch"/>) — the same "budget or
    /// invalidation" split <see cref="RouteDwell"/> already uses for the steering RESULT one layer up.
    ///
    /// Pure: no transform, no Unity clock beyond the <c>dt</c> it's handed — same idiom as
    /// <see cref="RouteDwell"/>, <see cref="WallLatch"/>, <see cref="ZoneHysteresis"/>,
    /// <see cref="PursuitStall"/>, so it's testable without a GameObject.
    /// </summary>
    public sealed class ZoneRouteBudget
    {
        /// <summary>How long a resolved in-room step is trusted before the next Chase tick is allowed
        /// to re-solve it. Short enough that a moving goal never reads as stale to the player (well
        /// under RouteDwell's own 0.75 s commit window one layer up), long enough that it's nowhere
        /// near "every frame" for a robot that spends several consecutive frames blocked by the same
        /// piece of cover.</summary>
        public const float ResolveInterval = 0.15f;

        private Vector2? _cachedStep;
        private float _timer;
        private int _epochAtSolve = -1;

        /// <summary>The step this budget last resolved (or cached), if any — null before the first
        /// resolve, or immediately after a solve that found no path at all.</summary>
        public Vector2? CachedStep => _cachedStep;

        /// <summary>True when the caller should actually resolve a fresh step this tick — the cache is
        /// empty, the budget has elapsed, or the level's routing has changed since the last solve —
        /// rather than reuse <see cref="CachedStep"/>. Ticks the internal timer down by
        /// <paramref name="dt"/> as a side effect, same as <see cref="RouteDwell.Resolve"/>.</summary>
        public bool ShouldResolve(int currentEpoch, float dt)
        {
            _timer -= dt;
            return !_cachedStep.HasValue || _timer <= 0f || currentEpoch != _epochAtSolve;
        }

        /// <summary>Record a fresh solve's result, restarting the budget window.</summary>
        public void Commit(Vector2? step, int epoch)
        {
            _cachedStep = step;
            _timer = ResolveInterval;
            _epochAtSolve = epoch;
        }

        /// <summary>Drop the cached step. A pooled robot doesn't inherit the last one's in-room route
        /// (same convention as <see cref="RouteDwell.Reset"/>/<see cref="WallLatch.Reset"/>).</summary>
        public void Reset()
        {
            _cachedStep = null;
            _timer = 0f;
            _epochAtSolve = -1;
        }
    }
}
