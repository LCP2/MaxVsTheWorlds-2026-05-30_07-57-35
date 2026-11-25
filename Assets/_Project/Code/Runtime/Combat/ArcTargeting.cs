using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Combat
{
    /// <summary>
    /// MV-1451: the one "nearest OTHER robot with a clear line of sight" selection, moved out of
    /// <see cref="PulseLaser"/> (MV-858 ARC, which still calls it with dormant robots included, exactly as
    /// before) so RICOCHET's visual-only chain lightning picks its next robot with the SAME code rather
    /// than a second copy.
    /// </summary>
    public static class ArcTargeting
    {
        /// <summary>The nearest alive robot other than <paramref name="exclude"/> (and anything in
        /// <paramref name="alsoExclude"/>) within <paramref name="range"/> of <paramref name="from"/> with a
        /// clear line of sight from it, ON THE SAME COMBAT LEVEL as <paramref name="from"/> (MV-944).
        /// <paramref name="includeDormant"/> true is PulseLaser ARC's behaviour (dormant robots count and
        /// are woken by the caller); RICOCHET's chain passes false.</summary>
        public static RobotEnemy NearestOtherAliveRobotWithLineOfSight(RobotEnemy exclude, Vector3 from,
            float range, bool includeDormant = true, HashSet<RobotEnemy> alsoExclude = null)
        {
            var active = RobotEnemy.Active;
            MapData map = EnemyNavigation.Map;
            RobotEnemy best = null;
            float bestSq = float.MaxValue;
            float rangeSq = range * range;

            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy candidate = active[i];
                if (candidate == null || candidate == exclude || !candidate.IsAlive) continue;
                if (!includeDormant && candidate.IsDormant) continue;
                if (alsoExclude != null && alsoExclude.Contains(candidate)) continue;
                if (!CombatLevel.SameLevel(map, from, candidate.transform.position)) continue;

                float distSq = (candidate.transform.position - from).sqrMagnitude;
                if (distSq > rangeSq) continue;
                if (HomingSteering.BlockedByGeometry(from, candidate.transform.position, out _)) continue;
                if (distSq < bestSq) { bestSq = distSq; best = candidate; }
            }
            return best;
        }
    }
}
