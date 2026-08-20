using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Holds a robot to its current route decision for a minimum dwell, so upstream jitter — WallLatch
    /// re-deciding a side, ZoneHysteresis settling a room, Perception's goal flipping between Max's
    /// live position and his last-known one — cannot repeatedly flip the direction actually walked
    /// (MV-477).
    ///
    /// None of <see cref="WallLatch"/> (MV-447), <see cref="ObstacleSteering.SlideAlongWall"/> or
    /// <see cref="ZoneHysteresis"/> puts a commit window on the RESULT of a route decision, only on the
    /// inputs that feed it. A hedge is the case none of them were tuned against: it has a collider, so
    /// WallLatch latches onto it, but it is deliberately off the Cover layer (MV-400), so the sight ray
    /// passes straight through it and <see cref="Perception.HasSight"/> flips every frame the ray
    /// happens to clear a gap — flipping <c>RobotEnemy.TickChase</c>'s goal between Max's live position
    /// and his last-known one, and the resulting steering direction with it, several times a second,
    /// with no net displacement either way.
    ///
    /// Pure: no transform, no clock of its own beyond the <c>dt</c> it is handed — same idiom as
    /// <see cref="PursuitStall"/>, <see cref="WallLatch"/> and <see cref="ZoneHysteresis"/>, so it is
    /// testable without a GameObject. Deliberately dumb about WHY a candidate changed — it only ever
    /// measures how far a new one differs from the direction it is currently committed to, and how long
    /// it has held that commitment.
    /// </summary>
    public sealed class RouteDwell
    {
        /// <summary>How long a committed direction must be held before another large reversal is
        /// allowed to replace it (decided with Lee 2026-08-19).</summary>
        public const float MinDwell = 0.75f;

        /// <summary>How far a candidate must differ from the committed direction to count as a
        /// reversal that needs dwelling, rather than the ordinary frame-to-frame drift of a chase still
        /// tracking a moving goal. Below this, every candidate is accepted immediately — this is what
        /// keeps open-ground chase responsive: a robot smoothly tracking Max never produces a change
        /// this large tick to tick, so the dwell never engages at all.</summary>
        public const float ReversalThresholdDegrees = 90f;

        private Vector3 _committed;
        private bool _hasCommitted;
        private float _elapsed;

        /// <summary>
        /// Call once per Chase tick with the direction the ordinary steering pipeline (routing + wall
        /// latch) would walk this frame. Returns that candidate unchanged unless it reverses by more
        /// than <see cref="ReversalThresholdDegrees"/> from the currently committed direction AND less
        /// than <see cref="MinDwell"/> has elapsed since the last such reversal — in which case it
        /// returns the still-committed direction instead.
        /// </summary>
        /// <param name="candidate">This frame's desired direction (Y ignored). A near-zero vector is
        /// treated as "no opinion" and returns whatever is already committed.</param>
        /// <param name="dt">Seconds since the last call.</param>
        /// <param name="forceImmediate">Bypass the dwell and commit to <paramref name="candidate"/>
        /// immediately — the current waypoint was just reached, or the route was invalidated (a gate
        /// changed, a level/area reset). Both are a genuinely new decision, not a re-litigation of the
        /// current one, and must never wait out the clock.</param>
        public Vector3 Resolve(Vector3 candidate, float dt, bool forceImmediate = false)
        {
            candidate.y = 0f;
            bool candidateValid = candidate.sqrMagnitude > 1e-6f;
            if (candidateValid) candidate.Normalize();

            if (!_hasCommitted || forceImmediate)
            {
                if (candidateValid)
                {
                    _committed = candidate;
                    _hasCommitted = true;
                }
                _elapsed = 0f;
                return _hasCommitted ? _committed : Vector3.zero;
            }

            _elapsed += dt;

            if (!candidateValid) return _committed;

            float angle = Vector3.Angle(_committed, candidate);
            if (angle <= ReversalThresholdDegrees)
            {
                _committed = candidate; // ordinary drift — always allowed, never dwelled
                return _committed;
            }

            if (_elapsed >= MinDwell)
            {
                _committed = candidate; // dwell served — this reversal may land
                _elapsed = 0f;
            }

            return _committed;
        }

        /// <summary>Drop the commitment. A pooled robot doesn't inherit the last one's route decision
        /// (same convention as <see cref="WallLatch.Reset"/>/<see cref="ZoneHysteresis.Reset"/>).</summary>
        public void Reset()
        {
            _hasCommitted = false;
            _committed = Vector3.zero;
            _elapsed = 0f;
        }
    }
}
