using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Shared homing/obstruction logic for seeking projectiles (MV-708) — extracted from
    /// <see cref="MaxWorlds.Enemies.HomingMissile"/> (MV-293/MV-364) so <see cref="SeekerPulse"/> reuses
    /// the exact same steering and Cover-layer obstruction check instead of a second, drifting copy.
    /// <see cref="MaxWorlds.Enemies.HomingMissile.BlockedByGeometry"/> stays as a thin public wrapper
    /// over this for source/test compatibility.
    /// </summary>
    public static class HomingSteering
    {
        /// <summary>Horizontal-only turn toward <paramref name="targetPos"/> at a capped rate — the
        /// same steering <see cref="MaxWorlds.Enemies.HomingMissile"/> always used. Returns
        /// <paramref name="current"/> unchanged if the target sits (near) directly overhead/underfoot,
        /// so a projectile never spins toward an undefined bearing.</summary>
        public static Quaternion TurnToward(Quaternion current, Vector3 currentPos, Vector3 targetPos,
            float turnRateDegPerSec, float dt)
        {
            Vector3 to = targetPos - currentPos;
            to.y = 0f;
            if (to.sqrMagnitude <= 1e-4f) return current;
            Quaternion wanted = Quaternion.LookRotation(to.normalized, Vector3.up);
            return Quaternion.RotateTowards(current, wanted, turnRateDegPerSec * dt);
        }

        /// <summary>Whether solid geometry on the Cover layer stands between two points on a flight
        /// path this frame (MV-364) — a fence is cover for both a robot's missile and Max's own seeking
        /// pulse alike.</summary>
        public static bool BlockedByGeometry(Vector3 from, Vector3 to, out RaycastHit hit)
        {
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 1e-4f)
            {
                hit = default;
                return false;
            }

            return Physics.Raycast(from, delta / dist, out hit, dist, CoverLayer.Mask,
                                   QueryTriggerInteraction.Ignore);
        }
    }
}
