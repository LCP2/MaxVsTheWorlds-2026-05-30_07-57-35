using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// MV-373: picks the Water Balloon landing point auto-fire throws at, since a PLACED weapon's
    /// auto-fire is otherwise incoherent — something has to choose where it lands (Lee's design
    /// direction, 12 Aug 2026: "we'll select a position where the most number of robots are that are
    /// in range").
    ///
    /// <see cref="Weapons.PlayerAbilities.TryThrowWaterBalloon"/> always lands at exactly the current
    /// Range distance from the thrower, along whatever direction it's given — a full-pull manual throw
    /// never lands short (see its own landing calc). So direction is the only free variable; a
    /// candidate landing point is fully described by which way to aim. This scans each live target's
    /// own bearing as a candidate direction — the landing point nearest a real cluster is always at, or
    /// very close to, one of the cluster's own members' bearing — an O(n^2) scan (n = targets) rather
    /// than a continuous angle search, so it stays cheap enough to re-run every auto-fire. Pure and
    /// static so it's EditMode-testable against a known layout with no live scene/physics.
    /// </summary>
    public static class WaterBalloonAutoAim
    {
        /// <summary>Finds the direction, from <paramref name="origin"/>, whose <paramref name="throwDistance"/>-away
        /// landing point catches the most of <paramref name="targetPositions"/> within <paramref name="splashRadius"/>.
        /// Returns false (direction left at <see cref="Vector3.forward"/>) when there are no targets, or
        /// none of them are within reach of any candidate landing point — the "nothing in range" case
        /// (MV-373 AC5) a caller should read as "don't fire, don't spend a cell".</summary>
        public static bool TryFindBestDirection(
            Vector3 origin,
            float throwDistance,
            float splashRadius,
            IReadOnlyList<Vector3> targetPositions,
            out Vector3 direction)
        {
            direction = Vector3.forward;
            if (targetPositions == null || targetPositions.Count == 0 || throwDistance <= 0f) return false;

            float splashRadiusSqr = Mathf.Max(0f, splashRadius) * Mathf.Max(0f, splashRadius);
            int bestCount = 0;
            Vector3 bestDirection = Vector3.zero;

            for (int i = 0; i < targetPositions.Count; i++)
            {
                Vector3 toTarget = Flatten(targetPositions[i] - origin);
                if (toTarget.sqrMagnitude < 1e-6f) continue;

                Vector3 candidateDirection = toTarget.normalized;
                Vector3 landing = origin + candidateDirection * throwDistance;

                int count = 0;
                for (int j = 0; j < targetPositions.Count; j++)
                {
                    if (Flatten(targetPositions[j] - landing).sqrMagnitude <= splashRadiusSqr) count++;
                }

                if (count > bestCount)
                {
                    bestCount = count;
                    bestDirection = candidateDirection;
                }
            }

            if (bestCount <= 0) return false;
            direction = bestDirection;
            return true;
        }

        private static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
