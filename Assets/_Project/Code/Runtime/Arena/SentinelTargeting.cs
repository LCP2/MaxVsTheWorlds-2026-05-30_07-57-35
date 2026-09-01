using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Pure proximity-based target-selection rule for MV-362: "Aggro is proximity-based, not
    /// absolute. Robots attack a sentinel when it blocks them or is the nearest target — they must
    /// NOT always prefer sentinels over Max, or the player just builds a distraction and walks past
    /// every fight." Kept static/pure (no live scene) so the rule itself is unit-testable in
    /// isolation from <see cref="MaxWorlds.Enemies.RobotEnemy"/>.
    /// </summary>
    public static class SentinelTargeting
    {
        /// <summary>How far a robot will divert toward a Sentinel it hasn't reached yet — beyond this
        /// a distant, off-fight sentinel never steals a robot away from a chase happening somewhere
        /// else in the arena. Matches <see cref="MaxWorlds.Enemies.EnemyArchetype.Launcher"/>'s own max
        /// fire range (10 m) — the widest "how far away a robot already treats as a live threat"
        /// number already in the game, rather than inventing a new one.</summary>
        public const float AggroRadius = 10f;

        /// <summary>True if a robot standing this close to Max and to the nearest Sentinel should
        /// engage the Sentinel instead — strictly closer AND within <paramref name="aggroRadius"/>,
        /// so it is never an absolute preference (a robot two rooms away from its target sentinel
        /// still goes straight for Max).</summary>
        public static bool ShouldEngageSentinel(float distanceToPlayer, float distanceToSentinel, float aggroRadius)
        {
            if (distanceToSentinel > aggroRadius) return false;
            return distanceToSentinel < distanceToPlayer;
        }

        /// <summary>The nearest living deployed Sentinel to <paramref name="from"/>, or null if none
        /// are deployed (or all are dead).</summary>
        public static Sentinel Nearest(Vector3 from)
        {
            Sentinel best = null;
            float bestSq = float.MaxValue;
            var active = Sentinel.Active;
            for (int i = 0; i < active.Count; i++)
            {
                Sentinel s = active[i];
                if (s == null || !s.IsAlive) continue;
                float d = (s.transform.position - from).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = s; }
            }
            return best;
        }

        /// <summary>Attack Mode's (MV-636) forward-cone half-angle, degrees either side of Max's own
        /// facing — the ticket's chosen starting value; a tuning retune is a new ticket, not a reopen
        /// of this one.</summary>
        public const float AttackModeForwardConeHalfAngleDegrees = 60f;

        /// <summary>True if <paramref name="candidatePosition"/> lies within the forward cone of Max's
        /// facing, measured from Max's own position (MV-636 Attack Mode target priority) — flattened to
        /// the XZ plane, same "this is a top-down game" convention <see cref="Sentinel"/>'s own sidestep
        /// maths uses. A candidate standing exactly on Max's own position has no defined direction and
        /// reads as NOT in the cone rather than dividing by zero.</summary>
        public static bool IsWithinForwardCone(Vector3 maxPosition, Vector3 maxForward, Vector3 candidatePosition, float halfAngleDegrees)
        {
            Vector3 toCandidate = new Vector3(candidatePosition.x - maxPosition.x, 0f, candidatePosition.z - maxPosition.z);
            if (toCandidate.sqrMagnitude < 1e-6f) return false;

            Vector3 forward = new Vector3(maxForward.x, 0f, maxForward.z);
            if (forward.sqrMagnitude < 1e-6f) return false;

            return Vector3.Angle(forward.normalized, toCandidate.normalized) <= halfAngleDegrees;
        }

        /// <summary>Attack Mode's (MV-636) fire-target priority: the nearest-to-<paramref name="sentinelPosition"/>
        /// candidate that also lies within Max's forward cone wins over the globally-nearest candidate,
        /// falling back to nearest-overall when none qualify (per the ticket: "falling back to the
        /// globally-nearest robot if none are within that cone"). Returns the winning index into
        /// <paramref name="candidatePositions"/>, or -1 if it's empty. Index-based and pure — deliberately
        /// decoupled from <see cref="MaxWorlds.Enemies.RobotEnemy"/>/live physics, same reasoning as
        /// <see cref="ShouldEngageSentinel"/>, so the priority rule is unit-testable off plain positions.</summary>
        public static int SelectAttackModeTargetIndex(Vector3 sentinelPosition, IReadOnlyList<Vector3> candidatePositions,
            Vector3 maxPosition, Vector3 maxForward, float coneHalfAngleDegrees)
        {
            int nearestOverallIndex = -1;
            float nearestOverallSq = float.MaxValue;
            int nearestInConeIndex = -1;
            float nearestInConeSq = float.MaxValue;

            for (int i = 0; i < candidatePositions.Count; i++)
            {
                float dSq = (candidatePositions[i] - sentinelPosition).sqrMagnitude;
                if (dSq < nearestOverallSq) { nearestOverallSq = dSq; nearestOverallIndex = i; }

                if (dSq < nearestInConeSq && IsWithinForwardCone(maxPosition, maxForward, candidatePositions[i], coneHalfAngleDegrees))
                {
                    nearestInConeSq = dSq;
                    nearestInConeIndex = i;
                }
            }

            return nearestInConeIndex >= 0 ? nearestInConeIndex : nearestOverallIndex;
        }
    }
}
