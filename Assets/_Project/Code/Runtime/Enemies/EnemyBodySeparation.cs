using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-434: keeps a robot's body from overlapping Max's even though
    /// <c>EnemySpawner.LetThePlayerThrough</c>/<c>AreaAccumulationDirector.LetThePlayerThrough</c>
    /// (MV-321) call <c>Physics.IgnoreCollision</c> on every spawn, which leaves nothing physical
    /// stopping a robot from occupying Max's exact position. That collision skip stays exactly as
    /// it is — it's what keeps Max from ever being pinned by a converging swarm — so separation has
    /// to be enforced by the robot's own movement instead.
    ///
    /// Measured against Max's CURRENT position every call, not wherever a robot was last chasing
    /// toward: a robot standing its ground gets shoved aside as Max walks into it, which is what
    /// keeps this from reintroducing the pin — Max is never the one being held back.
    ///
    /// Pure maths, no transforms, no clock — same testable idiom as <see cref="EnemySeparation"/>.
    /// </summary>
    public static class EnemyBodySeparation
    {
        /// <summary>Extra clearance beyond the two colliders' own radii.</summary>
        public const float DefaultMargin = 0.15f;

        /// <summary>Fastest a robot may re-aim, degrees/second — caps the instant
        /// <c>Quaternion.LookRotation</c> snap that read as a rapid spin once a robot pinned
        /// against Max made its target direction numerically unstable frame to frame.</summary>
        public const float DefaultMaxTurnDegreesPerSecond = 540f;

        /// <summary>The closest a robot's centre may sit to Max's, XZ-plane only.</summary>
        public static float MinDistance(float robotRadius, float playerRadius, float margin = DefaultMargin) =>
            robotRadius + playerRadius + margin;

        /// <summary>
        /// If <paramref name="robotPos"/> is inside <paramref name="minDistance"/> of
        /// <paramref name="playerPos"/>, returns it pushed back out along the XZ vector between
        /// them; otherwise returns <paramref name="robotPos"/> unchanged. Y passes through
        /// untouched — this only ever separates on the ground plane.
        /// </summary>
        public static Vector3 Clamp(Vector3 robotPos, Vector3 playerPos, float minDistance)
        {
            // MV-1021: a non-finite playerPos/robotPos (a one-frame glitch upstream — target lost,
            // a division by a momentarily-zero distance elsewhere) used to propagate straight through:
            // `away` went NaN, both magnitude comparisons below are false for NaN operands, and the
            // "degenerate" branch built its correction from the NaN playerPos anyway. That NaN reached
            // RobotEnemy's disable/set/enable reposition and fed PhysX a non-finite desc — the crash
            // this method's own caller chain (ClampBodySeparation) is named in the ticket for. Refusing
            // here, before any of that maths runs, is cheaper than guarding every caller separately.
            if (!IsFinite(robotPos) || !IsFinite(playerPos)) return robotPos;

            Vector3 away = robotPos - playerPos;
            away.y = 0f;
            float dist = away.magnitude;
            if (dist >= minDistance) return robotPos;

            // Degenerate — robot exactly on Max's position, no direction to push along. Any
            // direction is equally valid here; picking one deterministically beats leaving the
            // two stacked.
            Vector3 dir = dist > 1e-4f ? away / dist : Vector3.forward;
            Vector3 corrected = playerPos + dir * minDistance;
            corrected.y = robotPos.y;
            return corrected;
        }

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
            !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
            !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    }
}
