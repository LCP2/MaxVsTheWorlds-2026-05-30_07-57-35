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

        /// <summary>Where a Blinker at <paramref name="attackerPos"/> lands, <paramref name="distance"/>
        /// from <paramref name="targetPos"/>, on whichever side <paramref name="sign"/> (±1) picks.
        /// Falls back to a fixed approach direction if the attacker is standing on top of the target
        /// (no meaningful "current side" to rotate away from).</summary>
        public static Vector3 FlankPoint(Vector3 targetPos, Vector3 attackerPos, float distance, float sign)
        {
            Vector3 toAttacker = attackerPos - targetPos;
            toAttacker.y = 0f;
            if (toAttacker.sqrMagnitude < 1e-4f) toAttacker = Vector3.forward;
            toAttacker.Normalize();

            Quaternion rot = Quaternion.AngleAxis(FlankAngleDeg * Mathf.Sign(sign), Vector3.up);
            Vector3 dir = rot * toAttacker;
            return targetPos + dir * distance;
        }
    }
}
