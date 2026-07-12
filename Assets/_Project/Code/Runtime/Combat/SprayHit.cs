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
    }
}
