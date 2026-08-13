using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Pure maths for a coordinated Blinker squad jump (MV-366) — Lee's ask for an occasional group
    /// variant of <see cref="BlinkerTeleport"/>'s solo flank-blink: several Blinkers vanish together
    /// and reappear as one unit, closer to Max than the rest of the pack, so the squad becomes the
    /// immediate threat instead of the tail end of a chase.
    ///
    /// Two constraints a solo blink never had to satisfy: the landing point must never sit on the
    /// north/south axis through Max (the fixed ~72° camera pitch flattens that axis into a sliver Lee
    /// said he can "barely see"), and it must land closer to Max than every robot NOT taking part in
    /// the jump, or "the group leapfrogs the pack" doesn't read.
    /// </summary>
    public static class BlinkerSquadTeleport
    {
        /// <summary>Degrees of dead-zone straddling due north (+Z) and due south (-Z) of Max that a
        /// squad's landing point may never fall inside.</summary>
        public const float NorthSouthExclusionDeg = 25f;

        /// <summary>Clear space kept between the squad's landing distance and the nearest
        /// non-participating robot's current distance to Max — "closer than the pack" with a real
        /// margin, not a coin-flip on floating point.</summary>
        public const float PackClearanceMetres = 1.5f;

        /// <summary>How close a squad may land to Max at the very nearest — never on top of him.</summary>
        public const float MinLandingDistance = 1.5f;

        /// <summary>How many Blinkers make up a jump: never a solo (that's the existing blink), never
        /// more than a small, readable knot.</summary>
        public const int MinGroupSize = 2;
        public const int MaxGroupSize = 3;

        /// <summary>
        /// Whether the pack is spread out enough for a squad jump to have somewhere legal to land: a
        /// point strictly closer than every non-participating robot, and not on top of Max.
        /// <paramref name="nearestPackDistance"/> is the smallest current distance-to-Max among every
        /// active robot NOT taking part in the jump — pass <see cref="float.PositiveInfinity"/> when
        /// there are none (e.g. a field left with nothing but Blinkers).
        /// </summary>
        public static bool CanLandCloserThanPack(float nearestPackDistance) =>
            nearestPackDistance > MinLandingDistance + PackClearanceMetres;

        /// <summary>
        /// How far from Max the squad lands: as close as <paramref name="preferredDistance"/> (the
        /// attack range that lets it commit the instant it arrives), but always strictly nearer than
        /// <paramref name="nearestPackDistance"/> and never closer than <see cref="MinLandingDistance"/>.
        /// Only meaningful when <see cref="CanLandCloserThanPack"/> is true for the same distance.
        /// </summary>
        public static float LandingDistance(float nearestPackDistance, float preferredDistance)
        {
            float ceiling = nearestPackDistance - PackClearanceMetres;
            return Mathf.Clamp(preferredDistance, MinLandingDistance, Mathf.Max(MinLandingDistance, ceiling));
        }

        /// <summary>
        /// Where the squad lands: a single flank point <paramref name="distance"/> from
        /// <paramref name="targetPos"/>, on whichever side <paramref name="sign"/> (±1) picks, biased
        /// off the pack's approach line the same way <see cref="BlinkerTeleport.FlankPoint"/> is, then
        /// rotated clear of due north/south of Max if the raw flank angle would have landed inside the
        /// dead zone.
        /// </summary>
        public static Vector3 GroupFlankPoint(Vector3 targetPos, Vector3 packPos, float distance, float sign)
        {
            Vector3 raw = BlinkerTeleport.FlankPoint(targetPos, packPos, distance, sign);
            Vector3 dir = raw - targetPos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right * (sign >= 0f ? 1f : -1f);
            dir.Normalize();

            // 0 = due north (+Z), ±180 = due south (-Z) — the axis the fixed camera pitch flattens.
            float fromNorth = Vector3.SignedAngle(Vector3.forward, dir, Vector3.up);
            float clamped = ClampAwayFromNorthSouth(fromNorth);
            if (!Mathf.Approximately(clamped, fromNorth))
                dir = Quaternion.AngleAxis(clamped, Vector3.up) * Vector3.forward;

            return targetPos + dir * distance;
        }

        /// <summary>Push an angle-from-north (degrees, -180..180) out of the dead zones straddling 0
        /// (north) and ±180 (south), to whichever edge of the zone is nearest.</summary>
        private static float ClampAwayFromNorthSouth(float angleDeg)
        {
            if (Mathf.Abs(angleDeg) < NorthSouthExclusionDeg)
                return Mathf.Sign(angleDeg) * NorthSouthExclusionDeg;

            float degreesFromSouth = 180f - Mathf.Abs(angleDeg);
            if (degreesFromSouth < NorthSouthExclusionDeg)
                return Mathf.Sign(angleDeg) * (180f - NorthSouthExclusionDeg);

            return angleDeg;
        }

        /// <summary>The smallest distance-to-Max among the robots NOT taking part in the jump — the
        /// "remaining pack" a squad's landing point must land closer than.
        /// <see cref="float.PositiveInfinity"/> if every active robot is a participant.</summary>
        public static float NearestPackDistance(IReadOnlyList<float> distancesToTarget, IReadOnlyList<bool> isParticipant)
        {
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < distancesToTarget.Count; i++)
                if (!isParticipant[i] && distancesToTarget[i] < nearest) nearest = distancesToTarget[i];
            return nearest;
        }
    }
}
