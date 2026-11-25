using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// MV-1451 chain lightning (VISUAL ONLY): from POWER 6, when a RICOCHET disc hits a robot, arcs jump
    /// from it to the nearest other alive, AWAKE robot within <see cref="Range"/> on the same combat level
    /// with a clear line of sight (the <see cref="ArcTargeting"/> selection PulseLaser's ARC also uses),
    /// continuing from the robot reached, never revisiting one: 1 jump at POWER 6, 2 at 7, 3 at 8. Draws
    /// with <c>LppeVfx.Arc</c> and gives each reached robot the hit frazzle. Deals NO damage, wakes nobody,
    /// and only ever considers <see cref="RobotEnemy"/> (never pylons, relays, cover, canisters,
    /// Sentinels, bosses or gates).
    /// </summary>
    public static class RicochetChain
    {
        public const float Range = 4f;
        private static readonly HashSet<RobotEnemy> s_visited = new HashSet<RobotEnemy>();

        public static int JumpsForPower(int power) => power >= 8 ? 3 : power == 7 ? 2 : power == 6 ? 1 : 0;

        /// <summary>Runs the chain from <paramref name="hit"/>; returns how many arcs were drawn.</summary>
        public static int Run(RobotEnemy hit, int power)
        {
            int jumps = JumpsForPower(power);
            if (jumps == 0 || hit == null) return 0;

            s_visited.Clear();
            s_visited.Add(hit);
            RobotEnemy current = hit;
            LppeVfx vfx = LppeVfx.GetShared();
            int drawn = 0;
            for (int j = 0; j < jumps; j++)
            {
                RobotEnemy next = ArcTargeting.NearestOtherAliveRobotWithLineOfSight(
                    current, current.transform.position, Range, includeDormant: false, alsoExclude: s_visited);
                if (next == null) break;

                vfx.Arc(current.transform.position + Vector3.up * 0.6f, next.transform.position + Vector3.up * 0.6f);
                RobotFrazzleVfx.Trigger(next);
                s_visited.Add(next);
                current = next;
                drawn++;
            }
            s_visited.Clear();
            return drawn;
        }
    }
}
