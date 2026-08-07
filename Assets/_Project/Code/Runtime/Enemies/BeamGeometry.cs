using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Pure geometry for the Gunner's laser (MV-293): is a point inside a fixed-width beam fired from
    /// an origin along a LOCKED direction. Separate from <see cref="MaxWorlds.Arena.LineOfSight"/>,
    /// which asks whether anything BLOCKS the beam — this asks whether the target is even standing IN
    /// it, which is the other half of "dodge or break line of sight": the direction is committed the
    /// instant the telegraph ends (<see cref="RobotEnemy"/> re-aims live only up to that point), so
    /// side-stepping out of the beam's width works even with a clear sight-line to the shooter.
    /// </summary>
    public static class BeamGeometry
    {
        /// <summary>True if <paramref name="point"/> sits inside a beam of <paramref name="halfWidth"/>
        /// fired from <paramref name="origin"/> toward <paramref name="direction"/> (assumed normalized,
        /// measured in the XZ plane) out to <paramref name="range"/>.</summary>
        public static bool Hits(Vector3 origin, Vector3 direction, float range, float halfWidth, Vector3 point)
        {
            Vector3 to = point - origin;
            to.y = 0f;

            float forward = Vector3.Dot(to, direction);
            if (forward < 0f || forward > range) return false;

            Vector3 right = Vector3.Cross(Vector3.up, direction);
            float lateral = Vector3.Dot(to, right);
            return Mathf.Abs(lateral) <= halfWidth;
        }
    }
}
