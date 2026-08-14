using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Pure maths for where a Blinker lands (MV-293). It doesn't blink to Max's own spot, and it
    /// doesn't blink back the way it came — either would read as "warps in front of you", which is
    /// just a rusher with a teleport-shaped coat of paint. It lands to the SIDE of whatever line it
    /// was already approaching on, so the beat reads as a flank: gone from where you last saw it,
    /// suddenly pressing from an angle you weren't watching.
    /// </summary>
    public static class BlinkerTeleport
    {
        /// <summary>Rotation applied to the current approach vector to find the flank point — enough
        /// off the nose that the landing spot is a clear side/rear angle, not a re-tread of the front
        /// door.</summary>
        public const float FlankAngleDeg = 110f;

        /// <summary>Degrees of dead-zone straddling due north (+Z) and due south (-Z) of the target
        /// that a landing point may never fall inside — the fixed ~72° camera pitch flattens that axis
        /// into a sliver Lee said he can "barely see" (MV-366 for the group jump, MV-384 for this solo
        /// path — every Blinker teleport shares the same dead zone, mirrors
        /// <see cref="BlinkerSquadTeleport.NorthSouthExclusionDeg"/>).</summary>
        public const float NorthSouthExclusionDeg = 25f;

        /// <summary>Where a Blinker at <paramref name="attackerPos"/> lands, <paramref name="distance"/>
        /// from <paramref name="targetPos"/>, on whichever side <paramref name="sign"/> (±1) picks.
        /// Falls back to a fixed approach direction if the attacker is standing on top of the target
        /// (no meaningful "current side" to rotate away from). Never lands inside the north/south dead
        /// zone through the target, even if the raw flank angle would have put it there.</summary>
        public static Vector3 FlankPoint(Vector3 targetPos, Vector3 attackerPos, float distance, float sign)
        {
            Vector3 toAttacker = attackerPos - targetPos;
            toAttacker.y = 0f;
            if (toAttacker.sqrMagnitude < 1e-4f) toAttacker = Vector3.forward;
            toAttacker.Normalize();

            Quaternion rot = Quaternion.AngleAxis(FlankAngleDeg * Mathf.Sign(sign), Vector3.up);
            Vector3 dir = rot * toAttacker;

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
    }
}
