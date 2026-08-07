using UnityEngine;

namespace MaxWorlds.Combat
{
    /// <summary>
    /// Pure hit-test for the Water Blaster's Spray cone (YT-64): a target is hit if it's within
    /// range and inside the aim cone's half-angle. Planar (top-down) — height is ignored so a
    /// slightly-raised robot still counts. No MonoBehaviour, so the cone maths is unit-testable.
    /// </summary>
    public static class SprayHit
    {
        /// <summary>True if <paramref name="targetPos"/> is inside the spray cone fired from
        /// <paramref name="origin"/> along <paramref name="aimDir"/>.</summary>
        public static bool InCone(Vector3 origin, Vector3 aimDir, Vector3 targetPos, float range, float halfAngleDeg)
        {
            Vector3 to = targetPos - origin;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > range) return false;
            if (dist < 0.05f) return true;                 // point-blank / overlapping = hit

            Vector3 aim = new Vector3(aimDir.x, 0f, aimDir.z);
            if (aim.sqrMagnitude < 1e-6f) return true;      // no aim direction: treat range as a bubble

            float ang = Vector3.Angle(aim.normalized, to / dist);
            return ang <= halfAngleDeg;
        }

        /// <summary>Angle in degrees between <paramref name="aimDir"/> and <paramref name="targetPos"/>,
        /// planar (top-down) — the same calculation <see cref="InCone"/> uses internally, exposed so
        /// damage falloff (MV-281) reads the identical angle the hit test already approved. 0° for a
        /// point-blank target or an unset aim direction, matching <see cref="InCone"/>'s always-hit
        /// fallback for those cases.</summary>
        public static float AngleDeg(Vector3 origin, Vector3 aimDir, Vector3 targetPos)
        {
            Vector3 to = targetPos - origin;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0025f) return 0f;
            Vector3 aim = new Vector3(aimDir.x, 0f, aimDir.z);
            if (aim.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.Angle(aim.normalized, to.normalized);
        }

        /// <summary>Per-hit damage multiplier for the spray cone (MV-281): full power (1x) on the
        /// centre-line, falling off linearly to <paramref name="edgeMultiplier"/> at the cone's outer
        /// edge (<paramref name="halfAngleDeg"/>) — so the spray reads as a real fan with a hot core
        /// rather than a uniform-power wall. Clamped beyond the edge in case a caller passes an angle
        /// wider than the cone that admitted the hit.</summary>
        public static float DamageFalloff(float angleDeg, float halfAngleDeg, float edgeMultiplier)
        {
            if (halfAngleDeg <= 0f) return 1f;
            float t = Mathf.Clamp01(angleDeg / halfAngleDeg);
            return Mathf.Lerp(1f, edgeMultiplier, t);
        }
    }
}
