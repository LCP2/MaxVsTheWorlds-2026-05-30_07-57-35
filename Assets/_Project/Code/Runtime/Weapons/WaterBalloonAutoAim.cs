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
    /// MV-992: LOB is a RADIUS, not a fixed lob — Lee's TestFlight observation was that the balloon
    /// ignored robots standing close to Max and only threw once he backed off to about the full LOB
    /// distance, the opposite of the intent. The old <c>TryFindBestDirection</c> only ever scored a
    /// landing point at the full <c>throwDistance</c> (a thin ring); any robot inside that ring, at any
    /// distance from 0 out to the LOB range, is now a real candidate landing point in its own right —
    /// <see cref="TryFindBestLanding"/> scores each live target's own position, clamped to at least
    /// <see cref="AbilityTuning.MinThrowDistance"/>, exactly what <see cref="PlayerAbilities.TryThrowWaterBalloon"/>
    /// can now actually land at. Pure and static so it's EditMode-testable against a known layout with
    /// no live scene/physics.
    /// </summary>
    public static class WaterBalloonAutoAim
    {
        /// <summary>Finds the best landing point, from <paramref name="origin"/>, among
        /// <paramref name="targets"/> whose flat distance from <paramref name="origin"/> is at most
        /// <paramref name="maxDistance"/> (the current LOB radius). Each candidate lands exactly at its
        /// own target's position — clamped to at least <see cref="AbilityTuning.MinThrowDistance"/> so
        /// a target standing almost on top of Max still gets a real throw — and is scored by how many
        /// targets fall within <paramref name="splashRadius"/> of that landing. The highest-scoring
        /// candidate wins; ties go to the NEAREST candidate. Returns false (direction left at
        /// <see cref="Vector3.forward"/>, distance left at 0) only when no target is within
        /// <paramref name="maxDistance"/> — the "nothing in range" case (MV-373 AC5) a caller should
        /// read as "don't fire, don't spend a cell".</summary>
        public static bool TryFindBestLanding(
            Vector3 origin,
            float maxDistance,
            float splashRadius,
            IReadOnlyList<Vector3> targets,
            out Vector3 direction,
            out float distance)
        {
            direction = Vector3.forward;
            distance = 0f;
            if (targets == null || targets.Count == 0 || maxDistance <= 0f) return false;

            float maxDistanceSqr = maxDistance * maxDistance;
            float splashRadiusSqr = Mathf.Max(0f, splashRadius) * Mathf.Max(0f, splashRadius);

            bool found = false;
            int bestCount = -1;
            float bestRawDistanceSqr = float.MaxValue;
            Vector3 bestDirection = Vector3.zero;
            float bestDistance = 0f;

            for (int i = 0; i < targets.Count; i++)
            {
                Vector3 toTarget = Flatten(targets[i] - origin);
                float rawDistanceSqr = toTarget.sqrMagnitude;
                if (rawDistanceSqr > maxDistanceSqr) continue;

                float rawDistance = Mathf.Sqrt(rawDistanceSqr);
                Vector3 candidateDirection = rawDistance > 1e-6f ? toTarget / rawDistance : Vector3.forward;
                float candidateDistance = Mathf.Max(rawDistance, AbilityTuning.MinThrowDistance);
                Vector3 landing = origin + candidateDirection * candidateDistance;

                int count = 0;
                for (int j = 0; j < targets.Count; j++)
                {
                    if (Flatten(targets[j] - landing).sqrMagnitude <= splashRadiusSqr) count++;
                }

                bool better = count > bestCount ||
                    (count == bestCount && rawDistanceSqr < bestRawDistanceSqr);
                if (better)
                {
                    found = true;
                    bestCount = count;
                    bestRawDistanceSqr = rawDistanceSqr;
                    bestDirection = candidateDirection;
                    bestDistance = candidateDistance;
                }
            }

            if (!found) return false;
            direction = bestDirection;
            distance = bestDistance;
            return true;
        }

        private static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
