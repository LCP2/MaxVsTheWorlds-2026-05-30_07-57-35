using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Keeps a chasing pack from clumping into a single stack (MV-321 — feedback, Max 0.7 doc: robots
    /// were pressing shoulder-to-shoulder rather than reading as a spread-out group).
    ///
    /// <see cref="EnemyFormation"/> already fans each robot's GOAL out onto its own lane, but that fan
    /// is angular and collapses to zero right at the point of contact — exactly where clumping is
    /// worst — and it never looks at where other robots actually ARE, only where the pack is headed.
    /// This adds the missing term: a steering push, layered on top of whatever formation/obstacle
    /// steering already produced, away from anything nearer than <see cref="DefaultMinDistance"/>.
    ///
    /// Pure maths, no transforms, no clock — a caller hands in the positions it already has
    /// (<see cref="RobotEnemy.Active"/>) so this stays unit-testable the same way
    /// <see cref="EnemyFormation"/>/<see cref="ObstacleSteering"/> are.
    /// </summary>
    public static class EnemySeparation
    {
        /// <summary>How close two robots may get before this starts pushing them apart. Comfortably
        /// wider than any two archetypes' combined collider radii today (rusher 0.4 + bruiser 0.55 =
        /// 0.95 at the largest) so it reads as spacing rather than just anti-overlap.</summary>
        public const float DefaultMinDistance = 1.8f;

        /// <summary>
        /// Sums a push-apart vector from every position in <paramref name="others"/> closer than
        /// <paramref name="minDistance"/> to <paramref name="selfPos"/> — bigger the closer they are,
        /// zero once they're clear. XZ-plane only; Y is ignored so a step or slope doesn't skew it.
        /// </summary>
        public static Vector3 Push(Vector3 selfPos, IReadOnlyList<Vector3> others, float minDistance)
        {
            Vector3 total = Vector3.zero;
            for (int i = 0; i < others.Count; i++)
            {
                Vector3 away = selfPos - others[i];
                away.y = 0f;
                float dist = away.magnitude;
                if (dist < 1e-4f || dist >= minDistance) continue;
                total += (away / dist) * (minDistance - dist);
            }
            return total;
        }

        /// <summary>
        /// Blends a separation push into a desired steering direction, returning a unit vector. A
        /// zero push leaves <paramref name="desired"/> untouched; a push that would fully cancel it
        /// (two robots pushing exactly opposite the way each wants to go) falls back to
        /// <paramref name="desired"/> rather than handing the caller a zero vector it can't act on.
        /// </summary>
        public static Vector3 Steer(Vector3 desired, Vector3 push)
        {
            if (push.sqrMagnitude < 1e-6f) return desired;
            Vector3 blended = desired + push;
            return blended.sqrMagnitude > 1e-6f ? blended.normalized : desired;
        }
    }
}
