using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// MV-946 (Lee, TestFlight 0.9.9): "Max can teleport out of the playable area... if he keeps
    /// walking away he drops into infinity with no way back." A blink/deploy landing point is already
    /// clamped onto walkable ground (<see cref="MapData.IsWalkable"/>, MV-945) — this is the backstop
    /// for every OTHER way an entity can end up somewhere it should never be (a level-design gap, a
    /// knockback chain, anything solid ground stops existing underneath). Two pure, Tier-2-testable
    /// checks that <see cref="FallRecoveryState"/> shares between Max and every deployed
    /// <see cref="Sentinel"/>.
    /// </summary>
    public static class FallSafetyNet
    {
        /// <summary>Metres below the world's floor plane (Y=0 — every area's floor sits there; only
        /// its decks rise, per <see cref="MapData.deckHeight"/>) before a position counts as "fallen
        /// through", per the ticket's own AC.</summary>
        public const float BelowFloorMargin = 2f;

        /// <summary>Seconds a position may stay out of play before <see cref="FallRecoveryState"/>
        /// recovers it — the ticket's own "briefly might be acceptable" grace window.</summary>
        public const float GraceSeconds = 0.5f;

        /// <summary>True if <paramref name="position"/> is more than <see cref="BelowFloorMargin"/>
        /// below the floor plane, or outside <paramref name="map"/>'s own <see cref="MapData.Bounds"/>
        /// (XZ only — falling past a DECK's height is normal, only the floor plane underneath every
        /// area is the true bottom). No map loaded (a bare fixture) only ever fails the floor check —
        /// there is no map to be out of bounds of.</summary>
        public static bool IsOutOfPlay(Vector3 position, MapData map)
        {
            if (position.y < -BelowFloorMargin) return true;
            if (map == null) return false;

            Rect bounds = map.Bounds();
            return position.x < bounds.xMin || position.x > bounds.xMax ||
                   position.z < bounds.yMin || position.z > bounds.yMax;
        }

        /// <summary>Resets to 0 the instant <paramref name="outOfPlay"/> is false — a position that
        /// dips out of play and straight back in never accumulates toward the recovery threshold. Pure,
        /// same shape as <see cref="MaxWorlds.Player.PlayerHealth.Regenerate"/>.</summary>
        public static float AccumulateOutOfPlayTime(float current, bool outOfPlay, float dt) =>
            outOfPlay ? current + Mathf.Max(0f, dt) : 0f;

        /// <summary>True once <see cref="AccumulateOutOfPlayTime"/>'s running total clears
        /// <see cref="GraceSeconds"/>.</summary>
        public static bool ShouldRecover(float timeOutOfPlay) => timeOutOfPlay > GraceSeconds;
    }

    /// <summary>
    /// Per-entity state for the safety net above — shared by Max's own
    /// <see cref="MaxWorlds.Player.PlayerController"/> and <see cref="Sentinel"/>. Deliberately a plain
    /// class, not a MonoBehaviour: both owners already run their own Update loop with a live position
    /// and an explicit dt (MV-503/MV-624's own "never read Time.deltaTime inside the thing under test"
    /// idiom), so this only needs to hold the two running numbers and hand back a recovery point the
    /// instant the grace period lapses.
    /// </summary>
    public sealed class FallRecoveryState
    {
        private Vector3 _lastSafePosition;
        private float _timeOutOfPlay;

        /// <summary>Seeded with wherever the entity starts (spawn/deploy) — assumed safe, so this
        /// doubles as the ticket's "or the current area's entry if there is none" fallback: there is
        /// never a true null case, since a fresh state already has somewhere real to return to before a
        /// single grounded tick has run.</summary>
        public FallRecoveryState(Vector3 initialSafePosition) => _lastSafePosition = initialSafePosition;

        /// <summary>Call once a tick with the entity's CURRENT position. Records it as the new safe
        /// point whenever it's grounded and in play; otherwise accumulates out-of-play time. Returns the
        /// position to recover to the instant <see cref="FallSafetyNet.ShouldRecover"/> trips — null
        /// every other tick, including the tick recovery just happened on (the timer resets with it) —
        /// so a caller only ever teleports on a non-null return and never has to track the threshold
        /// itself.</summary>
        public Vector3? Tick(Vector3 position, MapData map, bool grounded, float dt)
        {
            bool outOfPlay = FallSafetyNet.IsOutOfPlay(position, map);
            if (!outOfPlay && grounded) _lastSafePosition = position;

            _timeOutOfPlay = FallSafetyNet.AccumulateOutOfPlayTime(_timeOutOfPlay, outOfPlay, dt);
            if (!FallSafetyNet.ShouldRecover(_timeOutOfPlay)) return null;

            _timeOutOfPlay = 0f;
            return _lastSafePosition;
        }
    }
}
